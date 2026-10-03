using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// Public calls are independent requests, including calls awaited inside a context
/// provider. Implementation delegation and format repair keep the original request.
/// The assertions inspect the HTTP contract, not private execution identities.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class RequestExecutionIdentityTests
{
    public enum EntryPath { String, Message, Builder, BuilderStream, BuilderRun }

    public static IEnumerable<object[]> NestedCases()
    {
        foreach (var model in new[] { AIModels.Anthropic.ClaudeSonnet4_6, AIModels.Anthropic.ClaudeSonnet5_5 })
        foreach (var path in Enum.GetValues<EntryPath>())
        foreach (var reuseProfile in new[] { false, true })
        foreach (var reuseMessage in path == EntryPath.Message ? new[] { false, true } : new[] { false })
            yield return new object[] { model, path, reuseProfile, reuseMessage };
    }

    [TestMethod]
    [DynamicData(nameof(NestedCases))]
    public async Task NestedPublicRequest_HasItsOwnContext_EvenWithTheSameProfileAndMessage(
        string model, EntryPath path, bool reuseProfile, bool reuseMessage)
    {
        using var wire = new ClaudeWire();
        using var http = new HttpClient(wire);
        var service = new ProfileService(model, http) { Temperature = 0.83f };
        var originalChat = service.ActivateChat;
        originalChat.Messages.Add(new Message(ActorRole.User, "PREEXISTING_HISTORY"));
        var originalHistory = JsonSerializer.Serialize(originalChat.Messages);
        var profile = RequestProfiles.QueryRewrite;
        var outerInput = new Message(ActorRole.User, "OUTER_ORIGINAL");
        var innerInput = reuseMessage ? outerInput : new Message(ActorRole.User, "INNER_ORIGINAL");
        var contextCalls = 0;
        service.WithSystemMessageProvider(async token =>
        {
            if (++contextCalls != 1) return null;
            var result = await service.GetCompletionAsync(innerInput,
                reuseProfile ? profile : RequestProfiles.QueryRewrite, Context("INNER"), token);
            Assert.AreEqual("answer", result);
            return Context("OUTER");
        });

        Assert.AreEqual("answer", await Send(service, path, outerInput, profile));

        Assert.HasCount(2, wire.Requests);
        AssertOwnContext(wire.Requests[0], "INNER", "OUTER");
        AssertOwnContext(wire.Requests[1], "OUTER", "INNER");
        Assert.AreEqual(2, contextCalls, "The nested call builds its own dynamic context once.");
        CollectionAssert.AreEqual(new[] { AIRequestPurpose.QueryRewrite, AIRequestPurpose.QueryRewrite }, service.Applied.ToArray(),
            "Reusing a profile must not merge logical requests; overload and builder delegation must not reapply it.");
        Assert.AreEqual(2, service.Restored);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(originalHistory, JsonSerializer.Serialize(originalChat.Messages));
        Assert.AreEqual("OUTER_ORIGINAL", outerInput.Content);
        Assert.AreEqual(reuseMessage ? "OUTER_ORIGINAL" : "INNER_ORIGINAL", innerInput.Content);
        Assert.AreEqual(0.83f, service.Temperature);
        Assert.IsFalse(service.StatelessMode);
    }

    public static IEnumerable<object[]> ChildExitCases()
    {
        foreach (var path in new[] { EntryPath.Message, EntryPath.BuilderStream, EntryPath.BuilderRun })
        foreach (var outcome in new[] { "success", "failure", "cancel" })
        foreach (var configureChildOptions in new[] { false, true })
        foreach (var useChildProfile in new[] { false, true })
            yield return new object[] { path, outcome, configureChildOptions, useChildProfile };
    }

    [TestMethod]
    [DynamicData(nameof(ChildExitCases))]
    public async Task NestedPublicRequest_RestoresParentSettingsAndFeatures_OnEveryExit(
        EntryPath childPath, string outcome, bool configureChildOptions, bool useChildProfile)
    {
        using var childCancellation = new CancellationTokenSource();
        using var wire = new ClaudeWire();
        using var http = new HttpClient(wire);
        var service = new ProfileService(AIModels.Anthropic.ClaudeSonnet5_5, http)
        {
            Temperature = 0.83f,
            MaxTokens = 3333,
            StatelessMode = true
        };
        var parentProfile = new AIRequestProfile { Temperature = 0.17f, MaxTokens = 1111, DisableReasoning = true };
        var childProfile = new AIRequestProfile { Temperature = 0.71f, MaxTokens = 2222, DisableReasoning = true };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "parent.example" } });
        service.WithTurnInstruction("PARENT_TURN_ONLY");
        var entered = false;
        service.WithSystemMessageProvider(async _ =>
        {
            if (entered) return null;
            entered = true;
            if (configureChildOptions)
            {
                service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "child.example" } });
                service.WithTurnInstruction("CHILD_TURN_ONLY");
            }
            Task<string> Child() => Send(service, childPath, new Message(ActorRole.User, "CHILD_ORIGINAL"),
                useChildProfile ? childProfile : null, Context("CHILD"), childCancellation.Token);
            if (outcome == "success") Assert.AreEqual("answer", await Child());
            else if (outcome == "failure") await Assert.ThrowsExactlyAsync<HttpRequestException>(Child);
            else await Assert.ThrowsAsync<OperationCanceledException>(Child);
            return Context("PARENT");
        });
        wire.BeforeResponse = (index, token) =>
        {
            if (index != 0) return;
            if (outcome == "failure") throw new HttpRequestException("Synthetic child request failure.");
            if (outcome == "cancel") { childCancellation.Cancel(); token.ThrowIfCancellationRequested(); }
        };

        Assert.AreEqual("answer", await service.GetCompletionAsync("PARENT_ORIGINAL", parentProfile));

        Assert.HasCount(2, wire.Requests);
        AssertOwnContext(wire.Requests[0], "CHILD", "PARENT");
        AssertOwnContext(wire.Requests[1], "PARENT", "CHILD");
        Assert.AreEqual(useChildProfile ? 2222 : 3333, wire.Requests[0]["max_tokens"]!.GetValue<int>(),
            "A public child without a profile starts from service defaults, not the parent's temporary overrides.");
        Assert.AreEqual(1111, wire.Requests[1]["max_tokens"]!.GetValue<int>());
        if (configureChildOptions)
        {
            AssertSearchDomain(wire.Requests[0], "child.example");
            AssertContains("CHILD_TURN_ONLY", wire.Requests[0].ToJsonString());
        }
        else Assert.IsFalse(wire.Requests[0]["tools"]?.AsArray().Any(tool => tool?["name"]?.GetValue<string>() == "web_search") == true);
        AssertSearchDomain(wire.Requests[1], "parent.example");
        AssertDoesNotContain("PARENT_TURN_ONLY", wire.Requests[0].ToJsonString());
        AssertContains("PARENT_TURN_ONLY", wire.Requests[1].ToJsonString());
        AssertDoesNotContain("CHILD_TURN_ONLY", wire.Requests[1].ToJsonString());
        Assert.HasCount(useChildProfile ? 2 : 1, service.Applied);
        Assert.AreEqual(service.Applied.Count, service.Restored);

        Assert.AreEqual("answer", await service.GetCompletionAsync("UNRELATED_FOLLOWUP"));

        var next = wire.Requests[2];
        Assert.AreEqual(3333, next["max_tokens"]!.GetValue<int>());
        Assert.IsFalse(next["tools"]?.AsArray().Any(tool => tool?["name"]?.GetValue<string>() == "web_search") == true);
        foreach (var marker in new[] { "PARENT_", "CHILD_" }) AssertDoesNotContain(marker, next.ToJsonString());
        Assert.IsEmpty(service.ActivateChat.Messages);
        Assert.AreEqual(0.83f, service.Temperature);
        Assert.AreEqual(3333u, service.MaxTokens);
        Assert.AreEqual(service.Applied.Count, service.Restored);
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5_5)]
    public async Task StructuredRepair_RetainsParentOptions_WhileNestedPublicHelperOwnsItsContext(string model)
    {
        using var wire = new ClaudeWire();
        using var http = new HttpClient(wire);
        var service = new ProfileService(model, http) { StructuredOutputMaxRetries = 1 };
        var entered = false;
        service.WithSystemMessageProvider(async token =>
        {
            if (!entered)
            {
                entered = true;
                Assert.AreEqual("answer", await service.GetCompletionAsync("HELPER_ORIGINAL", RequestProfiles.QueryRewrite,
                    Context("HELPER"), token));
            }
            return new AIRequestContext { RequestMessageOverride = new Message(ActorRole.User, "STRUCTURED_PARENT_OVERRIDE") };
        });
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "parent.example" } });
        wire.TextForResponse = index => index switch { 1 => "invalid-json", 2 => "{\"Value\":7}", _ => "answer" };
        wire.BeforeResponse = (index, _) =>
        {
            if (index == 1) service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "later.example" } });
        };

        var answer = await service.GetCompletionAsync<StructuredAnswer>("STRUCTURED_PARENT_ORIGINAL");

        Assert.AreEqual(7, answer.Value);
        Assert.HasCount(3, wire.Requests);
        AssertContains("HELPER_OVERRIDE", wire.Requests[0].ToJsonString());
        AssertContains("HELPER_ADDITIONAL", wire.Requests[0].ToJsonString());
        AssertDoesNotContain("[STRUCTURED OUTPUT]", wire.Requests[0].ToJsonString());
        AssertDoesNotContain("parent.example", wire.Requests[0].ToJsonString());
        foreach (var parent in wire.Requests.Skip(1))
        {
            AssertSearchDomain(parent, "parent.example");
            AssertContains("STRUCTURED_PARENT_OVERRIDE", parent.ToJsonString());
            AssertDoesNotContain("HELPER_", parent.ToJsonString());
        }
        AssertContains("[STRUCTURED OUTPUT CORRECTION]", wire.Requests[2].ToJsonString());
        CollectionAssert.AreEqual(new[] { AIRequestPurpose.QueryRewrite }, service.Applied.ToArray());
        Assert.AreEqual(1, service.Restored);

        service.StatelessMode = true;
        service.SystemMessageProvider = null;
        await service.GetCompletionAsync("FOLLOWUP");
        AssertSearchDomain(wire.Requests[3], "later.example");
        AssertDoesNotContain("[STRUCTURED OUTPUT]", wire.Requests[3].ToJsonString());
        AssertDoesNotContain("STRUCTURED_PARENT_OVERRIDE", wire.Requests[3].ToJsonString());
    }

    private static AIRequestContext Context(string owner) => new()
    {
        RequestMessageOverride = new Message(ActorRole.User, owner + "_OVERRIDE"),
        AdditionalMessages = new[] { new Message(ActorRole.User, owner + "_ADDITIONAL") },
        SystemMessageSuffix = owner + "_SUFFIX"
    };

    private static void AssertOwnContext(JsonObject body, string owner, string other)
    {
        var serialized = body.ToJsonString();
        foreach (var part in new[] { "OVERRIDE", "ADDITIONAL", "SUFFIX" })
        {
            AssertContains(owner + "_" + part, serialized);
            AssertDoesNotContain(other + "_" + part, serialized);
        }
        AssertDoesNotContain("_ORIGINAL", serialized);
        AssertDoesNotContain("PREEXISTING_HISTORY", serialized);
    }

    private static void AssertSearchDomain(JsonObject body, string domain)
    {
        var search = body["tools"]!.AsArray().Single(tool => tool?["name"]?.GetValue<string>() == "web_search");
        CollectionAssert.AreEqual(new[] { domain }, search!["allowed_domains"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
    }

    private static void AssertContains(string expected, string actual) => StringAssert.Contains(actual, expected);
    private static void AssertDoesNotContain(string unexpected, string actual)
        => Assert.IsFalse(actual.Contains(unexpected, StringComparison.Ordinal), $"Unexpected '{unexpected}' in: {actual}");

    private static async Task<string> Send(ProfileService service, EntryPath path, Message input,
        AIRequestProfile? profile, AIRequestContext? context = null, CancellationToken token = default)
    {
        if (path == EntryPath.String) return await service.GetCompletionAsync(input.Content, profile, context, token);
        if (path == EntryPath.Message) return await service.GetCompletionAsync(input, profile, context, token);
        var request = service.CreateRequest(input);
        if (profile != null) request = request.WithProfile(profile);
        if (context != null) request = request.WithContext(context);
        if (path == EntryPath.Builder) return await request.GetCompletionAsync(token);
        if (path == EntryPath.BuilderStream)
        {
            var text = new StringBuilder();
            await foreach (var chunk in request.StreamAsync(token)) text.Append(chunk);
            return text.ToString();
        }
        await using var run = await request.StartRunAsync(cancellationToken: token);
        return (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text;
    }

    public sealed class StructuredAnswer { public int Value { get; set; } }

    private sealed class ProfileService(string model, HttpClient http) : AnthropicService("offline-key", model, http)
    {
        public List<AIRequestPurpose> Applied { get; } = [];
        private int _restored;
        public int Restored => Volatile.Read(ref _restored);

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            Applied.Add(profile.Purpose);
            var restore = base.ApplyRequestProfile(profile);
            return () => { Interlocked.Increment(ref _restored); restore(); };
        }
    }

    private sealed class ClaudeWire : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];
        public Action<int, CancellationToken>? BeforeResponse { get; set; }
        public Func<int, string> TextForResponse { get; set; } = _ => "answer";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            var index = Requests.Count;
            Requests.Add(body);
            BeforeResponse?.Invoke(index, token);
            var text = TextForResponse(index);
            var response = new JsonObject
            {
                ["id"] = "fixture-" + index, ["type"] = "message", ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["stop_reason"] = "end_turn", ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 }
            };
            var streaming = body["stream"]?.GetValue<bool>() == true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streaming ? ToSse(response, text) : response.ToJsonString(), Encoding.UTF8,
                    streaming ? "text/event-stream" : "application/json")
            };
        }
    }

    private static string ToSse(JsonObject response, string text)
    {
        var start = (JsonObject)response.DeepClone();
        start["content"] = new JsonArray();
        var frames = new JsonObject[]
        {
            new() { ["type"] = "message_start", ["message"] = start },
            new() { ["type"] = "content_block_start", ["index"] = 0,
                ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = "" } },
            new() { ["type"] = "content_block_delta", ["index"] = 0,
                ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = text } },
            new() { ["type"] = "content_block_stop", ["index"] = 0 },
            new() { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = "end_turn" } },
            new() { ["type"] = "message_stop" }
        };
        return string.Concat(frames.Select(frame => "data: " + frame.ToJsonString() + "\n\n"));
    }
}
