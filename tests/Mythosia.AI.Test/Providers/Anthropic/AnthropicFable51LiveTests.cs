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
[TestCategory("Fable51")]
[DoNotParallelize]
public class AnthropicFable51LiveTests
{
    private const string Calculation = "Calculate 314159 times 271828 exactly. Reply with only the integer.";
    private static readonly string Product = (314159L * 271828L).ToString();

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task BasicMultiTurn_RetainsSignedThinkingAndUnchangedPrefix(FableExecutionMode mode)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        var first = await probe.ExecuteAsync(Calculation, mode);
        StringAssert.Contains(first.Text, Product);
        var second = await probe.ExecuteAsync("Add 17 to the previous result. Reply with only the integer.", mode);
        StringAssert.Contains(second.Text, (314159L * 271828L + 17).ToString());
        Assert.AreEqual(2, probe.Requests.Count);
        AssertPrefixUnchanged(probe.Requests[0], probe.Requests[1]);
        AssertThinkingReplayed(probe.Requests[0], probe.Requests[1]);
        Assert.AreEqual(0, probe.Service.LastInputTransformations.Count);
        AssertSuccessful(probe, mode, AnthropicFableLiveProbe.BindingBeta);
        Console.WriteLine($"LIVE_FABLE_OK feature=basic-prefix mode={mode}");
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public Task AutomaticToolAllocation_UsesHandlerOnlyDataAndReadableUpdates(FableExecutionMode mode)
        => AnthropicFableAllocationLiveScenario.VerifyAsync(mode);

