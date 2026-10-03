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
public class AnthropicCanonicalCompactionTests
{
    public static IEnumerable<object[]> MythosCases()
    {
        foreach (var model in new[] { "claude-mythos-5-1", "claude-mythos-5-1-20261001" })
        foreach (var location in new[] { "public", "override", "additional" })
        foreach (var binding in new[] { "default", "error", "drop" })
            yield return new object[] { model, location, binding };
    }

    [TestMethod]
    [DynamicData(nameof(MythosCases))]
    public async Task MythosBoundHistory_ProtectsEverySignedSourceUnlessDropWasSelected(
        string model, string location, string binding)
    {
        using var fixture = new Fixture(model);
        var service = fixture.Service;
        if (binding != "default") service.WithThinkingBinding(binding == "drop"
            ? ClaudeThinkingPrefixMismatchBehavior.DropBlock : ClaudeThinkingPrefixMismatchBehavior.Error);
        await service.GetCompletionAsync("older input");
        var signed = SignedAssistant();
        AIRequestContext? context = null;
        if (location == "public") fixture.SignedNext = true;
        else context = new AIRequestContext
        {
            RequestMessageOverride = location == "override" ? signed : null,
            AdditionalMessages = location == "override"
                ? new[] { new Message(ActorRole.User, "followup") }
                : new[] { signed, new Message(ActorRole.User, "followup") }
        };
        await service.GetCompletionAsync("retained input", context: context);
        await service.GetInputTokenCountAsync();
        var before = fixture.Requests[^1]["messages"]!.DeepClone();
        service.ConversationPolicy = Policy(2);

        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();

        var after = fixture.Requests[^1]["messages"]!;
        Assert.AreEqual(binding == "drop" ? 2 : 4, service.ActivateChat.Messages.Count);
        StringAssert.Contains(after.ToJsonString(), "opaque-signature");
        if (binding != "drop") Assert.IsTrue(JsonNode.DeepEquals(before, after));
        else Assert.IsFalse(after.ToJsonString().Contains("older input", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public async Task AcceptedResultOverride_ClampsPublicCutToItsWireCall(bool legacy, bool drop, bool force)
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        if (drop) service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "older input"));
        var (call, result) = Pair("toolu_override", legacy);
        service.ActivateChat.Messages.Add(call);
        await service.GetCompletionAsync("local display label", context: new AIRequestContext { RequestMessageOverride = result });
        var originals = service.ActivateChat.Messages.ToArray();
        service.ConversationPolicy = Policy(2);

        if (force) await service.ForceCompactAsync();
        else await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();

        Assert.AreEqual(3, service.ActivateChat.Messages.Count);
        Assert.AreSame(originals[1], service.ActivateChat.Messages[0]);
        AssertPaired(fixture.Requests[^1]["messages"]!, "toolu_override");
        await service.GetCompletionAsync("next input");
        AssertPaired(fixture.Requests[^1]["messages"]!, "toolu_override");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdditionalResult_KeepsEarlierBatchAfterThePublicPairClamp(bool legacy)
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        var (olderCall, olderResult) = Pair("toolu_older", legacy);
        var (recentCall, recentResult) = Pair("toolu_recent", legacy);
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "older input"));
        service.ActivateChat.Messages.Add(olderCall);
        service.ActivateChat.Messages.Add(recentCall);
        await service.GetCompletionAsync(recentResult, context: new AIRequestContext { AdditionalMessages = new[] { olderResult } });
        service.ConversationPolicy = Policy(2);

        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();

        Assert.AreEqual(4, service.ActivateChat.Messages.Count);
        Assert.AreSame(olderCall, service.ActivateChat.Messages[0]);
        AssertPaired(fixture.Requests[^1]["messages"]!, "toolu_older", "toolu_recent");
    }

    [TestMethod]
    public async Task AcceptedServerToolResultOverride_KeepsItsServerCall()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "older input"));
        var call = new Message(ActorRole.Assistant, "")
        {
            Metadata = new() { ["mythosia_claude_native_content"] =
                """[{"type":"server_tool_use","id":"srvtoolu_search","name":"web_search","input":{"query":"query"}}]""" }
        };
        service.ActivateChat.Messages.Add(call);
        await service.GetCompletionAsync("display label", context: new AIRequestContext
        {
            RequestMessageOverride = new Message(ActorRole.Assistant, "")
            {
                Metadata = new() { ["mythosia_claude_native_content"] =
                    """[{"type":"web_search_tool_result","tool_use_id":"srvtoolu_search","content":[]}]""" }
            }
        });
        service.ConversationPolicy = Policy(2);

        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();

        Assert.AreSame(call, service.ActivateChat.Messages[0]);
        Assert.AreEqual(3, service.ActivateChat.Messages.Count);
        var wire = fixture.Requests[^1]["messages"]!.ToJsonString();
        StringAssert.Contains(wire, "server_tool_use");
        StringAssert.Contains(wire, "web_search_tool_result");
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("failure")]
    [DataRow("cancel")]
    public async Task AttachedSelfContainedBatch_CanBeCompactedWithoutPublishingPreviewState(string outcome)
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        await service.GetCompletionAsync("older input");
        var (call, result) = Pair("toolu_attached", false);
        await service.GetCompletionAsync("retained input", context: new AIRequestContext { AdditionalMessages = new[] { call, result } });
        await service.GetInputTokenCountAsync();
        var before = fixture.Requests[^1]["messages"]!.DeepClone();
        var originals = service.ActivateChat.Messages.ToArray();
        service.ConversationPolicy = Policy(2);
        using var cancellation = new CancellationTokenSource();
        fixture.FailNext = outcome == "failure";
        fixture.CancelNext = outcome == "cancel" ? cancellation : null;

        if (outcome == "failure")
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.ApplySummaryPolicyIfNeededAsync());
        else if (outcome == "cancel")
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ApplySummaryPolicyIfNeededAsync(cancellation.Token));
        else await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();

        var after = fixture.Requests[^1]["messages"]!;
        AssertPaired(after, "toolu_attached");
        Assert.AreEqual(outcome == "success" ? 2 : 4, service.ActivateChat.Messages.Count);
        if (outcome != "success")
        {
            Assert.IsTrue(JsonNode.DeepEquals(before, after));
            Assert.IsNull(service.ConversationPolicy.CurrentSummary);
            for (var index = 0; index < originals.Length; index++) Assert.AreSame(originals[index], service.ActivateChat.Messages[index]);
        }
    }

    public static IEnumerable<object[]> ProjectionCases()
    {
        foreach (var model in new[] { "claude-sonnet-5-5", "claude-opus-5-5", "claude-fable-5-1", "claude-mythos-5-1" })
        foreach (var location in new[] { "public", "additional", "override-and-additional" })
            yield return new object[] { model, location };
    }

    [TestMethod]
    [DynamicData(nameof(ProjectionCases))]
    public async Task LegacyParallelProjection_IsSharedByHistoryOverridesAndAttachments(string model, string location)
    {
        using var fixture = new Fixture(model);
        var service = fixture.Service;
        var records = LegacyParallelRecords();
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "older input"));
        AIRequestContext? context = null;
        if (location == "public") foreach (var record in records) service.ActivateChat.Messages.Add(record);
        else context = new AIRequestContext
        {
            RequestMessageOverride = location == "override-and-additional" ? records[0] : null,
            AdditionalMessages = location == "override-and-additional" ? records.Skip(1).ToArray() : records
        };

        await service.GetCompletionAsync("display input", context: context);
        var generation = fixture.Requests[^1]["messages"]!.AsArray();
        AssertSingleParallelTurn(generation);
        var prefix = generation.DeepClone().AsArray();
        // Caller edits cannot alter the grouping identity or retained wire snapshots.
        if (location != "public") records[0].Metadata![MessageMetadataKeys.FunctionId] = "external mutation";
        await service.GetInputTokenCountAsync();
        var counted = fixture.Requests[^1]["messages"]!.AsArray();
        AssertSingleParallelTurn(counted);
        for (var index = 0; index < prefix.Count; index++) Assert.AreEqual(prefix[index]!.ToJsonString(), counted[index]!.ToJsonString());

        if (location == "override-and-additional")
        {
            // The second redundant record must survive even though it was grouped
            // away on the first wire. Replacing the public source removes only its
            // override; the complete attached parallel assistant turn remains.
            service.ActivateChat.Messages[1].Content = "edited public source";
            await service.GetInputTokenCountAsync();
            AssertSingleParallelTurn(fixture.Requests[^1]["messages"]!.AsArray());
        }
    }

    [TestMethod]
    public async Task ExplicitKnownCut_WithAliasedInputsRetainsOnlyTheSurvivingAttachmentOwner()
    {
        using var fixture = new Fixture();
        var service = fixture.Service;
        var (call, result) = Pair("toolu_owned", false);
        await service.GetCompletionAsync("aliased input", context: new AIRequestContext { AdditionalMessages = new[] { call, result } });
        service.ActivateChat.Messages.Add(service.ActivateChat.Messages[0]);
        await service.GetCompletionAsync("accept alias");
        service.ConversationPolicy = Policy(3);

        await service.ApplySummaryPolicyIfNeededAsync();
        await service.GetInputTokenCountAsync();

        Assert.AreEqual(3, service.ActivateChat.Messages.Count);
        Assert.IsFalse(fixture.Requests[^1].ToJsonString().Contains("toolu_owned", StringComparison.Ordinal));
    }

    private static SummaryConversationPolicy Policy(uint keep) => new() { TriggerCount = 0, KeepRecentCount = keep };

    private static Message SignedAssistant() => new(ActorRole.Assistant, "signed")
    {
        Metadata = new() { ["mythosia_claude_native_content"] = SignedContent }
    };

    private const string SignedContent = """[{"type":"thinking","thinking":"opaque","signature":"opaque-signature"},{"type":"redacted_thinking","data":"opaque-redaction"},{"type":"text","text":"signed"}]""";

    private static (Message Call, Message Result) Pair(string id, bool legacy)
    {
        var call = new FunctionCall { Id = id, Source = IdSource.Claude, Name = "lookup", Arguments = new() };
        if (legacy) return (new Message(ActorRole.Assistant, "")
        {
            Metadata = new()
            {
                [MessageMetadataKeys.MessageType] = "function_call", [MessageMetadataKeys.FunctionId] = id,
                [MessageMetadataKeys.FunctionSource] = IdSource.Claude, [MessageMetadataKeys.FunctionName] = "lookup",
                [MessageMetadataKeys.FunctionArguments] = "{}"
            }
        }, new Message(ActorRole.Function, "result")
        {
            Metadata = new() { [MessageMetadataKeys.FunctionId] = id, [MessageMetadataKeys.FunctionSource] = IdSource.Claude }
        });
        var batch = new FunctionCallBatch(new[] { call });
        return (new Message(ActorRole.Assistant, "") { FunctionCallBatch = batch },
            new Message(ActorRole.Function, "")
            {
                FunctionCallResultBatch = new FunctionCallResultBatch(batch.Id,
                    new[] { new FunctionCallResult { Call = call, Content = "result" } })
            });
    }

    private static Message[] LegacyParallelRecords()
    {
        const string content = """[{"type":"thinking","thinking":"opaque","signature":"opaque-signature","extension":{"unchanged":true}},{"type":"text","text":"parallel"},{"type":"tool_use","id":"toolu_a","name":"lookup","input":{}},{"type":"tool_use","id":"toolu_b","name":"lookup","input":{}}]""";
        var first = Pair("toolu_a", true);
        var second = Pair("toolu_b", true);
        foreach (var call in new[] { first.Call, second.Call })
        {
            call.Content = "parallel";
            call.Metadata![MessageMetadataKeys.OriginalContent] = JsonSerializer.Deserialize<JsonElement>(content);
        }
        return new[] { first.Call, second.Call, first.Result, second.Result };
    }

    private static void AssertSingleParallelTurn(JsonArray messages)
    {
        var toolTurns = messages.Where(message => message?["role"]?.GetValue<string>() == "assistant" &&
            message?["content"] is JsonArray blocks && blocks.Any(block => block?["type"]?.GetValue<string>() == "tool_use")).ToArray();
        Assert.HasCount(1, toolTurns);
        Assert.AreEqual(1, messages.Count(message => message?["content"] is JsonArray blocks &&
            blocks.Any(block => block?["type"]?.GetValue<string>() == "tool_result")));
        StringAssert.Contains(toolTurns[0]!.ToJsonString(), "opaque-signature");
        StringAssert.Contains(toolTurns[0]!.ToJsonString(), "unchanged");
        AssertPaired(messages, "toolu_a", "toolu_b");
    }

    private static void AssertPaired(JsonNode messages, params string[] ids)
    {
        var blocks = messages.AsArray().Where(message => message?["content"] is JsonArray)
            .SelectMany(message => message!["content"]!.AsArray()).ToArray();
        foreach (var id in ids)
        {
            Assert.AreEqual(1, blocks.Count(block => block?["type"]?.GetValue<string>() == "tool_use" && block?["id"]?.GetValue<string>() == id), "Call " + id);
            Assert.AreEqual(1, blocks.Count(block => block?["type"]?.GetValue<string>() == "tool_result" && block?["tool_use_id"]?.GetValue<string>() == id), "Result " + id);
        }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = [];
        public bool SignedNext { get; set; }
        public bool FailNext { get; set; }
        public CancellationTokenSource? CancelNext { get; set; }
        public Fixture(string model = "claude-sonnet-5-5")
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline", model, _http);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
            if (FailNext) { FailNext = false; throw new HttpRequestException("Synthetic failure."); }
            if (CancelNext is { } source) { CancelNext = null; source.Cancel(); }
            var count = request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal);
            var content = SignedNext ? SignedContent : """[{"type":"text","text":"answer"}]""";
            SignedNext = false;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(count ? "{\"input_tokens\":7}" :
                    "{\"id\":\"answer\",\"content\":" + content + ",\"stop_reason\":\"end_turn\"}", Encoding.UTF8, "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
