using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicTokenCountingContractTests
{
    private const string Answer = """
        {"id":"answer-id","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn","usage":{"input_tokens":8,"output_tokens":2}}
        """;
    private const string ToolBlocks = """
        [{"type":"thinking","thinking":"opaque reasoning","signature":"unaltered-signature"},
         {"type":"text","text":"Checking both."},
         {"type":"tool_use","id":"toolu_first","name":"lookup","input":{"city":"Seoul","days":["Monday"]}},
         {"type":"tool_use","id":"toolu_second","name":"lookup","input":{"city":"Busan","days":["Tuesday"]}}]
        """;
    private static string ToolAnswer => "{\"content\":" + ToolBlocks + ",\"stop_reason\":\"tool_use\"}";

    [TestMethod]
    [DataRow("claude-sonnet-4-6")]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task CompletedToolConversation_CountRetainsGenerationHistoryAndSchemas(string model)
    {
        using var fixture = new Fixture(model);
        var executed = 0;
        fixture.Service.Functions.Add(Tool(_ => { executed++; return Task.FromResult("sunny"); }));
        fixture.GenerationResponses.Enqueue(ToolAnswer);
        fixture.GenerationResponses.Enqueue(Answer);
        await fixture.Service.GetCompletionAsync("Check two cities.");
        Assert.AreEqual(2, executed);
        var history = Snapshot(fixture.Service);

        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        var count = fixture.Requests[2];
        Assert.AreEqual("/v1/messages/count_tokens", fixture.Paths[2]);
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["tools"], count["tools"]));
        Assert.AreEqual("auto", count["tool_choice"]?["type"]?.GetValue<string>());
        AssertPrefix(fixture.Requests[1], count);
        var messages = count["messages"]!.AsArray();
        Assert.AreEqual(4, messages.Count);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(ToolBlocks), messages[1]!["content"]));
        Assert.AreEqual("user", messages[2]!["role"]!.GetValue<string>());
        var results = messages[2]!["content"]!.AsArray();
        CollectionAssert.AreEqual(new[] { "toolu_first", "toolu_second" }, results.Select(block => block!["tool_use_id"]!.GetValue<string>()).ToArray());
        Assert.IsTrue(results.All(block => block!["type"]!.GetValue<string>() == "tool_result"));
        Assert.AreEqual("assistant", messages[3]!["role"]!.GetValue<string>());
        AssertCountOnlyFields(count);
        Assert.AreEqual(history, Snapshot(fixture.Service));
        Assert.AreEqual(2, executed, "Counting must not invoke registered handlers.");
    }

    [TestMethod]
    [DataRow("claude-sonnet-4-6", false)]
    [DataRow("claude-sonnet-4-6", true)]
    [DataRow("claude-opus-5", false)]
    [DataRow("claude-opus-5", true)]
    [DataRow("claude-sonnet-5-5", false)]
    [DataRow("claude-sonnet-5-5", true)]
    public async Task HistoricalBatches_RemainValidWhenToolsAreDisabledOrRemoved(string model, bool disabled)
    {
        using var fixture = new Fixture(model);
        fixture.Service.ActivateChat.SystemMessage = "Preserved system prompt.";
        if (disabled)
        {
            fixture.Service.Functions.Add(Tool());
            fixture.Service.FunctionsDisabled = true;
        }
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent("Check this image."),
            new ImageContent(new byte[] { 1, 2, 3, 4 }, "image/png")
        }));
        var calls = new[]
        {
            new FunctionCall { Id = "toolu_first", Source = IdSource.Claude, Name = "lookup", Arguments = new() { ["city"] = "Seoul" } },
            new FunctionCall { Id = "toolu_second", Source = IdSource.Claude, Name = "lookup", Arguments = new() { ["city"] = "Busan" } }
        };
        var batch = new FunctionCallBatch(calls);
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Checking both.") { FunctionCallBatch = batch });
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "")
        {
            FunctionCallResultBatch = new FunctionCallResultBatch(batch.Id, new[]
            {
                new FunctionCallResult { Call = calls[0], Content = "sunny" },
                new FunctionCallResult { Call = calls[1], Content = "unavailable", IsError = true }
            })
        });
        var history = Snapshot(fixture.Service);

        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        var count = fixture.Requests.Single();
        Assert.IsNull(count["tools"]);
        Assert.IsNull(count["tool_choice"]);
        Assert.AreEqual("Preserved system prompt.", count["system"]!.GetValue<string>());
        var messages = count["messages"]!.AsArray();
        CollectionAssert.AreEqual(new[] { "user", "assistant", "user" }, messages.Select(message => message!["role"]!.GetValue<string>()).ToArray());
        Assert.AreEqual("Check this image.", messages[0]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.AreEqual("image/png", messages[0]!["content"]![1]!["source"]!["media_type"]!.GetValue<string>());
        Assert.AreEqual("AQIDBA==", messages[0]!["content"]![1]!["source"]!["data"]!.GetValue<string>());
        Assert.AreEqual("Checking both.", messages[1]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.AreEqual("toolu_first", messages[1]!["content"]![1]!["id"]!.GetValue<string>());
        Assert.AreEqual("Seoul", messages[1]!["content"]![1]!["input"]!["city"]!.GetValue<string>());
        Assert.AreEqual("toolu_second", messages[1]!["content"]![2]!["id"]!.GetValue<string>());
        Assert.AreEqual("toolu_first", messages[2]!["content"]![0]!["tool_use_id"]!.GetValue<string>());
        Assert.AreEqual("toolu_second", messages[2]!["content"]![1]!["tool_use_id"]!.GetValue<string>());
        Assert.AreEqual("unavailable", messages[2]!["content"]![1]!["content"]!.GetValue<string>());
        Assert.IsTrue(messages[2]!["content"]![1]!["is_error"]!.GetValue<bool>());
        Assert.AreEqual(history, Snapshot(fixture.Service));
    }

    [TestMethod]
    [DataRow("claude-sonnet-4-6")]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task LegacyParallelToolRecords_CountEmitsOneAssistantTurnAndPairedResults(string model)
    {
        using var fixture = new Fixture(model);
        fixture.Service.Functions.Add(Tool());
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Check both."));
        foreach (var id in new[] { "toolu_first", "toolu_second" })
            fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Checking both.")
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_call",
                    [MessageMetadataKeys.FunctionId] = id,
                    [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                    [MessageMetadataKeys.FunctionName] = "lookup",
                    [MessageMetadataKeys.OriginalContent] = ToolBlocks
                }
            });
        foreach (var id in new[] { "toolu_first", "toolu_second" })
            fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "result-" + id)
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.FunctionId] = id,
                    [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                    [MessageMetadataKeys.FunctionName] = "lookup"
                }
            });
        var history = Snapshot(fixture.Service);

        await fixture.Service.GetInputTokenCountAsync();
        var count = fixture.Requests.Single();
        var messages = count["messages"]!.AsArray();
        Assert.AreEqual(3, messages.Count);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(ToolBlocks), messages[1]!["content"]));
        Assert.AreEqual("user", messages[2]!["role"]!.GetValue<string>());
        Assert.AreEqual(2, messages[2]!["content"]!.AsArray().Count);
        Assert.AreEqual("toolu_second", messages[2]!["content"]![1]!["tool_use_id"]!.GetValue<string>());
        Assert.AreEqual(history, Snapshot(fixture.Service));

        await fixture.Service.GetInputTokenCountAsync();
        Assert.IsTrue(JsonNode.DeepEquals(count, fixture.Requests[1]));
        Assert.AreEqual(history, Snapshot(fixture.Service));
        await fixture.Service.GetCompletionAsync("Continue.");
        AssertPrefix(count, fixture.Requests[2]);
    }

    [TestMethod]
    [DataRow("claude-sonnet-5-5", "success", ClaudeThinkingPrefixMismatchBehavior.Error)]
    [DataRow("claude-sonnet-5-5", "failure", ClaudeThinkingPrefixMismatchBehavior.DropBlock)]
    [DataRow("claude-sonnet-5-5", "cancel", ClaudeThinkingPrefixMismatchBehavior.Error)]
    [DataRow("claude-opus-5-5", "success", ClaudeThinkingPrefixMismatchBehavior.DropBlock)]
    [DataRow("claude-opus-5-5", "failure", ClaudeThinkingPrefixMismatchBehavior.Error)]
    [DataRow("claude-opus-5-5", "cancel", ClaudeThinkingPrefixMismatchBehavior.DropBlock)]
    public async Task LegacyParallelToolRecords_DisabledToolsPreserveInstructionsAndCountRollback(
        string model, string outcome, ClaudeThinkingPrefixMismatchBehavior binding)
    {
        using var fixture = new Fixture(model);
        fixture.Service.Functions.Add(Tool());
        fixture.Service.FunctionsDisabled = true;
        fixture.Service.WithThinkingBinding(binding);
        AddLegacyParallelTurn(fixture.Service);
        await fixture.Service.WithConversationInstruction("Keep this historical instruction.")
            .GetCompletionAsync("Continue imported history.");
        var original = fixture.Requests[0];
        AssertLegacyCalls(original, "toolu_first", "toolu_second");
        Assert.IsNull(original["tools"]);
        Assert.AreEqual(1, CountInstruction(original, "Keep this historical instruction."));

        fixture.Service.WithTurnInstruction("Only the next generation.");
        var originalInput = fixture.Service.ActivateChat.Messages[0];
        originalInput.Content = "Temporary user edit for counting.";
        var before = Snapshot(fixture.Service);
        fixture.CountStatus = outcome == "failure" ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
        fixture.CancelCount = outcome == "cancel";
        if (outcome == "failure")
            await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetInputTokenCountAsync());
        else if (outcome == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.GetInputTokenCountAsync());
        else
            await fixture.Service.GetInputTokenCountAsync();
        Assert.AreEqual(before, Snapshot(fixture.Service));
        AssertLegacyCalls(fixture.Requests[1], "toolu_first", "toolu_second");
        Assert.AreEqual(0, CountInstruction(fixture.Requests[1], "Only the next generation."));
        Assert.AreEqual(1, CountInstruction(fixture.Requests[1], "Keep this historical instruction."));

        originalInput.Content = "Check both.";
        fixture.CountStatus = HttpStatusCode.OK;
        fixture.CancelCount = false;
        await fixture.Service.GetCompletionAsync("Continue after count.");
        var continued = fixture.Requests[2];
        AssertPrefix(original, continued);
        AssertLegacyCalls(continued, "toolu_first", "toolu_second");
        Assert.AreEqual(1, CountInstruction(continued, "Only the next generation."));
        Assert.AreEqual(1, CountInstruction(continued, "Keep this historical instruction."));
        Assert.IsFalse(continued.ToJsonString().Contains("Temporary user edit", StringComparison.Ordinal));
        Assert.AreEqual(binding == ClaudeThinkingPrefixMismatchBehavior.Error ? "error" : "drop_block",
            continued["thinking"]!["block_binding"]!["prefix_mismatch_behavior"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task LegacyParallelToolRecords_ValidateEverySignedRecordAndRetainDistinctRounds(string model)
    {
        using var fixture = new Fixture(model);
        AddLegacyParallelTurn(fixture.Service);
        AddLegacyParallelTurn(fixture.Service, "next_first", "next_second", addUser: false);
        var original = Snapshot(fixture.Service);
        await fixture.Service.GetInputTokenCountAsync();
        AssertLegacyCalls(fixture.Requests[0], "toolu_first", "toolu_second", "next_first", "next_second");
        var messages = fixture.Requests[0]["messages"]!.AsArray();
        Assert.AreEqual(5, messages.Count);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(ToolBlocks), messages[1]!["content"]));
        Assert.AreEqual(original, Snapshot(fixture.Service));

        // The second public record is collapsed on the wire, but its signed text
        // must still be validated; otherwise an edit could be silently ignored.
        var secondRecord = fixture.Service.ActivateChat.Messages[2];
        secondRecord.Content = "Edited hidden duplicate.";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual(1, fixture.Requests.Count);
        secondRecord.Content = "Checking both.";
        await fixture.Service.GetInputTokenCountAsync();
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0], fixture.Requests[1]));
        Assert.AreEqual(original, Snapshot(fixture.Service));
        await fixture.Service.GetCompletionAsync("Continue.");
        AssertPrefix(fixture.Requests[0], fixture.Requests[2]);
    }

    private static void AddLegacyParallelTurn(AnthropicService service,
        string first = "toolu_first", string second = "toolu_second", bool addUser = true)
    {
        if (addUser) service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Check both."));
        var blocks = ToolBlocks.Replace("toolu_first", first, StringComparison.Ordinal)
            .Replace("toolu_second", second, StringComparison.Ordinal);
        foreach (var id in new[] { first, second })
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Checking both.")
            {
                Metadata = new()
                {
                    [MessageMetadataKeys.MessageType] = "function_call",
                    [MessageMetadataKeys.FunctionId] = id,
                    [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                    [MessageMetadataKeys.FunctionName] = "lookup",
                    [MessageMetadataKeys.OriginalContent] = blocks
                }
            });
        foreach (var id in new[] { first, second })
            service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "result-" + id)
            {
                Metadata = new()
                {
                    [MessageMetadataKeys.FunctionId] = id,
                    [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                    [MessageMetadataKeys.FunctionName] = "lookup"
                }
            });
    }

    private static void AssertLegacyCalls(JsonObject body, params string[] expectedIds)
    {
        var calls = body["messages"]!.AsArray().Where(message => message!["role"]!.GetValue<string>() == "assistant")
            .Where(message => message!["content"] is JsonArray)
            .SelectMany(message => message!["content"]!.AsArray())
            .Where(block => block!["type"]!.GetValue<string>() == "tool_use")
            .Select(block => block!["id"]!.GetValue<string>()).ToArray();
        CollectionAssert.AreEqual(expectedIds, calls);
    }

    private static int CountInstruction(JsonObject body, string instruction)
        => body["messages"]!.AsArray().Count(message => message!["role"]!.GetValue<string>() == "system" &&
            message["content"] is JsonValue text && text.TryGetValue<string>(out var value) && value == instruction);

    [TestMethod]
    [DataRow(FunctionCallMode.Auto, null, "auto")]
    [DataRow(FunctionCallMode.None, null, "none")]
    [DataRow(FunctionCallMode.Auto, "lookup", "tool")]
    public async Task CountToolsAndChoice_MatchGenerationPolicy(FunctionCallMode mode, string? force, string expected)
    {
        using var fixture = new Fixture("claude-opus-5");
        fixture.Service.Functions.Add(Tool());
        fixture.Service.FunctionCallMode = mode;
        fixture.Service.ForceFunctionName = force;
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, "First input."));
        await fixture.Service.GetInputTokenCountAsync();
        var count = fixture.Requests[0];
        Assert.AreEqual(expected, count["tool_choice"]!["type"]!.GetValue<string>());
        Assert.AreEqual(force, count["tool_choice"]!["name"]?.GetValue<string>());
        var schema = count["tools"]![0]!["input_schema"]!;
        Assert.AreEqual("object", schema["type"]!.GetValue<string>());
        var expectedProperties = JsonNode.Parse("""
            {
                "city": { "type": "string", "description": "City name", "enum": ["Seoul", "Busan"] },
                "days": { "type": "array", "items": { "type": "string" } }
            }
            """);
        Assert.IsTrue(JsonNode.DeepEquals(expectedProperties, schema["properties"]));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("[\"city\"]"), schema["required"]));
        await fixture.Service.GetCompletionAsync("Continue.");
        Assert.IsTrue(JsonNode.DeepEquals(count["tools"], fixture.Requests[1]["tools"]));
        Assert.IsTrue(JsonNode.DeepEquals(count["tool_choice"], fixture.Requests[1]["tool_choice"]));
    }

    [TestMethod]
    [DataRow("claude-opus-5", "success")]
    [DataRow("claude-opus-5", "failure")]
    [DataRow("claude-opus-5", "cancel")]
    [DataRow("claude-sonnet-5-5", "success")]
    [DataRow("claude-sonnet-5-5", "failure")]
    [DataRow("claude-sonnet-5-5", "cancel")]
    public async Task Counting_DoesNotCommitHistoryOrConsumePendingInstructions(string model, string outcome)
    {
        using var fixture = new Fixture(model);
        fixture.Service.Functions.Add(Tool());
        await fixture.Service.WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync("accepted");
        fixture.Service.WithTurnInstruction("instruction for the next generation");
        var temporary = new Message(ActorRole.User, "temporary history to count");
        fixture.Service.ActivateChat.Messages.Add(temporary);
        var history = Snapshot(fixture.Service);
        fixture.CountStatus = outcome == "failure" ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
        fixture.CancelCount = outcome == "cancel";

        if (outcome == "failure")
            await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetInputTokenCountAsync());
        else if (outcome == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.GetInputTokenCountAsync());
        else
            Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual(history, Snapshot(fixture.Service));
        Assert.IsFalse(fixture.Requests[1]["messages"]!.ToJsonString().Contains("instruction for the next generation", StringComparison.Ordinal));
        AssertCountOnlyFields(fixture.Requests[1]);
        Assert.AreEqual("high", fixture.Requests[1]["messages"]![0]!["output_config"]!["effort"]!.GetValue<string>());

        fixture.Service.ActivateChat.Messages.Remove(temporary);
        fixture.CountStatus = HttpStatusCode.OK;
        fixture.CancelCount = false;
        await fixture.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("continue");
        AssertPrefix(fixture.Requests[0], fixture.Requests[2]);
        Assert.IsFalse(fixture.Requests[2]["messages"]!.ToJsonString().Contains("temporary history to count", StringComparison.Ordinal));
        StringAssert.Contains(fixture.Requests[2]["messages"]!.ToJsonString(), "instruction for the next generation");
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["thinking"], fixture.Requests[2]["thinking"]));
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["output_config"], fixture.Requests[2]["output_config"]));
    }

    [TestMethod]
    public async Task StandalonePromptCount_RemainsIndependentOfHistorySystemAndTools()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        fixture.Service.ActivateChat.SystemMessage = "conversation system";
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, "conversation history"));
        fixture.Service.Functions.Add(Tool());
        var history = Snapshot(fixture.Service);
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync("only this prompt"));
        var count = fixture.Requests.Single();
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("[{\"role\":\"user\",\"content\":\"only this prompt\"}]"), count["messages"]));
        Assert.IsNull(count["system"]);
        Assert.IsNull(count["tools"]);
        Assert.IsNull(count["tool_choice"]);
        AssertCountOnlyFields(count);
        Assert.AreEqual(history, Snapshot(fixture.Service));
    }

    private static FunctionDefinition Tool(Func<Dictionary<string, object>, Task<string>>? handler = null) => new()
    {
        Name = "lookup",
        Description = "Look up the weather for a city and selected days.",
        Parameters = new FunctionParameters
        {
            Properties = new()
            {
                ["city"] = new ParameterProperty { Type = "string", Description = "City name", Enum = new() { "Seoul", "Busan" } },
                ["days"] = new ParameterProperty { Type = "array", Items = new ParameterProperty { Type = "string" } }
            },
            Required = new() { "city" }
        },
        Handler = handler ?? (_ => throw new AssertFailedException("Counting must not execute tools."))
    };

    private static string Snapshot(AnthropicService service) => JsonSerializer.Serialize(new
    {
        service.ActivateChat.Messages,
        service.ActivateChat.SystemMessage,
        service.LastThinkingContent,
        service.LastInputTransformations,
        service.LastCitations,
        service.LastProcessing
    });

    private static void AssertCountOnlyFields(JsonObject body)
    {
        foreach (var excluded in new[] { "temperature", "max_tokens", "stream", "speed", "output_config" })
            Assert.IsNull(body[excluded], $"Counting must omit generation field {excluded}.");
        Assert.IsTrue(body["messages"]!.AsArray().All(message => message!["role"]!.GetValue<string>() != "function"));
    }

    private static void AssertPrefix(JsonObject original, JsonObject continued)
    {
        var prefix = original["messages"]!.AsArray();
        var messages = continued["messages"]!.AsArray();
        Assert.IsTrue(messages.Count >= prefix.Count);
        for (var index = 0; index < prefix.Count; index++)
            Assert.IsTrue(JsonNode.DeepEquals(prefix[index], messages[index]), $"Message {index} changed on the wire.");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public AnthropicService Service { get; }
        public HttpClient Client { get; }
        public List<JsonObject> Requests { get; } = new();
        public List<string> Paths { get; } = new();
        public Queue<string> GenerationResponses { get; } = new();
        public HttpStatusCode CountStatus { get; set; } = HttpStatusCode.OK;
        public bool CancelCount { get; set; }

        public Fixture(string model)
        {
            Client = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline-key", model, Client);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            Paths.Add(request.RequestUri!.AbsolutePath);
            var counting = request.RequestUri.AbsolutePath.EndsWith("/count_tokens", StringComparison.Ordinal);
            if (counting && CancelCount)
            {
                Client.CancelPendingRequests();
                cancellationToken.ThrowIfCancellationRequested();
                throw new AssertFailedException("HTTP cancellation must cancel the token-count request.");
            }
            return new HttpResponseMessage(counting ? CountStatus : HttpStatusCode.OK)
            {
                Content = new StringContent(counting ? "{\"input_tokens\":42}" :
                    GenerationResponses.Count > 0 ? GenerationResponses.Dequeue() : Answer, Encoding.UTF8, "application/json")
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Client.Dispose();
            base.Dispose(disposing);
        }
    }
}
