using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Utilities;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicHistoryProjectionTests
{
    private const string Blocks = """
        [{"type":"thinking","thinking":"opaque\nreasoning","signature":"signature/한글+==","provider_extension":{"future":[1,"unchanged"]}},
         {"type":"text","text":"Checking both."},
         {"type":"tool_use","id":"toolu_first","name":"lookup","input":{"city":"Seoul","days":["Monday"]}},
         {"type":"tool_use","id":"toolu_second","name":"lookup","input":{"city":"Busan","days":["Tuesday"]}}]
        """;

    [TestMethod]
    [DataRow("claude-sonnet-4-6")]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task LegacyHistory_ImportAndPersistenceRepresentationsShareOneWireContract(string model)
    {
        foreach (var representation in new[] { "enum", "int", "long", "json-number", "json-name", "name", "numeric-string" })
        {
            using var fixture = new Fixture(model);
            AddLegacyTurn(fixture.Service);
            RewriteSources(fixture.Service, representation);
            // The second per-call record has equivalent JSON with different object
            // property order and formatting. No signed or unknown field is removed.
            fixture.Service.ActivateChat.Messages[2].Metadata![MessageMetadataKeys.OriginalContent] =
                Reformat(Blocks).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var history = Snapshot(fixture.Service);

            await fixture.Service.GetInputTokenCountAsync();
            await fixture.Service.GetInputTokenCountAsync();
            Assert.AreEqual(history, Snapshot(fixture.Service), representation);
            Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0], fixture.Requests[1]), representation);
            AssertTurn(fixture.Requests[0], "toolu_first", "toolu_second");
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(Blocks), fixture.Requests[0]["messages"]![1]!["content"]));

            await fixture.Service.GetCompletionAsync("Continue.");
            AssertPrefix(fixture.Requests[0], fixture.Requests[2]);
            AssertTurn(fixture.Requests[2], "toolu_first", "toolu_second");
            Assert.AreEqual("signature/한글+==", fixture.Requests[2]["messages"]![1]!["content"]![0]!["signature"]!.GetValue<string>());
        }
    }

    [TestMethod]
    [DataRow("claude-sonnet-4-6")]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    public async Task DisablingToolDefinitions_DoesNotRewriteHistoricalProtocol(string model)
    {
        using var fixture = new Fixture(model);
        AddLegacyTurn(fixture.Service);
        fixture.Service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Handler = _ => throw new InvalidOperationException("No historical tool should execute.")
        });
        await fixture.Service.GetCompletionAsync("Continue with tools enabled.");
        Assert.IsNotNull(fixture.Requests[0]["tools"]);
        fixture.Service.FunctionsDisabled = true;
        await fixture.Service.GetCompletionAsync("Continue with tools disabled.");
        Assert.IsNull(fixture.Requests[1]["tools"]);
        AssertPrefix(fixture.Requests[0], fixture.Requests[1]);
        AssertTurn(fixture.Requests[1], "toolu_first", "toolu_second");
    }

    [TestMethod]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task AcceptedPrefix_RemainsVerbatimAfterWholeMetadataRoundtripAndEquivalentRawEdits(string model)
    {
        using var fixture = new Fixture(model);
        AddLegacyTurn(fixture.Service);
        // Imported history first needs one accepted generation to establish its
        // reasoning baseline; token counting intentionally does not accept it.
        await fixture.Service.CreateRequest("Accept imported history.")
            .WithReasoning(ReasoningLevel.High).GetCompletionAsync();
        await fixture.Service.CreateRequest("Establish cached reasoning.")
            .WithReasoning(ReasoningLevel.High, CachePreservation.Required).GetCompletionAsync();
        var accepted = fixture.Requests[1];
        var originalAssistant = accepted["messages"]!.AsArray().First(message =>
            message!["role"]!.GetValue<string>() == "assistant")!.ToJsonString();

        foreach (var message in fixture.Service.ActivateChat.Messages.Where(message => message.Metadata != null))
            message.Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(message.Metadata));
        foreach (var message in fixture.Service.ActivateChat.Messages.Where(message => message.Metadata?.ContainsKey(MessageMetadataKeys.OriginalContent) == true))
            message.Metadata![MessageMetadataKeys.OriginalContent] = Reformat(Blocks).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var history = Snapshot(fixture.Service);

        await fixture.Service.GetInputTokenCountAsync();
        Assert.AreEqual(history, Snapshot(fixture.Service));
        await fixture.Service.CreateRequest("Continue with the same history.")
            .WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync();
        AssertPrefix(accepted, fixture.Requests[2]);
        AssertPrefix(accepted, fixture.Requests[3]);
        AssertTurn(fixture.Requests[3], "toolu_first", "toolu_second");
        if (model != "claude-opus-5")
            Assert.AreEqual(originalAssistant, fixture.Requests[3]["messages"]!.AsArray().First(message =>
                message!["role"]!.GetValue<string>() == "assistant")!.ToJsonString(), "Retain the accepted first snapshot without reserializing reordered raw content.");
    }

    [TestMethod]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task Projection_DistinguishesTypedBatchesRoundsAndRepeatedCallRecords(string model)
    {
        using var fixture = new Fixture(model);
        AddLegacyTurn(fixture.Service);
        var call = new FunctionCall { Id = "toolu_typed", Source = IdSource.Claude, Name = "lookup" };
        var batch = new FunctionCallBatch(new[] { call });
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "") { FunctionCallBatch = batch });
        fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "")
        {
            FunctionCallResultBatch = new FunctionCallResultBatch(batch.Id, new[] { new FunctionCallResult { Call = call, Content = "typed-result" } })
        });
        AddLegacyTurn(fixture.Service, suffix: "_next", addUser: false);

        await fixture.Service.GetInputTokenCountAsync();
        AssertTurn(fixture.Requests[0], "toolu_first", "toolu_second", "toolu_typed", "toolu_first_next", "toolu_second_next");
        CollectionAssert.AreEqual(new[] { "user", "assistant", "user", "assistant", "user", "assistant", "user" },
            fixture.Requests[0]["messages"]!.AsArray().Select(message => message!["role"]!.GetValue<string>()).ToArray());

        // A duplicate of the same per-call ID is not a different call in one
        // parallel batch. Keep it visible instead of silently dropping a turn.
        var duplicate = fixture.Service.ActivateChat.Messages[1].Clone();
        fixture.Service.ActivateChat.Messages.Insert(2, duplicate);
        await fixture.Service.GetInputTokenCountAsync();
        AssertTurn(fixture.Requests[1], false, "toolu_first", "toolu_second", "toolu_first", "toolu_second", "toolu_typed", "toolu_first_next", "toolu_second_next");
    }

    [TestMethod]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    public async Task ImportedCrossProviderIds_ArePairedAfterWholeMetadataRoundtrip(string model)
    {
        using var fixture = new Fixture(model);
        var first = FunctionIdConverter.ToClaudeId("call_first", IdSource.OpenAI);
        var second = FunctionIdConverter.ToClaudeId("call_second", IdSource.OpenAI);
        AddLegacyTurn(fixture.Service);
        foreach (var message in fixture.Service.ActivateChat.Messages.Where(message => message.Metadata != null))
        {
            var metadata = message.Metadata!;
            metadata[MessageMetadataKeys.FunctionId] = metadata[MessageMetadataKeys.FunctionId].ToString() == "toolu_first" ? "call_first" : "call_second";
            metadata[MessageMetadataKeys.FunctionSource] = IdSource.OpenAI;
            if (metadata.ContainsKey(MessageMetadataKeys.OriginalContent))
                metadata[MessageMetadataKeys.OriginalContent] = Blocks.Replace("toolu_first", first).Replace("toolu_second", second);
            message.Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(metadata));
        }
        await fixture.Service.GetInputTokenCountAsync();
        AssertTurn(fixture.Requests[0], first, second);
    }

    [TestMethod]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    public async Task SignedOrUnknownPayloadChanges_AreNeverHiddenByDuplicateProjection(string model)
    {
        using var fixture = new Fixture(model);
        AddLegacyTurn(fixture.Service);
        var changed = JsonNode.Parse(Blocks)!.AsArray();
        changed[0]!["signature"] = "different-signature";
        changed[0]!["provider_extension"]!["future"]![1] = "different-extension";
        fixture.Service.ActivateChat.Messages[2].Metadata![MessageMetadataKeys.OriginalContent] = changed.ToJsonString();
        await fixture.Service.GetInputTokenCountAsync();
        AssertTurn(fixture.Requests[0], requirePairing: false, "toolu_first", "toolu_second", "toolu_first", "toolu_second");
        Assert.AreEqual("different-signature", fixture.Requests[0]["messages"]![2]!["content"]![0]!["signature"]!.GetValue<string>());
    }

    private static void AddLegacyTurn(AnthropicService service, string suffix = "", bool addUser = true)
    {
        if (addUser) service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Check both."));
        var raw = Blocks.Replace("toolu_first", "toolu_first" + suffix).Replace("toolu_second", "toolu_second" + suffix);
        foreach (var id in new[] { "toolu_first" + suffix, "toolu_second" + suffix })
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Checking both.")
            {
                Metadata = new()
                {
                    [MessageMetadataKeys.MessageType] = "function_call", [MessageMetadataKeys.FunctionId] = id,
                    [MessageMetadataKeys.FunctionSource] = IdSource.Claude, [MessageMetadataKeys.FunctionName] = "lookup",
                    [MessageMetadataKeys.OriginalContent] = raw
                }
            });
        foreach (var id in new[] { "toolu_first" + suffix, "toolu_second" + suffix })
            service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "result-" + id)
            {
                Metadata = new()
                {
                    [MessageMetadataKeys.FunctionId] = id, [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                    [MessageMetadataKeys.FunctionName] = "lookup"
                }
            });
    }

    private static void RewriteSources(AnthropicService service, string representation)
    {
        object source = representation switch
        {
            "int" => (int)IdSource.Claude,
            "long" => (long)IdSource.Claude,
            "json-number" => JsonSerializer.SerializeToElement(IdSource.Claude),
            "json-name" => JsonSerializer.SerializeToElement("Claude"),
            "name" => "Claude",
            "numeric-string" => "2",
            _ => IdSource.Claude
        };
        foreach (var message in service.ActivateChat.Messages.Where(message => message.Metadata != null))
            message.Metadata![MessageMetadataKeys.FunctionSource] = source;
    }

    private static JsonNode Reformat(string json) => Reorder(JsonNode.Parse(json)!)!;
    private static JsonNode? Reorder(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Reverse().Select(pair => KeyValuePair.Create(pair.Key, Reorder(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Reorder).ToArray()),
        _ => node?.DeepClone()
    };
    private static string Snapshot(AnthropicService service) => JsonSerializer.Serialize(service.ActivateChat.Messages);
    private static void AssertPrefix(JsonObject first, JsonObject next)
    {
        var prefix = first["messages"]!.AsArray();
        for (var i = 0; i < prefix.Count; i++)
            Assert.IsTrue(JsonNode.DeepEquals(prefix[i], next["messages"]![i]), $"Changed prefix at {i}.");
    }
    private static void AssertTurn(JsonObject body, params string[] expected) => AssertTurn(body, true, expected);
    private static void AssertTurn(JsonObject body, bool requirePairing, params string[] expected)
    {
        var blocks = body["messages"]!.AsArray().Where(message => message!["content"] is JsonArray)
            .SelectMany(message => message!["content"]!.AsArray()).ToArray();
        CollectionAssert.AreEqual(expected, blocks.Where(block => block!["type"]!.GetValue<string>() == "tool_use")
            .Select(block => block!["id"]!.GetValue<string>()).ToArray());
        if (requirePairing)
            CollectionAssert.AreEqual(expected, blocks.Where(block => block!["type"]!.GetValue<string>() == "tool_result")
                .Select(block => block!["tool_use_id"]!.GetValue<string>()).ToArray());
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = new();
        public AnthropicService Service { get; }
        private readonly HttpClient _client;
        public Fixture(string model)
        {
            _client = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline", model, _client);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
            var response = request.RequestUri!.AbsolutePath.EndsWith("/count_tokens", StringComparison.Ordinal)
                ? "{\"input_tokens\":42}"
                : "{\"id\":\"answer\",\"content\":[{\"type\":\"text\",\"text\":\"answer\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":8,\"output_tokens\":2}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) _client.Dispose(); base.Dispose(disposing); }
    }
}