    [TestMethod]
    public async Task CompletedConversation_CanCountInputTokensWithoutChangingHistory()
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await probe.ExecuteAsync(Calculation, FableExecutionMode.Completion);
        var history = JsonSerializer.Serialize(probe.Service.ActivateChat.Messages);
        var count = await probe.Service.GetInputTokenCountAsync();
        Assert.IsTrue(count > 0);
        Assert.AreEqual(history, JsonSerializer.Serialize(probe.Service.ActivateChat.Messages));
        Assert.AreEqual(2, probe.Requests.Count);
        Assert.AreEqual("/v1/messages", probe.Requests[0].Path);
        Assert.AreEqual("api.anthropic.com", probe.Requests[1].Host);
        Assert.AreEqual("/v1/messages/count_tokens", probe.Requests[1].Path);
        Assert.AreEqual(200, probe.Requests[1].StatusCode);
        Assert.IsFalse(probe.Requests[1].Streaming);
        Assert.AreEqual(AIModels.Anthropic.ClaudeFable5_1, probe.Requests[1].Body["model"]?.GetValue<string>());
        Assert.AreEqual("assistant", Messages(probe.Requests[1]).Last()["role"]?.GetValue<string>());
        AssertPrefixUnchanged(probe.Requests[0], probe.Requests[1]);
        AssertThinkingReplayed(probe.Requests[0], probe.Requests[1]);
        Console.WriteLine("LIVE_FABLE_OK feature=count-completed-conversation");
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    public async Task RequiredEffort_ChangesPerMessageWithoutRewritingTopLevel(FableExecutionMode mode)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required);
        StringAssert.Contains((await probe.ExecuteAsync("Calculate 17 times 19. Reply with only the integer.", mode)).Text, "323");
        probe.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required);
        StringAssert.Contains((await probe.ExecuteAsync("Now calculate 18 times 19. Reply with only the integer.", mode)).Text, "342");
        Assert.AreEqual(2, probe.Requests.Count);
        AssertPrefixUnchanged(probe.Requests[0], probe.Requests[1]);
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["output_config"], probe.Requests[1].Body["output_config"]));
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["thinking"], probe.Requests[1].Body["thinking"]));
        var effortMessage = Messages(probe.Requests[1]).Last(message => message["output_config"]?["effort"] != null);
        Assert.AreEqual("system", effortMessage["role"]!.GetValue<string>());
        Assert.AreEqual("low", effortMessage["output_config"]!["effort"]!.GetValue<string>());
        Assert.AreEqual(0, effortMessage["content"]!.AsArray().Count);
        Assert.AreEqual(0, probe.Service.LastInputTransformations.Count);
        AssertSuccessful(probe, mode, AnthropicFableLiveProbe.BindingBeta, AnthropicFableLiveProbe.EffortBeta);
        Console.WriteLine($"LIVE_FABLE_OK feature=per-message-effort mode={mode} cache=Required");
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    public async Task TurnInstruction_ClearsWhileConversationInstructionAndWireHistoryPersist(FableExecutionMode mode)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync();
        var persistent = "PERSIST_" + Guid.NewGuid().ToString("N");
        var temporary = "TURN_" + Guid.NewGuid().ToString("N");
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)
            .WithConversationInstruction("Start every reply in this conversation with " + persistent + ".")
            .WithTurnInstruction("For the current turn, end your reply with " + temporary + ".");
        var first = await probe.ExecuteAsync("Reply with Ready between any required markers.", mode);
        StringAssert.Contains(first.Text, persistent);
        StringAssert.Contains(first.Text, temporary);
        var second = await probe.ExecuteAsync("Reply with Updated. Follow currently active instructions, without quoting the previous reply.", mode);
        StringAssert.Contains(second.Text, persistent);
        Assert.IsFalse(second.Text.Contains(temporary, StringComparison.Ordinal), "The old turn-scoped instruction must have cleared.");
        var scoped = Messages(probe.Requests[0]).Single(message => message["clear_at"]?.GetValue<string>() == "next_user_message");
        Assert.IsTrue(Messages(probe.Requests[1]).Any(message => JsonNode.DeepEquals(scoped, message)),
            "Clearing must retain the original instruction in the wire history.");
        AssertPrefixUnchanged(probe.Requests[0], probe.Requests[1]);
        Assert.AreEqual(0, probe.Service.LastInputTransformations.Count);
        AssertSuccessful(probe, mode, AnthropicFableLiveProbe.BindingBeta, AnthropicFableLiveProbe.TurnBeta);
        Console.WriteLine($"LIVE_FABLE_OK feature=turn-clear-and-persistent-instruction mode={mode}");
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    public async Task EditedPrefix_ErrorPolicyReceivesAnExplicitRealApiRejection(FableExecutionMode mode)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await probe.ExecuteAsync(Calculation, mode);
        AssertHasSignedThinking(probe.Requests[0]);
        probe.Service.ActivateChat.Messages.First(message => message.Role == ActorRole.User).Content = "This earlier task was intentionally replaced for a prefix-binding test.";
        await Assert.ThrowsAsync<AIServiceException>(async () => await probe.ExecuteAsync("Reply with OK.", mode));
        Assert.AreEqual(2, probe.Requests.Count, "The explicit Error policy must reach the server exactly once after the edit.");
        Assert.AreEqual(400, probe.Requests[1].StatusCode);
        Assert.AreEqual("error", probe.Requests[1].Body["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        var error = probe.Requests[1].Frames().Single()["error"];
        Assert.AreEqual("invalid_request_error", error?["type"]?.GetValue<string>());
        Assert.IsTrue(error?["message"]?.GetValue<string>().Contains("bound to a different conversation", StringComparison.OrdinalIgnoreCase) == true,
            "The API must identify a prefix-binding failure, not an unrelated 400.");
        probe.AssertTransport(mode, AnthropicFableLiveProbe.BindingBeta);
        Console.WriteLine($"LIVE_FABLE_OK feature=explicit-prefix-error mode={mode} expected_http_status=400");
    }

    [TestMethod]
    [DataRow(FableExecutionMode.Completion)]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public async Task EditedPrefix_DropBlockCollectsRealInputTransformations(FableExecutionMode mode)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync();
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        await probe.ExecuteAsync(Calculation, mode);
        AssertHasSignedThinking(probe.Requests[0]);
        probe.Service.ActivateChat.Messages.First(message => message.Role == ActorRole.User).Content = "This earlier task was intentionally replaced for a prefix-binding test.";
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
        var answer = await probe.ExecuteAsync("Reply with exactly RECOVERED.", mode);
        StringAssert.Contains(answer.Text, "RECOVERED");
        Assert.AreEqual(2, probe.Requests.Count);
        Assert.AreEqual("drop_block", probe.Requests[1].Body["thinking"]?["block_binding"]?["prefix_mismatch_behavior"]?.GetValue<string>());
        AssertThinkingReplayed(probe.Requests[0], probe.Requests[1]);
        var actual = probe.Requests[1].Transformations().Where(value => value["type"]?.GetValue<string>() == "thinking_dropped" &&
            value["reason"]?.GetValue<string>() == "prefix_binding_mismatch").ToArray();
        Assert.IsTrue(actual.Length > 0, "The server must actually drop signed thinking after the intentional edit.");
        foreach (var transformation in actual)
            Assert.IsTrue(probe.Service.LastInputTransformations.Any(value => value.Type == "thinking_dropped" &&
                value.Reason == "prefix_binding_mismatch" && value.Path == transformation["path"]!.GetValue<string>()),
                "Every real provider transformation must reach the public collection.");
        AssertSuccessful(probe, mode, AnthropicFableLiveProbe.BindingBeta);
        Console.WriteLine($"LIVE_FABLE_OK feature=drop-block-transformations mode={mode} transformations={actual.Length}");
    }

    private static JsonObject[] Messages(AnthropicFableLiveProbe.RequestRecord request) => request.Body["messages"]!.AsArray().OfType<JsonObject>().ToArray();
    private static void AssertSuccessful(AnthropicFableLiveProbe probe, FableExecutionMode mode, params string[] betas)
    {
        probe.AssertTransport(mode, betas);
        Assert.IsTrue(probe.Requests.All(request => request.StatusCode == 200), "Every non-negative-test API request must succeed.");
    }
    private static void AssertPrefixUnchanged(AnthropicFableLiveProbe.RequestRecord earlier, AnthropicFableLiveProbe.RequestRecord later)
    {
        Assert.IsTrue(JsonNode.DeepEquals(earlier.Body["system"], later.Body["system"]), "The top-level system prefix changed.");
        Assert.IsTrue(JsonNode.DeepEquals(earlier.Body["tools"], later.Body["tools"]), "The tools prefix changed.");
        var before = Messages(earlier);
        var after = Messages(later);
        Assert.IsTrue(after.Length >= before.Length);
        for (var index = 0; index < before.Length; index++)
            Assert.AreEqual(before[index].ToJsonString(), after[index].ToJsonString(), "A previously sent message was rewritten.");
    }
    private static void AssertHasSignedThinking(AnthropicFableLiveProbe.RequestRecord response) =>
        Assert.IsTrue(response.ResponseBlocks().Any(block => block["type"]?.GetValue<string>() == "thinking" &&
            !string.IsNullOrWhiteSpace(block["signature"]?.GetValue<string>())), "The test requires actual signed thinking, not only an accepted option.");
    private static void AssertThinkingReplayed(AnthropicFableLiveProbe.RequestRecord response, AnthropicFableLiveProbe.RequestRecord next)
    {
        AssertHasSignedThinking(response);
        var signatures = Messages(next).Where(message => message["content"] is JsonArray)
            .SelectMany(message => message["content"]!.AsArray().OfType<JsonObject>())
            .Where(block => block["type"]?.GetValue<string>() == "thinking")
            .Select(block => block["signature"]?.GetValue<string>()).ToArray();
        foreach (var block in response.ResponseBlocks().Where(block => block["type"]?.GetValue<string>() == "thinking"))
            Assert.IsTrue(signatures.Contains(block["signature"]?.GetValue<string>()), "A real thinking signature was lost before replay.");
    }
}
