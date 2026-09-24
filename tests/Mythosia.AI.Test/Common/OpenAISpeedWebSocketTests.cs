using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("OpenAI")]
public class OpenAISpeedWebSocketTests
{
    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task ServerSteeringContinuation_PreservesEachResponsesActualTier(string model)
    {
        using var socket = new ScriptedSocket();
        var firstText = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        socket.OnSend = payload =>
        {
            if (payload.GetProperty("type").GetString() == "response.create")
            {
                Assert.AreEqual("fast", payload.GetProperty("service_tier").GetString());
                socket.Push(Created("resp_1", "fast"), Text("before "));
            }
            else
            {
                Assert.AreEqual("response.steer", payload.GetProperty("type").GetString());
                socket.Push(
                    """{"type":"response.steer.accepted","steer":{"id":"steer_1","previous_response_id":"resp_1"}}""",
                    Completed("resp_1"), Created("resp_2", "default"), Text("after"), Completed("resp_2"));
            }
        };
        var service = new SocketService(socket, model);
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await using var run = await service.StartRunAsync("draft", _ => firstText.TrySetResult(), StreamOptions.TextOnlyOptions);
        await firstText.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.SteerAsync("revise").WaitAsync(TimeSpan.FromSeconds(5));
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("before after", result.Text);
        Assert.AreEqual(1, socket.Sent.Count(item => item.GetProperty("type").GetString() == "response.create"));
        Assert.AreEqual(2, result.Processing.Count);
        Assert.AreEqual(1, result.Processing[0].RequestIndex);
        Assert.AreEqual(2, result.Processing[1].RequestIndex);
        Assert.AreEqual("resp_1", result.Processing[0].ResponseId);
        Assert.AreEqual("resp_2", result.Processing[1].ResponseId);
        Assert.AreEqual(InferenceSpeed.Fast, result.Processing[0].AppliedSpeed);
        Assert.AreEqual(InferenceSpeed.Standard, result.Processing[1].AppliedSpeed);
        Assert.IsTrue(result.Processing[1].IsDowngraded);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task ToolContinuation_ResendsSpeedAndCreatesOneObservationPerRequest(string model)
    {
        using var socket = new ScriptedSocket();
        var sends = 0;
        socket.OnSend = payload =>
        {
            Assert.AreEqual("response.create", payload.GetProperty("type").GetString());
            Assert.AreEqual("fast", payload.GetProperty("service_tier").GetString());
            if (++sends == 1)
                socket.Push(Created("resp_tool", "priority"),
                    """{"type":"response.completed","response":{"id":"resp_tool","status":"completed","output":[{"id":"fc_read","call_id":"call_read","type":"function_call","status":"completed","name":"read_value","arguments":"{}"}]}}""");
            else
                socket.Push(Created("resp_answer", "default"), Text("answer"), Completed("resp_answer"));
        };
        var service = new SocketService(socket, model);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "read_value", Description = "Returns a value", Handler = _ => Task.FromResult("42")
        });
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await using var run = await service.StartRunAsync("use the tool");
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual(2, sends);
        Assert.AreEqual(2, result.Processing.Count);
        Assert.AreEqual("resp_tool", result.Processing[0].ResponseId);
        Assert.AreEqual("resp_answer", result.Processing[1].ResponseId);
        Assert.AreEqual(InferenceSpeed.Fast, result.Processing[0].AppliedSpeed);
        Assert.IsTrue(result.Processing[1].IsDowngraded);
    }

    [TestMethod]
    public async Task ProviderDefault_OmitsTierAndLeavesUnreportedProcessingUnknown()
    {
        using var socket = new ScriptedSocket();
        socket.OnSend = payload =>
        {
            Assert.IsFalse(payload.TryGetProperty("service_tier", out _));
            socket.Push(Created("resp_answer", null), Text("answer"), Completed("resp_answer"));
        };
        var service = new SocketService(socket);
        await using var run = await service.StartRunAsync("default");
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, result.Processing.Count);
        Assert.AreEqual(InferenceSpeed.ProviderDefault, result.Processing[0].RequestedSpeed);
        Assert.IsNull(result.Processing[0].AppliedSpeed);
        Assert.IsNull(result.Processing[0].RawAppliedMode);
    }

    private static string Created(string id, string? tier)
    {
        var response = new Dictionary<string, object> { ["id"] = id, ["model"] = "gpt-6-astra", ["status"] = "in_progress" };
        if (tier != null) response["service_tier"] = tier;
        return JsonSerializer.Serialize(new { type = "response.created", response });
    }
    private static string Text(string text) => JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = text });
    private static string Completed(string id) => JsonSerializer.Serialize(new { type = "response.completed", response = new { id, status = "completed", output = Array.Empty<object>() } });

    private sealed class SocketService(ScriptedSocket socket, string model = AIModels.OpenAI.Gpt6Astra) : OpenAIService("test", model, new HttpClient(new NoHttpHandler()))
    {
        protected override Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
            => Task.FromResult<WebSocket>(socket);
    }
    private sealed class NoHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("The run must use its WebSocket transport.");
    }
    private sealed class ScriptedSocket : WebSocket
    {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
        private readonly CancellationTokenSource _aborted = new();
        private WebSocketState _state = WebSocketState.Open;
        public List<JsonElement> Sent { get; } = new();
        public Action<JsonElement>? OnSend { get; set; }
        public void Push(params string[] events)
        { foreach (var item in events) _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(item)); }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { _state = WebSocketState.Aborted; _aborted.Cancel(); }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
        public override void Dispose() => Abort();
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _aborted.Token);
            var frame = await _incoming.Reader.ReadAsync(linked.Token);
            Assert.IsTrue(frame.Length <= buffer.Count);
            frame.CopyTo(buffer.Array!, buffer.Offset);
            return new WebSocketReceiveResult(frame.Length, WebSocketMessageType.Text, true);
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(buffer.AsMemory());
            var payload = document.RootElement.Clone();
            Sent.Add(payload);
            OnSend?.Invoke(payload);
            return Task.CompletedTask;
        }
    }
}
