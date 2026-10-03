using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicThinkingProfileTests
{
    public enum RequestPath { Direct, Builder, Stream, Run }

    public static IEnumerable<object[]> ProfileCases()
    {
        var models = new (string Model, string? Type, string? Effort)[]
        {
            (AIModels.Anthropic.ClaudeSonnet5_5, "between_tools", "high"),
            (AIModels.Anthropic.ClaudeOpus5_5, "adaptive", "low"),
            (AIModels.Anthropic.ClaudeFable5, "adaptive", "low"),
            (AIModels.Anthropic.ClaudeFable5_1, "adaptive", "low"),
            (AIModels.Anthropic.ClaudeMythos5, "adaptive", "low"),
            (AIModels.Anthropic.ClaudeMythos5_1, "adaptive", "low"),
            (AIModels.Anthropic.ClaudeOpus5, "disabled", null),
            (AIModels.Anthropic.ClaudeSonnet5, "disabled", null),
            (AIModels.Anthropic.ClaudeOpus4_8, null, null),
            (AIModels.Anthropic.ClaudeOpus4_7, null, null),
            (AIModels.Anthropic.ClaudeOpus4_6, null, null),
            (AIModels.Anthropic.ClaudeSonnet4_6, null, null),
            (AIModels.Anthropic.ClaudeOpus4_5_251101, null, null),
            (AIModels.Anthropic.ClaudeSonnet4_5_250929, null, null),
            (AIModels.Anthropic.ClaudeHaiku4_5_251001, null, null)
        };
        foreach (var model in models)
        foreach (var path in new[] { RequestPath.Direct, RequestPath.Builder })
        foreach (var purpose in new[] { AIRequestPurpose.QueryRewrite, AIRequestPurpose.Summarization })
            yield return new object[] { model.Model, model.Type!, model.Effort!, path, purpose };
    }

    [TestMethod]
    [DynamicData(nameof(ProfileCases))]
    public async Task IsolatedProfile_ResolvesModelThinkingWithoutBindingAndPreservesConversation(
        string model, string? thinkingType, string? effort, RequestPath path, AIRequestPurpose purpose)
    {
        using var fixture = new Fixture(model);
        fixture.Service.WithThinkingParameters(4096).WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await fixture.Service.GetCompletionAsync("original conversation");
        var chat = fixture.Service.ActivateChat;
        var history = JsonSerializer.Serialize(chat.Messages);
        fixture.BeforeResponse = _ => AssertDefaults(fixture.Service, 4096, ClaudeReasoningEffort.Auto, ClaudeThinkingDisplay.Summarized);

        await Execute(fixture.Service, Profile(purpose), path);

        AssertHelper(fixture.Requests[1], fixture.Betas[1], thinkingType, effort);
        Assert.IsFalse(fixture.Requests[1]["messages"]!.ToJsonString().Contains("original conversation", StringComparison.Ordinal));
        Assert.AreSame(chat, fixture.Service.ActivateChat);
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages));
        AssertDefaults(fixture.Service, 4096, ClaudeReasoningEffort.Auto, ClaudeThinkingDisplay.Summarized);

        await fixture.Service.GetCompletionAsync("ordinary follow-up");
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["thinking"], fixture.Requests[2]["thinking"]));
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[2]["output_config"]));
        Assert.AreEqual("error", fixture.Requests[2]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5, "between_tools", "high", RequestPath.Stream)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5, "between_tools", "high", RequestPath.Run)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5, "adaptive", "low", RequestPath.Stream)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5, "adaptive", "low", RequestPath.Run)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5, "disabled", null, RequestPath.Stream)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5, "disabled", null, RequestPath.Run)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6, null, null, RequestPath.Stream)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6, null, null, RequestPath.Run)]
    public async Task StreamingProfile_UsesSameResolutionAsCompletion(
        string model, string? thinkingType, string? effort, RequestPath path)
    {
        using var fixture = new Fixture(model);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Summarized)
            .WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await Execute(fixture.Service, RequestProfiles.QueryRewrite, path);
        AssertHelper(fixture.Requests.Single(), fixture.Betas.Single(), thinkingType, effort);
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages);
        AssertDefaults(fixture.Service, 1024, ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Summarized);
    }

    [TestMethod]
    [DataRow(RequestPath.Direct)]
    [DataRow(RequestPath.Builder)]
    public async Task ProfileWithoutStatelessOverride_UsesCapturedEffectiveStatelessMode(RequestPath path)
    {
        using var fixture = BoundSonnet();
        fixture.Service.StatelessMode = true;
        await Execute(fixture.Service, new AIRequestProfile
        {
            Purpose = AIRequestPurpose.QueryRewrite,
            DisableReasoning = true
        }, path);
        AssertHelper(fixture.Requests.Single(), fixture.Betas.Single(), "between_tools", "high");
        Assert.IsTrue(fixture.Service.StatelessMode);
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages);
    }

    [TestMethod]
    [DataRow(RequestPath.Direct)]
    [DataRow(RequestPath.Builder)]
    public async Task ExplicitStatefulProfile_DoesNotDiscardBindingFromStatelessService(RequestPath path)
    {
        using var fixture = BoundSonnet();
        fixture.Service.StatelessMode = true;
        var profile = Profile(AIRequestPurpose.QueryRewrite);
        profile.Stateless = false;
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Execute(fixture.Service, profile, path));
        Assert.IsEmpty(fixture.Requests);
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, fixture.Service.ThinkingPrefixMismatchBehavior);
        Assert.IsTrue(fixture.Service.StatelessMode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LaterBuilderStatelessOverride_DeterminesAuxiliaryIsolation(bool effectiveStateless)
    {
        using var fixture = BoundSonnet();
        var profile = Profile(AIRequestPurpose.Summarization);
        profile.Stateless = !effectiveStateless;
        var request = fixture.Service.CreateRequest("helper").WithProfile(profile).WithStatelessMode(effectiveStateless);
        if (effectiveStateless)
        {
            await request.GetCompletionAsync();
            AssertHelper(fixture.Requests.Single(), fixture.Betas.Single(), "between_tools", "high");
        }
        else
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => request.GetCompletionAsync());
            Assert.IsEmpty(fixture.Requests);
        }
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, fixture.Service.ThinkingPrefixMismatchBehavior);
        Assert.IsFalse(fixture.Service.StatelessMode);
    }

    [TestMethod]
    public async Task AuxiliaryProfile_SuppressesCapturedCommonReasoningOnlyForDerivedRequest()
    {
        using var fixture = BoundSonnet();
        var basis = fixture.Service.CreateRequest("shared input").WithReasoning(ReasoningLevel.Max);
        var helper = basis.WithProfile(RequestProfiles.QueryRewrite);
        await helper.GetCompletionAsync();
        AssertHelper(fixture.Requests[0], fixture.Betas[0], "between_tools", "high");
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages);

        await basis.GetCompletionAsync();
        Assert.AreEqual("adaptive", fixture.Requests[1]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("max", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("error", fixture.Requests[1]["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        AssertDefaults(fixture.Service, 1024, ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
    }

    [TestMethod]
    public async Task DirectAuxiliaryProfile_PreservesPendingCommonReasoningAndTurnInstruction()
    {
        using var fixture = BoundSonnet();
        fixture.Service.WithReasoning(ReasoningLevel.Max);
        fixture.Service.WithTurnInstruction("ordinary turn only");
        await fixture.Service.GetCompletionAsync("helper", RequestProfiles.Summarization);
        AssertHelper(fixture.Requests[0], fixture.Betas[0], "between_tools", "high");
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("ordinary turn only", StringComparison.Ordinal));

        await fixture.Service.GetCompletionAsync("ordinary turn");
        Assert.AreEqual("max", fixture.Requests[1]["output_config"]?["effort"]?.GetValue<string>());
        StringAssert.Contains(fixture.Requests[1].ToJsonString(), "ordinary turn only");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5)]
    [DataRow(AIModels.Anthropic.ClaudeHaiku4_5_251001)]
    public async Task OrdinaryExplicitReasoningNone_WithBindingRemainsRejected(string model)
    {
        using var fixture = new Fixture(model);
        fixture.Service.WithThinkingParameters(4096).WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        var request = fixture.Service.CreateRequest("invalid ordinary request").WithReasoning(ReasoningLevel.None);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => request.GetCompletionAsync());
        Assert.IsEmpty(fixture.Requests);
        Assert.AreEqual(4096, fixture.Service.ThinkingBudget);
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, fixture.Service.ThinkingPrefixMismatchBehavior);
    }

    [TestMethod]
    public async Task CommonAdaptiveEffort_OverridesInvalidBetweenToolsEffortBeforeNativeValidation()
    {
        using var fixture = new Fixture(AIModels.Anthropic.ClaudeSonnet5_5);
        fixture.Service.WithBetweenToolsThinking(ClaudeReasoningEffort.Max);
        await fixture.Service.CreateRequest("override").WithReasoning(ReasoningLevel.Low).GetCompletionAsync();
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("low", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(ClaudeThinkingMode.BetweenTools, fixture.Service.ThinkingMode);
        Assert.AreEqual(ClaudeReasoningEffort.Max, fixture.Service.AdaptiveThinkingEffort);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("native again"));
        Assert.AreEqual(1, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task CommonSupportedEffort_OverridesUnsupportedNativeEffortBeforeNativeValidation()
    {
        using var fixture = new Fixture(AIModels.Anthropic.ClaudeSonnet4_6);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.XHigh);
        await fixture.Service.CreateRequest("override").WithReasoning(ReasoningLevel.High).GetCompletionAsync();
        Assert.AreEqual("adaptive", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Requests[0]["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(ClaudeReasoningEffort.XHigh, fixture.Service.AdaptiveThinkingEffort);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Service.GetCompletionAsync("native again"));
        Assert.AreEqual(1, fixture.Requests.Count);
    }

    [TestMethod]
    public async Task CommonNone_OverridesExcessiveManualBudgetBeforeNativeValidation()
    {
        using var fixture = new Fixture(AIModels.Anthropic.ClaudeHaiku4_5_251001);
        fixture.Service.WithThinkingParameters(64000);
        await fixture.Service.CreateRequest("override").WithReasoning(ReasoningLevel.None).GetCompletionAsync();
        Assert.AreEqual("disabled", fixture.Requests[0]["thinking"]?["type"]?.GetValue<string>());
        Assert.IsNull(fixture.Requests[0]["thinking"]?["budget_tokens"]);
        Assert.AreEqual(64000, fixture.Service.ThinkingBudget);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Service.GetCompletionAsync("native again"));
        Assert.AreEqual(1, fixture.Requests.Count);
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5, ClaudeThinkingDisplay.Summarized)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5, ClaudeThinkingDisplay.Omitted)]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1, ClaudeThinkingDisplay.Summarized)]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1, ClaudeThinkingDisplay.Omitted)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5, ClaudeThinkingDisplay.Summarized)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5, ClaudeThinkingDisplay.Omitted)]
    public async Task CachedAdaptiveThinking_UsesCurrentDisplayAndMatchingBetaWithoutChangingHistory(
        string model, ClaudeThinkingDisplay nextDisplay)
    {
        using var fixture = new Fixture(model);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        await fixture.Service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        var chat = fixture.Service.ActivateChat;
        var originalMessages = JsonSerializer.Serialize(chat.Messages);
        Assert.AreEqual("updates", fixture.Requests[0]["thinking"]?["display"]?.GetValue<string>());
        Assert.IsTrue(fixture.Betas[0].Contains("thinking-display-updates", StringComparison.Ordinal));

        fixture.Service.AdaptiveThinkingDisplay = nextDisplay;
        await fixture.Service.GetCompletionAsync("second");
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual(nextDisplay.ToString().ToLowerInvariant(), fixture.Requests[1]["thinking"]?["display"]?.GetValue<string>());
        Assert.IsFalse(fixture.Betas[1].Contains("thinking-display-updates", StringComparison.Ordinal));
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[1]["output_config"]));
        Assert.AreSame(chat, fixture.Service.ActivateChat);
        Assert.AreEqual(originalMessages, JsonSerializer.Serialize(chat.Messages.Take(2)));
        AssertMessagePrefix(fixture.Requests[0], fixture.Requests[1]);

        var secondMessages = JsonSerializer.Serialize(chat.Messages);
        fixture.Service.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Updates;
        await fixture.Service.GetCompletionAsync("third");
        Assert.AreEqual(3, fixture.Requests.Count);
        Assert.AreEqual("updates", fixture.Requests[2]["thinking"]?["display"]?.GetValue<string>());
        Assert.IsTrue(fixture.Betas[2].Contains("thinking-display-updates", StringComparison.Ordinal));
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[2]["output_config"]));
        Assert.AreEqual(secondMessages, JsonSerializer.Serialize(chat.Messages.Take(4)));
        AssertMessagePrefix(fixture.Requests[1], fixture.Requests[2]);
        Assert.AreEqual(ClaudeReasoningEffort.High, fixture.Service.AdaptiveThinkingEffort);
        Assert.AreEqual(1024, fixture.Service.ThinkingBudget);
    }

    [TestMethod]
    public async Task CachedAdaptiveThinking_WithBinding_OverridesDisabledNativeDefaultOnOrdinaryContinuation()
    {
        using var fixture = new Fixture(AIModels.Anthropic.ClaudeOpus5);
        fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        Assert.AreEqual(-1, fixture.Service.ThinkingBudget);
        await fixture.Service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("first");
        var chat = fixture.Service.ActivateChat;
        var history = JsonSerializer.Serialize(chat.Messages);

        await fixture.Service.GetCompletionAsync("second");

        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages.Take(2)));
        Assert.AreEqual(4, chat.Messages.Count);
        var continuedHistory = JsonSerializer.Serialize(chat.Messages);
        await fixture.Service.WithReasoning(ReasoningLevel.Auto, CachePreservation.Required).GetCompletionAsync("third");

        Assert.AreEqual(3, fixture.Requests.Count);
        foreach (var request in fixture.Requests)
        {
            Assert.AreEqual("adaptive", request["thinking"]?["type"]?.GetValue<string>());
            Assert.AreEqual("high", request["output_config"]?["effort"]?.GetValue<string>());
            Assert.AreEqual("error", request["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        }
        Assert.IsTrue(fixture.Betas.All(beta => beta.Contains("thinking-binding-controls", StringComparison.Ordinal)));
        AssertMessagePrefix(fixture.Requests[0], fixture.Requests[1]);
        AssertMessagePrefix(fixture.Requests[1], fixture.Requests[2]);
        Assert.AreSame(chat, fixture.Service.ActivateChat);
        Assert.AreEqual(continuedHistory, JsonSerializer.Serialize(chat.Messages.Take(4)));
        Assert.AreEqual(6, chat.Messages.Count);
        Assert.AreEqual(-1, fixture.Service.ThinkingBudget);
        Assert.AreEqual(ClaudeReasoningEffort.Auto, fixture.Service.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, fixture.Service.ThinkingPrefixMismatchBehavior);
    }

    [TestMethod]
    [DataRow(RequestPath.Direct, false)]
    [DataRow(RequestPath.Builder, false)]
    [DataRow(RequestPath.Stream, false)]
    [DataRow(RequestPath.Run, false)]
    [DataRow(RequestPath.Direct, true)]
    [DataRow(RequestPath.Builder, true)]
    [DataRow(RequestPath.Stream, true)]
    [DataRow(RequestPath.Run, true)]
    public async Task FailedOrCancelledProfile_PreservesNativeSettingsAndSignedHistory(RequestPath path, bool cancel)
    {
        using var fixture = BoundSonnet();
        await fixture.Service.GetCompletionAsync("original conversation");
        var chat = fixture.Service.ActivateChat;
        var history = JsonSerializer.Serialize(chat.Messages);
        using var cancellation = new CancellationTokenSource();
        fixture.BeforeResponse = token =>
        {
            AssertDefaults(fixture.Service, 1024, ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
            if (cancel)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                throw new AssertFailedException("The caller cancellation must reach the transport.");
            }
            throw new HttpRequestException("synthetic transport failure");
        };
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => Execute(fixture.Service, RequestProfiles.QueryRewrite, path, cancellation.Token));
        else
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => Execute(fixture.Service, RequestProfiles.QueryRewrite, path));

        AssertHelper(fixture.Requests[1], fixture.Betas[1], "between_tools", "high");
        Assert.AreSame(chat, fixture.Service.ActivateChat);
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages));
        AssertDefaults(fixture.Service, 1024, ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        fixture.BeforeResponse = null;
        await fixture.Service.GetCompletionAsync("after failure");
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["thinking"], fixture.Requests[2]["thinking"]));
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[2]["output_config"]));
    }

    [TestMethod]
    public async Task TokenCountingAfterAuxiliaryRequest_UsesUnchangedBoundConversationSettings()
    {
        using var fixture = BoundSonnet();
        await fixture.Service.GetCompletionAsync("original conversation");
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);
        await fixture.Service.CreateRequest("helper").WithProfile(RequestProfiles.QueryRewrite).GetCompletionAsync();
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        var count = fixture.Requests[2];
        Assert.AreEqual("adaptive", count["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual("error", count["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        Assert.IsNull(count["output_config"]);
        StringAssert.Contains(count["messages"]!.ToJsonString(), "original conversation");
        Assert.IsFalse(count["messages"]!.ToJsonString().Contains("helper", StringComparison.Ordinal));
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
    }

    private static AIRequestProfile Profile(AIRequestPurpose purpose) => purpose == AIRequestPurpose.Summarization
        ? RequestProfiles.Summarization : RequestProfiles.QueryRewrite;

    public static IEnumerable<object[]> CachedAuxiliaryCases()
    {
        foreach (var model in CachedModels())
        foreach (var path in Enum.GetValues<RequestPath>())
            yield return new object[] { model, path };
    }

    public static IEnumerable<object[]> CachedAuxiliaryFailureCases()
    {
        foreach (var model in CachedModels())
        foreach (var cancel in new[] { false, true })
            yield return new object[] { model, cancel };
    }

    private static IEnumerable<string> CachedModels() => new[]
    {
        AIModels.Anthropic.ClaudeSonnet5_5, AIModels.Anthropic.ClaudeOpus5_5,
        AIModels.Anthropic.ClaudeFable5_1, AIModels.Anthropic.ClaudeMythos5_1,
        AIModels.Anthropic.ClaudeOpus5
    };

    [TestMethod]
    [DynamicData(nameof(CachedAuxiliaryCases))]
    public async Task CachedConversation_AuxiliaryProfileIsIsolatedAcrossExecutionPaths(string model, RequestPath path)
    {
        using var fixture = new Fixture(model);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High)
            .WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await fixture.Service.CreateRequest("parent private conversation")
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync();
        var chat = fixture.Service.ActivateChat;
        var history = JsonSerializer.Serialize(chat.Messages);
        var helper = fixture.Service.CreateRequest("helper").WithProfile(RequestProfiles.QueryRewrite);
        // Builder capture starts a fresh provider observation snapshot. Auxiliary execution
        // must preserve that snapshot rather than publish its own thinking observations.
        var thinking = fixture.Service.LastThinkingContent;
        fixture.Service.WithTurnInstruction("next parent turn only");

        // Use the pre-created builder for Run to prove recapture cannot consume pending options.
        if (path == RequestPath.Run)
        {
            await using var run = await helper.StartRunAsync();
            Assert.AreEqual("answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        }
        else
        {
            // Direct auxiliary requests also leave pending options untouched. Builder creation
            // consumes them by design, so use the builder already captured above.
            if (path == RequestPath.Direct) await fixture.Service.GetCompletionAsync("helper", RequestProfiles.QueryRewrite);
            else if (path == RequestPath.Builder) await helper.GetCompletionAsync();
            else await foreach (var _ in helper.StreamAsync()) { }
        }

        Assert.AreSame(chat, fixture.Service.ActivateChat);
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages));
        Assert.AreEqual(thinking, fixture.Service.LastThinkingContent);
        Assert.HasCount(2, fixture.Requests);
        Assert.IsFalse(fixture.Requests[1].ToJsonString().Contains("parent private conversation", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Requests[1].ToJsonString().Contains("next parent turn only", StringComparison.Ordinal));
        Assert.IsNull(fixture.Requests[1]["thinking"]?["block_binding"]);

        await fixture.Service.GetCompletionAsync("parent continues");
        StringAssert.Contains(fixture.Requests[2].ToJsonString(), "next parent turn only");
        AssertMessagePrefix(fixture.Requests[0], fixture.Requests[2]);
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["thinking"], fixture.Requests[2]["thinking"]));
    }

    [TestMethod]
    [DynamicData(nameof(CachedAuxiliaryFailureCases))]
    public async Task CachedConversation_FailedAuxiliaryRunRestoresHistoryAndAllowsNextRun(string model, bool cancel)
    {
        using var fixture = new Fixture(model);
        await fixture.Service.CreateRequest("parent private conversation")
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync();
        var chat = fixture.Service.ActivateChat;
        var history = JsonSerializer.Serialize(chat.Messages);
        using var cts = new CancellationTokenSource();
        fixture.BeforeResponse = token =>
        {
            if (cancel) { cts.Cancel(); token.ThrowIfCancellationRequested(); }
            throw new HttpRequestException("Synthetic helper failure.");
        };
        Exception? error = null;
        try
        {
            await using var run = await fixture.Service.CreateRequest("helper")
                .WithProfile(RequestProfiles.QueryRewrite).StartRunAsync(cancellationToken: cts.Token);
            await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) { error = exception; }

        Assert.IsNotNull(error);
        if (cancel) Assert.IsInstanceOfType<OperationCanceledException>(error);
        else Assert.IsInstanceOfType<HttpRequestException>(error);
        Assert.HasCount(2, fixture.Requests, "The helper must reach its isolated HTTP request.");
        Assert.AreSame(chat, fixture.Service.ActivateChat);
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages));
        fixture.BeforeResponse = null;
        await using var next = await fixture.Service.CreateRequest("parent continues").StartRunAsync();
        await next.Result.WaitAsync(TimeSpan.FromSeconds(5));
        AssertMessagePrefix(fixture.Requests[0], fixture.Requests[2]);
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5_1)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    public async Task CachedConversation_OrdinaryStatelessRunStillRejects(string model)
    {
        using var fixture = new Fixture(model);
        await fixture.Service.CreateRequest("parent")
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync();
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.CreateRequest("invalid")
            .WithStatelessMode().StartRunAsync());
        Assert.HasCount(1, fixture.Requests);
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
    }

    private static async Task Execute(AnthropicService service, AIRequestProfile profile, RequestPath path, CancellationToken token = default)
    {
        if (path == RequestPath.Direct)
        {
            Assert.AreEqual("answer", await service.GetCompletionAsync("helper", profile, cancellationToken: token));
            return;
        }
        var request = service.CreateRequest("helper").WithProfile(profile);
        if (path == RequestPath.Builder) Assert.AreEqual("answer", await request.GetCompletionAsync(token));
        else if (path == RequestPath.Stream)
        {
            var text = new StringBuilder();
            await foreach (var chunk in request.StreamAsync(token)) text.Append(chunk);
            Assert.AreEqual("answer", text.ToString());
        }
        else
        {
            await using var run = await request.StartRunAsync(cancellationToken: token);
            await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static void AssertHelper(JsonObject body, string betas, string? type, string? effort)
    {
        Assert.AreEqual(type, body["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual(effort, body["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsNull(body["thinking"]?["budget_tokens"]);
        Assert.IsNull(body["thinking"]?["block_binding"]);
        Assert.AreEqual(type == "adaptive" ? "omitted" : null, body["thinking"]?["display"]?.GetValue<string>());
        Assert.IsFalse(betas.Contains("thinking-binding-controls", StringComparison.Ordinal));
        Assert.IsFalse(betas.Contains("thinking-display-updates", StringComparison.Ordinal));
        if (type == "between_tools") Assert.AreEqual(1, body["thinking"]!.AsObject().Count);
    }

    private static void AssertMessagePrefix(JsonObject before, JsonObject after)
    {
        var original = before["messages"]!.AsArray();
        var continued = after["messages"]!.AsArray();
        Assert.IsTrue(continued.Count >= original.Count);
        for (var index = 0; index < original.Count; index++)
            Assert.IsTrue(JsonNode.DeepEquals(original[index], continued[index]), "A cache-preserving continuation changed earlier wire history.");
    }

    private static void AssertDefaults(AnthropicService service, int budget, ClaudeReasoningEffort effort, ClaudeThinkingDisplay display)
    {
        Assert.AreEqual(budget, service.ThinkingBudget);
        Assert.AreEqual(ClaudeThinkingMode.Auto, service.ThinkingMode);
        Assert.AreEqual(effort, service.AdaptiveThinkingEffort);
        Assert.AreEqual(display, service.AdaptiveThinkingDisplay);
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, service.ThinkingPrefixMismatchBehavior);
        Assert.IsFalse(service.StatelessMode);
    }

    private static Fixture BoundSonnet()
    {
        var fixture = new Fixture(AIModels.Anthropic.ClaudeSonnet5_5);
        fixture.Service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates)
            .WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        return fixture;
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = [];
        public List<string> Betas { get; } = [];
        public Action<CancellationToken>? BeforeResponse { get; set; }

        public Fixture(string model)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline-test-key", model, _http);
            Service.DefaultPolicy.TimeoutSeconds = 10;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add(body);
            Betas.Add(request.Headers.TryGetValues("anthropic-beta", out var values) ? string.Join(",", values) : "");
            BeforeResponse?.Invoke(cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/count_tokens", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"input_tokens\":42}", Encoding.UTF8, "application/json") };
            var content = new JsonArray();
            if (body["thinking"]?["type"]?.GetValue<string>() is "adaptive" or "enabled")
                content.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = "reasoning", ["signature"] = "signature-" + Requests.Count });
            content.Add(new JsonObject { ["type"] = "text", ["text"] = "answer" });
            var response = new JsonObject
            {
                ["id"] = "response-" + Requests.Count, ["type"] = "message", ["role"] = "assistant",
                ["model"] = body["model"]!.DeepClone(), ["content"] = content, ["stop_reason"] = "end_turn",
                ["usage"] = new JsonObject { ["input_tokens"] = 2, ["output_tokens"] = 3 }
            };
            var streaming = body["stream"]?.GetValue<bool>() == true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streaming ? ToSse(response) : response.ToJsonString(), Encoding.UTF8,
                    streaming ? "text/event-stream" : "application/json")
            };
        }

        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }

    private static string ToSse(JsonObject response)
    {
        var start = (JsonObject)response.DeepClone();
        start["content"] = new JsonArray();
        var frames = new List<JsonObject> { new() { ["type"] = "message_start", ["message"] = start } };
        var index = 0;
        foreach (var raw in response["content"]!.AsArray().OfType<JsonObject>())
        {
            var block = (JsonObject)raw.DeepClone();
            var thinking = block["type"]!.GetValue<string>() == "thinking";
            block[thinking ? "thinking" : "text"] = "";
            if (thinking) block["signature"] = "";
            frames.Add(new JsonObject { ["type"] = "content_block_start", ["index"] = index, ["content_block"] = block });
            Delta(thinking ? "thinking_delta" : "text_delta", thinking ? "thinking" : "text");
            if (thinking) Delta("signature_delta", "signature");
            frames.Add(new JsonObject { ["type"] = "content_block_stop", ["index"] = index++ });
            void Delta(string type, string field) => frames.Add(new JsonObject
            {
                ["type"] = "content_block_delta", ["index"] = index,
                ["delta"] = new JsonObject { ["type"] = type, [field] = raw[field]!.DeepClone() }
            });
        }
        frames.Add(new JsonObject { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = "end_turn" } });
        frames.Add(new JsonObject { ["type"] = "message_stop" });
        return string.Concat(frames.Select(frame => "data: " + frame.ToJsonString() + "\n\n"));
    }
}
