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
[TestCategory("SummaryPolicy")]
public class AnthropicPersistentInstructionCompactionTests
{
    private const string First = "Use Korean for the remaining conversation.";
    private const string Second = "Keep future answers under three sentences.";
    private const string Third = "Use metric units for the remaining conversation.";

    [TestMethod]
    [DataRow("claude-sonnet-5-5", false, false)]
    [DataRow("claude-sonnet-5-5", false, true)]
    [DataRow("claude-sonnet-5-5", true, false)]
    [DataRow("claude-sonnet-5-5", true, true)]
    [DataRow("claude-opus-5-5", true, false)]
    [DataRow("claude-opus-5-5", true, true)]
    [DataRow("claude-fable-5-1", true, false)]
    [DataRow("claude-fable-5-1", true, true)]
    [DataRow("claude-mythos-5-1", true, false)]
    [DataRow("claude-mythos-5-1", true, true)]
    public async Task AllowedCompaction_KeepsPersistentAuthorityAndDoesNotPromoteTransientContext(
        string model, bool signed, bool automatic)
    {
        using var fixture = new Fixture(model, signed);
        var service = fixture.Service;
        service.SystemMessage = "BASE_SYSTEM";
        service.WithConversationInstruction(First).WithTurnInstruction("EXPIRED_TURN");
        await service.GetCompletionAsync("old input", context: new AIRequestContext
        {
            SystemMessagePrefix = "EXPIRED_PREFIX", SystemMessageSuffix = "EXPIRED_SUFFIX",
            AdditionalMessages = new[] { new Message(ActorRole.System, "UNMARKED_SYSTEM_ATTACHMENT") }
        });
        await service.GetCompletionAsync("retained input");
        service.ConversationPolicy = Policy(2);

        if (automatic) await service.GetCompletionAsync("after compaction");
        else await service.ApplySummaryPolicyIfNeededAsync();
        service.ConversationPolicy.TriggerCount = 1000;
        await service.GetInputTokenCountAsync();

        var count = fixture.Requests[^1];
        AssertPersistentSystem(count, First);
        StringAssert.Contains(count["system"]![0]!["text"]!.GetValue<string>(), "BASE_SYSTEM");
        foreach (var expired in new[] { "EXPIRED_TURN", "EXPIRED_PREFIX", "EXPIRED_SUFFIX", "UNMARKED_SYSTEM_ATTACHMENT", "old input" })
            Assert.IsFalse(count.ToJsonString().Contains(expired, StringComparison.Ordinal), expired);
        Assert.AreEqual(automatic ? 4 : 2, service.ActivateChat.Messages.Count);
        Assert.AreEqual("SUMMARY_WITH_NO_INSTRUCTIONS", service.ConversationPolicy.CurrentSummary);
        await service.GetCompletionAsync("next answer");
        AssertPersistentSystem(fixture.Requests[^1], First);
    }

    [TestMethod]
    public async Task RepeatedCompaction_PreservesInstructionOrderAndSurvivingNewerAuthority()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        service.WithConversationInstruction(First);
        await service.GetCompletionAsync("first");
        service.WithConversationInstruction(Second);
        await service.GetCompletionAsync("second");
        service.ConversationPolicy = Policy(2);
        await service.ApplySummaryPolicyIfNeededAsync();
        service.ConversationPolicy.TriggerCount = 1000;
        service.WithConversationInstruction(Third);
        await service.GetCompletionAsync("third");

        var afterFirstCut = fixture.Requests[^1];
        AssertPersistentSystem(afterFirstCut, First);
        CollectionAssert.AreEqual(new[] { Second, Third }, ConversationInstructions(afterFirstCut));
        service.ConversationPolicy.TriggerCount = 0;
        await service.ApplySummaryPolicyIfNeededAsync();
        service.ConversationPolicy.TriggerCount = 1000;
        await service.GetInputTokenCountAsync();
        AssertPersistentSystem(fixture.Requests[^1], First, Second);
        CollectionAssert.AreEqual(new[] { Third }, ConversationInstructions(fixture.Requests[^1]));

