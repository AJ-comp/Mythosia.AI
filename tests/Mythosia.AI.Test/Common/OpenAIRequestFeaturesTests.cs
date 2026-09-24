using Mythosia.AI.Extensions;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIRequestFeaturesTests
{
    private const string Answer = """
        {"id":"resp_answer","status":"completed","output_text":"answer","output":[{"id":"msg_answer","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"answer","annotations":[]}]}]}
        """;
    private const string Sources = """
        {"id":"resp_sources","status":"completed","output_text":"grounded","output":[
          {"id":"ws_1","type":"web_search_call","status":"completed","action":{"type":"search","query":"reference"}},
          {"id":"msg_sources","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"grounded","annotations":[
            {"type":"url_citation","start_index":0,"end_index":8,"url":"https://example.com/reference","title":"Reference"},
            {"type":"file_citation","index":8,"file_id":"file_reference","filename":"reference.pdf"},
            {"type":"future_annotation","value":"ignored"}
          ]}]}
        ]}
        """;

    [TestMethod]
    public async Task Reasoning_OverridesOneLogicalRequestAndRestoresProviderDefaults()
    {
        var handler = new CaptureHandler(Answer, Answer);
        var service = CreateService(handler);
        await service.WithReasoning(ReasoningLevel.High).GetCompletionAsync(new Message(ActorRole.User, "reason"));
        await service.GetCompletionAsync("next");
        Assert.AreEqual("high", Effort(handler.Requests[0]));
        Assert.AreEqual("medium", Effort(handler.Requests[1]));
    }

    [TestMethod]
    [DataRow("gpt-6-astra", ReasoningLevel.None)]
    [DataRow("gpt-4.1", ReasoningLevel.High)]
    [DataRow("gpt-5-pro", ReasoningLevel.Low)]
    public async Task UnsupportedReasoning_FailsBeforeNetworkOrHistoryMutation(string model, ReasoningLevel level)
    {
        var handler = new CaptureHandler(Answer);
        var service = CreateService(handler, model);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.WithReasoning(level).GetCompletionAsync("invalid"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task CachePreservation_KeepsOriginalEffortAndReplaysUpdatesAtTheirHistoryPositions(string model)
    {
        var handler = new CaptureHandler(Answer, Answer, Answer, Answer);
        var service = CreateService(handler, model);
        await service.WithReasoning(ReasoningLevel.Low).GetCompletionAsync("first");
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("second");
        await service.GetCompletionAsync("third");
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("fourth");

        Assert.IsTrue(handler.Requests.All(request => Effort(request) == "low"));
        Assert.AreEqual(0, Updates(handler.Requests[0]).Count);
        Assert.AreEqual("disabled", handler.Requests[1].GetProperty("truncation").GetString());
        AssertUpdateBefore(handler.Requests[1], "high", "second");
        AssertUpdateBefore(handler.Requests[2], "high", "second");
        Assert.AreEqual(1, Updates(handler.Requests[2]).Count);
        AssertUpdateBefore(handler.Requests[3], "high", "second");
        AssertUpdateBefore(handler.Requests[3], "low", "fourth");
        Assert.AreEqual(2, Updates(handler.Requests[3]).Count);
        Assert.AreEqual("msg_answer", handler.Requests[3].GetProperty("input")[1].GetProperty("id").GetString(),
            "Original provider output items, including IDs and annotations, must survive replay.");
    }

    [TestMethod]
    public async Task CachePreservation_TemporaryReasoningRestoresPersistentLevelOnNextRequest()
    {
        var handler = new CaptureHandler(Answer, Answer, Answer);
        var service = CreateService(handler);
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("persistent");
        await service.WithReasoning(ReasoningLevel.Low).GetCompletionAsync("temporary");
        await service.GetCompletionAsync("restore");
        Assert.IsTrue(handler.Requests.All(request => Effort(request) == "high"));
        Assert.AreEqual(0, Updates(handler.Requests[0]).Count);
        AssertUpdateBefore(handler.Requests[1], "low", "temporary");
        AssertUpdateBefore(handler.Requests[2], "high", "restore");
        Assert.AreEqual(2, Updates(handler.Requests[2]).Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClearedHistory_StartsFreshBaselineWithoutAConfigurationUpdate(bool previouslyPreserving)
    {
        var handler = new CaptureHandler(Answer, Answer);
        var service = CreateService(handler);
        await service.WithReasoning(ReasoningLevel.Low, previouslyPreserving ? CachePreservation.Required : CachePreservation.None)
            .GetCompletionAsync("old");
        service.ActivateChat.Messages.Clear();
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("fresh");
        Assert.AreEqual("high", Effort(handler.Requests[1]));
        Assert.AreEqual(0, Updates(handler.Requests[1]).Count);
        Assert.AreEqual(1, handler.Requests[1].GetProperty("input").GetArrayLength());
    }

    [TestMethod]
    public async Task PreservedHistory_RejectsTruncationAndModelChangesBeforeSending()
    {
        var handler = new CaptureHandler(Answer);
        var service = CreateService(handler);
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("first");
        service.ActivateChat.Messages.RemoveAt(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCompletionAsync("truncated"));
        Assert.AreEqual(1, handler.Requests.Count);
        service.ChangeModel("gpt-5.4");
        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync("changed"));
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task RequiredCache_RejectsProStatelessAndImportedHistoryBeforeSending()
    {
        var handler = new CaptureHandler(Answer);
        var service = CreateService(handler);
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;
        await Assert.ThrowsAsync<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("pro"));
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Standard;
        service.StatelessMode = true;
        await Assert.ThrowsAsync<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("stateless"));
        service.StatelessMode = false;
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "imported"));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("imported"));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task HostedSearch_MergesClientFunctionsAndSnapshotsOptionsForOneRequest()
    {
        var handler = new CaptureHandler(Sources, Answer);
        var service = CreateService(handler);
        var domains = new[] { "example.com" };
        var executions = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup", Description = "Read a local value", Handler = _ => { executions++; return Task.FromResult("value"); }
        });
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = domains })
            .WithFileSearch(new FileSearchStore("OpenAI", "vs_reference"));
        domains[0] = "changed.example";
        Assert.AreEqual("grounded", await service.GetCompletionAsync("research"));
        var tools = handler.Requests[0].GetProperty("tools").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(new[] { "function", "web_search", "file_search" }, tools.Select(Type).ToArray());
        Assert.AreEqual("example.com", tools.Single(tool => Type(tool) == "web_search").GetProperty("filters").GetProperty("allowed_domains")[0].GetString());
        Assert.AreEqual("vs_reference", tools.Single(tool => Type(tool) == "file_search").GetProperty("vector_store_ids")[0].GetString());
        Assert.AreEqual(0, executions, "Hosted searches must never dispatch a client function.");
        await service.GetCompletionAsync("next");
        Assert.AreEqual(1, handler.Requests[1].GetProperty("tools").GetArrayLength());
    }

    [TestMethod]
    public async Task HostedSearch_RemainsAvailableWhenClientFunctionsAreDisabled()
    {
        var handler = new CaptureHandler(Answer);
        var service = CreateService(handler);
        service.FunctionsDisabled = true;
        await service.WithWebSearch().GetCompletionAsync("search");
        Assert.AreEqual("web_search", Type(handler.Requests[0].GetProperty("tools")[0]));
    }

    [TestMethod]
    public async Task HostedSearch_RejectsUnsupportedModelsForeignStoresAndMalformedDomainsBeforeSending()
    {
        var handler = new CaptureHandler(Answer);
        var service = CreateService(handler, "gpt-4o");
        await Assert.ThrowsAsync<NotSupportedException>(() => service.WithWebSearch().GetCompletionAsync("legacy"));
        service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.WithFileSearch(new FileSearchStore("Google", "vs_foreign")).GetCompletionAsync("foreign"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.WithFileSearch(new FileSearchStore("OpenAI", "file_wrong")).GetCompletionAsync("wrong identifier"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "https://example.com/path" } }).GetCompletionAsync("bad domain"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task Citations_NormalizeUrlAndFileAnnotationsWithProviderLocalPositions()
    {
        var handler = new CaptureHandler(Sources, Answer);
        var service = CreateService(handler, AIModels.OpenAI.Gpt4_1);
        await service.WithWebSearch().WithFileSearch(new FileSearchStore("OpenAI", "vs_reference")).GetCompletionAsync("research");
        Assert.AreEqual(2, service.LastCitations.Count);
        var url = service.LastCitations[0];
        Assert.AreEqual("OpenAI", url.Provider);
        Assert.AreEqual("https://example.com/reference", url.Url);
        Assert.AreEqual("Reference", url.Title);
        Assert.AreEqual("resp_sources", url.ResponseId);
        Assert.AreEqual(1, url.OutputIndex);
        Assert.AreEqual(0, url.ContentIndex);
        Assert.AreEqual(0, url.StartIndex);
        Assert.AreEqual(8, url.EndIndex);
        var file = service.LastCitations[1];
        Assert.AreEqual("file_reference", file.FileId);
        Assert.AreEqual("reference.pdf", file.Title);
        Assert.IsNull(file.StartIndex, "A file citation's index is not a character offset.");
        Assert.IsNull(file.EndIndex);
        url.Title = "caller mutation";
        Assert.AreEqual("Reference", service.LastCitations[0].Title);
        await service.GetCompletionAsync("next");
        Assert.AreEqual(0, service.LastCitations.Count);
        Assert.AreEqual("web_search_call", Type(handler.Requests[1].GetProperty("input")[1]),
            "Hosted search output must remain in subsequent conversation replay.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Streaming_CitationsAreRetainedAndDeduplicatedAcrossIncrementalAndCompletedEvents(bool textOnly)
    {
        var handler = new CaptureHandler(ToSse(SourceEvents()));
        var service = CreateService(handler);
        var events = new List<StreamingContent>();
        await foreach (var item in service.WithWebSearch().StreamAsync("search", textOnly ? StreamOptions.TextOnlyOptions : StreamOptions.WithFunctions))
            events.Add(item);
        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error), JsonSerializer.Serialize(events));
        Assert.AreEqual(2, service.LastCitations.Count, JsonSerializer.Serialize(events));
        if (!textOnly)
        {
            Assert.AreEqual(2, events.Count(item => item.Type == StreamingContentType.Citation));
            Assert.IsTrue(events.Any(item => item.Type == StreamingContentType.Status && item.Metadata?.GetValueOrDefault("tool_type")?.ToString() == "web_search"));
        }
        Assert.AreEqual("grounded", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
    }

    [TestMethod]
    public async Task StructuredCompletion_RetainsHostedSearchDuringFormatRepair()
    {
        var valid = Answer.Replace("answer", "{\\\"Value\\\":7}", StringComparison.Ordinal);
        var handler = new CaptureHandler(Answer, valid);
        var service = CreateService(handler);
        var result = await service.WithWebSearch().GetCompletionAsync<StructuredValue>("Return the value as JSON");
        Assert.AreEqual(7, result.Value);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(request => request.GetProperty("tools").EnumerateArray().Any(tool => Type(tool) == "web_search")));
    }

    [TestMethod]
    public async Task Run_CapturesCitationsWithoutAStreamReaderAndSendsHostedToolsOverWebSocket()
    {
        using var socket = new ScriptedSocket();
        socket.OnSend = _ => socket.Push(SourceEvents());
        var service = new SocketService(socket);
        await using var run = await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required)
            .WithWebSearch().WithFileSearch(new FileSearchStore("OpenAI", "vs_reference"))
            .StartRunAsync("research");
        Assert.AreEqual("grounded", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(2, run.Citations.Count);
        Assert.AreEqual(2, service.LastCitations.Count);
        var payload = socket.Sent.Single();
        Assert.AreEqual("response.create", Type(payload));
        Assert.AreEqual("high", Effort(payload));
        Assert.AreEqual(2, payload.GetProperty("tools").GetArrayLength());
        Assert.AreEqual(0, Updates(payload).Count);
    }

    [TestMethod]
    public async Task Run_FunctionContinuationKeepsHostedToolsAndOriginalEffortWithoutReplayingConfigurationUpdates()
    {
        using var socket = new ScriptedSocket();
        var handler = new CaptureHandler(Answer);
        var service = new SocketService(socket, handler);
        await service.WithReasoning(ReasoningLevel.Low).GetCompletionAsync("baseline");
        var executions = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup", Description = "Read a local value", Handler = _ => { executions++; return Task.FromResult("local"); }
        });
        socket.OnSend = _ =>
        {
            if (socket.Sent.Count == 1)
                socket.Push(
                    """{"type":"response.created","response":{"id":"resp_tool","status":"in_progress"}}""",
                    """{"type":"response.completed","response":{"id":"resp_tool","status":"completed","output":[{"id":"fc_lookup","type":"function_call","status":"completed","call_id":"call_lookup","name":"lookup","arguments":"{}"}]}}""");
            else socket.Push(SourceEvents());
        };
        await using var run = await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).WithWebSearch().StartRunAsync("research");
        Assert.AreEqual("grounded", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(1, executions);
        Assert.AreEqual(2, socket.Sent.Count);
        AssertUpdateBefore(socket.Sent[0], "high", "research");
        Assert.IsTrue(socket.Sent.All(payload => Effort(payload) == "low"));
        Assert.AreEqual("resp_tool", socket.Sent[1].GetProperty("previous_response_id").GetString());
        var continuation = socket.Sent[1].GetProperty("input");
        Assert.AreEqual(1, continuation.GetArrayLength());
        Assert.AreEqual("function_call_output", Type(continuation[0]));
        Assert.AreEqual("call_lookup", continuation[0].GetProperty("call_id").GetString());
        Assert.IsTrue(socket.Sent.All(payload => payload.GetProperty("tools").EnumerateArray().Any(tool => Type(tool) == "web_search")));
        Assert.AreEqual(2, run.Citations.Count);
    }

    [TestMethod]
    public async Task CachePreservation_SuppressesAutomaticConversationCompaction()
    {
        var handler = new CaptureHandler(Answer, Answer);
        var service = CreateService(handler);
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("first");
        service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 2, keepRecentCount: 1);
        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetCompletionAsync("second");
        Assert.AreEqual(2, handler.Requests.Count, "No summary request may rewrite the preserved history.");
        Assert.AreEqual(4, service.ActivateChat.Messages.Count);
        Assert.AreEqual(3, handler.Requests[1].GetProperty("input").GetArrayLength());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedCacheChange_DoesNotReplaceTheAcceptedPersistentSetting(bool streaming)
    {
        const string failed = """{"id":"resp_failed","status":"failed","error":{"code":"server_error","message":"failed"},"output":[]}""";
        var response = streaming ? ToSse(new[] { "{\"type\":\"response.failed\",\"response\":" + failed + "}" }) : failed;
        var handler = new CaptureHandler(Answer, response, Answer);
        var service = CreateService(handler);
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("baseline");
        if (streaming)
        {
            var events = new List<StreamingContent>();
            await foreach (var item in service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).StreamAsync("failed change", StreamOptions.WithFunctions))
                events.Add(item);
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Error));
        }
        else
            await Assert.ThrowsAsync<AIServiceException>(() => service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("failed change"));

        await service.GetCompletionAsync("keep accepted effort");
        Assert.AreEqual("low", Effort(handler.Requests[2]));
        Assert.AreEqual(0, Updates(handler.Requests[2]).Count, "A rejected configuration update must not appear in future history.");
    }

    [TestMethod]
    public async Task FailedFirstCacheRequest_DoesNotEstablishAConversationBaseline()
    {
        var handler = new CaptureHandler("{invalid-json", Answer);
        var service = CreateService(handler);
        await Assert.ThrowsAsync<AIServiceException>(() => service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("failed"));
        await service.GetCompletionAsync("ordinary");
        Assert.AreEqual("medium", Effort(handler.Requests[1]));
        Assert.AreEqual(0, Updates(handler.Requests[1]).Count);
        Assert.IsFalse(handler.Requests[1].TryGetProperty("truncation", out _));
    }

    [TestMethod]
    [DataRow("gpt-4o")]
    [DataRow("gpt-6-astra")]
    public async Task MalformedHttpResponseWithoutFeatures_PreservesExistingErrorContract(string model)
    {
        var handler = new CaptureHandler("{invalid-json");
        var service = CreateService(handler, model);
        await Assert.ThrowsAsync<AIServiceException>(() => service.GetCompletionAsync("invalid"));
        Assert.AreEqual(0, service.LastCitations.Count);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    private static OpenAIService CreateService(CaptureHandler handler, string model = AIModels.OpenAI.Gpt6Astra)
        => new("offline-test-key", model, new HttpClient(handler));
    private static string? Type(JsonElement element) => element.TryGetProperty("type", out var type) ? type.GetString() : null;
    private static string? Effort(JsonElement request) => request.GetProperty("reasoning").GetProperty("effort").GetString();
    private static List<JsonElement> Updates(JsonElement request) => request.GetProperty("input").EnumerateArray().Where(item => Type(item) == "configuration_update").ToList();
    private static void AssertUpdateBefore(JsonElement request, string effort, string userText)
    {
        var input = request.GetProperty("input").EnumerateArray().ToArray();
        var index = Array.FindIndex(input, item => Type(item) == "configuration_update" && item.GetProperty("reasoning").GetProperty("effort").GetString() == effort);
        Assert.IsTrue(index >= 0 && index + 1 < input.Length);
        Assert.AreEqual("user", input[index + 1].GetProperty("role").GetString());
        Assert.AreEqual(userText, input[index + 1].GetProperty("content")[0].GetProperty("text").GetString());
    }
    private static string[] SourceEvents()
    {
        using var document = JsonDocument.Parse(Sources);
        var output = document.RootElement.GetProperty("output")[1];
        return new[]
        {
            """{"type":"response.created","response":{"id":"resp_sources","status":"in_progress"}}""",
            """{"type":"response.web_search_call.searching","item_id":"ws_1","output_index":0}""",
            """{"type":"response.output_text.delta","item_id":"msg_sources","output_index":1,"content_index":0,"delta":"grounded"}""",
            JsonSerializer.Serialize(new { type = "response.output_text.annotation.added", item_id = "msg_sources", output_index = 1, content_index = 0, annotation_index = 0, annotation = output.GetProperty("content")[0].GetProperty("annotations")[0] }),
            JsonSerializer.Serialize(new { type = "response.output_item.done", output_index = 1, item = output }),
            JsonSerializer.Serialize(new { type = "response.completed", response = document.RootElement })
        };
    }
    private static string ToSse(IEnumerable<string> events) => string.Join("\n\n", events.Select(item => "data: " + item)) + "\n\n";
    public sealed class StructuredValue { public int Value { get; set; } }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        public List<JsonElement> Requests { get; } = new();
        public CaptureHandler(params string[] responses) { _responses = new Queue<string>(responses); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(document.RootElement.Clone());
            Assert.IsTrue(_responses.Count > 0, "Unexpected extra HTTP request.");
            var content = _responses.Dequeue();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, content.StartsWith("data:") ? "text/event-stream" : "application/json") };
        }
    }
    private sealed class SocketService : OpenAIService
    {
        private readonly ScriptedSocket _socket;
        public SocketService(ScriptedSocket socket, CaptureHandler? handler = null) : base("offline-test-key", AIModels.OpenAI.Gpt6Astra, new HttpClient(handler ?? new CaptureHandler())) { _socket = socket; }
        protected override Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken) => Task.FromResult<WebSocket>(_socket);
    }
    private sealed class ScriptedSocket : WebSocket
    {
        private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
        private readonly CancellationTokenSource _aborted = new();
        private WebSocketState _state = WebSocketState.Open;
        public List<JsonElement> Sent { get; } = new();
        public Action<JsonElement>? OnSend { get; set; }
        public void Push(params string[] events) { foreach (var item in events) _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(item)); }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { _state = WebSocketState.Aborted; _aborted.Cancel(); }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
        public override void Dispose() { Abort(); }
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
