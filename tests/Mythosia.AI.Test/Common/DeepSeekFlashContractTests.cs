using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class DeepSeekFlashContractTests
{
    private const string ReasoningKey = "deepseek_reasoning_content";

    [TestMethod]
    [DataRow(DeepSeekReasoning.Auto, null)]
    [DataRow(DeepSeekReasoning.Low, "low")]
    [DataRow(DeepSeekReasoning.High, "high")]
    [DataRow(DeepSeekReasoning.Max, "max")]
    public async Task NativeEffort_IsPersistentAndUsesDocumentedSampling(DeepSeekReasoning effort, string? wire)
    {
        using var fixture = new Fixture();
        Assert.AreSame(fixture.Service, fixture.Service.WithDeepSeekReasoning(effort));
        fixture.Service.TopP = 0.1f;
        fixture.Service.Temperature = 0.2f;
        fixture.Service.FrequencyPenalty = 0.4f;
        fixture.Service.PresencePenalty = 0.3f;
        await fixture.Service.GetCompletionAsync("first");
        await fixture.Service.GetCompletionAsync("second");
        foreach (var request in fixture.Handler.Requests)
        {
            AssertThinking(request, true, wire);
            Assert.AreEqual(0.95f, request["top_p"]!.GetValue<float>());
            Assert.IsFalse(request.ContainsKey("temperature"));
            Assert.IsFalse(request.ContainsKey("frequency_penalty"));
            Assert.IsFalse(request.ContainsKey("presence_penalty"));
            Assert.AreEqual(8000, request["max_tokens"]!.GetValue<int>());
            Assert.IsTrue(Messages(request).Where(message => message["role"]?.GetValue<string>() == "assistant")
                .All(message => !message.ContainsKey("reasoning_content")), "Requests without tools omit stored reasoning on the wire.");
        }
        Assert.AreEqual(effort, fixture.Service.ReasoningEffort);
        Assert.IsTrue(fixture.Service.ThinkingEnabled);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.Auto, false, null)]
    [DataRow(ReasoningLevel.None, false, null)]
    [DataRow(ReasoningLevel.Minimal, true, "low")]
    [DataRow(ReasoningLevel.Low, true, "low")]
    [DataRow(ReasoningLevel.Medium, true, "high")]
    [DataRow(ReasoningLevel.High, true, "high")]
    [DataRow(ReasoningLevel.XHigh, true, "high")]
    [DataRow(ReasoningLevel.Max, true, "max")]
    public async Task CommonReasoning_IsOneRequestAndDoesNotMutateProviderDefaults(ReasoningLevel level, bool thinking, string? effort)
    {
        using var fixture = new Fixture();
        fixture.Service.WithReasoning(level);
        await fixture.Service.GetCompletionAsync("configured");
        AssertThinking(fixture.Handler.Requests[0], thinking, effort);
        Assert.IsFalse(fixture.Service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.Auto, fixture.Service.ReasoningEffort);
        await fixture.Service.GetCompletionAsync("ordinary");
        AssertThinking(fixture.Handler.Requests[1], false, null);
        Assert.IsFalse(fixture.Handler.Requests[1].ContainsKey("top_p"));
    }

    [TestMethod]
    public async Task CommonAuto_PreservesEnabledNativeEffort()
    {
        using var fixture = new Fixture();
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.Max).WithReasoning(ReasoningLevel.Auto);
        await fixture.Service.GetCompletionAsync("question");
        AssertThinking(fixture.Handler.Requests.Single(), true, "max");
    }

    [TestMethod]
    public async Task ExplicitLargeBudget_UsesTheDocumented384KOutputLimit()
    {
        using var fixture = new Fixture();
        fixture.Service.MaxTokens = uint.MaxValue;
        await fixture.Service.GetCompletionAsync("question");
        Assert.AreEqual(393216, fixture.Handler.Requests.Single()["max_tokens"]!.GetValue<int>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RequestContext_EnrichesOnlyOriginalUserAcrossToolRounds(bool streaming)
    {
        using var fixture = new Fixture(
            Reply("", "first-thought", Call("a", "first", "{}")),
            Reply("", "second-thought", Call("b", "second", "{}")), Reply("done", "final-thought"));
        fixture.Service.WithDeepSeekReasoning();
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "first", Handler = _ => Task.FromResult("first-result") });
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "second", Handler = _ => Task.FromResult("second-result") });
        var context = new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "synthetic retrieved context and original question") };
        if (!streaming) Assert.AreEqual("done", await fixture.Service.GetCompletionAsync("original question", context: context));
        else
        {
            var text = new StringBuilder();
            await foreach (var item in fixture.Service.StreamAsync(new Message(ActorRole.User, "original question"), StreamOptions.FullOptions, context))
                if (item.Type == StreamingContentType.Text) text.Append(item.Content);
            Assert.AreEqual("done", text.ToString());
        }
        Assert.AreEqual(3, fixture.Handler.Requests.Count);
        for (var index = 0; index < 3; index++)
        {
            var messages = Messages(fixture.Handler.Requests[index]).ToArray();
            Assert.AreEqual(context.RequestMessageOverride.Content, messages.Single(message => message["role"]?.GetValue<string>() == "user")["content"]?.GetValue<string>());
            var results = messages.Where(message => message["role"]?.GetValue<string>() == "tool").ToArray();
            Assert.AreEqual(index, results.Length);
            CollectionAssert.AreEqual(new[] { "a", "b" }.Take(index).ToArray(), results.Select(message => message["tool_call_id"]!.GetValue<string>()).ToArray());
            CollectionAssert.AreEqual(new[] { "first-result", "second-result" }.Take(index).ToArray(), results.Select(message => message["content"]!.GetValue<string>()).ToArray());
        }
        Assert.AreEqual("original question", fixture.Service.ActivateChat.Messages.First().Content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AllowAsyncFunction_RemainsAnOrdinaryClientToolOnDeepSeek(bool run)
    {
        using var fixture = new Fixture(Reply("", "tool-thought", Call("ordinary-call", "tool", "{}")), Reply("done", "final-thought"));
        var executed = 0;
        fixture.Service.Functions.Add(new FunctionDefinition
        {
            Name = "tool", AllowAsync = true,
            Handler = _ => { executed++; return Task.FromResult("ordinary-result"); }
        });
        Assert.AreEqual("done", await Execute(fixture.Service, "use tool", run));
        Assert.AreEqual(1, executed);
        Assert.AreEqual(2, fixture.Handler.Requests.Count);
        foreach (var request in fixture.Handler.Requests)
        {
            Assert.IsFalse(request.ToJsonString().Contains("allow_async", StringComparison.Ordinal));
            Assert.IsFalse(request.ToJsonString().Contains("async_tool", StringComparison.Ordinal));
            Assert.IsFalse(request.ContainsKey("background"));
        }
        var result = Messages(fixture.Handler.Requests[1]).Single(message => message["role"]?.GetValue<string>() == "tool");
        Assert.AreEqual("ordinary-call", result["tool_call_id"]?.GetValue<string>());
        Assert.AreEqual("ordinary-result", result["content"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tools_ReplayAllAssistantReasoningAcrossBatchesAndLaterUserTurns(bool run)
    {
        using var fixture = new Fixture(
            Reply("", "thought-first", Call("call-a", "first", "{}")),
            Reply("", "thought-second", Call("call-b", "second", "{\"value\":\"first-result\"}")),
            Reply("finished", "thought-final"), Reply("follow-up", "thought-follow-up"));
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.High);
        var order = new List<string>();
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "first", Handler = _ => { order.Add("first"); return Task.FromResult("first-result"); } });
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "second", Handler = args => { Assert.AreEqual("first-result", args["value"].ToString()); order.Add("second"); return Task.FromResult("second-result"); } });
        Assert.AreEqual("finished", await Execute(fixture.Service, "use the tools", run));
        Assert.AreEqual("follow-up", await Execute(fixture.Service, "new user question", run));
        CollectionAssert.AreEqual(new[] { "first", "second" }, order);
        Assert.AreEqual(4, fixture.Handler.Requests.Count);
        var assistants = Messages(fixture.Handler.Requests[3]).Where(message => message["role"]?.GetValue<string>() == "assistant").ToArray();
        CollectionAssert.AreEqual(new[] { "thought-first", "thought-second", "thought-final" }, assistants.Select(message => message["reasoning_content"]?.GetValue<string>()).ToArray());
        Assert.AreEqual("call-a", assistants[0]["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.AreEqual("call-b", assistants[1]["tool_calls"]![0]!["id"]!.GetValue<string>());
        var results = Messages(fixture.Handler.Requests[3]).Where(message => message["role"]?.GetValue<string>() == "tool").ToArray();
        CollectionAssert.AreEqual(new[] { "call-a", "call-b" }, results.Select(message => message["tool_call_id"]!.GetValue<string>()).ToArray());
        Assert.AreEqual("thought-final", fixture.Service.ActivateChat.Messages.Where(message => message.Role == ActorRole.Assistant).Last(message => message.Content == "finished").Metadata![ReasoningKey]);
        foreach (var request in fixture.Handler.Requests)
        {
            AssertThinking(request, true, "high");
            Assert.AreEqual(8000, request["max_tokens"]!.GetValue<int>());
        }
    }

    [TestMethod]
    [DataRow(FunctionExecutionMode.Sequential, false)]
    [DataRow(FunctionExecutionMode.Sequential, true)]
    [DataRow(FunctionExecutionMode.Parallel, false)]
    [DataRow(FunctionExecutionMode.Parallel, true)]
    public async Task MultipleFunctionCalls_UseConfiguredExecutionAndOneContinuation(FunctionExecutionMode execution, bool run)
    {
        using var fixture = new Fixture(Reply("", "batch-thought", Call("one", "first", "{}"), Call("two", "second", "{}")), Reply("done", "final-thought"));
        fixture.Service.WithDeepSeekReasoning();
        fixture.Service.DefaultPolicy.ExecutionMode = execution;
        fixture.Service.DefaultPolicy.MaxConcurrency = 2;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFinished = false;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "first", Handler = async _ =>
        {
            firstEntered.SetResult();
            if (execution == FunctionExecutionMode.Parallel) await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            firstFinished = true;
            return "a";
        } });
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "second", Handler = async _ =>
        {
            await firstEntered.Task;
            if (execution == FunctionExecutionMode.Sequential) Assert.IsTrue(firstFinished);
            secondEntered.SetResult();
            return "b";
        } });
        Assert.AreEqual("done", await Execute(fixture.Service, "both", run));
        Assert.AreEqual(2, fixture.Handler.Requests.Count);
        CollectionAssert.AreEqual(new[] { "one", "two" }, Messages(fixture.Handler.Requests[1]).Where(message => message["role"]?.GetValue<string>() == "tool").Select(message => message["tool_call_id"]!.GetValue<string>()).ToArray());
    }

    [TestMethod]
    [DataRow(false, "missing-id")]
    [DataRow(true, "missing-id")]
    [DataRow(false, "bad-arguments")]
    [DataRow(true, "bad-arguments")]
    [DataRow(false, "duplicate-id")]
    [DataRow(true, "duplicate-id")]
    public async Task InvalidToolBatch_ExecutesNoHandlers(bool run, string error)
    {
        var call = Call("call-id", "tool", "{}");
        JsonObject[] calls;
        if (error == "missing-id") { call.Remove("id"); calls = new[] { call }; }
        else if (error == "bad-arguments") { call["function"]!["arguments"] = "not-json"; calls = new[] { Call("first", "tool", "{}"), call }; }
        else calls = new[] { call, Call("call-id", "tool", "{}") };
        using var fixture = new Fixture(Reply("", "thinking", calls));
        var executed = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ => { executed++; return Task.FromResult("unused"); } });
        await Assert.ThrowsAsync<AIServiceException>(() => Execute(fixture.Service, "tool", run));
        Assert.AreEqual(0, executed);
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.FunctionCallBatch != null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ForcedToolSelection_RejectsThinkingBeforeTransportOrHistory(bool run)
    {
        using var fixture = new Fixture();
        fixture.Service.ThinkingEnabled = true;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ => Task.FromResult("value") });
        fixture.Service.ForceFunctionName = "tool";
        await Assert.ThrowsAsync<NotSupportedException>(() => Execute(fixture.Service, "question", run));
        Assert.AreEqual(0, fixture.Handler.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitThinkingOff_AllowsForcedToolAndRestoresNativeSetting(bool run)
    {
        using var fixture = new Fixture(Reply("", "", Call("forced-call", "tool", "{}")), Reply("done", ""));
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.High);
        fixture.Service.ForceFunctionName = "tool";
        var executions = 0;
        fixture.Service.Functions.Add(new FunctionDefinition
        {
            Name = "tool", Handler = _ => { executions++; return Task.FromResult("tool-value"); }
        });
        string answer;
        if (!run)
            answer = await fixture.Service.GetCompletionAsync("question", new AIRequestProfile
            {
                Purpose = AIRequestPurpose.Default,
                DisableReasoning = true
            });
        else
        {
            // Run exposes common per-request reasoning, rather than an AIRequestProfile argument.
            fixture.Service.WithReasoning(ReasoningLevel.None);
            answer = await Execute(fixture.Service, "question", true);
        }
        Assert.AreEqual("done", answer);
        Assert.AreEqual(1, executions);
        Assert.AreEqual(2, fixture.Handler.Requests.Count);
        foreach (var request in fixture.Handler.Requests) AssertThinking(request, false, null);
        Assert.AreEqual("tool", fixture.Handler.Requests[0]["tool_choice"]?["function"]?["name"]?.GetValue<string>());
        Assert.AreEqual("auto", fixture.Handler.Requests[1]["tool_choice"]?.GetValue<string>());
        Assert.IsTrue(fixture.Service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.High, fixture.Service.ReasoningEffort);
        fixture.Service.ForceFunctionName = null;
        await Execute(fixture.Service, "next question", run);
        AssertThinking(fixture.Handler.Requests[2], true, "high");
    }

    [TestMethod]
    public async Task Stream_SeparatesReasoningTextAndProcessesUsageWithEmptyChoices()
    {
        using var fixture = new Fixture();
        fixture.Service.ThinkingEnabled = true;
        fixture.Handler.Usage = true;
        var events = new List<StreamingContent>();
        await foreach (var item in fixture.Service.StreamAsync("question", StreamOptions.FullOptions)) events.Add(item);
        Assert.AreEqual("answer", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        Assert.AreEqual("reasoning", string.Concat(events.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content)));
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
        var usage = events.Select(item => item.Usage).Last(value => value != null)!;
        Assert.AreEqual(100, usage.InputTokens);
        Assert.AreEqual(25, usage.OutputTokens);
        Assert.AreEqual(125, usage.TotalTokens);
        Assert.AreEqual(60, usage.CachedInputTokens);
        Assert.AreEqual(20, usage.ReasoningTokens);
        Assert.AreEqual("reasoning", fixture.Service.ActivateChat.Messages.Last().Metadata![ReasoningKey]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImageInputs_PreserveOrderedInlineBytesAndText(bool run)
    {
        using var fixture = new Fixture();
        var png = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var jpg = new byte[] { 255, 216, 255, 1, 2, 3 };
        var message = new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent("Compare both images"), new ImageContent(png, "image/png"), new ImageContent(jpg, "image/jpeg")
        });
        if (run) { await using var execution = await fixture.Service.StartRunAsync(message); Assert.AreEqual("answer", (await execution.Result).Text); }
        else Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync(message));
        var content = Messages(fixture.Handler.Requests.Single()).Single(message => message["role"]?.GetValue<string>() == "user")["content"]!.AsArray();
        Assert.AreEqual("Compare both images", content[0]!["text"]?.GetValue<string>());
        Assert.AreEqual("data:image/png;base64," + Convert.ToBase64String(png), content[1]!["image_url"]?["url"]?.GetValue<string>());
        Assert.AreEqual("data:image/jpeg;base64," + Convert.ToBase64String(jpg), content[2]!["image_url"]?["url"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task TextOnlyModel_RejectsImageBeforeTransportOrHistory()
    {
        using var fixture = new Fixture();
        fixture.Service.ChangeModel("deepseek-v4-pro");
        var message = new Message(ActorRole.User, new ImageContent(new byte[] { 1, 2, 3 }, "image/png"));
        await Assert.ThrowsAsync<MultimodalNotSupportedException>(() => fixture.Service.GetCompletionAsync(message));
        Assert.AreEqual(0, fixture.Handler.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task ManualToolImage_PreservesToolRoleAndCallIdentity()
    {
        using var fixture = new Fixture();
        var bytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var tool = new Message(ActorRole.Function, new ImageContent(bytes, "image/png"))
        {
            Metadata = new() { [MessageMetadataKeys.FunctionId] = "image-call" }
        };
        await fixture.Service.GetCompletionAsync(tool);
        var sent = Messages(fixture.Handler.Requests.Single()).Single();
        Assert.AreEqual("tool", sent["role"]?.GetValue<string>());
        Assert.AreEqual("image-call", sent["tool_call_id"]?.GetValue<string>());
        Assert.AreEqual("data:image/png;base64," + Convert.ToBase64String(bytes), sent["content"]![0]!["image_url"]?["url"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(ActorRole.Assistant)]
    [DataRow(ActorRole.System)]
    public async Task ImageInUnsupportedRole_RejectsBeforeHistoryOrTransport(ActorRole role)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync(
            new Message(role, new ImageContent(new byte[] { 1, 2, 3 }, "image/png"))));
        Assert.AreEqual(0, fixture.Handler.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StreamingErrorBody_ObservesRunCancellationAndPolicyTimeout(bool policyTimeout)
    {
        using var fixture = new Fixture();
        using var body = new BlockingReadStream();
        fixture.Handler.ResponseStatus = HttpStatusCode.BadRequest;
        fixture.Handler.ResponseContent = () => new StreamContent(body);
        fixture.Service.DefaultPolicy.TimeoutSeconds = policyTimeout ? 1 : 30;
        await using var run = await fixture.Service.StartRunAsync("question");
        await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!policyTimeout) run.Cancel();
        if (policyTimeout)
            await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        await body.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    public async Task Run_CapturesNativeEffortUntilCompleteAndReleasesAfterCancellation()
    {
        using var fixture = new Fixture();
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.Max);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Block = async token =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
        };
        await using (var run = await fixture.Service.StartRunAsync("first"))
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Service.ReasoningEffort = DeepSeekReasoning.Low;
            AssertThinking(fixture.Handler.Requests[0], true, "max");
            run.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await run.Result);
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        fixture.Handler.Block = null;
        await using var next = await fixture.Service.StartRunAsync("next");
        Assert.AreEqual("answer", (await next.Result).Text);
        AssertThinking(fixture.Handler.Requests[1], true, "low");
    }

    [TestMethod]
    public async Task TypedRepair_PreservesNativeEffortAndJsonMode()
    {
        using var fixture = new Fixture(Reply("not-json", "first"), Reply("{\"Value\":42}", "repair"));
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.Max);
        fixture.Handler.Before = index => { if (index == 0) fixture.Service.ReasoningEffort = DeepSeekReasoning.Low; };
        var result = await fixture.Service.GetCompletionAsync<TypedValue>("Return JSON");
        Assert.AreEqual(42, result.Value);
        foreach (var request in fixture.Handler.Requests)
        {
            AssertThinking(request, true, "max");
            Assert.AreEqual("json_object", request["response_format"]?["type"]?.GetValue<string>());
        }
        Assert.AreEqual(DeepSeekReasoning.Low, fixture.Service.ReasoningEffort);
    }

    public sealed class TypedValue { public int Value { get; set; } }
    private static async Task<string> Execute(DeepSeekService service, string prompt, bool run)
    {
        if (!run) return await service.GetCompletionAsync(prompt);
        await using var execution = await service.StartRunAsync(prompt, options: StreamOptions.FullOptions);
        return (await execution.Result).Text;
    }
    private static IEnumerable<JsonObject> Messages(JsonObject request) => request["messages"]!.AsArray().OfType<JsonObject>();
    private static void AssertThinking(JsonObject request, bool thinking, string? effort)
    {
        Assert.AreEqual(thinking ? "enabled" : "disabled", request["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual(effort, request["reasoning_effort"]?.GetValue<string>());
        if (effort == null) Assert.IsFalse(request.ContainsKey("reasoning_effort"));
    }
    private static JsonObject Call(string id, string name, string arguments) => new()
    {
        ["id"] = id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
    };
    private static JsonObject Reply(string text, string reasoning, params JsonObject[] calls) => new()
    {
        ["role"] = "assistant", ["content"] = text, ["reasoning_content"] = reasoning,
        ["tool_calls"] = calls.Length == 0 ? null : new JsonArray(calls.Select(call => (JsonNode)call.DeepClone()).ToArray())
    };
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        public Handler Handler { get; }
        public DeepSeekService Service { get; }
        public Fixture(params JsonObject[] replies)
        {
            Handler = new Handler(replies);
            _http = new HttpClient(Handler);
            Service = new DeepSeekService("offline-test-key", _http);
        }
        public void Dispose() => _http.Dispose();
    }
    private sealed class Handler(JsonObject[] replies) : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = new();
        public Func<CancellationToken, Task>? Block { get; set; }
        public Action<int>? Before { get; set; }
        public bool Usage { get; set; }
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
        public Func<HttpContent>? ResponseContent { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.AreEqual("api.deepseek.com", request.RequestUri!.Host);
            Assert.AreEqual("/chat/completions", request.RequestUri.AbsolutePath);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            var index = Requests.Count;
            Requests.Add(body);
            Before?.Invoke(index);
            if (Block != null) await Block(token);
            var message = replies.Length > index ? replies[index].DeepClone().AsObject() : Reply("answer", "reasoning");
            var hasCalls = message["tool_calls"] is JsonArray calls && calls.Count > 0;
            var finish = hasCalls ? "tool_calls" : "stop";
            string payload;
            var streaming = body["stream"]?.GetValue<bool>() == true;
            if (streaming)
            {
                var delta = message.DeepClone().AsObject();
                if (delta["tool_calls"] is JsonArray toolCalls)
                    for (var i = 0; i < toolCalls.Count; i++) toolCalls[i]!["index"] = i;
                payload = "data: " + new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = delta, ["finish_reason"] = null }) }.ToJsonString() + "\n\n";
                payload += "data: " + new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject(), ["finish_reason"] = finish }) }.ToJsonString() + "\n\n";
                if (Usage) payload += "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":25,\"total_tokens\":125,\"prompt_cache_hit_tokens\":60,\"completion_tokens_details\":{\"reasoning_tokens\":20}}}\n\n";
                payload += "data: [DONE]\n\n";
            }
            else payload = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = message, ["finish_reason"] = finish }) }.ToJsonString();
            return new HttpResponseMessage(ResponseStatus) { Content = ResponseContent?.Invoke() ?? new StringContent(payload, Encoding.UTF8, streaming ? "text/event-stream" : "application/json") };
        }
    }

    private sealed class BlockingReadStream : MemoryStream
    {
        private readonly CancellationTokenSource _lifetime = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanSeek => false;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WaitForCancellation(cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(WaitForCancellation(cancellationToken));
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            => WaitForCancellation(cancellationToken);
        private async Task<int> WaitForCancellation(CancellationToken token)
        {
            Entered.TrySetResult();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            try { await Task.Delay(Timeout.Infinite, linked.Token); }
            catch (OperationCanceledException) { Canceled.TrySetResult(); throw; }
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _lifetime.Cancel();
            base.Dispose(disposing);
        }
    }
}