        await service.GetCompletionAsync("fourth");
        service.ConversationPolicy.TriggerCount = 0;
        await service.ApplySummaryPolicyIfNeededAsync();
        service.ConversationPolicy.TriggerCount = 1000;
        await service.GetInputTokenCountAsync();
        AssertPersistentSystem(fixture.Requests[^1], First, Second, Third);
        Assert.IsEmpty(ConversationInstructions(fixture.Requests[^1]));
    }

    [TestMethod]
    [DataRow("failure")]
    [DataRow("cancel")]
    public async Task FailedSummary_DoesNotMoveInstructionsAndRetryMovesThemExactlyOnce(string outcome)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var service = fixture.Service;
        service.WithConversationInstruction(First);
        await service.GetCompletionAsync("first");
        await service.GetCompletionAsync("second");
        await service.GetInputTokenCountAsync();
        var before = fixture.Requests[^1]["messages"]!.DeepClone();
        var originals = service.ActivateChat.Messages.ToArray();
        service.ConversationPolicy = Policy(2);
        if (outcome == "failure") fixture.FailNext = true;
        else fixture.CancelNext = cancellation;

        if (outcome == "failure")
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.ApplySummaryPolicyIfNeededAsync());
        else
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplySummaryPolicyIfNeededAsync(cancellation.Token));

        Assert.IsNull(service.ConversationPolicy.CurrentSummary);
        CollectionAssert.AreEqual(originals, service.ActivateChat.Messages.ToArray());
        await service.GetInputTokenCountAsync();
        Assert.IsTrue(JsonNode.DeepEquals(before, fixture.Requests[^1]["messages"]));
        Assert.IsNull(fixture.Requests[^1]["system"]);
        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();
        AssertPersistentSystem(fixture.Requests[^1], First);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedInput_DoesNotCreatePersistentInstruction(bool cancel)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var service = fixture.Service;
        service.WithConversationInstruction("UNACCEPTED_INSTRUCTION");
        if (cancel) fixture.CancelNext = cancellation;
        else fixture.FailNext = true;
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetCompletionAsync("failed input", cancellationToken: cancellation.Token));
        else
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.GetCompletionAsync("failed input"));
        await service.GetCompletionAsync("accepted later input");
        service.ConversationPolicy = Policy(2);
        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();
        Assert.IsFalse(fixture.Requests[^1].ToJsonString().Contains("UNACCEPTED_INSTRUCTION", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClearAndReplaceHistory_ResetsMovedInstructions(bool refillBeforeRequest)
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        service.WithConversationInstruction(First);
        await service.GetCompletionAsync("first");
        await service.GetCompletionAsync("second");
        service.ConversationPolicy = Policy(2);
        await service.ApplySummaryPolicyIfNeededAsync();
        service.ConversationPolicy.TriggerCount = 1000;
        service.ActivateChat.ClearMessages();
        if (refillBeforeRequest) service.ActivateChat.Messages.Add(new Message(ActorRole.User, "replacement imported input"));
        await service.GetInputTokenCountAsync();
        Assert.IsFalse(fixture.Requests[^1].ToJsonString().Contains(First, StringComparison.Ordinal));
        await service.GetCompletionAsync("fresh input");
        Assert.IsFalse(fixture.Requests[^1].ToJsonString().Contains(First, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ChatSwitchAndStatelessHelper_DoNotShareMovedInstructions()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        var firstChat = service.ActivateChat;
        service.WithConversationInstruction(First);
        await service.GetCompletionAsync("first");
        await service.GetCompletionAsync("second");
        service.ConversationPolicy = Policy(2);
        await service.ApplySummaryPolicyIfNeededAsync();
        service.ConversationPolicy.TriggerCount = 1000;
        await service.GetCompletionAsync("isolated helper", RequestProfiles.QueryRewrite);
        Assert.IsFalse(fixture.Requests[^1].ToJsonString().Contains(First, StringComparison.Ordinal));
        service.AddNewChat();
        await service.GetCompletionAsync("other conversation");
        Assert.IsFalse(fixture.Requests[^1].ToJsonString().Contains(First, StringComparison.Ordinal));
        service.SetActivateChat(firstChat.Id);
        await service.GetCompletionAsync("original conversation");
        AssertPersistentSystem(fixture.Requests[^1], First);
    }

    [TestMethod]
    public async Task FullCut_KeepsLatestUserTurnSoExplicitClearRemainsAReset()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        service.WithConversationInstruction(First);
        await service.GetCompletionAsync("first");
        await service.GetCompletionAsync("latest user turn");
        var latest = service.ActivateChat.Messages.Skip(2).ToArray();
        service.ConversationPolicy = Policy(0);
        await service.ApplySummaryPolicyIfNeededAsync();
        CollectionAssert.AreEqual(latest, service.ActivateChat.Messages.ToArray());
        await service.GetInputTokenCountAsync();
        AssertPersistentSystem(fixture.Requests[^1], First);
        service.ActivateChat.ClearMessages();
        await service.GetCompletionAsync("new conversation");
        Assert.IsFalse(fixture.Requests[^1].ToJsonString().Contains(First, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FullCut_ClosesHiddenToolDependenciesOfItsRetainedUserAnchor()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        service.WithConversationInstruction(First);
        await service.GetCompletionAsync("instruction owner");
        var call = new FunctionCall { Id = "toolu_retained", Source = IdSource.Claude, Name = "lookup", Arguments = new() };
        var batch = new FunctionCallBatch(new[] { call });
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "") { FunctionCallBatch = batch });
        await service.GetCompletionAsync("retained result label", context: new AIRequestContext
        {
            RequestMessageOverride = new Message(ActorRole.Function, "")
            {
                FunctionCallResultBatch = new FunctionCallResultBatch(batch.Id,
                    new[] { new FunctionCallResult { Call = call, Content = "tool result" } })
            }
        });
        var retained = service.ActivateChat.Messages.Skip(2).ToArray();
        service.ConversationPolicy = Policy(0);
        await service.ApplySummaryPolicyIfNeededAsync();
        CollectionAssert.AreEqual(retained, service.ActivateChat.Messages.ToArray());
        await service.GetInputTokenCountAsync();
        AssertPersistentSystem(fixture.Requests[^1], First);
        var blocks = fixture.Requests[^1]["messages"]!.AsArray().Where(message => message?["content"] is JsonArray)
            .SelectMany(message => message!["content"]!.AsArray()).ToArray();
        Assert.AreEqual(1, blocks.Count(block => block?["type"]?.GetValue<string>() == "tool_use"));
        Assert.AreEqual(1, blocks.Count(block => block?["type"]?.GetValue<string>() == "tool_result"));
    }

    [TestMethod]
    public async Task IdenticalTurnAndPersistentText_RemainsOnePersistentInstructionAfterCompactionAndCounting()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        service.WithTurnInstruction(First).WithConversationInstruction(First);
        await service.GetCompletionAsync("first");
        await service.GetCompletionAsync("second");
        service.ConversationPolicy = Policy(2);
        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();
        var firstCount = fixture.Requests[^1].DeepClone();
        await service.GetInputTokenCountAsync();
        Assert.IsTrue(JsonNode.DeepEquals(firstCount, fixture.Requests[^1]));
        AssertPersistentSystem(fixture.Requests[^1], First);
    }

    private static SummaryConversationPolicy Policy(uint keep) => new() { TriggerCount = 0, KeepRecentCount = keep };
    private static string[] ConversationInstructions(JsonObject body) => body["messages"]!.AsArray()
        .Where(message => message?["role"]?.GetValue<string>() == "system" && message?["clear_at"] == null && message?["content"] is JsonValue)
        .Select(message => message!["content"]!.GetValue<string>()).ToArray();
    private static void AssertPersistentSystem(JsonObject body, params string[] expected)
    {
        var blocks = body["system"]!.AsArray();
        var actual = blocks.Where(block => block?["type"]?.GetValue<string>() == "text")
            .Select(block => block!["text"]!.GetValue<string>())
            .Where(text => !text.StartsWith("[Previous conversation summary]", StringComparison.Ordinal) && text != "BASE_SYSTEM").ToArray();
        CollectionAssert.AreEqual(expected, actual);
        foreach (var instruction in expected)
            Assert.IsFalse(body["messages"]!.ToJsonString().Contains(instruction, StringComparison.Ordinal), "A moved instruction must be sent only once.");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        private readonly bool _signed;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = [];
        public bool FailNext { get; set; }
        public CancellationTokenSource? CancelNext { get; set; }
        public Fixture(string model = "claude-sonnet-5-5", bool signed = false)
        {
            _signed = signed;
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline", model, _http);
            if (signed) Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
            else Service.WithBetweenToolsThinking();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            Requests.Add(body);
            if (FailNext) { FailNext = false; throw new HttpRequestException("Synthetic failure."); }
            if (CancelNext is { } source) { CancelNext = null; source.Cancel(); }
            var count = request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal);
            bool summary = body["messages"]!.ToJsonString().Contains("Please summarize the following conversation", StringComparison.Ordinal);
            var content = new List<object>();
            if (_signed && !summary) content.Add(new { type = "thinking", thinking = "", signature = "opaque-fixture" });
            content.Add(new { type = "text", text = summary ? "SUMMARY_WITH_NO_INSTRUCTIONS" : "answer" });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(count ? "{\"input_tokens\":7}" : JsonSerializer.Serialize(new
                {
                    id = "answer", content, stop_reason = "end_turn", usage = new { input_tokens = 8, output_tokens = 2 }
                }), Encoding.UTF8, "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
