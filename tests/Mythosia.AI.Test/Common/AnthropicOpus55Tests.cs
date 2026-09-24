using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicOpus55Tests
{
    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task DefaultRequest_UsesMediumAdaptiveWithoutSampling_AndPreservesThinking(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Service.Temperature = .7f;
        fixture.Service.TopP = .9f;
        await Execute(fixture.Service, "first", mode);
        await Execute(fixture.Service, "second", mode);
        foreach (var request in fixture.Requests)
        {
            Assert.AreEqual(AIModels.Anthropic.ClaudeOpus5_5, request["model"]?.GetValue<string>());
            Assert.AreEqual("adaptive", request["thinking"]?["type"]?.GetValue<string>());
            Assert.AreEqual("medium", request["output_config"]?["effort"]?.GetValue<string>());
            Assert.IsFalse(request.ContainsKey("temperature"));
            Assert.IsFalse(request.ContainsKey("top_p"));
            Assert.IsFalse(request.ContainsKey("top_k"));
            Assert.IsNull(request["thinking"]?["budget_tokens"]);
            Assert.IsNull(request["thinking"]?["display"]);
        }
        AssertNativeReplay(fixture.Responses[0], fixture.Requests[1]);
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.Low, "low")]
    [DataRow(ReasoningLevel.Medium, "medium")]
    [DataRow(ReasoningLevel.High, "high")]
    [DataRow(ReasoningLevel.XHigh, "xhigh")]
    [DataRow(ReasoningLevel.Max, "max")]
    public async Task RequestBuilderReasoning_MapsSupportedLevels_AndLeavesServiceDefaults(ReasoningLevel level, string wire)
    {
        using var fixture = new Fixture();
        var builder = fixture.Service.CreateRequest("explicit effort").WithReasoning(level);
        Assert.AreEqual(CapabilitySupport.Supported, builder.GetCapabilities().GetReasoningSupport(level));
        await builder.GetCompletionAsync();
        Assert.AreEqual(wire, fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.IsNull(fixture.Requests[0]["thinking"]?["budget_tokens"]);
        await fixture.Service.GetCompletionAsync("default again");
        Assert.AreEqual("medium", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(-1, fixture.Service.ThinkingBudget);
        Assert.AreEqual(ClaudeReasoningEffort.Auto, fixture.Service.AdaptiveThinkingEffort);
    }

    [TestMethod]
    [DataRow(ClaudeReasoningEffort.Auto, "medium")]
    [DataRow(ClaudeReasoningEffort.Low, "low")]
    [DataRow(ClaudeReasoningEffort.Medium, "medium")]
    [DataRow(ClaudeReasoningEffort.High, "high")]
    [DataRow(ClaudeReasoningEffort.XHigh, "xhigh")]
    [DataRow(ClaudeReasoningEffort.Max, "max")]
    public async Task NativeEffort_UsesAdaptiveAndSummarizedDisplay(ClaudeReasoningEffort effort, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(effort, ClaudeThinkingDisplay.Summarized);
        await fixture.Service.GetCompletionAsync("native effort");
        Assert.AreEqual(wire, fixture.Requests.Single()["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("summarized", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
        Assert.IsNull(fixture.Requests[0]["thinking"]?["budget_tokens"]);
    }

    [TestMethod]
    [DataRow(8192, "high")]
    [DataRow(32768, "xhigh")]
    [DataRow(100000, "max")]
    public async Task LegacyBudget_MapsToEffortWithoutSendingManualBudget(int budget, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.ThinkingBudget = budget;
        await fixture.Service.GetCompletionAsync("legacy configuration");
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual(wire, fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsNull(fixture.Requests[0]["thinking"]?["budget_tokens"]);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.None)]
    [DataRow(ReasoningLevel.Minimal)]
    public async Task UnsupportedCommonReasoning_RejectsBeforeTransportAndHistory(ReasoningLevel level)
    {
        using var fixture = new Fixture();
        Assert.AreEqual(CapabilitySupport.Unsupported, fixture.Service.GetCapabilities().GetReasoningSupport(level));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.CreateRequest("rejected").WithReasoning(level).GetCompletionAsync());
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task ForcedTool_RejectsBeforeTransportAndHistory(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Service.ForceFunctionName = "lookup";
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => throw new InvalidOperationException("Must not execute.") });
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Execute(fixture.Service, "force lookup", mode));
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TokenCount_RejectsForcedToolWithoutTransport(bool promptOnly)
    {
        using var fixture = new Fixture();
        fixture.Service.ForceFunctionName = "lookup";
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => promptOnly
            ? fixture.Service.GetInputTokenCountAsync("count") : fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual(0, fixture.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AssistantPrefill_RejectsDirectAndAdditionalMessageInput(bool additional)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => additional
            ? fixture.Service.GetCompletionAsync("question", context: new AIRequestContext { AdditionalMessages = [new Message(ActorRole.Assistant, "prefill")] })
            : fixture.Service.GetCompletionAsync(new Message(ActorRole.Assistant, "prefill")));
        Assert.AreEqual(0, fixture.Requests.Count);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task ToolRound_PreservesRawThinkingAndAutomaticSelection(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Respond = (_, index) => Reply(index, index == 0 ? "" : "done", tool: index == 0);
        var calls = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => { calls++; return Task.FromResult("tool-proof"); } });
        await Execute(fixture.Service, "lookup then answer", mode);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.IsTrue(fixture.Requests.All(body => body["tool_choice"]?["type"]?.GetValue<string>() == "auto"));
        AssertNativeReplay(fixture.Responses[0], fixture.Requests[1]);
        var result = Messages(fixture.Requests[1]).SelectMany(m => m["content"] is JsonArray blocks ? blocks.OfType<JsonObject>() : [])
            .Single(b => b["type"]?.GetValue<string>() == "tool_result");
        Assert.AreEqual("tool-0", result["tool_use_id"]?.GetValue<string>());
        Assert.AreEqual("tool-proof", result["content"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Updates_StreamDeliversReasoningWithRequiredBeta()
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);
        var events = new List<StreamingContent>();
        await foreach (var item in fixture.Service.StreamAsync("updates", StreamOptions.FullOptions)) events.Add(item);
        Assert.AreEqual("updates", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
        StringAssert.Contains(fixture.Betas[0], AnthropicFableLiveProbe.UpdatesBeta);
        Assert.AreEqual("progress-0", string.Concat(events.Where(e => e.Type == StreamingContentType.Reasoning).Select(e => e.Content)));
    }

    [TestMethod]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.Error)]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.DropBlock)]
    public async Task PrefixPolicyAndTurnInstructions_KeepHistoricalPrefixAndBinding(ClaudeThinkingPrefixMismatchBehavior policy)
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingBinding(policy).WithTurnInstruction("first turn only");
        await fixture.Service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        await fixture.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("second");
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        AssertNativeReplay(fixture.Responses[0], fixture.Requests[1]);
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["thinking"], fixture.Requests[1]["thinking"]));
        Assert.AreEqual(policy == ClaudeThinkingPrefixMismatchBehavior.Error ? "error" : "drop_block",
            fixture.Requests[1]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        Assert.AreEqual(1, Messages(fixture.Requests[1]).Count(message => message["clear_at"]?.GetValue<string>() == "next_user_message"));
        Assert.AreEqual("low", Messages(fixture.Requests[1]).Last(message => message["output_config"]?["effort"] != null)["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsTrue(fixture.Betas.All(beta => beta.Contains(AnthropicFableLiveProbe.BindingBeta) && beta.Contains(AnthropicFableLiveProbe.TurnBeta)));
    }

    [TestMethod]
    public async Task CompletedConversationTokenCount_PreservesSignedHistoryWithoutPrefillFailure()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("first");
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);
        fixture.Respond = (_, _) => new JsonObject { ["input_tokens"] = 42 };
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual("/v1/messages/count_tokens", fixture.Paths[1]);
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
        AssertNativeReplay(fixture.Responses[0], fixture.Requests[1]);
        Assert.AreEqual("assistant", Messages(fixture.Requests[1]).Last()["role"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task InternalDisableReasoningProfile_UsesLowOmittedAndRestoresNativeDefaults()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("helper", new AIRequestProfile { DisableReasoning = true });
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("omitted", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
        Assert.AreEqual("low", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        await fixture.Service.GetCompletionAsync("normal");
        Assert.AreEqual("medium", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task AutoCachePreservation_UsesMediumMarkerAndKeepsTopLevelWhenEffortChanges()
    {
        using var fixture = new Fixture();
        await fixture.Service.WithReasoning(ReasoningLevel.Auto, CachePreservation.Required).GetCompletionAsync("first");
        await fixture.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("second");
        Assert.AreEqual("medium", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("medium", Messages(fixture.Requests[0]).Last(message => message["output_config"]?["effort"] != null)["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[1]["output_config"]));
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["thinking"], fixture.Requests[1]["thinking"]));
        Assert.AreEqual("low", Messages(fixture.Requests[1]).Last(message => message["output_config"]?["effort"] != null)["output_config"]?["effort"]?.GetValue<string>());
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        Assert.IsTrue(fixture.Betas.All(beta => beta.Contains(AnthropicFableLiveProbe.EffortBeta)));
    }
    [TestMethod]
    [DataRow(FableExecutionMode.Completion, "content")]
    [DataRow(FableExecutionMode.Completion, "text-block")]
    [DataRow(FableExecutionMode.Completion, "image")]
    [DataRow(FableExecutionMode.Run, "content")]
    [DataRow(FableExecutionMode.Run, "text-block")]
    [DataRow(FableExecutionMode.Run, "image")]
    [DataRow(FableExecutionMode.LegacyStream, "content")]
    [DataRow(FableExecutionMode.LegacyStream, "text-block")]
    [DataRow(FableExecutionMode.LegacyStream, "image")]
    public async Task EditedAssistantHistory_RejectsBeforeHttpAndPreservesSignedContent(FableExecutionMode mode, string edit)
    {
        using var fixture = new Fixture();
        await Execute(fixture.Service, "first", mode);
        var assistant = fixture.Service.ActivateChat.Messages.Single(message => message.Role == ActorRole.Assistant);
        var originalText = assistant.Content;
        var originalContents = assistant.Contents;
        var originalMetadata = JsonSerializer.Serialize(assistant.Metadata);
        EditAssistant(assistant, edit);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Execute(fixture.Service, "rejected follow-up", mode));
        Assert.AreEqual(1, fixture.Requests.Count, "A changed public assistant message must not be silently replaced by hidden native content.");
        Assert.AreEqual(originalMetadata, JsonSerializer.Serialize(assistant.Metadata), "Validation must not alter signed content to accommodate an edit.");
        assistant.Content = originalText;
        assistant.Contents = originalContents;
        await Execute(fixture.Service, "restored follow-up", mode);
        Assert.AreEqual(2, fixture.Requests.Count);
        AssertNativeReplay(fixture.Responses[0], fixture.Requests[1]);
    }

    [TestMethod]
    [DataRow("content")]
    [DataRow("text-block")]
    [DataRow("image")]
    public async Task TokenCount_RejectsEditedAssistantBeforeHttpAndRecoversAfterRestore(string edit)
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("first");
        var assistant = fixture.Service.ActivateChat.Messages.Single(message => message.Role == ActorRole.Assistant);
        var originalText = assistant.Content;
        var originalContents = assistant.Contents;
        var originalMetadata = JsonSerializer.Serialize(assistant.Metadata);
        EditAssistant(assistant, edit);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(originalMetadata, JsonSerializer.Serialize(assistant.Metadata));
        assistant.Content = originalText;
        assistant.Contents = originalContents;
        fixture.Respond = (_, _) => new JsonObject { ["input_tokens"] = 42 };
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        AssertNativeReplay(fixture.Responses[0], fixture.Requests[1]);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task AssistantLocalMetadataAndTimestampEdits_DoNotInvalidateNativeHistory(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        await Execute(fixture.Service, "first", mode);
        var assistant = fixture.Service.ActivateChat.Messages.Single(message => message.Role == ActorRole.Assistant);
        assistant.Metadata!["local_display_note"] = "reviewed";
        assistant.Timestamp = assistant.Timestamp.AddDays(1);
        await Execute(fixture.Service, "second", mode);
        Assert.AreEqual(2, fixture.Requests.Count);
        AssertNativeReplay(fixture.Responses[0], fixture.Requests[1]);
        Assert.IsFalse(fixture.Requests[1].ToJsonString().Contains("local_display_note", StringComparison.Ordinal));
    }

    private static void EditAssistant(Message assistant, string edit)
    {
        if (edit == "content") assistant.Content = "The user explicitly replaced this assistant reply.";
        else if (edit == "text-block") assistant.Contents = [new TextContent("Edited public text block.")];
        else assistant.Contents = [new ImageContent(new byte[] { 1, 2, 3 }, "image/png")];
    }
    [TestMethod]
    public async Task ExplicitAdaptiveAuto_ResetsPreviousLegacyBudgetToMedium()
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingParameters(100000);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Auto);
        await fixture.Service.GetCompletionAsync("Use the model default effort.");
        Assert.AreEqual("medium", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.IsNull(fixture.Requests[0]["thinking"]?["budget_tokens"]);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion, "content")]
    [DataRow(FableExecutionMode.Completion, "text-block")]
    [DataRow(FableExecutionMode.Completion, "image")]
    [DataRow(FableExecutionMode.Run, "content")]
    [DataRow(FableExecutionMode.Run, "text-block")]
    [DataRow(FableExecutionMode.Run, "image")]
    [DataRow(FableExecutionMode.LegacyStream, "content")]
    [DataRow(FableExecutionMode.LegacyStream, "text-block")]
    [DataRow(FableExecutionMode.LegacyStream, "image")]
    public async Task EditedAssistantToolHistory_RejectsBeforeHttpAndRestoresExactRawBatch(FableExecutionMode mode, string edit)
    {
        using var fixture = new Fixture();
        ConfigureToolReply(fixture);
        await Execute(fixture.Service, "lookup then answer", mode);
        Assert.AreEqual(2, fixture.Requests.Count);
        var assistant = fixture.Service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        Assert.AreEqual("Checking the inventory.", assistant.Content);
        Assert.HasCount(0, assistant.Contents);
        var originalText = assistant.Content;
        var originalContents = assistant.Contents;
        var originalNative = assistant.FunctionCallBatch!.Metadata![MessageMetadataKeys.OriginalContent].ToString();
        EditAssistant(assistant, edit);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Execute(fixture.Service, "rejected follow-up", mode));
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual(originalNative, assistant.FunctionCallBatch.Metadata[MessageMetadataKeys.OriginalContent].ToString());
        assistant.Content = originalText;
        assistant.Contents = originalContents;
        await Execute(fixture.Service, "restored follow-up", mode);
        Assert.AreEqual(3, fixture.Requests.Count);
        AssertRawToolReplay(fixture.Responses[0], fixture.Requests[2]);
    }

    [TestMethod]
    [DataRow("content")]
    [DataRow("text-block")]
    [DataRow("image")]
    public async Task TokenCount_RejectsEditedAssistantToolHistoryAndRecoversAfterRestore(string edit)
    {
        using var fixture = new Fixture();
        ConfigureToolReply(fixture);
        await fixture.Service.GetCompletionAsync("lookup then answer");
        var assistant = fixture.Service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        var originalText = assistant.Content;
        var originalContents = assistant.Contents;
        var originalNative = assistant.FunctionCallBatch!.Metadata![MessageMetadataKeys.OriginalContent].ToString();
        EditAssistant(assistant, edit);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual(originalNative, assistant.FunctionCallBatch.Metadata[MessageMetadataKeys.OriginalContent].ToString());
        assistant.Content = originalText;
        assistant.Contents = originalContents;
        fixture.Respond = (_, _) => new JsonObject { ["input_tokens"] = 42 };
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual(3, fixture.Requests.Count);
        AssertRawToolReplay(fixture.Responses[0], fixture.Requests[2]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LegacyAssistantToolMetadata_AlsoRejectsEditedTextBeforeHttp(bool countTokens)
    {
        using var fixture = new Fixture();
        ConfigureToolReply(fixture);
        await fixture.Service.GetCompletionAsync("lookup then answer");
        var assistant = fixture.Service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        // Convert the captured single-tool message to the supported legacy metadata
        // representation, retaining its exact original content and correlation IDs.
        var originalNative = assistant.FunctionCallBatch!.Metadata![MessageMetadataKeys.OriginalContent];
        assistant.Metadata![MessageMetadataKeys.OriginalContent] = originalNative;
        assistant.FunctionCallBatch = null;
        var originalText = assistant.Content;
        assistant.Content = "An explicit edit to the legacy tool narration.";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            if (countTokens) await fixture.Service.GetInputTokenCountAsync();
            else await fixture.Service.GetCompletionAsync("rejected follow-up");
        });
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreSame(originalNative, assistant.Metadata[MessageMetadataKeys.OriginalContent]);
        assistant.Content = originalText;
        if (countTokens)
        {
            fixture.Respond = (_, _) => new JsonObject { ["input_tokens"] = 42 };
            Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        }
        else await fixture.Service.GetCompletionAsync("restored follow-up");
        Assert.AreEqual(3, fixture.Requests.Count);
        AssertRawToolReplay(fixture.Responses[0], fixture.Requests[2]);
    }

    private static void ConfigureToolReply(Fixture fixture)
    {
        fixture.Respond = (_, index) => Reply(index, index == 0 ? "Checking the inventory." : "done", tool: index == 0);
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("tool-proof") });
    }
    private static void AssertRawToolReplay(JsonObject response, JsonObject request)
        => Assert.IsTrue(Messages(request).Any(message => message["role"]?.GetValue<string>() == "assistant" &&
            JsonNode.DeepEquals(response["content"], message["content"])), "The entire original thinking/text/tool-use sequence must be replayed unchanged after restoration.");
    private static async Task Execute(AnthropicService service, string input, FableExecutionMode mode)
    {
        if (mode == FableExecutionMode.Completion) { await service.GetCompletionAsync(input); return; }
        if (mode == FableExecutionMode.Run) { await using var run = await service.StartRunAsync(input); await run.Result; return; }
        await foreach (var item in service.StreamAsync(input, StreamOptions.FullOptions)) Assert.AreNotEqual(StreamingContentType.Error, item.Type);
    }
    private static JsonObject[] Messages(JsonObject body) => body["messages"]!.AsArray().OfType<JsonObject>().ToArray();
    private static void AssertPrefix(JsonObject before, JsonObject after)
    {
        Assert.IsTrue(JsonNode.DeepEquals(before["system"], after["system"]));
        Assert.IsTrue(JsonNode.DeepEquals(before["tools"], after["tools"]));
        var earlier = Messages(before); var later = Messages(after);
        Assert.IsTrue(later.Length >= earlier.Length);
        for (var i = 0; i < earlier.Length; i++) Assert.IsTrue(JsonNode.DeepEquals(earlier[i], later[i]));
    }
    private static void AssertNativeReplay(JsonObject response, JsonObject request)
    {
        var assistant = Messages(request).Last(message => message["role"]?.GetValue<string>() == "assistant");
        Assert.IsTrue(JsonNode.DeepEquals(response["content"], assistant["content"]), "Native content, order, signatures and tool blocks must survive replay unchanged.");
    }
    private static JsonObject Reply(int index, string text = "answer", bool tool = false)
    {
        var blocks = new JsonArray
        {
            new JsonObject { ["type"] = "thinking", ["thinking"] = "progress-" + index, ["signature"] = "sig-" + index },
            new JsonObject { ["type"] = "text", ["text"] = text }
        };
        if (tool) blocks.Add(new JsonObject { ["type"] = "tool_use", ["id"] = "tool-0", ["name"] = "lookup", ["input"] = new JsonObject() });
        return new JsonObject
        {
            ["id"] = "response-" + index, ["model"] = AIModels.Anthropic.ClaudeOpus5_5,
            ["content"] = blocks, ["stop_reason"] = tool ? "tool_use" : "end_turn",
            ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 }
        };
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = new();
        public List<JsonObject> Responses { get; } = new();
        public List<string> Paths { get; } = new();
        public List<string> Betas { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Func<JsonObject, int, JsonObject> Respond { get; set; } = (_, index) => Reply(index);
        public Func<int, CancellationToken, Task>? BeforeResponseAsync { get; set; }
        public bool TransformationsOnFinalDelta { get; set; }
        public Fixture(string model = AIModels.Anthropic.ClaudeOpus5_5)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline-test-key", model, _http);
            Service.ActivateChat.SystemMessage = "fixed system";
            Service.DefaultPolicy.TimeoutSeconds = 10;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var index = Requests.Count;
            Requests.Add(body);
            Paths.Add(request.RequestUri!.AbsolutePath);
            Betas.Add(request.Headers.TryGetValues("anthropic-beta", out var values) ? string.Join(",", values) : "");
            if (BeforeResponseAsync != null) await BeforeResponseAsync(index, cancellationToken);
            var response = Respond(body, index);
            Responses.Add((JsonObject)response.DeepClone());
            var stream = body["stream"]?.GetValue<bool>() == true && Status == HttpStatusCode.OK;
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(stream ? StreamResponse(response, TransformationsOnFinalDelta) : response.ToJsonString(), Encoding.UTF8,
                    stream ? "text/event-stream" : "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }

    private static string StreamResponse(JsonObject response, bool transformationsOnFinalDelta = false)
    {
        var frames = new List<JsonObject>();
        var start = (JsonObject)response.DeepClone(); start["content"] = new JsonArray();
        if (transformationsOnFinalDelta) start.Remove("input_transformations");
        frames.Add(new JsonObject { ["type"] = "message_start", ["message"] = start });
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
            frames.Add(new JsonObject { ["type"] = "content_block_stop", ["index"] = index });
            index++;
            void Delta(string deltaType, string field, string value) => frames.Add(new JsonObject
            {
                ["type"] = "content_block_delta", ["index"] = index,
                ["delta"] = new JsonObject { ["type"] = deltaType, [field] = value }
            });
        }
        var finalDelta = new JsonObject { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = response["stop_reason"]!.DeepClone() } };
        if (transformationsOnFinalDelta) finalDelta["input_transformations"] = response["input_transformations"]?.DeepClone();
        frames.Add(finalDelta);
        frames.Add(new JsonObject { ["type"] = "message_stop" });
        return string.Concat(frames.Select(frame => "data: " + frame.ToJsonString() + "\n\n"));
    }
}
