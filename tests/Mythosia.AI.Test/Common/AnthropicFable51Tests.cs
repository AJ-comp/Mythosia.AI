using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicFable51Tests
{
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1, FableExecutionMode.Completion)]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1, FableExecutionMode.Run)]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1, FableExecutionMode.LegacyStream)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5_1, FableExecutionMode.Completion)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5_1, FableExecutionMode.Run)]
    public async Task ForcedToolSelection_RejectsBeforeTransportAndHistory(string model, FableExecutionMode mode)
    {
        using var fixture = new Fixture(model);
        fixture.Service.ForceFunctionName = "lookup";
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => throw new InvalidOperationException("Must not execute.") });
        fixture.Service.WithTurnInstruction("must be consumed on failure");
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Execute(fixture.Service, "rejected", mode));
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
        fixture.Service.ForceFunctionName = null;
        await fixture.Service.GetCompletionAsync("next");
        Assert.IsFalse(fixture.Requests.Single().ToJsonString().Contains("must be consumed on failure", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5)]
    public async Task Claude50_StillAcceptsSpecificToolChoiceWithout51Betas(string model)
    {
        using var fixture = new Fixture(model);
        fixture.Service.ForceFunctionName = "lookup";
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("done") });
        await fixture.Service.GetCompletionAsync("first");
        Assert.AreEqual("tool", fixture.Requests[0]["tool_choice"]?["type"]?.GetValue<string>());
        Assert.AreEqual("lookup", fixture.Requests[0]["tool_choice"]?["name"]?.GetValue<string>());
        Assert.IsFalse(fixture.Betas[0].Contains(AnthropicFableLiveProbe.UpdatesBeta));
        Assert.IsFalse(fixture.Betas[0].Contains(AnthropicFableLiveProbe.BindingBeta));
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task OrdinaryResponses_PreserveNativeThinkingOrderAndSignatureOnNextTurn(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        await Execute(fixture.Service, "first", mode);
        await Execute(fixture.Service, "second", mode);
        var assistant = Messages(fixture.Requests[1]).Single(message => message["role"]!.GetValue<string>() == "assistant");
        var blocks = assistant["content"]!.AsArray();
        Assert.AreEqual("thinking", blocks[0]?["type"]?.GetValue<string>());
        Assert.AreEqual("sig-0", blocks[0]?["signature"]?.GetValue<string>());
        Assert.AreEqual("progress-0", blocks[0]?["thinking"]?.GetValue<string>());
        Assert.AreEqual("text", blocks[1]?["type"]?.GetValue<string>());
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
    }

    [TestMethod]
    public async Task Updates_UsesBetaAndDeliversActualThinkingDelta()
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        var events = new List<StreamingContent>();
        await foreach (var item in fixture.Service.StreamAsync("progress", StreamOptions.FullOptions)) events.Add(item);
        Assert.AreEqual("updates", fixture.Requests.Single()["thinking"]?["display"]?.GetValue<string>());
        Assert.IsTrue(fixture.Betas.Single().Contains(AnthropicFableLiveProbe.UpdatesBeta));
        Assert.AreEqual("progress-0", string.Concat(events.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content)));
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5_1)]
    public async Task DisableReasoningProfile_UsesLowOmittedThenRestoresUpdates(string model)
    {
        using var fixture = new Fixture(model);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        await fixture.Service.GetCompletionAsync("quiet task", new AIRequestProfile { DisableReasoning = true });
        Assert.AreEqual("omitted", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
        Assert.AreEqual("low", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(ClaudeThinkingDisplay.Updates, fixture.Service.AdaptiveThinkingDisplay);
        Assert.AreEqual(ClaudeReasoningEffort.High, fixture.Service.AdaptiveThinkingEffort);
        await fixture.Service.GetCompletionAsync("normal task");
        Assert.AreEqual("updates", fixture.Requests[1]["thinking"]?["display"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsTrue(fixture.Betas[1].Contains(AnthropicFableLiveProbe.UpdatesBeta));
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    public async Task Updates_OnUnsupportedModelRejectsBeforeRequest(string model)
    {
        using var fixture = new Fixture(model);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("reject"));
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task TypedRepair_KeepsNativeHistoryAndTransientSchemaOutOfTopLevelSystem()
    {
        using var fixture = new Fixture();
        fixture.Respond = (_, index) => Reply(index, index == 0 ? "not-json" : index == 1 ? "{\"Value\":42}" : "ordinary");
        fixture.Service.WithTurnInstruction("typed instruction");
        var value = await fixture.Service.GetCompletionAsync<TypedValue>("return a typed result");
        Assert.AreEqual(42, value.Value);
        await fixture.Service.GetCompletionAsync("ordinary follow-up");
        Assert.AreEqual(3, fixture.Requests.Count);
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        AssertPrefix(fixture.Requests[1], fixture.Requests[2]);
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["system"], fixture.Requests[2]["system"]));
        Assert.IsFalse(fixture.Requests[2]["system"]?.ToJsonString().Contains("STRUCTURED OUTPUT", StringComparison.Ordinal) == true);
        Assert.IsTrue(Messages(fixture.Requests[2]).Where(message => message["content"] is JsonArray)
            .SelectMany(message => message["content"]!.AsArray().OfType<JsonObject>())
            .Any(block => block["signature"]?.GetValue<string>() == "sig-0"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RagOverride_PersistsSentContextWhileKeepingOriginalUserHistory(bool useRun)
    {
        using var fixture = new Fixture();
        var rag = fixture.Service.WithRag(builder => builder.AddText("LOCAL_RAG_PROOF shipping takes three days.", id: "proof").UseLocalEmbedding(64));
        if (useRun)
        {
            await using var run = await rag.StartRunAsync("shipping");
            await run.Result;
        }
        else await rag.GetCompletionAsync("shipping");
        Assert.AreEqual("shipping", fixture.Service.ActivateChat.Messages.First(message => message.Role == ActorRole.User).Content);
        Assert.IsTrue(fixture.Requests[0].ToJsonString().Contains("LOCAL_RAG_PROOF", StringComparison.Ordinal));
        await fixture.Service.GetCompletionAsync("a plain follow-up");
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
    }

    [TestMethod]
    public async Task RequestContext_AdditionalMessagesAndDynamicInstructionsStayInHistoricalPrefix()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("original", context: new AIRequestContext
        {
            RequestMessageOverride = new Message(ActorRole.User, "augmented"),
            AdditionalMessages = new List<Message> { new(ActorRole.User, "reference-only") },
            SystemMessagePrefix = "prefix-for-first-turn",
            SystemMessageSuffix = "suffix-for-first-turn"
        });
        await fixture.Service.GetCompletionAsync("next");
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        var scoped = Messages(fixture.Requests[0]).Where(message => message["clear_at"]?.GetValue<string>() == "next_user_message").ToArray();
        Assert.AreEqual(2, scoped.Length);
        Assert.IsFalse(fixture.Requests[0]["system"]?.ToJsonString().Contains("first-turn", StringComparison.Ordinal) == true);
    }

    [TestMethod]
    public async Task TurnInstruction_ReappendsAfterToolResultAndLeavesClearedCopiesUntouched()
    {
        using var fixture = new Fixture();
        fixture.Respond = (_, index) => index == 0 ? Reply(0, "", tool: true) : Reply(index, "done");
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("lookup-result") });
        fixture.Service.WithTurnInstruction("one logical task").WithConversationInstruction("persistent operator guidance");
        await fixture.Service.GetCompletionAsync("use lookup");
        await fixture.Service.GetCompletionAsync("next task");
        Assert.AreEqual(3, fixture.Requests.Count);
        Assert.AreEqual(1, Messages(fixture.Requests[0]).Count(IsTurnInstruction));
        Assert.AreEqual(2, Messages(fixture.Requests[1]).Count(IsTurnInstruction));
        Assert.AreEqual(2, Messages(fixture.Requests[2]).Count(IsTurnInstruction));
        Assert.AreEqual(1, Messages(fixture.Requests[2]).Count(message => message["content"]?.ToJsonString().Contains("persistent operator guidance", StringComparison.Ordinal) == true));
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        AssertPrefix(fixture.Requests[1], fixture.Requests[2]);
        Assert.IsTrue(fixture.Betas.All(beta => beta.Contains(AnthropicFableLiveProbe.TurnBeta)));
        static bool IsTurnInstruction(JsonObject message) => message["clear_at"]?.GetValue<string>() == "next_user_message";
    }

    [TestMethod]
    [DataRow(AIRequestPurpose.QueryRewrite)]
    [DataRow(AIRequestPurpose.Summarization)]
    public async Task InternalProfile_DoesNotConsumeOrInheritQueuedProviderInstructions(AIRequestPurpose purpose)
    {
        using var fixture = new Fixture();
        fixture.Service.WithTurnInstruction("answer-only");
        await fixture.Service.GetCompletionAsync("internal helper", profile: new AIRequestProfile { Purpose = purpose });
        await fixture.Service.GetCompletionAsync("answer");
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("answer-only", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("answer-only", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Run_CapturesProviderOptionsAndRetainsInstructionsQueuedAfterItStarts()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeResponseAsync = async (index, token) =>
        {
            if (index != 0) return;
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error).WithTurnInstruction("first-task-only");
        await using var run = await fixture.Service.StartRunAsync("first");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock).WithTurnInstruction("next-task-only");
        }
        finally { release.TrySetResult(); }
        await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Service.GetCompletionAsync("next");
        Assert.AreEqual("error", fixture.Requests[0]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        Assert.AreEqual("drop_block", fixture.Requests[1]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("next-task-only", StringComparison.Ordinal));
        Assert.AreEqual(1, Messages(fixture.Requests[1]).Count(message => message["content"]?.ToJsonString().Contains("next-task-only", StringComparison.Ordinal) == true));
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RagQueryRewrite_IsolatedFromQueuedInstructionsButAnswerInheritsThem(bool useRun)
    {
        using var fixture = new Fixture();
        fixture.Respond = (_, index) => Reply(index, index == 0 ? "shipping\nKEYWORDS: shipping" : "answer");
        var rag = fixture.Service.WithRag(builder => builder.AddText("shipping context", id: "source").UseLocalEmbedding(64).WithQueryRewriter());
        fixture.Service.WithTurnInstruction("answer-only").WithConversationInstruction("persistent-answer-guidance");
        if (useRun) { await using var run = await rag.StartRunAsync("shipping"); await run.Result; }
        else await rag.GetCompletionAsync("shipping");
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("answer-only", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("persistent-answer-guidance", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("answer-only", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("persistent-answer-guidance", StringComparison.Ordinal));
        await fixture.Service.GetCompletionAsync("next");
        Assert.AreEqual(1, Messages(fixture.Requests[2]).Count(message => message["clear_at"] != null));
        AssertPrefix(fixture.Requests[1], fixture.Requests[2]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RagPreparationFailure_ConsumesProviderInstructions(bool useRun)
    {
        using var fixture = new Fixture();
        var rag = fixture.Service.WithRag(builder => builder.AddText("unused", id: "source").UseLocalEmbedding(64).WithQueryRewriter(new FailingRewriter()));
        fixture.Service.WithTurnInstruction("failed-task-only").WithConversationInstruction("failed-persistent-guidance");
        if (useRun) await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await rag.StartRunAsync("question"));
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => rag.GetCompletionAsync("question"));
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
        await fixture.Service.GetCompletionAsync("next");
        Assert.IsFalse(fixture.Requests.Single().ToJsonString().Contains("failed-", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AutomaticSummary_DoesNotConsumePendingTurnInstructions()
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
        await fixture.Service.GetCompletionAsync("previous turn");
        fixture.Service.ConversationPolicy = SummaryConversationPolicy.ByMessage(1, 0);
        fixture.Service.WithTurnInstruction("next-answer-only");
        await fixture.Service.ApplySummaryPolicyIfNeededAsync();
        Assert.AreEqual(2, fixture.Requests.Count, "An actual internal summary must have been sent.");
        Assert.IsFalse(fixture.Requests[1].ToJsonString().Contains("next-answer-only", StringComparison.Ordinal));
        Assert.IsFalse(string.IsNullOrEmpty(fixture.Service.ConversationPolicy.CurrentSummary));
        fixture.Service.ConversationPolicy.TriggerCount = null;
        await fixture.Service.GetCompletionAsync("next answer");
        Assert.IsTrue(fixture.Requests[2].ToJsonString().Contains("next-answer-only", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PublicTextContentEdit_ChangesWireWhileKeepingSignedThinking()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync(new Message(ActorRole.User, new TextContent("original")));
        fixture.Service.ActivateChat.Messages[0].Contents.OfType<TextContent>().Single().Text = "explicit-text-edit";
        await fixture.Service.GetCompletionAsync("next");
        Assert.IsTrue(Messages(fixture.Requests[1])[0]["content"]!.ToJsonString().Contains("explicit-text-edit", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("sig-0", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LocalMetadataAndTimestampChanges_DoNotDiscardOriginalRequestOverride()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("original", context: new AIRequestContext
        {
            RequestMessageOverride = new Message(ActorRole.User, "RAG_OVERRIDE_MUST_REMAIN")
        });
        var user = fixture.Service.ActivateChat.Messages[0];
        user.Timestamp = user.Timestamp.AddHours(1);
        user.Metadata = new() { ["local_callback"] = (Action)(() => { }) };
        user.Metadata["local_cycle"] = user.Metadata;
        await fixture.Service.GetCompletionAsync("next");
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        Assert.IsTrue(Messages(fixture.Requests[1])[0]["content"]!.ToJsonString().Contains("RAG_OVERRIDE_MUST_REMAIN", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompletedHistory_CanBeCountedWithoutWeakeningGenerationPrefillValidation()
    {
        using var fixture = new Fixture();
        await fixture.Service.GetCompletionAsync("original");
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);
        fixture.Respond = (_, _) => new JsonObject { ["input_tokens"] = 23 };
        Assert.AreEqual(23u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual("/v1/messages/count_tokens", fixture.Paths[1]);
        Assert.AreEqual("assistant", Messages(fixture.Requests[1]).Last()["role"]?.GetValue<string>());
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("sig-0", StringComparison.Ordinal));
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync(new Message(ActorRole.Assistant, "prefill")));
        Assert.AreEqual(2, fixture.Requests.Count);
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task Updates_FromToolRoundRemainAvailableWhenFinalRoundHasOnlyText(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        fixture.Respond = (_, index) =>
        {
            var response = Reply(index, "done", tool: index == 0);
            if (index != 0) response["content"]!.AsArray().RemoveAt(0);
            return response;
        };
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "lookup", Handler = _ => Task.FromResult("tool-result") });
        await Execute(fixture.Service, "lookup", mode);
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual("progress-0", fixture.Service.LastThinkingContent);
        await fixture.Service.GetCompletionAsync("new task");
        Assert.IsTrue(string.IsNullOrEmpty(fixture.Service.LastThinkingContent), "The previous request's progress must not leak into a new logical request.");
    }

    [TestMethod]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.Error)]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.DropBlock)]
    public async Task ExplicitUserEdit_ReachesWireWithOriginalThinkingAndSelectedBindingPolicy(ClaudeThinkingPrefixMismatchBehavior policy)
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingBinding(policy);
        await fixture.Service.GetCompletionAsync("original");
        fixture.Service.ActivateChat.Messages[0].Content = "edited";
        await fixture.Service.GetCompletionAsync("next");
        Assert.IsTrue(Messages(fixture.Requests[1])[0]["content"]!.ToJsonString().Contains("edited", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("sig-0", StringComparison.Ordinal));
        Assert.AreEqual(policy == ClaudeThinkingPrefixMismatchBehavior.Error ? "error" : "drop_block",
            fixture.Requests[1]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        Assert.IsTrue(fixture.Betas.All(beta => beta.Contains(AnthropicFableLiveProbe.BindingBeta)));
    }

    [TestMethod]
    public async Task PrefixBindingError_IsPropagatedOnceWithoutDiscardingThinkingOrRetrying()
    {
        using var fixture = new Fixture();
        fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await fixture.Service.GetCompletionAsync("original");
        fixture.Service.ActivateChat.Messages[0].Content = "edited";
        fixture.Status = HttpStatusCode.BadRequest;
        fixture.Respond = (_, _) => JsonNode.Parse("""{"type":"error","error":{"type":"invalid_request_error","message":"Invalid signature: The block is bound to a different conversation."}}""")!.AsObject();
        await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetCompletionAsync("next"));
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.IsTrue(fixture.Requests[1].ToJsonString().Contains("sig-0", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task Transformations_AreCapturedClonedAndResetWithoutRequiringRunReader(FableExecutionMode mode)
    {
        using var fixture = new Fixture();
        fixture.Respond = (_, index) =>
        {
            var response = Reply(index, "answer");
            response["input_transformations"] = JsonNode.Parse("""[{"type":"thinking_dropped","path":"messages.1.content.0","reason":"prefix_binding_mismatch"}]""");
            return response;
        };
        fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
        await Execute(fixture.Service, "first", mode);
        var transformation = fixture.Service.LastInputTransformations.Single();
        Assert.AreEqual("thinking_dropped", transformation.Type);
        Assert.AreEqual("messages.1.content.0", transformation.Path);
        Assert.AreEqual("prefix_binding_mismatch", transformation.Reason);
        Assert.AreEqual("response-0", transformation.ResponseId);
        Assert.AreEqual(AIModels.Anthropic.ClaudeFable5_1, transformation.Model);
        transformation.Reason = "caller mutation";
        Assert.AreEqual("prefix_binding_mismatch", fixture.Service.LastInputTransformations.Single().Reason);
        fixture.Respond = (_, index) => Reply(index, "clean");
        await fixture.Service.GetCompletionAsync("next");
        Assert.AreEqual(0, fixture.Service.LastInputTransformations.Count);
    }

    public sealed class TypedValue { public int Value { get; set; } }

    [TestMethod]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task StreamingFallback_CollectsTransformationsFromFinalMessageDelta(FableExecutionMode mode)
    {
        using var fixture = new Fixture { TransformationsOnFinalDelta = true };
        fixture.Respond = (_, index) =>
        {
            var response = Reply(index);
            response["input_transformations"] = JsonNode.Parse("""[{"type":"thinking_dropped","path":"messages.1.content.0","reason":"prefix_binding_mismatch"}]""");
            return response;
        };
        await Execute(fixture.Service, "fallback", mode);
        var transformation = fixture.Service.LastInputTransformations.Single();
        Assert.AreEqual("response-0", transformation.ResponseId);
        Assert.AreEqual("prefix_binding_mismatch", transformation.Reason);
        Assert.AreEqual("messages.1.content.0", transformation.Path);
    }

    private sealed class FailingRewriter : IQueryRewriter
    {
        public Task<QueryRewriteResult> RewriteAsync(string query, IReadOnlyList<ConversationTurn>? history,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Synthetic preparation failure.");
    }

    private static async Task Execute(AnthropicService service, string input, FableExecutionMode mode)
    {
        if (mode == FableExecutionMode.Completion) { await service.GetCompletionAsync(input); return; }
        if (mode == FableExecutionMode.Run) { await using var run = await service.StartRunAsync(input); await run.Result; return; }
        await foreach (var item in service.StreamAsync(input, StreamOptions.FullOptions))
            Assert.AreNotEqual(StreamingContentType.Error, item.Type);
    }

    private static JsonObject[] Messages(JsonObject request) => request["messages"]!.AsArray().OfType<JsonObject>().ToArray();
    private static void AssertPrefix(JsonObject earlier, JsonObject later)
    {
        Assert.IsTrue(JsonNode.DeepEquals(earlier["system"], later["system"]));
        Assert.IsTrue(JsonNode.DeepEquals(earlier["tools"], later["tools"]));
        var before = Messages(earlier); var after = Messages(later);
        Assert.IsTrue(after.Length >= before.Length);
        for (var index = 0; index < before.Length; index++)
            Assert.AreEqual(before[index].ToJsonString(), after[index].ToJsonString(), "The previously sent wire prefix changed.");
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
            ["id"] = "response-" + index, ["model"] = AIModels.Anthropic.ClaudeFable5_1,
            ["content"] = blocks, ["stop_reason"] = tool ? "tool_use" : "end_turn",
            ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 }
        };
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = new();
        public List<string> Paths { get; } = new();
        public List<string> Betas { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Func<JsonObject, int, JsonObject> Respond { get; set; } = (_, index) => Reply(index);
        public Func<int, CancellationToken, Task>? BeforeResponseAsync { get; set; }
        public bool TransformationsOnFinalDelta { get; set; }
        public Fixture(string model = AIModels.Anthropic.ClaudeFable5_1)
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
