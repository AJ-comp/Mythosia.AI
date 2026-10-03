using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Anthropic;

// Synthetic inputs only; shares the existing authenticated live-test infrastructure.
[TestClass]
[TestCategory("Live")]
[TestCategory("Anthropic")]
[DoNotParallelize]
public class AnthropicRequestIsolationLiveTests
{
    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    public async Task ReusedDirectMessage_OwnsEachTurnAndPreservesAcceptedPrefix(string model)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync(model);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var input = new Message(ActorRole.User, "Synthetic reusable source input.");
        AIRequestContext Context(string marker) => new()
        {
            RequestMessageOverride = new Message(ActorRole.User, "Reply with exactly " + marker + ".")
        };
        probe.Service.WithTurnInstruction("This is the first synthetic turn.");
        StringAssert.Contains(await probe.Service.GetCompletionAsync(input, context: Context("FIRST_OWNED_TURN"),
            cancellationToken: timeout.Token), "FIRST_OWNED_TURN");
        var prefix = probe.Requests[0].Body["messages"]!.DeepClone().AsArray();

        probe.Service.WithTurnInstruction("This is the second synthetic turn.");
        StringAssert.Contains(await probe.Service.GetCompletionAsync(input, context: Context("SECOND_OWNED_TURN"),
            cancellationToken: timeout.Token), "SECOND_OWNED_TURN");
        var messages = probe.Requests[1].Body["messages"]!.AsArray();
        for (var index = 0; index < prefix.Count; index++)
            Assert.IsTrue(JsonNode.DeepEquals(prefix[index], messages[index]), "Accepted wire prefix must remain unchanged.");
        var current = new JsonArray(messages.Skip(prefix.Count + 1).Select(item => item!.DeepClone()).ToArray()).ToJsonString();
        StringAssert.Contains(current, "SECOND_OWNED_TURN");
        StringAssert.Contains(current, "second synthetic turn");
        Assert.IsFalse(current.Contains("FIRST_OWNED_TURN", StringComparison.Ordinal));
        Assert.AreEqual("Synthetic reusable source input.", input.Content);

