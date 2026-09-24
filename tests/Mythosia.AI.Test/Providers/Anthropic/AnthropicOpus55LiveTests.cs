using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Anthropic;

[TestClass]
[TestCategory("Live")]
[TestCategory("Anthropic")]
[TestCategory("Opus55")]
[DoNotParallelize]
public class AnthropicOpus55LiveTests
{
    [TestMethod]
    [DataRow(ReasoningLevel.Low, "low")]
    [DataRow(ReasoningLevel.Medium, "medium")]
    [DataRow(ReasoningLevel.High, "high")]
    [DataRow(ReasoningLevel.XHigh, "xhigh")]
    [DataRow(ReasoningLevel.Max, "max")]
    public async Task EffortLevels_AreAcceptedByActualMessagesApi(ReasoningLevel effort, string wire)
    {
        using var probe = await CreateAsync();
        probe.Service.WithReasoning(effort);
        var answer = await probe.ExecuteAsync("Reply with exactly OPUS55_OK.", FableExecutionMode.Completion);
        StringAssert.Contains(answer.Text, "OPUS55_OK");
        AssertSuccessful(probe, FableExecutionMode.Completion);
        Assert.AreEqual(wire, probe.Requests.Single().Body["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("adaptive", probe.Requests[0].Body["thinking"]?["type"]?.GetValue<string>());
        Assert.IsNull(probe.Requests[0].Body["thinking"]?["budget_tokens"]);
        Console.WriteLine($"LIVE_OPUS55_OK feature=effort level={wire}");
    }

    [TestMethod]
    public async Task DefaultMultiTurnAndCount_KeepSignedThinkingWithMediumEffort()
    {
        using var probe = await CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        var first = await probe.ExecuteAsync("Calculate 314159 times 271828 exactly. Reply with only the integer.", FableExecutionMode.Completion);
        StringAssert.Contains(first.Text, (314159L * 271828L).ToString());
        var second = await probe.ExecuteAsync("Add 17 to that result. Reply with only the integer.", FableExecutionMode.Completion);
        StringAssert.Contains(second.Text, (314159L * 271828L + 17).ToString());
        AssertSuccessful(probe, FableExecutionMode.Completion, AnthropicFableLiveProbe.BindingBeta);
        Assert.AreEqual(2, probe.Requests.Count);
        Assert.IsTrue(probe.Requests.All(request => request.Body["output_config"]?["effort"]?.GetValue<string>() == "medium"));
        AssertPrefix(probe.Requests[0], probe.Requests[1]);
        AssertThinkingReplay(probe.Requests[0], probe.Requests[1]);
        var history = JsonSerializer.Serialize(probe.Service.ActivateChat.Messages);
        Assert.IsTrue(await probe.Service.GetInputTokenCountAsync() > 0);
        Assert.AreEqual(history, JsonSerializer.Serialize(probe.Service.ActivateChat.Messages));
        var count = probe.Requests[2];
        Assert.AreEqual("/v1/messages/count_tokens", count.Path);
        Assert.AreEqual(200, count.StatusCode);
        Assert.AreEqual(AIModels.Anthropic.ClaudeOpus5_5, count.Body["model"]?.GetValue<string>());
        AssertThinkingReplay(probe.Requests[1], count);
        Console.WriteLine("LIVE_OPUS55_OK feature=default-prefix-and-token-count");
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public Task AutomaticToolRound_ReplaysSignedThinkingAndProvidesUpdates(FableExecutionMode mode)
        => AnthropicFableAllocationLiveScenario.VerifyAsync(mode, AIModels.Anthropic.ClaudeOpus5_5);
    [TestMethod]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.Error)]
    [DataRow(ClaudeThinkingPrefixMismatchBehavior.DropBlock)]
    public async Task EditedPrefix_UsesActualBindingPolicy(ClaudeThinkingPrefixMismatchBehavior policy)
    {
        using var probe = await CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await probe.ExecuteAsync("Calculate 314159 times 271828 exactly. Reply with only the integer.", FableExecutionMode.Completion);
        AssertHasThinking(probe.Requests[0]);
        probe.Service.ActivateChat.Messages.First(message => message.Role == ActorRole.User).Content = "The earlier calculation was intentionally replaced for a prefix-binding test.";
        probe.Service.WithThinkingBinding(policy);
        if (policy == ClaudeThinkingPrefixMismatchBehavior.Error)
        {
            await Assert.ThrowsAsync<AIServiceException>(async () => await probe.ExecuteAsync("Reply with OK.", FableExecutionMode.Completion));
            Assert.AreEqual(400, probe.Requests[1].StatusCode);
            var message = probe.Requests[1].Frames().Single()["error"]?["message"]?.GetValue<string>();
            Assert.IsTrue(message?.Contains("bound to a different conversation", StringComparison.OrdinalIgnoreCase) == true);
        }
        else
        {
            StringAssert.Contains((await probe.ExecuteAsync("Reply with exactly RECOVERED.", FableExecutionMode.Completion)).Text, "RECOVERED");
            AssertSuccessful(probe, FableExecutionMode.Completion, AnthropicFableLiveProbe.BindingBeta);
            Assert.IsTrue(probe.Service.LastInputTransformations.Any(value => value.Type == "thinking_dropped" && value.Reason == "prefix_binding_mismatch"));
        }
        Assert.AreEqual(2, probe.Requests.Count);
        AssertThinkingReplay(probe.Requests[0], probe.Requests[1]);
        Console.WriteLine($"LIVE_OPUS55_OK feature=edited-prefix policy={policy}");
    }

    [TestMethod]
    public async Task CachedEffortAndTurnInstruction_PreservePrefixAndClearTransientInstruction()
    {
        using var probe = await CreateAsync();
        var temporary = "TURN_" + Guid.NewGuid().ToString("N");
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)
            .WithTurnInstruction("End the current reply with " + temporary + ".")
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required);
        var first = await probe.ExecuteAsync("Calculate 314159 times 271828 exactly. Reply with the integer and any currently required marker.", FableExecutionMode.Completion);
        StringAssert.Contains(first.Text, (314159L * 271828L).ToString());
        StringAssert.Contains(first.Text, temporary);
        probe.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required);
        var second = await probe.ExecuteAsync("Add 17 to the previous result. Reply with only the integer; do not quote any earlier reply.", FableExecutionMode.Completion);
        StringAssert.Contains(second.Text, (314159L * 271828L + 17).ToString());
        Assert.IsFalse(second.Text.Contains(temporary, StringComparison.Ordinal), "The turn-scoped instruction must have cleared.");
        Assert.AreEqual(2, probe.Requests.Count);
        AssertSuccessful(probe, FableExecutionMode.Completion, AnthropicFableLiveProbe.BindingBeta, AnthropicFableLiveProbe.TurnBeta, AnthropicFableLiveProbe.EffortBeta);
        AssertPrefix(probe.Requests[0], probe.Requests[1]);
        AssertThinkingReplay(probe.Requests[0], probe.Requests[1]);
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["output_config"], probe.Requests[1].Body["output_config"]));
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["thinking"], probe.Requests[1].Body["thinking"]));
        Assert.AreEqual("low", Messages(probe.Requests[1]).Last(message => message["output_config"]?["effort"] != null)["output_config"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(1, Messages(probe.Requests[1]).Count(message => message["clear_at"]?.GetValue<string>() == "next_user_message"));
        Assert.AreEqual(0, probe.Service.LastInputTransformations.Count);
        Console.WriteLine("LIVE_OPUS55_OK feature=cached-effort-and-turn-instruction");
    }
    private static async Task<AnthropicFableLiveProbe> CreateAsync()
    {
        var probe = await AnthropicFableLiveProbe.CreateAsync(AIModels.Anthropic.ClaudeOpus5_5);
        probe.Service.AdaptiveThinkingEffort = ClaudeReasoningEffort.Auto;
        probe.Service.ThinkingBudget = -1;
        return probe;
    }
    private static void AssertSuccessful(AnthropicFableLiveProbe probe, FableExecutionMode mode, params string[] betas)
    {
        probe.AssertTransport(mode, betas);
        Assert.IsTrue(probe.Requests.All(request => request.StatusCode == 200));
    }
    private static JsonObject[] Messages(AnthropicFableLiveProbe.RequestRecord request) => request.Body["messages"]!.AsArray().OfType<JsonObject>().ToArray();
    private static void AssertPrefix(AnthropicFableLiveProbe.RequestRecord before, AnthropicFableLiveProbe.RequestRecord after)
    {
        Assert.IsTrue(JsonNode.DeepEquals(before.Body["system"], after.Body["system"]));
        Assert.IsTrue(JsonNode.DeepEquals(before.Body["tools"], after.Body["tools"]));
        var earlier = Messages(before); var later = Messages(after);
        Assert.IsTrue(later.Length >= earlier.Length);
        for (var i = 0; i < earlier.Length; i++) Assert.IsTrue(JsonNode.DeepEquals(earlier[i], later[i]));
    }
    private static void AssertHasThinking(AnthropicFableLiveProbe.RequestRecord response) =>
        Assert.IsTrue(response.ResponseBlocks().Any(block => block["type"]?.GetValue<string>() == "thinking" && !string.IsNullOrWhiteSpace(block["signature"]?.GetValue<string>())));
    private static void AssertThinkingReplay(AnthropicFableLiveProbe.RequestRecord response, AnthropicFableLiveProbe.RequestRecord next)
    {
        AssertHasThinking(response);
        var blocks = Messages(next).Where(message => message["content"] is JsonArray)
            .SelectMany(message => message["content"]!.AsArray().OfType<JsonObject>()).ToArray();
        foreach (var original in response.ResponseBlocks().Where(block => block["type"]?.GetValue<string>() == "thinking"))
            Assert.IsTrue(blocks.Any(block => JsonNode.DeepEquals(original, block)), "A real signed thinking block changed before replay.");
    }
}
