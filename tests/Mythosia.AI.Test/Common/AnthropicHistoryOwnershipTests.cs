using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
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
public class AnthropicHistoryOwnershipTests
{
    [TestMethod]
    [DataRow("claude-sonnet-5-5", false)]
    [DataRow("claude-opus-5-5", false)]
    [DataRow("claude-sonnet-5-5", true)]
    [DataRow("claude-opus-5-5", true)]
    public async Task ReusedDirectInput_PreservesEachAcceptedOccurrenceAndOwnsNewAttachments(string model, bool streaming)
    {
        using var fixture = new Fixture(model);
        var input = new Message(ActorRole.User, "reusable input");
        fixture.Service.WithTurnInstruction("FIRST TURN ONLY").WithConversationInstruction("FIRST PERSISTENT");
        await Send(fixture.Service, input, Context("first"), streaming);
        var first = fixture.Requests[0]["messages"]!.DeepClone().AsArray();

        fixture.Service.WithTurnInstruction("SECOND TURN ONLY").WithConversationInstruction("SECOND PERSISTENT");
        await Send(fixture.Service, input, Context("second"), streaming);
        var second = fixture.Requests[1]["messages"]!.AsArray();
        AssertPrefix(first, second);
        var newest = new JsonArray(second.Skip(first.Count + 1).Select(item => item!.DeepClone()).ToArray()).ToJsonString();
        StringAssert.Contains(newest, "second override");
        StringAssert.Contains(newest, "second additional");
        StringAssert.Contains(newest, "SECOND TURN ONLY");
        StringAssert.Contains(newest, "SECOND PERSISTENT");
        StringAssert.Contains(newest, "second prefix");
        StringAssert.Contains(newest, "second suffix");
        Assert.IsFalse(newest.Contains("first", StringComparison.OrdinalIgnoreCase));

        // Counts neither consume the next instruction nor modify accepted history.
        fixture.Service.WithTurnInstruction("NEXT REQUEST ONLY");
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);
        await fixture.Service.GetInputTokenCountAsync();
        await fixture.Service.GetInputTokenCountAsync();
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
        AssertPrefix(second, fixture.Requests[2]["messages"]!.AsArray());
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[2], fixture.Requests[3]));
        Assert.IsFalse(fixture.Requests[2].ToJsonString().Contains("NEXT REQUEST ONLY", StringComparison.Ordinal));
        await fixture.Service.GetCompletionAsync("third input");
        StringAssert.Contains(fixture.Requests[4].ToJsonString(), "NEXT REQUEST ONLY");
    }

    [TestMethod]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task ReusedCallerInput_DoesNotRewriteEarlierEffortOrAcceptedSource(string model)
    {
        using var fixture = new Fixture(model);
        var input = new Message(ActorRole.User, "first source");
        fixture.Service.WithReasoning(ReasoningLevel.High, CachePreservation.Required);
        await fixture.Service.GetCompletionAsync(input);
        var first = fixture.Requests[0]["messages"]!.AsArray();
        input.Content = "second source";
        fixture.Service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required);
        await fixture.Service.GetCompletionAsync(input);

        AssertPrefix(first, fixture.Requests[1]["messages"]!.AsArray());
        var efforts = fixture.Requests[1]["messages"]!.AsArray()
            .Where(item => item?["output_config"] != null)
            .Select(item => item!["output_config"]!["effort"]!.GetValue<string>()).ToArray();
        CollectionAssert.AreEqual(new[] { "high", "low" }, efforts);
        Assert.IsFalse(input.Metadata?.ContainsKey("mythosia_claude_effort") == true);

        // The actual history remains explicitly editable, independently of caller input.
        fixture.Service.ActivateChat.Messages[0].Content = "explicit history edit";
        await fixture.Service.GetInputTokenCountAsync();
        StringAssert.Contains(fixture.Requests[2].ToJsonString(), "explicit history edit");
        StringAssert.Contains(fixture.Requests[2].ToJsonString(), "second source");
    }

    [TestMethod]
    public async Task ImportedRepeatedMessageReference_DoesNotCopyAcceptedRequestAttachments()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        fixture.Service.WithTurnInstruction("ACCEPTED ONCE");
        await fixture.Service.GetCompletionAsync("accepted input", context: Context("accepted"));
        var first = fixture.Requests[0]["messages"]!.AsArray();
        // Repeating an actual history object bypasses execution input snapshots and
        // exercises the ledger's occurrence identity rather than unique message IDs.
        fixture.Service.ActivateChat.Messages.Add(fixture.Service.ActivateChat.Messages[0]);
        await fixture.Service.GetInputTokenCountAsync();
        await fixture.Service.GetCompletionAsync("new request");

        foreach (var body in fixture.Requests.Skip(1))
        {
            var messages = body["messages"]!.AsArray();
            AssertPrefix(first, messages);
            Assert.AreEqual(1, messages.Count(item => item?["content"]?.ToJsonString().Contains("ACCEPTED ONCE", StringComparison.Ordinal) == true));
            Assert.AreEqual(1, messages.Count(item => item?["content"]?.ToJsonString().Contains("accepted override", StringComparison.Ordinal) == true));
            Assert.AreEqual("accepted input", messages[first.Count + 1]!["content"]!.GetValue<string>());
        }
    }

    [TestMethod]
    public async Task FailedAttempt_DoesNotPublishItsWireAttachmentsToAnotherOccurrence()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var input = new Message(ActorRole.User, "repeated");
        fixture.Service.WithTurnInstruction("ACCEPTED FIRST");
        await fixture.Service.GetCompletionAsync(input);
        var first = fixture.Requests[0]["messages"]!.AsArray();
        fixture.FailNext = true;
        fixture.Service.WithTurnInstruction("FAILED ONLY");
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => fixture.Service.GetCompletionAsync(input));
        fixture.Service.WithTurnInstruction("ACCEPTED NEXT");
        await fixture.Service.GetCompletionAsync(input);

        AssertPrefix(first, fixture.Requests[2]["messages"]!.AsArray());
        Assert.IsFalse(fixture.Requests[2].ToJsonString().Contains("FAILED ONLY", StringComparison.Ordinal));
        StringAssert.Contains(fixture.Requests[2].ToJsonString(), "ACCEPTED NEXT");
    }

    [TestMethod]
    public async Task PartialAliasRemoval_WithDifferentAcceptedWiresIsRejectedWithoutGuessingOwnership()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        fixture.Service.WithTurnInstruction("FIRST OCCURRENCE ONLY");
        await fixture.Service.GetCompletionAsync("aliased history", context: Context("first"));
        fixture.Service.ActivateChat.Messages.Add(fixture.Service.ActivateChat.Messages[0]);
        await fixture.Service.GetCompletionAsync("accept repeated imported history");
        fixture.Service.ActivateChat.Messages.RemoveAt(0);
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetInputTokenCountAsync());

        StringAssert.Contains(error.Message, "occurrences own different");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetCompletionAsync("must not append"));
        fixture.Service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 3 };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ApplySummaryPolicyIfNeededAsync());
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
    }

    [TestMethod]
    public async Task StandaloneTokenCount_DoesNotInspectUnrelatedAmbiguousHistory()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        fixture.Service.WithTurnInstruction("FIRST OCCURRENCE ONLY");
        await fixture.Service.GetCompletionAsync("aliased history");
        fixture.Service.ActivateChat.Messages.Add(fixture.Service.ActivateChat.Messages[0]);
        await fixture.Service.GetCompletionAsync("accept repeated imported history");
        fixture.Service.ActivateChat.Messages.RemoveAt(0);
        var history = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);

        Assert.AreEqual(7u, await fixture.Service.GetInputTokenCountAsync("standalone prompt"));

        Assert.HasCount(3, fixture.Requests);
        Assert.AreEqual("standalone prompt", fixture.Requests[2]["messages"]![0]!["content"]!.GetValue<string>());
        Assert.AreEqual(history, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetInputTokenCountAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KnownCompactionCut_PreservesSurvivingOccurrenceWithoutTransferringAttachments(bool explicitSummary)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        fixture.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
        fixture.Service.WithTurnInstruction("FIRST OCCURRENCE ONLY");
        await fixture.Service.GetCompletionAsync("aliased history");
        fixture.Service.ActivateChat.Messages.Add(fixture.Service.ActivateChat.Messages[0]);
        await fixture.Service.GetCompletionAsync("accept repeated imported history");
        fixture.Service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 3 };
        if (explicitSummary)
        {
            await fixture.Service.ApplySummaryPolicyIfNeededAsync();
            fixture.Service.ConversationPolicy.TriggerCount = 100;
        }
        await fixture.Service.GetCompletionAsync("next request");

        Assert.HasCount(4, fixture.Requests);
        Assert.AreEqual("answer", fixture.Service.ConversationPolicy.CurrentSummary);
        Assert.HasCount(5, fixture.Service.ActivateChat.Messages);
        var retainedWire = fixture.Requests[3]["messages"]!.ToJsonString();
        StringAssert.Contains(retainedWire, "aliased history");
        Assert.IsFalse(retainedWire.Contains("FIRST OCCURRENCE ONLY", StringComparison.Ordinal));
        fixture.Service.ConversationPolicy = null;
        await fixture.Service.GetInputTokenCountAsync();
        Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("subsequent turn"));
    }

    [TestMethod]
    public async Task PartialAliasRemoval_WithEquivalentImportedWiresRemainsSupported()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var imported = new Message(ActorRole.User, "same imported content");
        fixture.Service.ActivateChat.Messages.Add(imported);
        fixture.Service.ActivateChat.Messages.Add(imported);
        await fixture.Service.GetCompletionAsync("accept imports");
        fixture.Service.ActivateChat.Messages.RemoveAt(0);
        await fixture.Service.GetInputTokenCountAsync();
        Assert.AreEqual(1, fixture.Requests[1]["messages"]!.AsArray()
            .Count(item => item?["content"]?.GetValueKind() == JsonValueKind.String &&
                item["content"]!.GetValue<string>() == "same imported content"));
    }

    public static IEnumerable<object[]> CompactionCases()
    {
        foreach (var model in new[] { "claude-sonnet-5-5", "claude-opus-5-5" })
        foreach (var representation in new[] { "legacy", "typed", "native" })
        foreach (var redacted in new[] { false, true })
        foreach (var explicitSummary in new[] { false, true })
        foreach (var binding in new[] { "default", "error", "drop" })
            yield return new object[] { model, representation, redacted, explicitSummary, binding };
    }

    [TestMethod]
    [DynamicData(nameof(CompactionCases))]
    public async Task SignedHistory_AllRepresentationsShareCountingSerializationAndCompactionPolicy(
        string model, string representation, bool redacted, bool explicitSummary, string binding)
    {
        using var fixture = new Fixture(model);
        if (binding != "default") fixture.Service.WithThinkingBinding(binding == "drop"
            ? ClaudeThinkingPrefixMismatchBehavior.DropBlock : ClaudeThinkingPrefixMismatchBehavior.Error);
        var raw = AddSignedHistory(fixture.Service, representation, redacted);
        var before = fixture.Service.ActivateChat.Messages.ToArray();
        await fixture.Service.GetInputTokenCountAsync();
        var count = fixture.Requests.Single()["messages"]!.AsArray();
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(raw), count[5]!["content"]));

        fixture.Service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 3 };
        if (explicitSummary)
        {
            await fixture.Service.ApplySummaryPolicyIfNeededAsync();
            Assert.AreEqual(binding == "drop" ? 3 : 7, fixture.Service.ActivateChat.Messages.Count);
            fixture.Service.ConversationPolicy.TriggerCount = 100;
        }
        await fixture.Service.GetCompletionAsync("next question");

        Assert.AreEqual(binding == "drop" ? 3 : 2, fixture.Requests.Count);
        var generation = fixture.Requests[^1]["messages"]!.AsArray();
        Assert.IsTrue(generation.Any(item => JsonNode.DeepEquals(JsonNode.Parse(raw), item?["content"])),
            "Signed blocks, block order and provider extensions must remain intact.");
        if (binding != "drop")
        {
            AssertPrefix(count, generation);
            Assert.AreEqual(9, fixture.Service.ActivateChat.Messages.Count);
            for (var index = 0; index < before.Length; index++)
                Assert.AreSame(before[index], fixture.Service.ActivateChat.Messages[index]);
        }
        else
        {
            Assert.AreEqual(5, fixture.Service.ActivateChat.Messages.Count);
            Assert.IsFalse(generation.ToJsonString().Contains("older-0", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow("legacy")]
    [DataRow("typed")]
    [DataRow("native")]
    public async Task MalformedPreservedContent_IsRejectedBeforeSummaryMutation(string representation)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        AddSignedHistory(fixture.Service, representation, false);
        var assistant = fixture.Service.ActivateChat.Messages[5];
        SetRawContent(assistant, representation, JsonSerializer.SerializeToElement(new { invalid = true }));
        fixture.Service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 3 };
        var before = JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetInputTokenCountAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ApplySummaryPolicyIfNeededAsync());

        Assert.IsEmpty(fixture.Requests);
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Service.ActivateChat.Messages));
    }

    [TestMethod]
    public async Task TypedHistory_PrecedenceIgnoresUnusedNativeAndLegacyMetadata()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var signed = AddSignedHistory(fixture.Service, "typed", false);
        var assistant = fixture.Service.ActivateChat.Messages[5];
        assistant.Metadata = new()
        {
            ["mythosia_claude_native_content"] = signed,
            [MessageMetadataKeys.OriginalContent] = signed
        };
        assistant.FunctionCallBatch!.Metadata![MessageMetadataKeys.OriginalContent] =
            """[{"type":"text","text":"Calling"},{"type":"tool_use","id":"toolu_one","name":"lookup","input":{}}]""";
        await fixture.Service.GetInputTokenCountAsync();
        Assert.IsFalse(fixture.Requests[0].ToJsonString().Contains("signature", StringComparison.Ordinal));
        fixture.Service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 3 };
        await fixture.Service.ApplySummaryPolicyIfNeededAsync();
        Assert.AreEqual(3, fixture.Service.ActivateChat.Messages.Count);
        Assert.AreEqual(2, fixture.Requests.Count);
    }

    private static AIRequestContext Context(string label) => new()
    {
        RequestMessageOverride = new Message(ActorRole.User, label + " override"),
        AdditionalMessages = new[] { new Message(ActorRole.User, label + " additional") },
        SystemMessagePrefix = label + " prefix",
        SystemMessageSuffix = label + " suffix"
    };

    private static async Task Send(AnthropicService service, Message input, AIRequestContext context, bool streaming)
    {
        if (streaming)
        {
            await foreach (var item in service.StreamAsync(input, StreamOptions.FullOptions, context))
                Assert.AreNotEqual(StreamingContentType.Error, item.Type);
        }
        else await service.GetCompletionAsync(input, context: context);
    }

    private static string AddSignedHistory(AnthropicService service, string representation, bool redacted)
    {
        for (var index = 0; index < 4; index++)
            service.ActivateChat.Messages.Add(new Message(index % 2 == 0 ? ActorRole.User : ActorRole.Assistant, "older-" + index));
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "current question"));
        var thinking = redacted ? """{"type":"redacted_thinking","data":"OPAQUE+/=","extension":{"unchanged":true}}"""
            : """{"type":"thinking","thinking":"opaque reasoning","signature":"signature+/한글=","extension":{"unchanged":true}}""";
        var raw = "[" + thinking + "," + """{"type":"text","text":"Calling"},{"type":"tool_use","id":"toolu_one","name":"lookup","input":{}}]""";
        var assistant = new Message(ActorRole.Assistant, "Calling");
        var call = new FunctionCall { Id = "toolu_one", Source = IdSource.Claude, Name = "lookup" };
        if (representation == "typed") assistant.FunctionCallBatch = new FunctionCallBatch(new[] { call }) { Metadata = new() };
        else if (representation == "legacy") assistant.Metadata = new()
        {
            [MessageMetadataKeys.MessageType] = "function_call", [MessageMetadataKeys.FunctionId] = call.Id,
            [MessageMetadataKeys.FunctionSource] = IdSource.Claude, [MessageMetadataKeys.FunctionName] = call.Name
        };
        else assistant.Metadata = new();
        // Exercise persisted JsonElement import for every representation.
        SetRawContent(assistant, representation, JsonSerializer.Deserialize<JsonElement>(raw));
        service.ActivateChat.Messages.Add(assistant);
        service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "result")
        {
            Metadata = new()
            {
                [MessageMetadataKeys.FunctionId] = call.Id, [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                [MessageMetadataKeys.FunctionName] = call.Name
            }
        });
        return raw;
    }

    private static void SetRawContent(Message assistant, string representation, object raw)
    {
        if (representation == "typed") assistant.FunctionCallBatch!.Metadata![MessageMetadataKeys.OriginalContent] = raw;
        else assistant.Metadata![representation == "legacy" ? MessageMetadataKeys.OriginalContent : "mythosia_claude_native_content"] = raw;
    }

    private static void AssertPrefix(JsonArray expected, JsonArray actual)
    {
        Assert.IsTrue(actual.Count >= expected.Count);
        for (var index = 0; index < expected.Count; index++)
            Assert.AreEqual(expected[index]!.ToJsonString(), actual[index]!.ToJsonString(), $"Wire prefix changed at {index}.");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = [];
        public bool FailNext { get; set; }
        public Fixture(string model)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline", model, _http);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            Requests.Add(body);
            if (FailNext) { FailNext = false; throw new HttpRequestException("synthetic failed attempt"); }
            var count = request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal);
            var stream = body["stream"]?.GetValue<bool>() == true;
            var response = count ? "{\"input_tokens\":7}" : stream ? Sse :
                """{"id":"answer","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":1}}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
        private const string Sse = """
            data: {"type":"message_start","message":{"id":"answer","role":"assistant","content":[],"usage":{"input_tokens":3,"output_tokens":0}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":1}}

            data: {"type":"message_stop"}


            """;
    }
}