        var history = JsonSerializer.Serialize(probe.Service.ActivateChat.Messages);
        Assert.IsTrue(await probe.Service.GetInputTokenCountAsync() > 0);
        Assert.AreEqual(history, JsonSerializer.Serialize(probe.Service.ActivateChat.Messages));
        Assert.HasCount(3, probe.Requests);
        Assert.IsTrue(probe.Requests.All(request => request.StatusCode == 200));
        Console.WriteLine($"LIVE_CLAUDE_ISOLATION_OK feature=reused-direct-message model={model}");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    public async Task NestedHelperInsideRun_WithSameProfileOwnsItsContext(string model)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync(model);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var profile = RequestProfiles.QueryRewrite;
        var parent = probe.Service.ActivateChat;
        var history = JsonSerializer.Serialize(parent.Messages);
        var entered = false;
        probe.Service.WithSystemMessageProvider(async token =>
        {
            if (entered) return null;
            entered = true;
            var inner = await probe.Service.GetCompletionAsync("INNER_ORIGINAL", profile,
                new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "Reply with exactly INNER_OWNED_CONTEXT.") }, token);
            StringAssert.Contains(inner, "INNER_OWNED_CONTEXT");
            return new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "Reply with exactly OUTER_OWNED_CONTEXT.") };
        });

        await using var run = await probe.Service.CreateRequest("OUTER_ORIGINAL").WithProfile(profile)
            .StartRunAsync(cancellationToken: timeout.Token);
        StringAssert.Contains((await run.Result.WaitAsync(timeout.Token)).Text, "OUTER_OWNED_CONTEXT");
        Assert.HasCount(2, probe.Requests);
        var innerWire = probe.Requests[0].Body["messages"]!.ToJsonString();
        var outerWire = probe.Requests[1].Body["messages"]!.ToJsonString();
        StringAssert.Contains(innerWire, "INNER_OWNED_CONTEXT");
        StringAssert.Contains(outerWire, "OUTER_OWNED_CONTEXT");
        Assert.IsFalse(innerWire.Contains("OUTER_", StringComparison.Ordinal));
        Assert.IsFalse(outerWire.Contains("INNER_", StringComparison.Ordinal));
        Assert.IsFalse(innerWire.Contains("ORIGINAL", StringComparison.Ordinal));
        Assert.IsFalse(outerWire.Contains("ORIGINAL", StringComparison.Ordinal));
        Assert.AreSame(parent, probe.Service.ActivateChat);
        Assert.AreEqual(history, JsonSerializer.Serialize(parent.Messages));
        Assert.IsTrue(probe.Requests.All(request => request.StatusCode == 200));
        Console.WriteLine($"LIVE_CLAUDE_ISOLATION_OK feature=nested-run-helper model={model}");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    public async Task CachedConversation_AuxiliaryRunIsIsolatedAndParentCanContinue(string model)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync(model);
        await probe.Service.CreateRequest("This is a synthetic parent conversation. Reply with PARENT_READY.")
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync();
        var parent = probe.Service.ActivateChat;
        var history = JsonSerializer.Serialize(parent.Messages);

        await using (var run = await probe.Service.CreateRequest("Reply with exactly HELPER_READY.")
            .WithProfile(RequestProfiles.QueryRewrite).StartRunAsync())
        {
            var result = await run.Result.WaitAsync(TimeSpan.FromMinutes(4));
            StringAssert.Contains(result.Text, "HELPER_READY");
        }
        Assert.AreSame(parent, probe.Service.ActivateChat);
        Assert.AreEqual(history, JsonSerializer.Serialize(parent.Messages));
        Assert.HasCount(2, probe.Requests);
        Assert.IsFalse(probe.Requests[1].Body["messages"]!.ToJsonString().Contains("synthetic parent conversation", StringComparison.Ordinal));
        Assert.IsNull(probe.Requests[1].Body["thinking"]?["block_binding"]);

        var answer = await probe.Service.GetCompletionAsync("Reply with exactly PARENT_CONTINUED.");
        StringAssert.Contains(answer, "PARENT_CONTINUED");
        Assert.HasCount(3, probe.Requests);
        Assert.IsTrue(probe.Requests.All(request => request.StatusCode == 200));
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[0].Body["thinking"], probe.Requests[2].Body["thinking"]));
        var firstMessages = probe.Requests[0].Body["messages"]!.AsArray();
        var continuedMessages = probe.Requests[2].Body["messages"]!.AsArray();
        for (var index = 0; index < firstMessages.Count; index++)
            Assert.IsTrue(JsonNode.DeepEquals(firstMessages[index], continuedMessages[index]));
        Console.WriteLine($"LIVE_CLAUDE_ISOLATION_OK feature=cached-auxiliary-run model={model}");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    public async Task TokenCountAfterClientTool_IsAcceptedAndPreservesToolHistory(string model)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync(model);
        var receipt = "receipt_" + Guid.NewGuid().ToString("N");
        var calls = 0;
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "read_test_receipt",
            Description = "Read the receipt for this synthetic validation task.",
            Handler = _ => { calls++; return Task.FromResult(JsonSerializer.Serialize(new { receipt })); }
        });
        var answer = await probe.ExecuteAsync(
            "Call read_test_receipt exactly once, then reply with only the receipt returned by the tool.",
            FableExecutionMode.Completion);
        StringAssert.Contains(answer.Text, receipt);
        Assert.AreEqual(1, calls);
        Assert.HasCount(2, probe.Requests);
        var history = JsonSerializer.Serialize(probe.Service.ActivateChat.Messages);

        Assert.IsTrue(await probe.Service.GetInputTokenCountAsync() > 0);

        var count = probe.Requests.Last();
        Assert.AreEqual("/v1/messages/count_tokens", count.Path);
        Assert.AreEqual(200, count.StatusCode);
        Assert.AreEqual(model, count.Body["model"]?.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(probe.Requests[1].Body["tools"], count.Body["tools"]));
        var messages = count.Body["messages"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.IsFalse(messages.Any(message => message["role"]?.GetValue<string>() == "function"));
        var blocks = messages.Where(message => message["content"] is JsonArray)
            .SelectMany(message => message["content"]!.AsArray().OfType<JsonObject>()).ToArray();
        Assert.AreEqual(1, blocks.Count(block => block["type"]?.GetValue<string>() == "tool_use"));
        Assert.AreEqual(1, blocks.Count(block => block["type"]?.GetValue<string>() == "tool_result"));
        Assert.AreEqual(history, JsonSerializer.Serialize(probe.Service.ActivateChat.Messages));
        Assert.IsTrue(probe.Requests.All(request => request.StatusCode == 200));
        Console.WriteLine($"LIVE_CLAUDE_ISOLATION_OK feature=tool-history-count model={model}");
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    public async Task StatelessStringHelper_DoesNotTriggerParentAutoSummary(string model)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync(model);
        probe.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Synthetic first message."));
        probe.Service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Synthetic first answer."));
        probe.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Synthetic second message."));
        probe.Service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Synthetic second answer."));
        probe.Service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 2 };
        var history = JsonSerializer.Serialize(probe.Service.ActivateChat.Messages);

        var answer = await probe.Service.GetCompletionAsync("Reply with exactly ISOLATED_HELPER_OK.", RequestProfiles.QueryRewrite);

        StringAssert.Contains(answer, "ISOLATED_HELPER_OK");
        Assert.HasCount(1, probe.Requests, "The helper must not generate an additional parent summary request.");
        Assert.AreEqual(200, probe.Requests[0].StatusCode);
        Assert.AreEqual(history, JsonSerializer.Serialize(probe.Service.ActivateChat.Messages));
        Assert.IsNull(probe.Service.ConversationPolicy.CurrentSummary);
        Assert.IsFalse(probe.Requests[0].Body["messages"]!.ToJsonString().Contains("Synthetic first", StringComparison.Ordinal));
        Console.WriteLine($"LIVE_CLAUDE_ISOLATION_OK feature=stateless-summary-skip model={model}");
    }
}
