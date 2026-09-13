using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Perplexity;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class PerplexityAgentContractTests
{
    [TestMethod]
    public async Task DefaultRequest_UsesAgentAndHostedSearchWithoutLegacyFields()
    {
        using var fixture = new Fixture();
        Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("question"));
        var request = fixture.Handler.Requests.Single();
        Assert.AreEqual("/v1/agent", fixture.Handler.Paths.Single());
        Assert.AreEqual("perplexity/sonar", request["model"]!.GetValue<string>());
        Assert.AreEqual(4096, request["max_output_tokens"]!.GetValue<int>());
        Assert.AreEqual("web_search", request["tools"]![0]!["type"]!.GetValue<string>());
        Assert.AreEqual("question", request["input"]![0]!["content"]!.GetValue<string>());
        foreach (var field in new[] { "messages", "max_tokens", "temperature", "top_p", "frequency_penalty", "presence_penalty", "return_citations", "search_recency_filter" })
            Assert.IsFalse(request.ContainsKey(field), field);
        Assert.AreEqual("resp-test", fixture.Service.LastResponseId);
        Assert.AreEqual("Bearer test-key", fixture.Handler.Authorization.Single());
    }

    [TestMethod]
    [DataRow(PerplexityPreset.Fast, "fast")]
    [DataRow(PerplexityPreset.Low, "low")]
    [DataRow(PerplexityPreset.Medium, "medium")]
    [DataRow(PerplexityPreset.High, "high")]
    [DataRow(PerplexityPreset.XHigh, "xhigh")]
    [DataRow(PerplexityPreset.WideResearch, "wide-research")]
    public async Task Preset_DoesNotOverrideItsModelOrTools(PerplexityPreset preset, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.UsePreset(preset);
        await fixture.Service.GetCompletionAsync("question");
        var request = fixture.Handler.Requests.Single();
        Assert.AreEqual(wire, request["preset"]!.GetValue<string>());
        Assert.IsFalse(request.ContainsKey("model"));
        Assert.IsFalse(request.ContainsKey("tools"));
        Assert.AreEqual("perplexity/sonar", fixture.Service.Model);
    }

    [TestMethod]
    public async Task PresetOverrideAndHostedParameters_AreSerializedWithoutChangingChatModel()
    {
        using var fixture = new Fixture();
        fixture.Service.WithPerplexityOptions(new PerplexityAgentOptions
        {
            Preset = PerplexityPreset.High, ModelOverride = "google/gemini-3.8-flash", MaxSteps = 7,
            Tools = [new() { Type = "web_search", Parameters = new() { ["filters"] = new Dictionary<string, object> { ["search_recency_filter"] = "week" }, ["max_tokens"] = 1234 } }]
        }).WithWebSearch(new WebSearchOptions { AllowedDomains = ["example.org"] });
        await fixture.Service.GetCompletionAsync("question");
        var request = fixture.Handler.Requests.Single();
        Assert.AreEqual("google/gemini-3.8-flash", request["model"]!.GetValue<string>());
        Assert.AreEqual(7, request["max_steps"]!.GetValue<int>());
        Assert.AreEqual(1, request["tools"]!.AsArray().Count);
        Assert.AreEqual("example.org", request["tools"]![0]!["filters"]!["search_domain_filter"]![0]!.GetValue<string>());
        Assert.AreEqual("week", request["tools"]![0]!["filters"]!["search_recency_filter"]!.GetValue<string>());
        Assert.AreEqual(1234, request["tools"]![0]!["max_tokens"]!.GetValue<int>());
        Assert.AreEqual("perplexity/sonar", fixture.Service.Model);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.Minimal, "minimal")]
    [DataRow(ReasoningLevel.Low, "low")]
    [DataRow(ReasoningLevel.Medium, "medium")]
    [DataRow(ReasoningLevel.High, "high")]
    [DataRow(ReasoningLevel.XHigh, "xhigh")]
    [DataRow(ReasoningLevel.Max, "max")]
    public async Task CommonReasoning_AppliesOnceToSelectedReasoningModel(ReasoningLevel level, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.ChangeModel("openai/gpt-5.6-sol");
        fixture.Service.WithReasoning(level);
        await fixture.Service.GetCompletionAsync("configured");
        await fixture.Service.GetCompletionAsync("ordinary");
        Assert.AreEqual(wire, fixture.Handler.Requests[0]["reasoning"]!["effort"]!.GetValue<string>());
        Assert.IsFalse(fixture.Handler.Requests[1].ContainsKey("reasoning"));
        Assert.AreEqual(ReasoningLevel.Auto, fixture.Service.AgentOptions.ReasoningEffort);
    }

    [TestMethod]
    public async Task ExplicitSampling_IsForwardedWithoutOverridingPresetDefaults()
    {
        using var fixture = new Fixture();
        fixture.Service.UsePreset(PerplexityPreset.Low);
        fixture.Service.Temperature = 0.25f;
        fixture.Service.TopP = 0.75f;
        await fixture.Service.GetCompletionAsync("question");
        Assert.AreEqual(0.25f, fixture.Handler.Requests.Single()["temperature"]!.GetValue<float>());
        Assert.AreEqual(0.75f, fixture.Handler.Requests.Single()["top_p"]!.GetValue<float>());
        Assert.IsFalse(fixture.Handler.Requests.Single().ContainsKey("model"));
    }

    [TestMethod]
    [DataRow("sonar")]
    [DataRow("sonar-pro")]
    public async Task LegacyModelIds_AreRejectedBeforeHistoryOrHttp(string model)
    {
        using var fixture = new Fixture();
        fixture.Service.ChangeModel(model);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.HasCount(0, fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedReasoningNone_IsRejectedBeforeHistoryOrHttp(bool native)
    {
        using var fixture = new Fixture();
        fixture.Service.ChangeModel("openai/gpt-5.6-luna");
        if (native) fixture.Service.AgentOptions.ReasoningEffort = ReasoningLevel.None;
        else fixture.Service.WithReasoning(ReasoningLevel.None);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.HasCount(0, fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task DirectSonarReasoning_IsRejectedBeforeHistoryInCompletionAndRun(bool native, bool run)
    {
        using var fixture = new Fixture();
        if (native) fixture.Service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        else fixture.Service.WithReasoning(ReasoningLevel.High);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            if (run) { await using var active = await fixture.Service.StartRunAsync("question"); await active.Result; }
            else await fixture.Service.GetCompletionAsync("question");
        });
        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.HasCount(0, fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    public async Task UnsupportedCommonFeatures_AreRejectedBeforeHttp()
    {
        using var fixture = new Fixture();
        fixture.Service.WithReasoning(ReasoningLevel.High, CachePreservation.Required);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.HasCount(0, fixture.Service.ActivateChat.Messages);
        fixture.Service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["-example.org"] });
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.HasCount(0, fixture.Handler.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClientTools_ReplayCallIdsSignaturesAndEnrichedUserAcrossRounds(bool run)
    {
        using var fixture = new Fixture(Reply(null, Call("original-call", "lookup", "{\"key\":\"a\"}", "opaque-signature")), Reply("answer"));
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", AllowAsync = true, Handler = arguments =>
        {
            Assert.AreEqual("a", arguments["key"].ToString());
            return Task.FromResult("local-result");
        } });
        var context = new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "enriched question") };
        if (run)
        {
            await using var active = await fixture.Service.StartRunAsync(new Message(ActorRole.User, "question"), context: context);
            Assert.AreEqual("answer", (await active.Result).Text);
        }
        else Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("question", context: context));
        Assert.HasCount(2, fixture.Handler.Requests);
        var followUp = fixture.Handler.Requests[1]["input"]!.AsArray();
        Assert.AreEqual("enriched question", followUp[0]!["content"]!.GetValue<string>());
        var call = followUp.Single(item => item?["type"]?.GetValue<string>() == "function_call")!;
        var result = followUp.Single(item => item?["type"]?.GetValue<string>() == "function_call_output")!;
        Assert.AreEqual("original-call", call["call_id"]!.GetValue<string>());
        Assert.AreEqual("original-call", result["call_id"]!.GetValue<string>());
        Assert.AreEqual("opaque-signature", call["thought_signature"]!.GetValue<string>());
        Assert.AreEqual("local-result", result["output"]!.GetValue<string>());
        Assert.IsFalse(fixture.Handler.Requests[0]["tools"]!.AsArray().Any(tool => tool!.AsObject().ContainsKey("async")));
        Assert.AreEqual("question", fixture.Service.ActivateChat.Messages[0].Content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WholeBatchValidation_PrecedesAnyHandler(bool run)
    {
        using var fixture = new Fixture(Reply(null, Call("same", "lookup", "{}"), Call("same", "lookup", "{}")));
        var executions = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => { executions++; return Task.FromResult("result"); } });
        if (run)
        {
            await using var active = await fixture.Service.StartRunAsync("question");
            await Assert.ThrowsAsync<AIServiceException>(async () => await active.Result);
        }
        else await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.AreEqual(0, executions);
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{")]
    [DataRow("{\"x\":1,\"x\":2}")]
    public async Task MalformedFunctionArguments_CannotExecute(string arguments)
    {
        using var fixture = new Fixture(Reply(null, Call("call", "lookup", arguments)));
        var executions = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => { executions++; return Task.FromResult("result"); } });
        await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.AreEqual(0, executions);
    }

    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("content")]
    [DataRow("parts")]
    public async Task PreservedHistory_ReflectsTextEditsAndRetainsOtherOutput(string edit)
    {
        var firstMessage = new JsonObject
        {
            ["type"] = "message", ["id"] = "msg-first", ["role"] = "assistant", ["status"] = "completed",
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "output_text", ["text"] = "one", ["annotations"] = new JsonArray(new JsonObject
                { ["type"] = "url_citation", ["url"] = "https://example.org", ["start_index"] = 0, ["end_index"] = 3 }), ["logprobs"] = new JsonArray(1) },
                new JsonObject { ["type"] = "output_text", ["text"] = "two", ["annotations"] = new JsonArray() })
        };
        var secondMessage = new JsonObject
        {
            ["type"] = "message", ["id"] = "msg-second", ["role"] = "assistant", ["status"] = "completed",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "three", ["annotations"] = new JsonArray() })
        };
        var reasoning = new JsonObject { ["type"] = "reasoning", ["id"] = "opaque-reasoning", ["encrypted_content"] = "opaque-signature", ["summary"] = new JsonArray() };
        var reply = Reply(null, firstMessage, reasoning, secondMessage);
        using var fixture = new Fixture(reply);
        Assert.AreEqual("onetwothree", await fixture.Service.GetCompletionAsync("first"));
        var history = fixture.Service.ActivateChat.Messages.Last();
        if (edit == "content") history.Content = "corrected";
        if (edit == "parts") history.Contents.Add(new TextContent("corrected"));
        await fixture.Service.GetCompletionAsync("next");
        var replay = fixture.Handler.Requests[1]["input"]!.AsArray().Skip(1).Take(2).ToArray();
        if (edit == "unchanged")
        {
            Assert.IsTrue(JsonNode.DeepEquals(firstMessage, replay[0]));
            Assert.IsTrue(JsonNode.DeepEquals(secondMessage, replay[1]));
        }
        else
        {
            Assert.AreEqual("corrected", replay[0]!["content"]![0]!["text"]!.GetValue<string>());
            Assert.AreEqual(string.Empty, replay[0]!["content"]![1]!["text"]!.GetValue<string>());
            Assert.AreEqual(string.Empty, replay[1]!["content"]![0]!["text"]!.GetValue<string>());
            Assert.HasCount(0, replay[0]!["content"]![0]!["annotations"]!.AsArray());
            Assert.HasCount(0, replay[0]!["content"]![0]!["logprobs"]!.AsArray());
            Assert.AreEqual("msg-first", replay[0]!["id"]!.GetValue<string>());
            Assert.AreEqual("msg-second", replay[1]!["id"]!.GetValue<string>());
        }
        var preserved = JsonSerializer.SerializeToNode(history.Metadata!["perplexity_agent_output_items"])!.AsArray();
        Assert.IsTrue(JsonNode.DeepEquals(reasoning, preserved[1]));
        Assert.IsFalse(fixture.Handler.Requests[1]["input"]!.AsArray().Any(item => item?["type"]?.GetValue<string>() == "reasoning"));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task HostedHistory_ReplaysOnlyLegalInputsAndPreservesFullMetadata(bool run, bool edited)
    {
        var types = new[] { "search_results", "fetch_url_results", "finance_results", "people_search_results",
            "sandbox_results", "sandbox_write_file", "mcp_list_tools", "mcp_call", "tool_search_output",
            "skill_loaded", "share_file", "reasoning", "future_hosted_output" };
        var traces = types.Select(type => new JsonObject { ["type"] = type }).ToArray();
        var nativeCall = Call("real-client-call", "lookup", "{}", "retained-thought-signature");
        using var fixture = new Fixture(Reply("explanation", traces.Append(nativeCall).ToArray()), Reply("answer"));
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("local-result") });
        await fixture.Service.GetCompletionAsync("first");
        var history = fixture.Service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        if (edited) history.Content = "corrected explanation";
        if (run) { await using var active = await fixture.Service.StartRunAsync("next"); await active.Result; }
        else await fixture.Service.GetCompletionAsync("next");
        foreach (var request in fixture.Handler.Requests.Skip(1))
        {
            var input = request["input"]!.AsArray();
            Assert.IsTrue(input.All(item => item?["type"]?.GetValue<string>() is "message" or "function_call" or "function_call_output"));
            Assert.IsTrue(JsonNode.DeepEquals(nativeCall, input.Single(item => item?["type"]?.GetValue<string>() == "function_call")));
            Assert.AreEqual("real-client-call", input.Single(item => item?["type"]?.GetValue<string>() == "function_call_output")!["call_id"]!.GetValue<string>());
        }
        var replayedText = fixture.Handler.Requests[2]["input"]!.AsArray()
            .First(item => item?["type"]?.GetValue<string>() == "message" && item?["role"]?.GetValue<string>() == "assistant")!["content"]![0]!["text"]!.GetValue<string>();
        Assert.AreEqual(edited ? "corrected explanation" : "explanation", replayedText);
        var preserved = JsonSerializer.SerializeToNode(history.Metadata!["perplexity_agent_output_items"])!.AsArray();
        Assert.HasCount(types.Length + 2, preserved);
        for (var index = 0; index < traces.Length; index++) Assert.IsTrue(JsonNode.DeepEquals(traces[index], preserved[index]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EditedToolHistory_PreservesNativeCallsAndSignatures(bool originallyHasText)
    {
        var native = Call("historical-call", "lookup", "{}", "native-signature");
        using var fixture = new Fixture(Reply(originallyHasText ? "old explanation" : null, native), Reply("old answer"));
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("result") });
        await fixture.Service.GetCompletionAsync("first");
        fixture.Service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null).Content = "corrected explanation";
        fixture.Service.ActivateChat.Messages.Last().Content = "corrected answer";
        await using var run = await fixture.Service.StartRunAsync("next");
        await run.Result;
        var input = fixture.Handler.Requests[2]["input"]!.AsArray();
        Assert.IsTrue(JsonNode.DeepEquals(native, input.Single(item => item?["type"]?.GetValue<string>() == "function_call")));
        Assert.AreEqual("historical-call", input.Single(item => item?["type"]?.GetValue<string>() == "function_call_output")!["call_id"]!.GetValue<string>());
        var text = input.Where(item => item?["type"]?.GetValue<string>() == "message" && item?["role"]?.GetValue<string>() == "assistant")
            .SelectMany(item => item!["content"]!.AsArray()).Select(part => part!["text"]!.GetValue<string>()).ToArray();
        CollectionAssert.AreEqual(new[] { "corrected explanation", "corrected answer" }, text);
    }

    [TestMethod]
    public async Task ConflictingHistoryTextEdits_FailBeforeAnotherHttpRequest()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("first");
        var history = fixture.Service.ActivateChat.Messages.Last();
        history.Content = "different content";
        history.Contents.Add(new TextContent("different parts"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetCompletionAsync("next"));
        Assert.HasCount(1, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task FunctionModeNone_OmitsClientFunctionsAndRetainsHostedTools()
    {
        using var fixture = new Fixture();
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("result") });
        fixture.Service.FunctionCallMode = FunctionCallMode.None;
        await fixture.Service.GetCompletionAsync("question");
        Assert.HasCount(1, fixture.Handler.Requests.Single()["tools"]!.AsArray());
        Assert.AreEqual("web_search", fixture.Handler.Requests.Single()["tools"]![0]!["type"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task ForcedNamedFunction_IsRejectedBeforeHistoryMutation()
    {
        using var fixture = new Fixture();
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("result") });
        fixture.Service.ForceFunctionName = "lookup";
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.HasCount(0, fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stream_PreservesUsageAndCitationsIndependentlyOfMetadata(bool metadata)
    {
        var reply = Reply("answer");
        reply["output"]![0]!["content"]![0]!["annotations"] = new JsonArray(new JsonObject
        {
            ["type"] = "url_citation", ["url"] = "https://example.org/source", ["title"] = "Source", ["start_index"] = 0, ["end_index"] = 6
        });
        using var fixture = new Fixture(reply);
        var events = new List<StreamingContent>();
        await foreach (var item in fixture.Service.StreamAsync("question", new StreamOptions { IncludeMetadata = metadata, IncludeReasoning = true }))
            events.Add(item);
        Assert.AreEqual("answer", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        var usage = events.Single(item => item.Type == StreamingContentType.RoundUsage).Usage!;
        Assert.AreEqual(10, usage.InputTokens);
        Assert.AreEqual(4, usage.CachedInputTokens);
        Assert.AreEqual(2, usage.ReasoningTokens);
        Assert.AreEqual(15, usage.TotalTokens);
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Citation));
        var citation = fixture.Service.LastCitations.Single();
        Assert.AreEqual("https://example.org/source", citation.Url);
        Assert.AreEqual("resp-test", citation.ResponseId);
        Assert.AreEqual(0, citation.OutputIndex);
        Assert.AreEqual(0, citation.ContentIndex);
    }

    [TestMethod]
    [DataRow("incomplete")]
    [DataRow("failed")]
    [DataRow("in_progress")]
    public async Task NonterminalResponses_DoNotBecomeAssistantHistory(string status)
    {
        var reply = Reply("partial");
        reply["status"] = status;
        using var fixture = new Fixture(reply);
        await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
        Assert.IsNull(fixture.Service.LastResponseId);
    }

    [TestMethod]
    [DataRow("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n")]
    [DataRow("data: invalid json\n\n")]
    public async Task TruncatedOrMalformedStream_EmitsErrorWithoutCompletionOrAssistantHistory(string sse)
    {
        using var fixture = new Fixture();
        fixture.Handler.StreamOverride = sse;
        var events = new List<StreamingContent>();
        await foreach (var item in fixture.Service.StreamAsync("question", StreamOptions.FullOptions)) events.Add(item);
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Error));
        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Completion));
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    public async Task RunSnapshot_SurvivesProviderOptionsMutationAndConsumesCommonFeaturesOnce()
    {
        using var fixture = new Fixture();
        fixture.Handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.UsePreset(PerplexityPreset.High);
        fixture.Service.AgentOptions.ModelOverride = "openai/gpt-5.6-sol";
        fixture.Service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["before.example"] });
        await using var active = await fixture.Service.StartRunAsync("question");
        await fixture.Handler.Started.Task;
        fixture.Service.AgentOptions.Preset = PerplexityPreset.Fast;
        fixture.Handler.Gate.SetResult();
        Assert.AreEqual("answer", (await active.Result).Text);
        Assert.AreEqual("high", fixture.Handler.Requests.Single()["preset"]!.GetValue<string>());
        Assert.AreEqual("before.example", fixture.Handler.Requests.Single()["tools"]![0]!["filters"]!["search_domain_filter"]![0]!.GetValue<string>());
    }

    [TestMethod]
    public async Task InternalProfile_SuppressesPresetAndHostedToolsThenRestoresOptions()
    {
        using var fixture = new Fixture();
        fixture.Service.UsePreset(PerplexityPreset.High);
        fixture.Service.AgentOptions.Tools.Add(new PerplexityHostedTool { Type = "sandbox" });
        fixture.Service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["next.example"] });
        await fixture.Service.GetCompletionAsync("rewrite", RequestProfiles.QueryRewrite);
        var request = fixture.Handler.Requests[0];
        Assert.IsFalse(request.ContainsKey("preset"));
        Assert.HasCount(0, request["tools"]!.AsArray());
        Assert.AreEqual(128, request["max_output_tokens"]!.GetValue<int>());
        Assert.HasCount(0, fixture.Service.ActivateChat.Messages);
        Assert.AreEqual(PerplexityPreset.High, fixture.Service.AgentOptions.Preset);
        await fixture.Service.GetCompletionAsync("ordinary");
        Assert.AreEqual("high", fixture.Handler.Requests[1]["preset"]!.GetValue<string>());
        Assert.IsTrue(fixture.Handler.Requests[1]["tools"]!.AsArray().Any(tool => tool!["type"]!.GetValue<string>() == "sandbox"));
    }

    [TestMethod]
    public async Task TypedOutput_UsesNativeSchemaOnInitialAndRepairRequests()
    {
        using var fixture = new Fixture(Reply("invalid"), Reply("{\"Value\":42}"));
        var result = await fixture.Service.GetCompletionAsync<StructuredValue>("question");
        Assert.AreEqual(42, result.Value);
        Assert.HasCount(2, fixture.Handler.Requests);
        foreach (var request in fixture.Handler.Requests)
        {
            Assert.AreEqual("json_schema", request["response_format"]!["type"]!.GetValue<string>());
            Assert.AreEqual("structuredoutput", request["response_format"]!["json_schema"]!["name"]!.GetValue<string>());
            Assert.IsNotNull(request["response_format"]!["json_schema"]!["schema"]);
        }
    }

    [TestMethod]
    [DataRow("image/png")]
    [DataRow("image/jpeg")]
    [DataRow("image/webp")]
    [DataRow("image/gif")]
    public async Task ImageBytes_AreSentAsAgentInputImages(string mime)
    {
        using var fixture = new Fixture();
        var message = new Message(ActorRole.User, "describe");
        message.Contents.Add(new ImageContent(new byte[] { 1, 2, 3 }, mime));
        await fixture.Service.GetCompletionAsync(message);
        var parts = fixture.Handler.Requests.Single()["input"]![0]!["content"]!.AsArray();
        Assert.AreEqual("input_text", parts[0]!["type"]!.GetValue<string>());
        Assert.AreEqual("input_image", parts[1]!["type"]!.GetValue<string>());
        Assert.AreEqual($"data:{mime};base64,AQID", parts[1]!["image_url"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task ImageParts_PreserveExplicitTextOrderWithoutDuplicatingCompatibilityContent()
    {
        using var fixture = new Fixture();
        var message = new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent("before"), new ImageContent("https://example.org/image.png"), new TextContent("after")
        });
        await fixture.Service.GetCompletionAsync(message);
        var parts = fixture.Handler.Requests.Single()["input"]![0]!["content"]!.AsArray();
        Assert.HasCount(3, parts);
        Assert.AreEqual("before", parts[0]!["text"]!.GetValue<string>());
        Assert.AreEqual("input_image", parts[1]!["type"]!.GetValue<string>());
        Assert.AreEqual("after", parts[2]!["text"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task ImageUrl_IsPreservedAndUnsupportedRoleFailsBeforeHttp()
    {
        using var fixture = new Fixture();
        var user = new Message(ActorRole.User, "describe");
        user.Contents.Add(new ImageContent("https://example.org/image.png"));
        await fixture.Service.GetCompletionAsync(user);
        Assert.AreEqual("https://example.org/image.png", fixture.Handler.Requests.Single()["input"]![0]!["content"]![1]!["image_url"]!.GetValue<string>());
        var assistant = new Message(ActorRole.Assistant, "bad");
        assistant.Contents.Add(new ImageContent(new byte[] { 1 }, "image/png"));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync(assistant));
        Assert.HasCount(1, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task RunCancellation_DoesNotCommitPartialHistory()
    {
        using var fixture = new Fixture();
        fixture.Handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var active = await fixture.Service.StartRunAsync("question", cancellationToken: cancellation.Token);
        await fixture.Handler.Started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await active.Result);
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    public sealed class StructuredValue { public int Value { get; set; } }

    private static JsonObject Call(string id, string name, string arguments, string? signature = null)
    {
        var call = new JsonObject { ["type"] = "function_call", ["id"] = "item-" + id, ["call_id"] = id, ["name"] = name, ["arguments"] = arguments, ["status"] = "completed" };
        if (signature != null) call["thought_signature"] = signature;
        return call;
    }

    private static JsonObject Reply(string? text, params JsonObject[] items)
    {
        var output = new JsonArray();
        foreach (var item in items) output.Add(item);
        if (text != null) output.Add(new JsonObject
        {
            ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JsonArray() })
        });
        return new JsonObject
        {
            ["id"] = "resp-test", ["status"] = "completed", ["model"] = "perplexity/sonar", ["output"] = output,
            ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 5, ["total_tokens"] = 15,
                ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 4 },
                ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 2 } }
        };
    }

    private sealed class Fixture : IDisposable
    {
        public Handler Handler { get; }
        private readonly HttpClient _client;
        public PerplexityService Service { get; }
        public Fixture(params JsonObject[] replies)
        {
            Handler = new Handler(replies);
            _client = new HttpClient(Handler);
            Service = new PerplexityService("test-key", _client);
        }
        public void Dispose() => _client.Dispose();
    }

    private sealed class Handler(JsonObject[] replies) : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];
        public List<string> Paths { get; } = [];
        public List<string> Authorization { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Gate { get; set; }
        public string? StreamOverride { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var index = Requests.Count;
            Requests.Add(body);
            Paths.Add(request.RequestUri!.AbsolutePath);
            Authorization.Add(request.Headers.Authorization!.ToString());
            Started.TrySetResult();
            if (Gate != null) await Gate.Task.WaitAsync(cancellationToken);
            var reply = replies.Length > index ? replies[index] : Reply("answer");
            if (body["stream"]?.GetValue<bool>() == true)
            {
                var text = string.Concat(reply["output"]!.AsArray().Where(item => item!["type"]!.GetValue<string>() == "message")
                    .SelectMany(item => item!["content"]!.AsArray()).Select(part => part!["text"]?.GetValue<string>()));
                var sse = StreamOverride ?? "data: " + new JsonObject { ["type"] = "response.output_text.delta", ["delta"] = text }.ToJsonString()
                    + "\n\ndata: " + new JsonObject { ["type"] = "response.completed", ["response"] = reply.DeepClone() }.ToJsonString() + "\n\n";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
