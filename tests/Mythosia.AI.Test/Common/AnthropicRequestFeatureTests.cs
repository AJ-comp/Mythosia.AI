using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicRequestFeatureTests
{
    private const string Answer = """{"id":"a1","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";
    private const string SearchBlocks = """
        [{"type":"server_tool_use","id":"s1","name":"web_search","input":{"query":"test"}},
        {"type":"web_search_tool_result","tool_use_id":"s1","content":[{"type":"web_search_result","url":"https://example.com/source","title":"Source","encrypted_content":"opaque-result"}]},
        {"type":"text","text":"answer","citations":[{"type":"web_search_result_location","url":"https://example.com/source","title":"Source","encrypted_index":"opaque-index","cited_text":"source text"}]}]
        """;

    [TestMethod]
    public async Task CommonReasoning_IsOneShot_AndUsesAdaptiveWithoutMutatingProviderSettings()
    {
        var (service, handler) = Create();
        await service.WithReasoning(ReasoningLevel.Low).GetCompletionAsync("first");
        await service.GetCompletionAsync("second");
        Assert.AreEqual("adaptive", Body(handler, 0).GetProperty("thinking").GetProperty("type").GetString());
        Assert.AreEqual("low", Body(handler, 0).GetProperty("output_config").GetProperty("effort").GetString());
        Assert.AreEqual("disabled", Body(handler, 1).GetProperty("thinking").GetProperty("type").GetString());
        Assert.AreEqual(-1, service.ThinkingBudget);
        Assert.AreEqual(ClaudeReasoningEffort.Auto, service.AdaptiveThinkingEffort);
    }

    [TestMethod]
    [DataRow("claude-fable-5")]
    [DataRow("claude-mythos-5")]
    [DataRow("claude-sonnet-5")]
    [DataRow("claude-opus-5-2")]
    public async Task UnsupportedPerMessageModels_FailBeforeHistoryOrHttp(string model)
    {
        var (service, handler) = Create(model);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("test"));
        Assert.AreEqual(0, handler.Bodies.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow("claude-opus-5")]
    [DataRow("claude-fable-5-1")]
    [DataRow("claude-mythos-5-1")]
    public async Task RequiredEffort_PreservesTopLevelAndPrefix_AndPersistsAcrossCalls(string model)
    {
        var (service, handler) = Create(model);
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("second");
        await service.GetCompletionAsync("third");
        var first = Body(handler, 0);
        var second = Body(handler, 1);
        var third = Body(handler, 2);
        Assert.AreEqual(first.GetProperty("thinking").GetRawText(), second.GetProperty("thinking").GetRawText());
        Assert.AreEqual(first.GetProperty("output_config").GetRawText(), second.GetProperty("output_config").GetRawText());
        Assert.AreEqual(first.GetProperty("messages")[0].GetRawText(), second.GetProperty("messages")[0].GetRawText());
        Assert.AreEqual(first.GetProperty("messages")[1].GetRawText(), second.GetProperty("messages")[1].GetRawText());
        Assert.AreEqual("low", LastEffort(second));
        Assert.AreEqual("low", LastEffort(third));
        Assert.IsTrue(handler.Betas.All(beta => beta.Contains("mid-conversation-output-config-2026-07-01")));
    }

    [TestMethod]
    public async Task OrdinaryOverrideInsidePreservedConversation_IsRequestScoped()
    {
        var (service, handler) = Create();
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        await service.WithReasoning(ReasoningLevel.Low).GetCompletionAsync("second");
        await service.GetCompletionAsync("third");
        Assert.AreEqual("low", LastEffort(Body(handler, 1)));
        Assert.AreEqual("high", LastEffort(Body(handler, 2)));
        Assert.AreEqual(Body(handler, 0).GetProperty("output_config").GetRawText(), Body(handler, 2).GetProperty("output_config").GetRawText());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedRequiredChange_RestoresAcceptedEffortAndUserMetadata(bool streaming)
    {
        var (service, handler) = Create(statuses: new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("accepted");
        var failed = new Message(ActorRole.User, "failed")
        {
            Metadata = new Dictionary<string, object> { ["application"] = "keep" }
        };

        await Assert.ThrowsAsync<AIServiceException>(async () =>
        {
            service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required);
            if (streaming)
            {
                await using var run = await service.StartRunAsync(failed);
                await run.Result;
            }
            else
                await service.GetCompletionAsync(failed);
        });

        var failedHistory = service.ActivateChat.Messages.Last(message => message.Role == ActorRole.User);
        Assert.AreEqual("keep", failedHistory.Metadata!["application"]);
        Assert.IsFalse(failedHistory.Metadata.ContainsKey("mythosia_claude_effort"));
        await service.GetCompletionAsync("after failure");
        Assert.AreEqual("high", LastEffort(Body(handler, 2)));
        Assert.AreEqual(Body(handler, 0).GetProperty("output_config").GetRawText(), Body(handler, 2).GetProperty("output_config").GetRawText());
        Assert.AreEqual("high", service.ActivateChat.Messages[0].Metadata!["mythosia_claude_effort"]);
    }

    [TestMethod]
    [DataRow(CachePreservation.None)]
    [DataRow(CachePreservation.Required)]
    public async Task FirstFailedRequest_DoesNotEstablishVerifiedReasoningBaseline(CachePreservation cache)
    {
        var (service, handler) = Create(statuses: new[] { HttpStatusCode.BadRequest });
        await Assert.ThrowsAsync<AIServiceException>(() =>
            service.WithReasoning(ReasoningLevel.High, cache).GetCompletionAsync("failed first request"));
        Assert.IsFalse(service.ActivateChat.Messages[0].Metadata?.ContainsKey("mythosia_claude_effort") == true);

        var imported = new Message(ActorRole.Assistant, "imported response");
        service.ActivateChat.Messages.Add(imported);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("unverified history"));
        Assert.AreEqual(1, handler.Bodies.Count);

        service.ActivateChat.Messages.Remove(imported);
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("retry");
        Assert.AreEqual("low", Body(handler, 1).GetProperty("output_config").GetProperty("effort").GetString());
        Assert.AreEqual("low", LastEffort(Body(handler, 1)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CanceledRequiredRun_RollsBackOnlyUnacceptedRounds(bool acceptedFirstRound)
    {
        var (service, handler) = Create(responses: new[] { Answer, NativeStream("accepted-pause", "pause_turn", true), Answer });
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        handler.WaitForCancellationOnRequest = acceptedFirstRound ? 3 : 2;
        await using (var run = await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required)
            .WithWebSearch().StartRunAsync("cancel this run"))
        {
            await handler.BlockedRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
            run.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        var canceledInput = service.ActivateChat.Messages.Single(message => message.Content == "cancel this run");
        Assert.AreEqual(acceptedFirstRound, canceledInput.Metadata?.ContainsKey("mythosia_claude_effort") == true);
        Assert.AreEqual("high", service.ActivateChat.Messages[0].Metadata!["mythosia_claude_effort"]);
        await service.GetCompletionAsync("continue");
        var next = Body(handler, handler.Bodies.Count - 1);
        Assert.AreEqual(acceptedFirstRound ? "low" : "high", LastEffort(next));
        Assert.AreEqual(Body(handler, 0).GetProperty("thinking").GetRawText(), next.GetProperty("thinking").GetRawText());
        if (acceptedFirstRound)
            Assert.IsTrue(next.GetRawText().Contains("opaque-result"));
    }

    [TestMethod]
    public async Task RequiredEffort_CanAdoptVerifiedAdaptiveBaseline()
    {
        var (service, handler) = Create();
        await service.WithReasoning(ReasoningLevel.High).GetCompletionAsync("first");
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("second");
        Assert.AreEqual(Body(handler, 0).GetProperty("thinking").GetRawText(), Body(handler, 1).GetProperty("thinking").GetRawText());
        Assert.AreEqual("high", Body(handler, 1).GetProperty("output_config").GetProperty("effort").GetString());
        Assert.AreEqual("low", LastEffort(Body(handler, 1)));
    }

    [TestMethod]
    public async Task PreservedHistory_CannotDropAssistantTurn()
    {
        var (service, handler) = Create();
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        service.ActivateChat.Messages.RemoveAt(1);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.GetCompletionAsync("second"));
        Assert.AreEqual(1, handler.Bodies.Count);
    }

    [TestMethod]
    public async Task RunRequestOverride_RetainsEffortMarkerAndOriginalHistory()
    {
        var (service, handler) = Create(responses: new[] { NativeStream("response", "end_turn", false) });
        await using var run = await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).StartRunAsync(
            "original", context: new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "augmented") });
        await run.Result;
        Assert.AreEqual("low", LastEffort(Body(handler, 0)));
        Assert.AreEqual("augmented", Body(handler, 0).GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.AreEqual("original", service.ActivateChat.Messages[0].Content);
    }

    [TestMethod]
    public async Task RequiredEffort_RejectsStatelessImportedHistoryAndDisabledBaseline()
    {
        var (service, handler) = Create();
        service.StatelessMode = true;
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("test"));
        service.StatelessMode = false;
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "imported"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("test"));
        service.ActivateChat.Messages.Clear();
        await service.GetCompletionAsync("ordinary");
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("test"));
        Assert.AreEqual(1, handler.Bodies.Count);
    }

    [TestMethod]
    public async Task PreservedConversation_RejectsModelChange_AndClearResetsBaseline()
    {
        var (service, handler) = Create();
        await service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        service.ChangeModel("claude-fable-5-1");
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.GetCompletionAsync("invalid"));
        service.ActivateChat.Messages.Clear();
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("fresh");
        Assert.AreEqual(2, handler.Bodies.Count);
        Assert.AreEqual("low", Body(handler, 1).GetProperty("output_config").GetProperty("effort").GetString());
    }

    [TestMethod]
    public async Task NativeSearch_PreservesEncryptedContentAndCitationsOnLaterCall()
    {
        var searchResponse = "{\"id\":\"a-search\",\"content\":" + SearchBlocks + ",\"stop_reason\":\"end_turn\"}";
        var (service, handler) = Create(responses: new[] { searchResponse, Answer });
        await service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } }).GetCompletionAsync("first");
        Assert.AreEqual("web_search_20250305", Body(handler, 0).GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.AreEqual("example.com", Body(handler, 0).GetProperty("tools")[0].GetProperty("allowed_domains")[0].GetString());
        Assert.AreEqual(0, service.Functions.Count);
        var citation = service.LastCitations.Single();
        Assert.AreEqual("https://example.com/source", citation.Url);
        Assert.AreEqual("a-search", citation.ResponseId);
        Assert.AreEqual(2, citation.ContentIndex);
        await service.GetCompletionAsync("second");
        var content = Body(handler, 1).GetProperty("messages")[1].GetProperty("content");
        Assert.AreEqual("opaque-result", content[1].GetProperty("content")[0].GetProperty("encrypted_content").GetString());
        Assert.AreEqual("opaque-index", content[2].GetProperty("citations")[0].GetProperty("encrypted_index").GetString());
    }

    [TestMethod]
    public async Task NativePauseTurn_ResumesWithoutExecutingServerToolLocally()
    {
        var pause = "{\"id\":\"pause\",\"content\":" + SearchBlocks + ",\"stop_reason\":\"pause_turn\"}";
        var (service, handler) = Create(responses: new[] { pause, Answer });
        Assert.AreEqual("answer", await service.WithWebSearch().GetCompletionAsync("test"));
        Assert.AreEqual(2, handler.Bodies.Count);
        Assert.AreEqual(3, Body(handler, 1).GetProperty("messages")[1].GetProperty("content").GetArrayLength());
    }

    [TestMethod]
    public async Task NativeAndClientTools_AreMergedAndBothContextsAreReplayed()
    {
        const string response = """
            {"content":[{"type":"server_tool_use","name":"web_search","id":"s1","input":{"query":"test"}},
            {"type":"tool_use","id":"c1","name":"local","input":{}}],"stop_reason":"tool_use"}
            """;
        var (service, handler) = Create(responses: new[] { response, Answer });
        var executed = 0;
        service.Functions.Add(new FunctionDefinition { Name = "local", Description = "Local", Handler = _ => { executed++; return Task.FromResult("done"); } });
        await service.WithWebSearch().GetCompletionAsync("test");
        Assert.AreEqual(1, executed);
        Assert.AreEqual(2, Body(handler, 0).GetProperty("tools").GetArrayLength());
        Assert.AreEqual("server_tool_use", Body(handler, 1).GetProperty("messages")[1].GetProperty("content")[0].GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task NativeRun_PauseContinuationAndCitationsWorkWithoutObserver()
    {
        var (service, handler) = Create(responses: new[]
        {
            NativeStream("pause-turn", "pause_turn", true),
            NativeStream("final-turn", "end_turn", false)
        });
        await using var run = await service.WithWebSearch().StartRunAsync("test");
        Assert.AreEqual("answeranswer", await run.Result);
        Assert.AreEqual(2, handler.Bodies.Count);
        Assert.AreEqual(1, service.LastCitations.Count);
        Assert.AreEqual("opaque-result", Body(handler, 1).GetProperty("messages")[1].GetProperty("content")[1].GetProperty("content")[0].GetProperty("encrypted_content").GetString());
    }

    [TestMethod]
    public async Task FileSearchIsUnsupported_ButDisabledClientFunctionsDoNotDisableNativeSearch()
    {
        var (service, handler) = Create();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithFileSearch(new FileSearchStore("Google", "fileSearchStores/test")).GetCompletionAsync("test"));
        service.FunctionsDisabled = true;
        Assert.AreEqual("answer", await service.WithWebSearch().GetCompletionAsync("test"));
        Assert.AreEqual(1, handler.Bodies.Count);
        Assert.AreEqual("web_search", Body(handler, 0).GetProperty("tools")[0].GetProperty("name").GetString());
    }

    private static string NativeStream(string id, string stopReason, bool search)
    {
        var events = new List<object> { new { type = "message_start", message = new { id, model = "claude-opus-5" } } };
        if (search)
        {
            events.Add(new { type = "content_block_start", index = 0, content_block = new { type = "server_tool_use", id = "s1", name = "web_search", input = new { } } });
            events.Add(new { type = "content_block_delta", index = 0, delta = new { type = "input_json_delta", partial_json = "{\"query\":\"test\"}" } });
            events.Add(new { type = "content_block_stop", index = 0 });
            events.Add(new { type = "content_block_start", index = 1, content_block = JsonSerializer.Deserialize<JsonElement>(SearchBlocks)[1] });
            events.Add(new { type = "content_block_stop", index = 1 });
        }
        var textIndex = search ? 2 : 0;
        events.Add(new { type = "content_block_start", index = textIndex, content_block = new { type = "text", text = "" } });
        events.Add(new { type = "content_block_delta", index = textIndex, delta = new { type = "text_delta", text = "answer" } });
        if (search)
            events.Add(new { type = "content_block_delta", index = textIndex, delta = new { type = "citations_delta", citation = JsonSerializer.Deserialize<JsonElement>(SearchBlocks)[2].GetProperty("citations")[0] } });
        events.Add(new { type = "content_block_stop", index = textIndex });
        events.Add(new { type = "message_delta", delta = new { stop_reason = stopReason } });
        events.Add(new { type = "message_stop" });
        return string.Join("", events.Select(item => "data: " + JsonSerializer.Serialize(item) + "\n\n"));
    }

    private static string? LastEffort(JsonElement body) =>
        body.GetProperty("messages").EnumerateArray().Last(message => message.GetProperty("role").GetString() == "system").GetProperty("output_config").GetProperty("effort").GetString();

    private static (AnthropicService Service, CaptureHandler Handler) Create(string model = "claude-opus-5", string[]? responses = null, HttpStatusCode[]? statuses = null)
    {
        var handler = new CaptureHandler(responses ?? new[] { Answer }, statuses);
        return (new AnthropicService("offline-key", model, new HttpClient(handler)), handler);
    }

    private static JsonElement Body(CaptureHandler handler, int index) => JsonSerializer.Deserialize<JsonElement>(handler.Bodies[index]);

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        private readonly Queue<HttpStatusCode> _statuses;
        public List<string> Bodies { get; } = new();
        public List<string> Betas { get; } = new();
        public int? WaitForCancellationOnRequest { get; set; }
        public TaskCompletionSource BlockedRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CaptureHandler(IEnumerable<string> responses, IEnumerable<HttpStatusCode>? statuses = null)
        {
            _responses = new Queue<string>(responses);
            _statuses = new Queue<HttpStatusCode>(statuses ?? Array.Empty<HttpStatusCode>());
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Betas.Add(request.Headers.TryGetValues("anthropic-beta", out var betas) ? string.Join(",", betas) : "");
            var response = _responses.Count > 0 ? _responses.Dequeue() : Answer;
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            if (Bodies.Count == WaitForCancellationOnRequest)
            {
                BlockedRequest.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(response, Encoding.UTF8, response.StartsWith("data:") ? "text/event-stream" : "application/json")
            };
        }
    }
}
