using Mythosia.AI.Attributes;
using Mythosia.AI.Builders;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("FunctionCalling")]
public class ToolCancellationContractTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow(FunctionExecutionMode.Sequential)]
    [DataRow(FunctionExecutionMode.Parallel)]
    public async Task PreCancelledBatch_DoesNotInvokeToolsOrWriteHistory(FunctionExecutionMode mode)
    {
        var service = new BatchService();
        var invoked = 0;
        service.Functions.Add(FunctionBuilder.Create("lookup").WithHandler((_, token) =>
        {
            Interlocked.Increment(ref invoked);
            return Task.FromResult("value");
        }).Build());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteAsync(
            Calls(2), Policy(mode), cancellation.Token));

        Assert.AreEqual(0, invoked);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(FunctionExecutionMode.Sequential)]
    [DataRow(FunctionExecutionMode.Parallel)]
    public async Task Cancellation_ReachesRunningToolsAndSkipsQueuedCallsInProviderOrder(FunctionExecutionMode mode)
    {
        var service = new BatchService();
        var started = Signal();
        var release = Signal();
        var invoked = 0;
        var runningCount = mode == FunctionExecutionMode.Parallel ? 2 : 1;
        var observedTokens = new System.Collections.Concurrent.ConcurrentBag<CancellationToken>();
        service.Functions.Add(FunctionBuilder.Create("lookup").WithHandler(async (_, token) =>
        {
            observedTokens.Add(token);
            if (Interlocked.Increment(ref invoked) == runningCount) started.TrySetResult();
            await release.Task.WaitAsync(token);
            return "unexpected result";
        }).Build());
        using var cancellation = new CancellationTokenSource();
        var calls = Calls(5);
        var execution = service.ExecuteAsync(calls, Policy(mode), cancellation.Token);

        try
        {
            await started.Task.WaitAsync(TestTimeout);
            cancellation.Cancel();
            var results = await execution.WaitAsync(TestTimeout);

            Assert.AreEqual(runningCount, invoked, "Calls still waiting for an execution slot must not start.");
            Assert.IsTrue(observedTokens.All(token => token.IsCancellationRequested));
            Assert.AreEqual(calls.Id, results.FunctionCallBatchId);
            CollectionAssert.AreEqual(calls.Calls.Select(call => call.Id).ToArray(),
                results.Results.Select(result => result.Call.Id).ToArray());
            Assert.IsTrue(results.Results.All(result => result.IsError && result.IsCancelled));

            service.Save(calls, results);
            AssertPairedHistory(service, 5);
        }
        finally
        {
            cancellation.Cancel();
            release.TrySetResult();
            await DrainAsync(execution);
        }
    }

    [TestMethod]
    public async Task Cancellation_AwaitsStartedLegacyHandlerAndPreservesItsActualResult()
    {
        var service = new BatchService();
        var started = Signal();
        var release = Signal();
        var invoked = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Handler = async _ =>
            {
                Interlocked.Increment(ref invoked);
                started.TrySetResult();
                await release.Task;
                return "operation finished";
            }
        });
        using var cancellation = new CancellationTokenSource();
        var execution = service.ExecuteAsync(Calls(2), Policy(FunctionExecutionMode.Sequential), cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TestTimeout);
            cancellation.Cancel();
            Assert.IsFalse(execution.IsCompleted, "Cancellation cannot abandon a running noncooperative handler.");
            release.TrySetResult();
            var results = await execution.WaitAsync(TestTimeout);

            Assert.AreEqual(1, invoked);
            Assert.AreEqual("operation finished", results.Results[0].Content);
            Assert.IsFalse(results.Results[0].IsError);
            Assert.IsFalse(results.Results[0].IsCancelled);
            Assert.IsTrue(results.Results[1].IsError);
            Assert.IsTrue(results.Results[1].IsCancelled);
        }
        finally
        {
            release.TrySetResult();
            await DrainAsync(execution);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task HttpRun_CancelOrDisposeCancelsToolAndRecordsResultsWithoutAnotherRequest(bool dispose, bool reflected)
    {
        var transport = new QueueHandler((OpenAIToolSse, "text/event-stream"));
        var service = new OpenAIService("offline-test-key", AIModels.OpenAI.Gpt5_6Sol, new HttpClient(transport));
        var tool = new CooperativeTool();
        var request = service.CreateRequest("Use the tools.").WithPolicy(Policy(FunctionExecutionMode.Sequential));
        var run = await (reflected ? request.WithFunctions(tool) : request.WithFunctions(tool.Definition)).StartRunAsync();
        try
        {
            await tool.Started.Task.WaitAsync(TestTimeout);
            if (dispose) await run.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            else run.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(TestTimeout));
            Assert.IsTrue(run.Result.IsCanceled);
            Assert.IsTrue(tool.Token.IsCancellationRequested);
            Assert.AreEqual(1, tool.Invocations);
            Assert.AreEqual(1, transport.Requests.Count, "A cancelled tool batch must not start another LLM request.");
            var results = AssertPairedHistory(service, 2);
            Assert.IsTrue(results.All(result => result.IsError && result.IsCancelled));
        }
        finally
        {
            tool.Release.TrySetResult();
            run.Cancel();
            await run.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [TestMethod]
    public async Task HttpRun_CancellationWaitsForLegacyToolBeforeCompletingResultAndHistory()
    {
        var transport = new QueueHandler((OpenAIToolSse, "text/event-stream"));
        var service = new OpenAIService("offline-test-key", AIModels.OpenAI.Gpt5_6Sol, new HttpClient(transport));
        var started = Signal();
        var release = Signal();
        var invocations = 0;
        var definition = new FunctionDefinition
        {
            Name = "lookup",
            Handler = async _ =>
            {
                Interlocked.Increment(ref invocations);
                started.TrySetResult();
                await release.Task;
                return "saved operation result";
            }
        };
        var run = await service.CreateRequest("Use the tools.").WithFunctions(definition)
            .WithPolicy(Policy(FunctionExecutionMode.Sequential)).StartRunAsync();
        try
        {
            await started.Task.WaitAsync(TestTimeout);
            run.Cancel();
            Assert.IsFalse(run.Result.IsCompleted, "A run must finish tool cleanup before publishing its cancellation.");
            release.TrySetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(TestTimeout));

            Assert.IsTrue(run.Result.IsCanceled);
            Assert.AreEqual(1, invocations);
            Assert.AreEqual(1, transport.Requests.Count);
            var results = AssertPairedHistory(service, 2);
            Assert.AreEqual("saved operation result", results[0].Content);
            Assert.IsFalse(results[0].IsError);
            Assert.IsFalse(results[0].IsCancelled);
            Assert.IsTrue(results[1].IsError);
            Assert.IsTrue(results[1].IsCancelled);
        }
        finally
        {
            release.TrySetResult();
            run.Cancel();
            await run.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task WebSocketRun_CancelOrDisposeCancelsRequiredAndNativeAsyncTools(bool nativeAsync, bool dispose)
    {
        using var socket = new ScriptedSocket();
        var continued = Signal();
        socket.OnSend = payload =>
        {
            Assert.AreEqual("response.create", payload.GetProperty("type").GetString());
            if (socket.Sent.Count == 1)
                socket.Push(Created("resp_tools"), ToolCompleted(nativeAsync));
            else
            {
                Assert.IsTrue(nativeAsync);
                Assert.AreEqual(2, socket.Sent.Count);
                socket.Push(Created("resp_waiting"));
                continued.TrySetResult();
            }
        };
        var service = new SocketService(socket);
        var tool = new CooperativeTool(nativeAsync);
        var run = await service.CreateRequest("Use the tool.").WithFunctions(tool.Definition).StartRunAsync();
        try
        {
            await tool.Started.Task.WaitAsync(TestTimeout);
            if (nativeAsync) await continued.Task.WaitAsync(TestTimeout);
            if (dispose) await run.DisposeAsync().AsTask().WaitAsync(TestTimeout);
            else run.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(TestTimeout));
            Assert.IsTrue(run.Result.IsCanceled);
            Assert.IsTrue(tool.Token.IsCancellationRequested);
            Assert.AreEqual(1, tool.Invocations);
            Assert.AreEqual(nativeAsync ? 2 : 1, socket.Sent.Count);
            var result = AssertPairedHistory(service, 1).Single();
            Assert.IsTrue(result.IsError);
            Assert.IsTrue(result.IsCancelled);
            Assert.AreEqual(nativeAsync, result.Call.IsAsync);
        }
        finally
        {
            tool.Release.TrySetResult();
            run.Cancel();
            await run.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [TestMethod]
    public async Task NativeAsyncProviderFailure_CancelsPendingToolAndPreservesFailureAfterCleanup()
    {
        using var socket = new ScriptedSocket();
        var continued = Signal();
        socket.OnSend = _ =>
        {
            if (socket.Sent.Count == 1) socket.Push(Created("resp_tools"), ToolCompleted(true));
            else
            {
                Assert.AreEqual(2, socket.Sent.Count);
                socket.Push(Created("resp_waiting"));
                continued.TrySetResult();
            }
        };
        var service = new SocketService(socket);
        var tool = new CooperativeTool(true);
        var run = await service.CreateRequest("Use the tool.").WithFunctions(tool.Definition).StartRunAsync();
        try
        {
            await tool.Started.Task.WaitAsync(TestTimeout);
            await continued.Task.WaitAsync(TestTimeout);
            socket.Push("{\"type\":\"error\",\"error\":{\"code\":\"server_error\",\"message\":\"scripted transport failure\"}}");

            await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TestTimeout));
            Assert.IsFalse(run.Result.IsCanceled, "Provider failure must not be disguised as user cancellation.");
            Assert.IsTrue(tool.Token.IsCancellationRequested);
            Assert.AreEqual(2, socket.Sent.Count);
            var result = AssertPairedHistory(service, 1).Single();
            Assert.IsTrue(result.IsError);
            Assert.IsTrue(result.IsCancelled);
        }
        finally
        {
            tool.Release.TrySetResult();
            run.Cancel();
            await run.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeAsyncThrowingCancellationCallback_DoesNotSkipToolCleanup(bool dispose)
    {
        using var socket = new ScriptedSocket();
        var started = Signal();
        var continued = Signal();
        var release = Signal();
        var callbackInvoked = Signal();
        socket.OnSend = _ =>
        {
            if (socket.Sent.Count == 1) socket.Push(Created("resp_tools"), ToolCompleted(true));
            else
            {
                Assert.AreEqual(2, socket.Sent.Count);
                socket.Push(Created("resp_waiting"));
                continued.TrySetResult();
            }
        };
        var tool = FunctionBuilder.Create("lookup").WithAsync().WithHandler(async (_, token) =>
        {
            var waiting = release.Task.WaitAsync(token);
            using var registration = token.Register(() =>
            {
                callbackInvoked.TrySetResult();
                throw new InvalidOperationException("cancellation callback failed");
            });
            started.TrySetResult();
            await waiting;
            return "released tool result";
        }).Build();
        var service = new SocketService(socket);
        var run = await service.CreateRequest("Use the tool.").WithFunctions(tool).StartRunAsync();
        try
        {
            await started.Task.WaitAsync(TestTimeout);
            await continued.Task.WaitAsync(TestTimeout);
            AggregateException exception;
            if (dispose)
            {
                exception = await Assert.ThrowsAsync<AggregateException>(() => run.DisposeAsync().AsTask().WaitAsync(TestTimeout));
                await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(TestTimeout));
            }
            else
            {
                socket.Push("{\"type\":\"error\",\"error\":{\"code\":\"server_error\",\"message\":\"scripted transport failure\"}}");
                exception = await Assert.ThrowsAsync<AggregateException>(() => run.Result.WaitAsync(TestTimeout));
            }

            Assert.IsTrue(exception.Flatten().InnerExceptions.Any(inner => inner.Message == "cancellation callback failed"));
            Assert.IsTrue(callbackInvoked.Task.IsCompletedSuccessfully);
            Assert.AreEqual(2, socket.Sent.Count);
            var result = AssertPairedHistory(service, 1).Single();
            Assert.IsTrue(result.IsError);
            Assert.IsTrue(result.IsCancelled);
        }
        finally
        {
            release.TrySetResult();
            run.Cancel();
            await run.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [TestMethod]
    [DataRow("sync_failure")]
    [DataRow("async_failure")]
    public async Task ReflectedToolException_IsSentToAnthropicAsAnErrorWithOriginalMessage(string functionName)
    {
        var toolResponse = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "tool_use", id = "tool_failure", name = functionName, input = new { } } },
            stop_reason = "tool_use"
        });
        var transport = new QueueHandler((toolResponse, "application/json"),
            ("{\"content\":[{\"type\":\"text\",\"text\":\"failure handled\"}],\"stop_reason\":\"end_turn\"}", "application/json"));
        var service = new AnthropicService("offline-test-key", new HttpClient(transport));
        service.ChangeModel(AIModels.Anthropic.ClaudeOpus5);

        var answer = await service.CreateRequest("Use the failing tool.").WithFunctions(new ThrowingTools())
            .GetCompletionAsync().WaitAsync(TestTimeout);

        Assert.AreEqual("failure handled", answer);
        Assert.AreEqual(2, transport.Requests.Count);
        using var continuation = JsonDocument.Parse(transport.Requests[1]);
        var resultBlock = continuation.RootElement.GetProperty("messages").EnumerateArray()
            .SelectMany(message => message.GetProperty("content").ValueKind == JsonValueKind.Array
                ? message.GetProperty("content").EnumerateArray().ToArray() : Array.Empty<JsonElement>())
            .Single(content => content.GetProperty("type").GetString() == "tool_result");
        Assert.IsTrue(resultBlock.GetProperty("is_error").GetBoolean());
        Assert.AreEqual("tool_failure", resultBlock.GetProperty("tool_use_id").GetString());
        StringAssert.Contains(resultBlock.GetProperty("content").GetString(), "original tool failure");
        var result = AssertPairedHistory(service, 1).Single();
        Assert.IsTrue(result.IsError);
        Assert.IsFalse(result.IsCancelled);
        StringAssert.Contains(result.Content, "original tool failure");
    }

    private static FunctionCallingPolicy Policy(FunctionExecutionMode mode) => new()
    {
        ExecutionMode = mode, MaxConcurrency = 2, MaxRounds = 4, TimeoutSeconds = 15
    };

    private static FunctionCallBatch Calls(int count) => new(Enumerable.Range(0, count).Select(index =>
        new FunctionCall { Id = $"call_{index}", Name = "lookup", Index = index, Source = IdSource.OpenAI }));

    private static FunctionCallResult[] AssertPairedHistory(AIService service, int count)
    {
        var calls = service.ActivateChat.Messages.Where(message => message.FunctionCallBatch != null)
            .SelectMany(message => message.FunctionCallBatch!.Calls).ToArray();
        var resultMessages = service.ActivateChat.Messages.Where(message => message.FunctionCallResultBatch != null).ToArray();
        var results = resultMessages.SelectMany(message => message.FunctionCallResultBatch!.Results).ToArray();
        Assert.AreEqual(count, calls.Length);
        Assert.AreEqual(count, results.Length, "Cleanup must persist one result for every recorded call.");
        CollectionAssert.AreEquivalent(calls.Select(call => call.Id).ToArray(), results.Select(result => result.Call.Id).ToArray());
        Assert.AreEqual(count, results.Select(result => result.Call.Id).Distinct().Count(), "Cleanup must not duplicate results.");
        foreach (var message in resultMessages)
            Assert.IsTrue(service.ActivateChat.Messages.Any(callMessage =>
                callMessage.FunctionCallBatch?.Id == message.FunctionCallResultBatch!.FunctionCallBatchId));
        return results;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task DrainAsync(Task task) { try { await task.WaitAsync(TestTimeout); } catch { } }
    private static string Created(string id) => JsonSerializer.Serialize(new
        { type = "response.created", response = new { id, model = "gpt-6-astra", status = "in_progress" } });
    private static string ToolCompleted(bool asynchronous) => JsonSerializer.Serialize(new
    {
        type = "response.completed",
        response = new
        {
            id = "resp_tools", status = "completed",
            output = new[] { new { type = "function_call", id = "item_0", call_id = "call_0", name = "lookup", arguments = "{}", status = "completed", @async = asynchronous } }
        }
    });

    private sealed class CooperativeTool
    {
        public TaskCompletionSource Started { get; } = Signal();
        public TaskCompletionSource Release { get; } = Signal();
        public CancellationToken Token { get; private set; }
        public int Invocations;
        public FunctionDefinition Definition { get; }
        public CooperativeTool(bool nativeAsync = false)
        {
            Definition = FunctionBuilder.Create("lookup").WithAsync(nativeAsync)
                .WithHandler((_, token) => WaitAsync(token)).Build();
        }

        [AiFunction("lookup", "Looks up a value asynchronously.")]
        public async Task<LookupResult> LookupAsync(CancellationToken cancellationToken)
            => new(await WaitAsync(cancellationToken));

        private async Task<string> WaitAsync(CancellationToken token)
        {
            Token = token;
            Interlocked.Increment(ref Invocations);
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            return "released tool result";
        }
    }

    private sealed record LookupResult(string Value);

    private sealed class ThrowingTools
    {
        [AiFunction("sync_failure", "Fails synchronously.")]
        public string SyncFailure() => throw new InvalidOperationException("original tool failure");

        [AiFunction("async_failure", "Fails asynchronously.")]
        public async Task<string> AsyncFailure()
        {
            await Task.Yield();
            throw new InvalidOperationException("original tool failure");
        }
    }

    private sealed class QueueHandler(params (string Body, string ContentType)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(string Body, string ContentType)> _responses = new(responses);
        public List<string> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (_responses.Count == 0) throw new AssertFailedException("Unexpected extra provider request.");
            var response = _responses.Dequeue();
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(response.Body, Encoding.UTF8, response.ContentType) };
        }
    }

    private sealed class BatchService : AIService
    {
        public BatchService() : base("offline-test-key", "https://localhost/", new HttpClient()) { AddNewChat(); }
        public override string Provider => nameof(AIProvider.OpenAI);
        public Task<FunctionCallResultBatch> ExecuteAsync(FunctionCallBatch calls, FunctionCallingPolicy policy, CancellationToken token)
            => ProcessFunctionCallsAsync(calls, policy, token);
        public void Save(FunctionCallBatch calls, FunctionCallResultBatch results)
        {
            AddFunctionCallBatchToHistory(string.Empty, calls);
            AddFunctionResultBatchToHistory(results);
        }
        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult(string.Empty);
        protected override HttpRequestMessage CreateMessageRequest() => throw new NotSupportedException();
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync) => Task.CompletedTask;
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new NotSupportedException();
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new());
    }

    private sealed class SocketService(ScriptedSocket socket)
        : OpenAIService("offline-test-key", AIModels.OpenAI.Gpt6Astra, new HttpClient(new NoHttpHandler()))
    {
        protected override Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
            => Task.FromResult<WebSocket>(socket);
    }

    private sealed class NoHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("The test must use the scripted socket.");
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
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        { Abort(); return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        { Abort(); return Task.CompletedTask; }
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

    private const string OpenAIToolSse = """
        data: {"type":"response.output_item.done","output_index":0,"item":{"id":"item_0","type":"function_call","status":"completed","call_id":"call_0","name":"lookup","arguments":"{}"}}

        data: {"type":"response.output_item.done","output_index":1,"item":{"id":"item_1","type":"function_call","status":"completed","call_id":"call_1","name":"lookup","arguments":"{}"}}

        data: {"type":"response.completed","response":{"id":"resp_tools","status":"completed","output":[{"id":"item_0","type":"function_call","status":"completed","call_id":"call_0","name":"lookup","arguments":"{}"},{"id":"item_1","type":"function_call","status":"completed","call_id":"call_1","name":"lookup","arguments":"{}"}]}}

        data: [DONE]

        """;
}
