using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Anthropic;

// Uses only synthetic prompts and the existing Anthropic Key Vault secret path.
[TestClass]
[TestCategory("Live")]
[TestCategory("Anthropic")]
[TestCategory("Sonnet55")]
[DoNotParallelize]
public class AnthropicSonnet55LiveTests
{
    [TestMethod]
    [DataRow(ReasoningLevel.Auto, "high")]
    [DataRow(ReasoningLevel.Low, "low")]
    [DataRow(ReasoningLevel.Medium, "medium")]
    [DataRow(ReasoningLevel.High, "high")]
    [DataRow(ReasoningLevel.XHigh, "xhigh")]
    [DataRow(ReasoningLevel.Max, "max")]
    public async Task EffortLevels_AreAcceptedByMessagesApi(ReasoningLevel effort, string wire)
    {
        using var probe = await CreateAsync();
        probe.Service.WithReasoning(effort);
        var answer = await probe.ExecuteAsync("Reply with exactly SONNET55_OK.", FableExecutionMode.Completion);
        StringAssert.Contains(answer.Text, "SONNET55_OK");
        AssertSuccessful(probe, FableExecutionMode.Completion);
        Assert.AreEqual(wire, probe.Requests.Single().Body["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("adaptive", probe.Requests[0].Body["thinking"]?["type"]?.GetValue<string>());
        Assert.IsNull(probe.Requests[0].Body["thinking"]?["budget_tokens"]);
        Console.WriteLine($"LIVE_SONNET55_OK feature=adaptive-effort level={wire}");
    }

    [TestMethod]
    [DataRow(ClaudeReasoningEffort.Auto, "high")]
    [DataRow(ClaudeReasoningEffort.Low, "low")]
    [DataRow(ClaudeReasoningEffort.Medium, "medium")]
    [DataRow(ClaudeReasoningEffort.High, "high")]
    public async Task BetweenTools_AcceptsDocumentedEffortsAndNoAdditionalThinkingFields(ClaudeReasoningEffort effort, string wire)
    {
        using var probe = await CreateAsync();
        probe.Service.WithBetweenToolsThinking(effort);
        var answer = await probe.ExecuteAsync("Reply with exactly BETWEEN_TOOLS_OK.", FableExecutionMode.Completion);
        StringAssert.Contains(answer.Text, "BETWEEN_TOOLS_OK");
        AssertSuccessful(probe, FableExecutionMode.Completion);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"between_tools"}"""), probe.Requests[0].Body["thinking"]));
        Assert.AreEqual(wire, probe.Requests[0].Body["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsTrue(probe.Requests[0].ResponseBlocks().All(block => block["type"]?.GetValue<string>() == "text"));
        Console.WriteLine($"LIVE_SONNET55_OK feature=between-tools effort={wire}");
    }

    [TestMethod]
    public async Task CommonNoneAndStructuredOutput_WorkThroughPublicApis()
    {
        using var probe = await CreateAsync();
        probe.Service.WithReasoning(ReasoningLevel.None);
        var result = await probe.Service.GetCompletionAsync<StructuredValue>("Return Value equal to 42 and Marker equal to SONNET55_STRUCTURED.");
        Assert.AreEqual(42, result.Value);
        Assert.AreEqual("SONNET55_STRUCTURED", result.Marker);
        AssertSuccessful(probe, FableExecutionMode.Completion);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"between_tools"}"""), probe.Requests.Single().Body["thinking"]));
        var schemaInstruction = Messages(probe.Requests[0]).Single(message => message["role"]?.GetValue<string>() == "system" &&
            message["content"]!.GetValue<string>().Contains("[STRUCTURED OUTPUT]", StringComparison.Ordinal));
        Assert.AreEqual("next_user_message", schemaInstruction["clear_at"]?.GetValue<string>());
        var instruction = schemaInstruction["content"]!.GetValue<string>();
        var schema = JsonNode.Parse(instruction[instruction.IndexOf('{')..])!;
        Assert.IsNotNull(schema["properties"]?["Value"]);
        Assert.IsNotNull(schema["properties"]?["Marker"]);
        Console.WriteLine("LIVE_SONNET55_OK feature=structured-none");
    }

    [TestMethod]
    public async Task DefaultMultiTurnAndTokenCount_KeepHighEffortAndSignedHistory()
    {
        using var probe = await CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        var first = await probe.ExecuteAsync("Calculate 314159 times 271828 exactly. Reply with only the integer.", FableExecutionMode.Completion);
        StringAssert.Contains(first.Text, (314159L * 271828L).ToString());
        var second = await probe.ExecuteAsync("Add 17 to that result. Reply with only the integer.", FableExecutionMode.Completion);
        StringAssert.Contains(second.Text, (314159L * 271828L + 17).ToString());
        AssertSuccessful(probe, FableExecutionMode.Completion, AnthropicFableLiveProbe.BindingBeta);
        Assert.AreEqual(2, probe.Requests.Count);
        Assert.IsTrue(probe.Requests.All(request => request.Body["output_config"]?["effort"]?.GetValue<string>() == "high"));
        AssertPrefix(probe.Requests[0], probe.Requests[1]);
        AssertThinkingReplay(probe.Requests[0], probe.Requests[1]);
        var history = JsonSerializer.Serialize(probe.Service.ActivateChat.Messages);
        Assert.IsTrue(await probe.Service.GetInputTokenCountAsync() > 0);
        Assert.AreEqual(history, JsonSerializer.Serialize(probe.Service.ActivateChat.Messages));
        var count = probe.Requests[2];
        Assert.AreEqual("/v1/messages/count_tokens", count.Path);
        Assert.AreEqual(200, count.StatusCode);
        AssertThinkingReplay(probe.Requests[0], count);
        AssertThinkingReplay(probe.Requests[1], count, requireThinking: false);
        Console.WriteLine("LIVE_SONNET55_OK feature=default-prefix-and-count");
    }

    [TestMethod]
    [DataRow(AIRequestPurpose.QueryRewrite)]
    [DataRow(AIRequestPurpose.Summarization)]
    public async Task BuilderAuxiliaryProfile_LeavesBoundConversationAndNativeThinkingUnchanged(AIRequestPurpose purpose)
    {
        using var probe = await CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        var first = await probe.ExecuteAsync("Calculate 314159 times 271828 exactly. Reply with only the integer.", FableExecutionMode.Completion);
        StringAssert.Contains(first.Text, (314159L * 271828L).ToString());
        var chat = probe.Service.ActivateChat;
        var history = JsonSerializer.Serialize(chat.Messages);
        var profile = purpose == AIRequestPurpose.Summarization ? RequestProfiles.Summarization : RequestProfiles.QueryRewrite;

        var helper = await probe.Service.CreateRequest("Reply with exactly AUXILIARY_PROFILE_OK.").WithProfile(profile).GetCompletionAsync();
        StringAssert.Contains(helper, "AUXILIARY_PROFILE_OK");
        Assert.AreSame(chat, probe.Service.ActivateChat);
        Assert.AreEqual(history, JsonSerializer.Serialize(chat.Messages));
        Assert.AreEqual(ClaudeThinkingPrefixMismatchBehavior.Error, probe.Service.ThinkingPrefixMismatchBehavior);
        var helperRequest = probe.Requests[1];
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"between_tools"}"""), helperRequest.Body["thinking"]));
        Assert.AreEqual("high", helperRequest.Body["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsFalse(helperRequest.Betas.Contains(AnthropicFableLiveProbe.BindingBeta, StringComparer.Ordinal));
        Assert.IsFalse(helperRequest.Betas.Contains(AnthropicFableLiveProbe.UpdatesBeta, StringComparer.Ordinal));
        Assert.IsFalse(helperRequest.Body["messages"]!.ToJsonString().Contains("314159", StringComparison.Ordinal));

        var second = await probe.ExecuteAsync("Add 17 to that result. Reply with only the integer.", FableExecutionMode.Completion);
        StringAssert.Contains(second.Text, (314159L * 271828L + 17).ToString());
        Assert.AreEqual(3, probe.Requests.Count);
        AssertSuccessful(probe, FableExecutionMode.Completion);
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["thinking"], probe.Requests[2].Body["thinking"]));
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["output_config"], probe.Requests[2].Body["output_config"]));
        AssertPrefix(probe.Requests[0], probe.Requests[2]);
        AssertThinkingReplay(probe.Requests[0], probe.Requests[2]);
        Console.WriteLine($"LIVE_SONNET55_OK feature=builder-auxiliary-profile purpose={purpose}");
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public Task AutomaticToolRound_ProvidesRealProgressUpdates(FableExecutionMode mode)
        => AnthropicFableAllocationLiveScenario.VerifyAsync(mode, AIModels.Anthropic.ClaudeSonnet5_5);

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task BetweenTools_AutomaticFunctionCallPreservesReturnedThinking(FableExecutionMode mode)
    {
        using var probe = await CreateAsync();
        probe.Service.WithBetweenToolsThinking(ClaudeReasoningEffort.High);
        var proof = "receipt_" + Guid.NewGuid().ToString("N");
        var calls = 0;
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "read_synthetic_receipt", Description = "Read the receipt code for this synthetic validation task.",
            Handler = _ => { calls++; return Task.FromResult(JsonSerializer.Serialize(new { receipt = proof })); }
        });
        var answer = await probe.ExecuteAsync("Call read_synthetic_receipt once, then reply with only the receipt code it returns.", mode);
        StringAssert.Contains(answer.Text, proof);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, probe.Requests.Count);
        AssertSuccessful(probe, mode);
        foreach (var request in probe.Requests)
        {
            Assert.AreEqual("auto", request.Body["tool_choice"]?["type"]?.GetValue<string>());
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"between_tools"}"""), request.Body["thinking"]));
        }
        AssertPrefix(probe.Requests[0], probe.Requests[1]);
        // Between-tools thinking is optional on this small task; every returned block must survive.
        AssertThinkingReplay(probe.Requests[0], probe.Requests[1], requireThinking: false);
        if (mode == FableExecutionMode.Completion)
        {
            probe.Service.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Updates;
            Assert.IsTrue(await probe.Service.GetInputTokenCountAsync() > 0);
            var count = probe.Requests[2];
            Assert.AreEqual(200, count.StatusCode);
            Assert.AreEqual("/v1/messages/count_tokens", count.Path);
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"between_tools"}"""), count.Body["thinking"]));
            Assert.IsFalse(count.Betas.Contains(AnthropicFableLiveProbe.UpdatesBeta, StringComparer.Ordinal));
            AssertThinkingReplay(probe.Requests[0], count, requireThinking: false);
            AssertThinkingReplay(probe.Requests[1], count, requireThinking: false);
        }
        Console.WriteLine($"LIVE_SONNET55_OK feature=between-tools-function mode={mode}");
    }

    [TestMethod]
    public async Task CachedEffortAndTurnInstruction_PreservePrefixAndClearTemporaryInstruction()
    {
        using var probe = await CreateAsync();
        var marker = "TURN_" + Guid.NewGuid().ToString("N");
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)
            .WithTurnInstruction("End this reply with " + marker + ".")
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required);
        var first = await probe.ExecuteAsync("Calculate 314159 times 271828 exactly. Reply with the integer and any required marker.", FableExecutionMode.Completion);
        StringAssert.Contains(first.Text, marker);
        probe.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required);
        var second = await probe.ExecuteAsync("Add 17 to the previous result. Reply only with the integer; do not quote earlier replies.", FableExecutionMode.Completion);
        StringAssert.Contains(second.Text, (314159L * 271828L + 17).ToString());
        Assert.IsFalse(second.Text.Contains(marker, StringComparison.Ordinal));
        AssertSuccessful(probe, FableExecutionMode.Completion, AnthropicFableLiveProbe.BindingBeta, AnthropicFableLiveProbe.EffortBeta, AnthropicFableLiveProbe.TurnBeta);
        Assert.AreEqual(2, probe.Requests.Count);
        AssertPrefix(probe.Requests[0], probe.Requests[1]);
        AssertThinkingReplay(probe.Requests[0], probe.Requests[1]);
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["output_config"], probe.Requests[1].Body["output_config"]));
        Assert.AreEqual("low", Messages(probe.Requests[1]).Last(message => message["output_config"]?["effort"] != null)["output_config"]?["effort"]?.GetValue<string>());
        Assert.IsEmpty(probe.Service.LastInputTransformations);
        Console.WriteLine("LIVE_SONNET55_OK feature=cached-effort-and-turn-instruction");
    }

    private static async Task<AnthropicFableLiveProbe> CreateAsync()
    {
        var probe = await AnthropicFableLiveProbe.CreateAsync(AIModels.Anthropic.ClaudeSonnet5_5);
        probe.Service.AdaptiveThinkingEffort = ClaudeReasoningEffort.Auto;
        // The shared probe leaves ThinkingBudget untouched; explicitly assigning -1 is opt-out.
        return probe;
    }

    public sealed class StructuredValue { public int Value { get; set; } public string Marker { get; set; } = ""; }

    private static void AssertSuccessful(AnthropicFableLiveProbe probe, FableExecutionMode mode, params string[] betas)
    {
        probe.AssertTransport(mode, betas);
        Assert.IsTrue(probe.Requests.All(request => request.StatusCode == 200));
        foreach (var request in probe.Requests)
        {
            var responseModels = request.Frames().Select(frame => (frame["model"] ?? frame["message"]?["model"])?.GetValue<string>())
                .OfType<string>().Distinct().ToArray();
            CollectionAssert.AreEqual(new[] { AIModels.Anthropic.ClaudeSonnet5_5 }, responseModels,
                "The actual provider response must identify the fixed Sonnet 5.5 model.");
        }
    }
    private static JsonObject[] Messages(AnthropicFableLiveProbe.RequestRecord request) => request.Body["messages"]!.AsArray().OfType<JsonObject>().ToArray();
    private static void AssertPrefix(AnthropicFableLiveProbe.RequestRecord before, AnthropicFableLiveProbe.RequestRecord after)
    {
        Assert.IsTrue(JsonNode.DeepEquals(before.Body["system"], after.Body["system"]));
        Assert.IsTrue(JsonNode.DeepEquals(before.Body["tools"], after.Body["tools"]));
        var earlier = Messages(before); var later = Messages(after);
        Assert.IsTrue(later.Length >= earlier.Length);
        for (var index = 0; index < earlier.Length; index++) Assert.IsTrue(JsonNode.DeepEquals(earlier[index], later[index]));
    }
    private static void AssertThinkingReplay(AnthropicFableLiveProbe.RequestRecord response, AnthropicFableLiveProbe.RequestRecord next, bool requireThinking = true)
    {
        var thinking = response.ResponseBlocks().Where(block => block["type"]?.GetValue<string>() == "thinking").ToArray();
        if (requireThinking) Assert.IsNotEmpty(thinking, "The real provider must supply signed thinking to exercise replay.");
        var replayed = Messages(next).Where(message => message["content"] is JsonArray)
            .SelectMany(message => message["content"]!.AsArray().OfType<JsonObject>()).ToArray();
        foreach (var block in thinking)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(block["signature"]?.GetValue<string>()));
            Assert.IsTrue(replayed.Any(candidate => JsonNode.DeepEquals(block, candidate)), "A real signed Sonnet 5.5 thinking block changed before replay.");
        }
    }
}
