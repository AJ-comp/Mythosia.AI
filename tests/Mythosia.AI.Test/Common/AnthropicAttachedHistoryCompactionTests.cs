using Mythosia.AI.Exceptions;
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
[TestCategory("SummaryPolicy")]
public class AnthropicAttachedHistoryCompactionTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task StatelessRequest_DoesNotProjectUnrelatedMalformedHistoryBeforeSending(
        bool queryRewrite, bool hasPolicy)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var service = fixture.Service;
        var unrelated = new Message(ActorRole.Function, "UNRELATED_MALFORMED_RESULT");
        service.ActivateChat.Messages.Add(unrelated);
        service.StatelessMode = !queryRewrite;
        if (hasPolicy)
            service.ConversationPolicy = new SummaryConversationPolicy
            {
                TriggerCount = 0, KeepRecentCount = 0, CurrentSummary = "EXISTING_SUMMARY"
            };
        var originalChat = service.ActivateChat;

        Assert.AreEqual("answer", await service.GetCompletionAsync("independent prompt",
            queryRewrite ? RequestProfiles.QueryRewrite : null));

        var request = Assert.ContainsSingle(fixture.Requests);
        Assert.HasCount(1, request["messages"]!.AsArray());
        StringAssert.Contains(request["messages"]!.ToJsonString(), "independent prompt");
        Assert.IsFalse(request.ToJsonString().Contains("UNRELATED_MALFORMED_RESULT", StringComparison.Ordinal));
        Assert.IsFalse(request.ToJsonString().Contains("EXISTING_SUMMARY", StringComparison.Ordinal));
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreSame(unrelated, Assert.ContainsSingle(service.ActivateChat.Messages));
        Assert.AreEqual(hasPolicy ? "EXISTING_SUMMARY" : null, service.ConversationPolicy?.CurrentSummary);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InapplicableCompaction_DoesNotProjectOrMutateMalformedHistory(bool unmetTrigger)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var service = fixture.Service;
        var unrelated = new Message(ActorRole.Function, "UNRELATED_MALFORMED_RESULT");
        service.ActivateChat.Messages.Add(unrelated);
        if (unmetTrigger)
            service.ConversationPolicy = new SummaryConversationPolicy
            {
                TriggerCount = 100, KeepRecentCount = 0, CurrentSummary = "EXISTING_SUMMARY"
            };

        await service.ApplySummaryPolicyIfNeededAsync();

        Assert.IsEmpty(fixture.Requests);
        Assert.AreSame(unrelated, Assert.ContainsSingle(service.ActivateChat.Messages));
        Assert.AreEqual("UNRELATED_MALFORMED_RESULT", unrelated.Content);
        Assert.AreEqual(unmetTrigger ? "EXISTING_SUMMARY" : null, service.ConversationPolicy?.CurrentSummary);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StatelessOverflow_PreservesOriginalFailureWithoutInspectingUnrelatedHistory(bool queryRewrite)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var service = fixture.Service;
        var unrelated = new Message(ActorRole.Function, "UNRELATED_MALFORMED_RESULT");
        service.ActivateChat.Messages.Add(unrelated);
        service.StatelessMode = !queryRewrite;
        service.ConversationPolicy = new SummaryConversationPolicy
        {
            TriggerCount = 0, KeepRecentCount = 0, CurrentSummary = "EXISTING_SUMMARY"
        };
        var originalChat = service.ActivateChat;
        fixture.OverflowNext = true;

        var exception = await Assert.ThrowsExactlyAsync<ContextLengthExceededException>(() =>
            service.GetCompletionAsync("independent prompt", queryRewrite ? RequestProfiles.QueryRewrite : null));

        Assert.AreSame(fixture.LastOverflow, exception);
        Assert.AreEqual("stateless", exception.RecoverySkipReason);
        Assert.AreEqual(0, exception.RecoveryAttempts);
        Assert.HasCount(1, fixture.Requests);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreSame(unrelated, Assert.ContainsSingle(service.ActivateChat.Messages));
        Assert.AreEqual("UNRELATED_MALFORMED_RESULT", unrelated.Content);
        Assert.AreEqual("EXISTING_SUMMARY", service.ConversationPolicy.CurrentSummary);
    }

    public static IEnumerable<object[]> AttachmentCases()
    {
        foreach (var model in new[] { "claude-sonnet-5-5", "claude-opus-5-5", "claude-fable-5-1" })
        foreach (var representation in new[] { "native", "typed", "legacy" })
        foreach (var binding in new[] { "default", "error", "drop" })
        foreach (var automatic in new[] { false, true })
            yield return new object[] { model, representation, binding, automatic };
    }

    [TestMethod]
    [DynamicData(nameof(AttachmentCases))]
    public async Task AcceptedSignedAttachments_UseTheSameCompactionPolicyAsPublicHistory(
        string model, string representation, string binding, bool automatic)
    {
        using var fixture = new Fixture(model);
        var service = fixture.Service;
        if (binding != "default") service.WithThinkingBinding(binding == "drop"
            ? ClaudeThinkingPrefixMismatchBehavior.DropBlock : ClaudeThinkingPrefixMismatchBehavior.Error);
        await service.GetCompletionAsync("older input");
        await service.GetCompletionAsync("attached input", context: AttachedContext(representation));
        var originals = service.ActivateChat.Messages.ToArray();
        await service.GetInputTokenCountAsync();
        var acceptedWire = fixture.Requests[^1]["messages"]!.DeepClone().AsArray();
        Assert.AreEqual(2, CountSignedBlocks(acceptedWire));
        service.ConversationPolicy = Policy();
        var requestsBefore = fixture.Requests.Count;

        if (automatic) await service.GetCompletionAsync("next input");
        else await service.ApplySummaryPolicyIfNeededAsync();

        var compacted = binding == "drop";
        Assert.AreEqual((compacted ? 1 : 0) + (automatic ? 1 : 0), fixture.Requests.Count - requestsBefore);
        Assert.AreEqual(compacted ? "answer" : null, service.ConversationPolicy.CurrentSummary);
        Assert.AreEqual((compacted ? 2 : 4) + (automatic ? 2 : 0), service.ActivateChat.Messages.Count);
        if (!compacted)
            for (var index = 0; index < originals.Length; index++)
                Assert.AreSame(originals[index], service.ActivateChat.Messages[index]);

        await service.GetInputTokenCountAsync();
        var retained = fixture.Requests[^1]["messages"]!.AsArray();
        Assert.AreEqual(2, CountSignedBlocks(retained), "Signed and redacted attachments must remain intact.");
        if (!compacted) AssertPrefix(acceptedWire, retained);
        else Assert.IsFalse(retained.ToJsonString().Contains("older input", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("native")]
    [DataRow("typed")]
    [DataRow("legacy")]
    public async Task AcceptedSignedOverride_CompactionPreviewAndCountingDoNotPublishTemporaryEdits(string representation)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var service = fixture.Service;
        await service.GetCompletionAsync("older input");
        var context = SignedOverrideContext(representation);
        await service.GetCompletionAsync("source input", context: context);
        var source = service.ActivateChat.Messages[2];
        await service.GetInputTokenCountAsync();
        var acceptedWire = fixture.Requests[^1]["messages"]!.DeepClone().AsArray();
        Assert.AreEqual(1, CountSignedBlocks(acceptedWire));

        // This edit replaces the accepted override in a temporary count projection.
        // Neither skipped compaction nor counting may publish that projection.
        source.Content = "temporary source edit";
        service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 100, KeepRecentCount = 2 };
        var requestsBefore = fixture.Requests.Count;
        await service.ApplySummaryPolicyIfNeededAsync();
        Assert.AreEqual(requestsBefore, fixture.Requests.Count);
        await service.GetInputTokenCountAsync();
        Assert.AreEqual(0, CountSignedBlocks(fixture.Requests[^1]["messages"]!.AsArray()));

        source.Content = "source input";
        await service.GetInputTokenCountAsync();
        AssertPrefix(acceptedWire, fixture.Requests[^1]["messages"]!.AsArray());
        service.ConversationPolicy.TriggerCount = 2;
        requestsBefore = fixture.Requests.Count;
        await service.ApplySummaryPolicyIfNeededAsync();
        Assert.AreEqual(requestsBefore, fixture.Requests.Count, "The accepted signed override must still block compaction.");
        Assert.HasCount(4, service.ActivateChat.Messages);
        Assert.IsNull(service.ConversationPolicy.CurrentSummary);
    }

    [TestMethod]
    public async Task RemovedAttachmentOwner_DoesNotLeaveStaleSignedContentInCompactionGuard()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var service = fixture.Service;
        await service.GetCompletionAsync("older input");
        await service.GetCompletionAsync("attachment owner", context: AttachedContext("native"));
        // Remove the whole accepted occurrence and its response, then import replacements.
        service.ActivateChat.Messages.RemoveAt(3);
        service.ActivateChat.Messages.RemoveAt(2);
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "replacement user"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "replacement assistant"));
        service.ConversationPolicy = Policy();
        var requestsBefore = fixture.Requests.Count;

        await service.ApplySummaryPolicyIfNeededAsync();

        Assert.AreEqual(requestsBefore + 1, fixture.Requests.Count);
        Assert.HasCount(2, service.ActivateChat.Messages);
        await service.GetInputTokenCountAsync();
        Assert.AreEqual(0, CountSignedBlocks(fixture.Requests[^1]["messages"]!.AsArray()));
    }

    [TestMethod]
    [DataRow("http")]
    [DataRow("cancel")]
    [DataRow("partial-stream")]
    public async Task UnacceptedSignedAttachments_DoNotBlockLaterCompaction(string failure)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        using var cancellation = new CancellationTokenSource();
        var service = fixture.Service;
        await service.GetCompletionAsync("older input");
        fixture.FailNext = failure == "http";
        fixture.CancelNext = failure == "cancel" ? cancellation : null;
        if (failure == "http")
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
                service.GetCompletionAsync("failed input", context: AttachedContext("native")));
        else if (failure == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                service.GetCompletionAsync("failed input", context: AttachedContext("native"), cancellationToken: cancellation.Token));
        else
        {
            var sawError = false;
            await foreach (var item in service.StreamAsync(new Message(ActorRole.User, "failed input"),
                StreamOptions.FullOptions, AttachedContext("native")))
                sawError |= item.Type == StreamingContentType.Error;
            Assert.IsTrue(sawError);
        }
        Assert.HasCount(3, service.ActivateChat.Messages);
        service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 1 };
        var requestsBefore = fixture.Requests.Count;

        await service.ApplySummaryPolicyIfNeededAsync();

        Assert.AreEqual(requestsBefore + 1, fixture.Requests.Count);
        Assert.HasCount(1, service.ActivateChat.Messages);
        Assert.AreEqual("answer", service.ConversationPolicy.CurrentSummary);
        await service.GetInputTokenCountAsync();
        Assert.AreEqual(0, CountSignedBlocks(fixture.Requests[^1]["messages"]!.AsArray()));
        Assert.AreEqual("answer", await service.GetCompletionAsync("retry"));
    }

    [TestMethod]
    [DataRow("http")]
    [DataRow("cancel")]
    public async Task FailedDropBlockSummary_RetainsAttachmentsAndAllowsProtectiveRetry(string failure)
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        using var cancellation = new CancellationTokenSource();
        var service = fixture.Service;
        await service.GetCompletionAsync("older input");
        await service.GetCompletionAsync("attachment owner", context: AttachedContext("native"));
        await service.GetInputTokenCountAsync();
        var beforeWire = fixture.Requests[^1]["messages"]!.DeepClone().AsArray();
        var beforeMessages = service.ActivateChat.Messages.ToArray();
        service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
        service.ConversationPolicy = Policy();
        fixture.FailNext = failure == "http";
        fixture.CancelNext = failure == "cancel" ? cancellation : null;

        if (failure == "http")
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.ApplySummaryPolicyIfNeededAsync());
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplySummaryPolicyIfNeededAsync(cancellation.Token));

        CollectionAssert.AreEqual(beforeMessages, service.ActivateChat.Messages.ToArray());
        Assert.IsNull(service.ConversationPolicy.CurrentSummary);
        service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        var requestsBefore = fixture.Requests.Count;
        await service.ApplySummaryPolicyIfNeededAsync();
        Assert.AreEqual(requestsBefore, fixture.Requests.Count, "The surviving signed attachments must block the retry.");
        await service.GetInputTokenCountAsync();
        AssertPrefix(beforeWire, fixture.Requests[^1]["messages"]!.AsArray());
    }

    private static SummaryConversationPolicy Policy() => new() { TriggerCount = 2, KeepRecentCount = 2 };

    private static AIRequestContext AttachedContext(string representation)
    {
        var messages = new List<Message>
        {
            new(ActorRole.Assistant, "unsigned attachment"), new(ActorRole.User, "between attachments")
        };
        messages.AddRange(SignedTurn(representation, redacted: false, "first"));
        messages.AddRange(SignedTurn(representation, redacted: true, "second"));
        return new AIRequestContext { AdditionalMessages = messages };
    }

    private static AIRequestContext SignedOverrideContext(string representation)
    {
        var turn = SignedTurn(representation, redacted: false, "override");
        return new AIRequestContext { RequestMessageOverride = turn[0], AdditionalMessages = turn.Skip(1).ToArray() };
    }

    private static Message[] SignedTurn(string representation, bool redacted, string id)
    {
        var call = new FunctionCall { Id = "toolu_" + id, Source = IdSource.Claude, Name = "lookup" };
        var blocks = new JsonArray
        {
            redacted ? new JsonObject { ["type"] = "redacted_thinking", ["data"] = "opaque-" + id }
                : new JsonObject { ["type"] = "thinking", ["thinking"] = "opaque reasoning", ["signature"] = "signature-" + id },
            new JsonObject { ["type"] = "text", ["text"] = "signed attachment" }
        };
        if (representation != "native") blocks.Add(new JsonObject
        {
            ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = new JsonObject()
        });
        var raw = JsonSerializer.Deserialize<JsonElement>(blocks.ToJsonString());
        var assistant = new Message(ActorRole.Assistant, "signed attachment");
        Message result;
        if (representation == "typed")
        {
            var batch = new FunctionCallBatch(new[] { call })
            {
                Metadata = new() { [MessageMetadataKeys.OriginalContent] = raw }
            };
            assistant.FunctionCallBatch = batch;
            result = new Message(ActorRole.Function, "")
            {
                FunctionCallResultBatch = new FunctionCallResultBatch(batch.Id,
                    new[] { new FunctionCallResult { Call = call, Content = "tool result" } })
            };
        }
        else if (representation == "legacy")
        {
            assistant.Metadata = new()
            {
                [MessageMetadataKeys.MessageType] = "function_call", [MessageMetadataKeys.OriginalContent] = raw,
                [MessageMetadataKeys.FunctionId] = call.Id, [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                [MessageMetadataKeys.FunctionName] = call.Name
            };
            result = new Message(ActorRole.Function, "tool result")
            {
                Metadata = new()
                {
                    [MessageMetadataKeys.FunctionId] = call.Id, [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                    [MessageMetadataKeys.FunctionName] = call.Name
                }
            };
        }
        else
        {
            assistant.Metadata = new() { ["mythosia_claude_native_content"] = raw };
            result = new Message(ActorRole.User, "after signed attachment");
        }
        return new[] { assistant, result };
    }

    private static int CountSignedBlocks(JsonArray messages) => messages
        .Where(message => message!["role"]!.GetValue<string>() == "assistant" && message["content"] is JsonArray)
        .SelectMany(message => message!["content"]!.AsArray())
        .Count(block => block!["type"]!.GetValue<string>() is "thinking" or "redacted_thinking");

    private static void AssertPrefix(JsonArray expected, JsonArray actual)
    {
        Assert.IsTrue(actual.Count >= expected.Count);
        for (var index = 0; index < expected.Count; index++)
            Assert.AreEqual(expected[index]!.ToJsonString(), actual[index]!.ToJsonString(), "Changed wire prefix at " + index);
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = [];
        public bool FailNext { get; set; }
        public bool OverflowNext { get; set; }
        public ContextLengthExceededException? LastOverflow { get; private set; }
        public CancellationTokenSource? CancelNext { get; set; }
        public Fixture(string model)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline", model, _http);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            Requests.Add(body);
            if (FailNext) { FailNext = false; throw new HttpRequestException("Synthetic transport failure."); }
            if (OverflowNext)
            {
                OverflowNext = false;
                LastOverflow = new ContextLengthExceededException("Synthetic context overflow", "offline fixture", 400);
                throw LastOverflow;
            }
            if (CancelNext is { } source) { CancelNext = null; source.Cancel(); }
            var count = request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal);
            var stream = body["stream"]?.GetValue<bool>() == true;
            var response = count ? "{\"input_tokens\":7}" : stream ? PartialSse :
                """{"id":"answer","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn"}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
        private const string PartialSse = """
            data: {"type":"message_start","message":{"id":"answer","role":"assistant","content":[]}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"partial"}}


            """;
    }
}
