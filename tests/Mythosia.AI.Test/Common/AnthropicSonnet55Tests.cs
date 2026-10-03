using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

// Synthetic HTTP/SSE responses pin Sonnet 5.5's request and conversation contracts.
// The separate Live class proves acceptance by the actual provider.
[TestClass]
[TestCategory("Unit")]
public class AnthropicSonnet55Tests
{
    [TestMethod]
    public void Capabilities_ExposeFixedModelAndSupportedControls()
    {
        using var fixture = new Fixture();
        var modelField = typeof(AIModels.Anthropic).GetField(nameof(AIModels.Anthropic.ClaudeSonnet5_5), BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(modelField);
        Assert.AreEqual("claude-sonnet-5-5", modelField.GetRawConstantValue());
        var capabilities = fixture.Service.GetCapabilities();
        CollectionAssert.AreEqual(new[] { ReasoningLevel.Auto, ReasoningLevel.None, ReasoningLevel.Low,
            ReasoningLevel.Medium, ReasoningLevel.High, ReasoningLevel.XHigh, ReasoningLevel.Max }, capabilities.ReasoningLevels.ToArray());
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.GetReasoningSupport(ReasoningLevel.Minimal));
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.ThinkingToggle);
        Assert.IsEmpty(capabilities.ThinkingBudgetPresets);
        Assert.AreEqual(128000u, capabilities.MaxOutputTokens);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.FunctionCalling);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Streaming);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.StructuredOutput);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.WebSearch);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.ReasoningCachePreservation);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.TopP);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Steering);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.AsyncFunctionCalling);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.FastSpeed);
    }

    [TestMethod]
    [DataRow("claude-sonnet-5-5-20260929")]
    [DataRow("claude-sonnet-5-5-2026-09-29")]
    [DataRow("claude-sonnet-5-5-experimental")]
    public void UnpublishedVariants_DoNotAcquireKnownCapabilities(string model)
    {
        using var fixture = new Fixture(model);
        Assert.AreEqual(CapabilitySupport.Unknown, fixture.Service.GetCapabilities().Reasoning);
        Assert.IsNull(fixture.Service.GetCapabilities().MaxOutputTokens);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task Defaults_PreserveHighAdaptiveAndNativeSignedHistory(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Service.Temperature = .7f;
        fixture.Service.TopP = .9f;
        fixture.Service.MaxTokens = 200000;
        await Execute(fixture.Service, "first", mode);
        await Execute(fixture.Service, "second", mode);
        foreach (var body in fixture.Requests)
        {
            Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet5_5, body["model"]?.GetValue<string>());
            Assert.AreEqual("adaptive", body["thinking"]?["type"]?.GetValue<string>());
            Assert.AreEqual("high", body["output_config"]?["effort"]?.GetValue<string>());
            Assert.AreEqual("omitted", body["thinking"]?["display"]?.GetValue<string>());
            Assert.AreEqual(128000u, body["max_tokens"]?.GetValue<uint>());
            Assert.IsNull(body["temperature"]);
            Assert.IsNull(body["top_p"]);
            Assert.IsNull(body["top_k"]);
            Assert.IsNull(body["thinking"]?["budget_tokens"]);
        }
        AssertReplay(fixture.Responses[0], fixture.Requests[1]);
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.Auto, "high")]
    [DataRow(ReasoningLevel.Low, "low")]
    [DataRow(ReasoningLevel.Medium, "medium")]
    [DataRow(ReasoningLevel.High, "high")]
    [DataRow(ReasoningLevel.XHigh, "xhigh")]
    [DataRow(ReasoningLevel.Max, "max")]
    public async Task BuilderReasoning_MapsEffortWithoutMutatingServiceDefaults(ReasoningLevel level, string wire)
    {
        using var fixture = new Fixture();
        await fixture.Service.CreateRequest("explicit").WithReasoning(level).GetCompletionAsync();
        Assert.AreEqual(wire, fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.IsNull(fixture.Requests[0]["thinking"]?["budget_tokens"]);
        await fixture.Service.GetCompletionAsync("default again");
        Assert.AreEqual("high", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(-1, fixture.Service.ThinkingBudget);
        Assert.AreEqual(ClaudeReasoningEffort.Auto, fixture.Service.AdaptiveThinkingEffort);
    }

    [TestMethod]
    [DataRow(ClaudeReasoningEffort.Auto, "high")]
    [DataRow(ClaudeReasoningEffort.Low, "low")]
    [DataRow(ClaudeReasoningEffort.Medium, "medium")]
    [DataRow(ClaudeReasoningEffort.High, "high")]
    [DataRow(ClaudeReasoningEffort.XHigh, "xhigh")]
    [DataRow(ClaudeReasoningEffort.Max, "max")]
    public async Task NativeEffort_SupportsSummarizedAdaptiveThinking(ClaudeReasoningEffort effort, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(effort, ClaudeThinkingDisplay.Summarized);
        await fixture.Service.GetCompletionAsync("native");
        Assert.AreEqual(wire, fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("summarized", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(8192, "high")]
    [DataRow(32768, "xhigh")]
    [DataRow(100000, "max")]
    public async Task LegacyPositiveBudget_MapsToAdaptiveEffort(int budget, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingParameters(budget);
        await fixture.Service.GetCompletionAsync("legacy");
        Assert.AreEqual(wire, fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.IsNull(fixture.Requests[0]["thinking"]?["budget_tokens"]);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task ReasoningNone_UsesBareBetweenTools_ThenRestoresDefault(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Service.WithReasoning(ReasoningLevel.None);
        await Execute(fixture.Service, "no up-front thinking", mode);
        AssertBetweenTools(fixture.Requests[0]);
        await Execute(fixture.Service, "default again", mode);
        Assert.AreEqual("adaptive", fixture.Requests[1]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task LegacyDisabledThinking_UsesBetweenToolsWithoutExtraThinkingFields()
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingParameters(-1);
        await fixture.Service.GetCompletionAsync("legacy disabled");
        AssertBetweenTools(fixture.Requests[0]);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Auto);
        await fixture.Service.GetCompletionAsync("adaptive again");
        Assert.AreEqual("adaptive", fixture.Requests[1]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task InternalDisableReasoningProfile_IsIsolatedFromNativeDefaults()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("internal helper", new AIRequestProfile { DisableReasoning = true });
        AssertBetweenTools(fixture.Requests[0]);
        await fixture.Service.GetCompletionAsync("ordinary request");
        Assert.AreEqual("adaptive", fixture.Requests[1]["thinking"]?["type"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task MinimalReasoning_RejectsBeforeTransportOrHistory()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.CreateRequest("invalid").WithReasoning(ReasoningLevel.Minimal).GetCompletionAsync());
        Assert.IsEmpty(fixture.Requests);
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task ForcedTool_RejectsBeforeTransportAndHandlerExecution(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Service.ForceFunctionName = "lookup";
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => throw new InvalidOperationException("Must not execute") });
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Execute(fixture.Service, "force", mode));
        Assert.IsEmpty(fixture.Requests);
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TokenCount_RejectsForcedToolBeforeTransport(bool promptOnly)
    {
        using var fixture = new Fixture();
        fixture.Service.ForceFunctionName = "lookup";
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => promptOnly
            ? fixture.Service.GetInputTokenCountAsync("count") : fixture.Service.GetInputTokenCountAsync());
        Assert.IsEmpty(fixture.Requests);
    }

    [TestMethod]
    public async Task AssistantPrefill_RejectsBeforeTransport()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync(new Message(ActorRole.Assistant, "prefill")));
        Assert.IsEmpty(fixture.Requests);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task AutomaticToolRound_ReplaysEmptySignedThinkingAndProgress(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        fixture.Respond = (_, index) => Reply(index, tool: index == 0);
        var calls = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => { calls++; return Task.FromResult("tool-proof"); } });
        await Execute(fixture.Service, "lookup", mode);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.IsTrue(fixture.Requests.All(body => body["tool_choice"]?["type"]?.GetValue<string>() == "auto"));
        Assert.IsTrue(fixture.Betas.All(beta => beta.Contains(AnthropicFableLiveProbe.UpdatesBeta, StringComparison.Ordinal)));
        AssertReplay(fixture.Responses[0], fixture.Requests[1]);
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        var result = Messages(fixture.Requests[1]).SelectMany(message => message["content"] is JsonArray blocks ? blocks.OfType<JsonObject>() : [])
            .Single(block => block["type"]?.GetValue<string>() == "tool_result");
        Assert.AreEqual("lookup-0", result["tool_use_id"]?.GetValue<string>());
        Assert.AreEqual("tool-proof", result["content"]?.GetValue<string>());
        StringAssert.Contains(fixture.Service.LastThinkingContent!, "progress-0");
    }

    [TestMethod]
    public async Task Updates_StreamDeliversReasoningSeparatelyFromText()
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        var events = new List<StreamingContent>();
        await foreach (var item in fixture.Service.StreamAsync("updates", StreamOptions.FullOptions)) events.Add(item);
        Assert.AreEqual("progress-0", string.Concat(events.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content)));
        Assert.AreEqual("answer", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
        Assert.AreEqual("updates", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task StructuredCompletion_UsesSchemaInstructionAndRetainsEffort()
    {
        using var fixture = new Fixture();
        fixture.Respond = (_, index) => Reply(index, "{\"Value\":42}");
        var value = await fixture.Service.GetCompletionAsync<TypedValue>("Return Value 42.");
        Assert.AreEqual(42, value.Value);
        var instruction = Messages(fixture.Requests[0]).Single(message => message["role"]?.GetValue<string>() == "system" &&
            message["content"]!.GetValue<string>().Contains("[STRUCTURED OUTPUT]", StringComparison.Ordinal));
        Assert.AreEqual("next_user_message", instruction["clear_at"]?.GetValue<string>());
        var instructionText = instruction["content"]!.GetValue<string>();
        var schema = JsonNode.Parse(instructionText[instructionText.IndexOf('{')..])!;
        Assert.AreEqual("integer", schema["properties"]?["Value"]?["type"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        fixture.Respond = (_, index) => Reply(index);
        await fixture.Service.GetCompletionAsync("ordinary follow-up");
        Assert.AreEqual(1, Messages(fixture.Requests[1]).Count(message => message["role"]?.GetValue<string>() == "system" &&
            message["content"]!.GetValue<string>().Contains("[STRUCTURED OUTPUT]", StringComparison.Ordinal)), "The historical instruction stays verbatim, but the next turn must not add another schema instruction.");
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
    }

    [TestMethod]
    public async Task CompletedConversationTokenCount_ReplaysNativeBlocksWithoutHistoryMutation()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("first");
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);
        fixture.Respond = (_, _) => new JsonObject { ["input_tokens"] = 42 };
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual("/v1/messages/count_tokens", fixture.Paths[1]);
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
        AssertReplay(fixture.Responses[0], fixture.Requests[1]);
    }

    [TestMethod]
    public async Task TokenCount_BetweenToolsUpdates_DoesNotManufactureAdaptiveDisplayControls()
    {
        using var fixture = new Fixture();
        fixture.Service.WithBetweenToolsThinking();
        fixture.Service.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Updates;
        await fixture.Service.GetCompletionAsync("original");
        fixture.Respond = (_, _) => new JsonObject { ["input_tokens"] = 42 };
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual("/v1/messages/count_tokens", fixture.Paths[1]);
        AssertBetweenTools(fixture.Requests[1]);
        Assert.IsNull(fixture.Requests[1]["output_config"], "Token counting must not emit generation effort controls.");
        Assert.IsFalse(fixture.Betas[1].Contains(AnthropicFableLiveProbe.UpdatesBeta, StringComparison.Ordinal));
        AssertReplay(fixture.Responses[0], fixture.Requests[1]);
    }

    [TestMethod]
    public async Task CachedEffortAndTurnInstruction_PreserveHighBaselineAndHistoricalPrefix()
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error).WithTurnInstruction("first-turn-only");
        await fixture.Service.WithReasoning(ReasoningLevel.Auto, CachePreservation.Required).GetCompletionAsync("first");
        await fixture.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("second");
        Assert.AreEqual("high", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[1]["output_config"]));
        Assert.AreEqual("low", Messages(fixture.Requests[1]).Last(message => message["output_config"]?["effort"] != null)["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(1, Messages(fixture.Requests[1]).Count(message => message["clear_at"]?.GetValue<string>() == "next_user_message"));
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        AssertReplay(fixture.Responses[0], fixture.Requests[1]);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task EditedAssistantHistory_RejectsWithoutDiscardingSignedContent(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        await Execute(fixture.Service, "first", mode);
        var assistant = fixture.Service.ActivateChat.Messages.Single(message => message.Role == ActorRole.Assistant);
        var original = assistant.Content;
        var metadata = JsonSerializer.Serialize(assistant.Metadata);
        assistant.Content = "edited public answer";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Execute(fixture.Service, "rejected", mode));
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(metadata, JsonSerializer.Serialize(assistant.Metadata));
        assistant.Content = original;
        await Execute(fixture.Service, "restored", mode);
        AssertReplay(fixture.Responses[0], fixture.Requests[1]);
    }

    [TestMethod]
    public async Task Run_CapturesNativeOptions_AndKeepsLaterInstructionsForNextRequest()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeResponse = async (index, token) => { if (index == 0) { entered.TrySetResult(); await release.Task.WaitAsync(token); } };
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Low, ClaudeThinkingDisplay.Summarized).WithTurnInstruction("first-only");
        await using var run = await fixture.Service.StartRunAsync("first");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Max, ClaudeThinkingDisplay.Updates).WithTurnInstruction("next-only");
        }
        finally { release.TrySetResult(); }
        await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Service.GetCompletionAsync("next");
        Assert.AreEqual("low", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("summarized", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
        Assert.AreEqual("max", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("next-only", StringComparison.Ordinal));
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_LeavesNoPartialAssistantAndNextRequestRecovers(bool run)
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeResponse = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
        using var cancellation = new CancellationTokenSource();
        var pending = ExecuteCancelable();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
        fixture.BeforeResponse = null;
        Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("recovered"));
        Assert.AreEqual(2, fixture.Requests.Count);
        async Task ExecuteCancelable()
        {
            var request = fixture.Service.CreateRequest("cancel");
            if (!run) { await request.GetCompletionAsync(cancellation.Token); return; }
            await using var active = await request.StartRunAsync(cancellationToken: cancellation.Token);
            await active.Result;
        }
    }

    [TestMethod]
    public async Task HttpFailure_IsNotRetriedOrSavedAsAssistant()
    {
        using var fixture = new Fixture { Status = HttpStatusCode.BadRequest };
        fixture.Respond = (_, _) => JsonNode.Parse("""{"type":"error","error":{"type":"invalid_request_error","message":"Synthetic bound-prefix error"}}""")!.AsObject();
        await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetCompletionAsync("failure"));
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task Transformations_AreExposedWithoutLeakingToNextRequest(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Respond = (_, index) =>
        {
            var response = Reply(index);
            response["input_transformations"] = JsonNode.Parse("""[{"type":"thinking_dropped","path":"messages.1.content.0","reason":"prefix_binding_mismatch"}]""");
            return response;
        };
        await Execute(fixture.Service, "transform", mode);
        var transformation = fixture.Service.LastInputTransformations.Single();
        Assert.AreEqual("prefix_binding_mismatch", transformation.Reason);
        Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet5_5, transformation.Model);
        transformation.Reason = "caller mutation";
        Assert.AreEqual("prefix_binding_mismatch", fixture.Service.LastInputTransformations.Single().Reason);
        fixture.Respond = (_, index) => Reply(index);
        await fixture.Service.GetCompletionAsync("next");
        Assert.IsEmpty(fixture.Service.LastInputTransformations);
    }

    [TestMethod]
    public async Task Sonnet5_StillUsesLegacyDisabledAndForcedToolContracts()
    {
        using var fixture = new Fixture(AIModels.Anthropic.ClaudeSonnet5);
        fixture.Service.ForceFunctionName = "lookup";
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("unused") });
        await fixture.Service.GetCompletionAsync("legacy model");
        Assert.AreEqual("disabled", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("tool", fixture.Requests[0]["tool_choice"]?["type"]?.GetValue<string>());
        Assert.AreEqual("lookup", fixture.Requests[0]["tool_choice"]?["name"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(ClaudeReasoningEffort.Auto, "high")]
    [DataRow(ClaudeReasoningEffort.Low, "low")]
    [DataRow(ClaudeReasoningEffort.Medium, "medium")]
    [DataRow(ClaudeReasoningEffort.High, "high")]
    public async Task NativeBetweenTools_UsesOnlyTypeAndSupportedEffort(ClaudeReasoningEffort effort, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingParameters(100000);
        fixture.Service.WithBetweenToolsThinking(effort);
        fixture.Service.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Updates;
        await fixture.Service.GetCompletionAsync("between tools");
        AssertBetweenTools(fixture.Requests[0]);
        Assert.AreEqual(wire, fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsFalse(fixture.Betas[0].Contains(AnthropicFableLiveProbe.UpdatesBeta, StringComparison.Ordinal));
        Assert.AreEqual(CapabilitySupport.Unsupported, fixture.Service.GetCapabilities().ReasoningCachePreservation);
    }

    [TestMethod]
    [DataRow(ClaudeReasoningEffort.XHigh)]
    [DataRow(ClaudeReasoningEffort.Max)]
    public async Task NativeBetweenTools_RejectsExcessEffortBeforeTransport(ClaudeReasoningEffort effort)
    {
        using var fixture = new Fixture();
        fixture.Service.WithBetweenToolsThinking(effort);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("invalid effort"));
        Assert.IsEmpty(fixture.Requests);
    }

    [TestMethod]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.Error)]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.DropBlock)]
    public async Task NativeBetweenTools_RejectsBindingBeforeTransportAndHistory(ClaudeThinkingPrefixMismatchBehavior binding)
    {
        using var fixture = new Fixture();
        fixture.Service.WithBetweenToolsThinking().WithThinkingBinding(binding);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("invalid binding"));
        Assert.IsEmpty(fixture.Requests);
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.None, "between_tools", "high")]
    [DataRow(ReasoningLevel.Low, "adaptive", "low")]
    [DataRow(ReasoningLevel.Max, "adaptive", "max")]
    public async Task ExplicitCommonReasoning_OverridesNativeBetweenToolsForOneRequest(ReasoningLevel level, string type, string effort)
    {
        using var fixture = new Fixture();
        fixture.Service.WithBetweenToolsThinking(ClaudeReasoningEffort.Medium);
        await fixture.Service.CreateRequest("common override").WithReasoning(level).GetCompletionAsync();
        Assert.AreEqual(type, fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual(effort, fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        await fixture.Service.GetCompletionAsync("native restored");
        AssertBetweenTools(fixture.Requests[1]);
        Assert.AreEqual("medium", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Builder_CapturesUntouchedDefaultBeforeLaterExplicitBudgetOptOut()
    {
        using var fixture = new Fixture();
        var defaultRequest = fixture.Service.CreateRequest("captured default");
        fixture.Service.ThinkingBudget = -1;
        var disabledRequest = fixture.Service.CreateRequest("captured opt-out");
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Max);
        await defaultRequest.GetCompletionAsync();
        await disabledRequest.GetCompletionAsync();
        await fixture.Service.GetCompletionAsync("current max");
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        AssertBetweenTools(fixture.Requests[1]);
        Assert.AreEqual("max", fixture.Requests[2]["output_config"]?["effort"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task ExplicitAdaptiveAuto_ResetsLegacyMaxAndBetweenTools()
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingParameters(100000).WithBetweenToolsThinking(ClaudeReasoningEffort.Low);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Auto);
        await fixture.Service.GetCompletionAsync("native default");
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        fixture.Service.ThinkingBudget = -1;
        fixture.Service.ThinkingMode = ClaudeThinkingMode.Adaptive;
        await fixture.Service.GetCompletionAsync("explicit phase");
        Assert.AreEqual("adaptive", fixture.Requests[1]["thinking"]?["type"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task BetweenToolsBaseline_RejectsLaterCachePreservingEffort()
    {
        using var fixture = new Fixture();
        fixture.Service.WithBetweenToolsThinking();
        await fixture.Service.GetCompletionAsync("between");
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("cannot preserve effort"));
        Assert.AreEqual(1, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task PersistentCacheBaseline_RejectsLaterNativeBetweenTools()
    {
        using var fixture = new Fixture();
        await fixture.Service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("adaptive cached");
        fixture.Service.WithBetweenToolsThinking();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("cannot change thinking mode"));
        Assert.AreEqual(1, fixture.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CommonAdaptiveCacheEffort_OverridesNativeBetweenToolsAcrossCalls(bool useBuilder)
    {
        using var fixture = new Fixture();
        fixture.Service.WithBetweenToolsThinking(ClaudeReasoningEffort.High);
        await Request("first", ReasoningLevel.Low);
        await Request("second", ReasoningLevel.Medium);
        Assert.AreEqual(ClaudeThinkingMode.BetweenTools, fixture.Service.ThinkingMode);
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("low", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["thinking"], fixture.Requests[1]["thinking"]));
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[1]["output_config"]));
        Assert.AreEqual("medium", Messages(fixture.Requests[1]).Last(message => message["output_config"]?["effort"] != null)["output_config"]?["effort"]?.GetValue<string>());
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        AssertReplay(fixture.Responses[0], fixture.Requests[1]);

        Task<string> Request(string prompt, ReasoningLevel level) => useBuilder
            ? fixture.Service.CreateRequest(prompt).WithReasoning(level, CachePreservation.Required).GetCompletionAsync()
            : fixture.Service.WithReasoning(level, CachePreservation.Required).GetCompletionAsync(prompt);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CommonAdaptiveOverride_AllowsBindingButNoneRejectsBeforeTransport(bool useBuilder)
    {
        using var fixture = new Fixture();
        fixture.Service.WithBetweenToolsThinking().WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await Request("adaptive override", ReasoningLevel.Low);
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("low", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("error", fixture.Requests[0]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Request("incompatible none", ReasoningLevel.None));
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(ClaudeThinkingMode.BetweenTools, fixture.Service.ThinkingMode);

        Task<string> Request(string prompt, ReasoningLevel level) => useBuilder
            ? fixture.Service.CreateRequest(prompt).WithReasoning(level).GetCompletionAsync()
            : fixture.Service.WithReasoning(level).GetCompletionAsync(prompt);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AutomaticCompaction_PreservesBoundHistoryUnlessAdaptiveDropBlockIsExplicit(bool dropBlocks)
    {
        using var fixture = new Fixture();
        if (dropBlocks) fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
        await fixture.Service.GetCompletionAsync("first");
        var before = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);
        fixture.Service.ConversationPolicy = SummaryConversationPolicy.ByMessage(1, 0);
        await fixture.Service.ApplySummaryPolicyIfNeededAsync();
        Assert.AreEqual(dropBlocks ? 2 : 1, fixture.Requests.Count);
        if (dropBlocks) Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.Service.ConversationPolicy.CurrentSummary));
        else Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
    }

    [TestMethod]
    [DataRow(AIRequestPurpose.QueryRewrite)]
    [DataRow(AIRequestPurpose.Summarization)]
    public async Task IsolatedHelper_DisablesThinkingWithoutInheritingBindingOrConsumingTurnInstructions(AIRequestPurpose purpose)
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates)
            .WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error).WithTurnInstruction("answer-turn-only");
        await fixture.Service.GetCompletionAsync("isolated helper", new AIRequestProfile { Purpose = purpose, Stateless = true, DisableReasoning = true });
        AssertBetweenTools(fixture.Requests[0]);
        Assert.IsFalse(fixture.Betas[0].Contains(AnthropicFableLiveProbe.BindingBeta, StringComparison.Ordinal));
        Assert.IsFalse(fixture.Betas[0].Contains(AnthropicFableLiveProbe.UpdatesBeta, StringComparison.Ordinal));
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("answer-turn-only", StringComparison.Ordinal));
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages);
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, fixture.Service.ThinkingPrefixMismatchBehavior);
        await fixture.Service.GetCompletionAsync("answer");
        Assert.AreEqual("adaptive", fixture.Requests[1]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("error", fixture.Requests[1]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("answer-turn-only", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Betas[1].Contains(AnthropicFableLiveProbe.BindingBeta, StringComparison.Ordinal));
    }

    public sealed class TypedValue { public int Value { get; set; } }

    private static void AssertBetweenTools(JsonObject request)
    {
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"between_tools"}"""), request["thinking"]));
        Assert.IsTrue(request["output_config"]?["effort"] == null || new[] { "low", "medium", "high" }.Contains(request["output_config"]!["effort"]!.GetValue<string>()));
    }

    private static async Task Execute(AnthropicService service, string prompt, FableExecutionMode mode)
    {
        if (mode == FableExecutionMode.Completion) { await service.GetCompletionAsync(prompt); return; }
        if (mode == FableExecutionMode.Run) { await using var run = await service.StartRunAsync(prompt); await run.Result; return; }
        await foreach (var item in service.StreamAsync(prompt, StreamOptions.FullOptions)) Assert.AreNotEqual(StreamingContentType.Error, item.Type);
    }

    private static JsonObject[] Messages(JsonObject request) => request["messages"]!.AsArray().OfType<JsonObject>().ToArray();
    private static void AssertReplay(JsonObject response, JsonObject request) => Assert.IsTrue(Messages(request).Any(message =>
        message["role"]?.GetValue<string>() == "assistant" && JsonNode.DeepEquals(response["content"], message["content"])),
        "Replay must retain native block order, empty thinking, signatures, and tool correlation IDs.");
    private static void AssertPrefix(JsonObject before, JsonObject after)
    {
        Assert.IsTrue(JsonNode.DeepEquals(before["system"], after["system"]));
        Assert.IsTrue(JsonNode.DeepEquals(before["tools"], after["tools"]));
        var earlier = Messages(before); var later = Messages(after);
        Assert.IsTrue(later.Length >= earlier.Length);
        for (var index = 0; index < earlier.Length; index++) Assert.IsTrue(JsonNode.DeepEquals(earlier[index], later[index]));
    }

    private static JsonObject Reply(int index, string text = "answer", bool tool = false)
    {
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "empty-sig-" + index },
            new JsonObject { ["type"] = "thinking", ["thinking"] = "progress-" + index, ["signature"] = "sig-" + index },
            new JsonObject { ["type"] = "text", ["text"] = text }
        };
        if (tool) content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = "lookup-0", ["name"] = "lookup", ["input"] = new JsonObject() });
        return new JsonObject { ["id"] = "sonnet-response-" + index, ["model"] = AIModels.Anthropic.ClaudeSonnet5_5,
            ["content"] = content, ["stop_reason"] = tool ? "tool_use" : "end_turn",
            ["usage"] = new JsonObject { ["input_tokens"] = 2, ["output_tokens"] = 3 } };
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = [];
        public List<JsonObject> Responses { get; } = [];
        public List<string> Betas { get; } = [];
        public List<string> Paths { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Func<JsonObject, int, JsonObject> Respond { get; set; } = (_, index) => Reply(index);
        public Func<int, CancellationToken, Task>? BeforeResponse { get; set; }
        public Fixture(string model = AIModels.Anthropic.ClaudeSonnet5_5)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline-test-key", model, _http);
            Service.ActivateChat.SystemMessage = "fixed synthetic system";
            Service.DefaultPolicy.TimeoutSeconds = 10;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            var index = Requests.Count;
            Requests.Add(body);
            Paths.Add(request.RequestUri!.AbsolutePath);
            Betas.Add(request.Headers.TryGetValues("anthropic-beta", out var values) ? string.Join(",", values) : "");
            if (BeforeResponse != null) await BeforeResponse(index, token);
            var response = Respond(body, index);
            Responses.Add((JsonObject)response.DeepClone());
            var streaming = body["stream"]?.GetValue<bool>() == true && Status == HttpStatusCode.OK;
            return new HttpResponseMessage(Status) { Content = new StringContent(streaming ? ToSse(response) : response.ToJsonString(),
                Encoding.UTF8, streaming ? "text/event-stream" : "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }

    private static string ToSse(JsonObject response)
    {
        var frames = new List<JsonObject>();
        var message = (JsonObject)response.DeepClone(); message["content"] = new JsonArray();
        frames.Add(new JsonObject { ["type"] = "message_start", ["message"] = message });
        var index = 0;
        foreach (var raw in response["content"]!.AsArray().OfType<JsonObject>())
        {
            var block = (JsonObject)raw.DeepClone();
            var type = block["type"]!.GetValue<string>();
            if (type == "text") block["text"] = "";
            if (type == "thinking") { block["thinking"] = ""; block["signature"] = ""; }
            if (type == "tool_use") block["input"] = new JsonObject();
            frames.Add(new JsonObject { ["type"] = "content_block_start", ["index"] = index, ["content_block"] = block });
            if (type == "text") Delta("text_delta", "text", raw["text"]!.GetValue<string>());
            if (type == "thinking") { Delta("thinking_delta", "thinking", raw["thinking"]!.GetValue<string>()); Delta("signature_delta", "signature", raw["signature"]!.GetValue<string>()); }
            if (type == "tool_use") Delta("input_json_delta", "partial_json", raw["input"]!.ToJsonString());
            frames.Add(new JsonObject { ["type"] = "content_block_stop", ["index"] = index++ });
            void Delta(string deltaType, string field, string value) => frames.Add(new JsonObject { ["type"] = "content_block_delta", ["index"] = index,
                ["delta"] = new JsonObject { ["type"] = deltaType, [field] = value } });
        }
        frames.Add(new JsonObject { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = response["stop_reason"]!.DeepClone() } });
        frames.Add(new JsonObject { ["type"] = "message_stop" });
        return string.Concat(frames.Select(frame => "data: " + frame.ToJsonString() + "\n\n"));
    }
}
