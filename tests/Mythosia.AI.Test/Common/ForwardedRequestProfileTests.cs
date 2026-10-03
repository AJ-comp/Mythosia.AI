using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ForwardedRequestProfileTests
{
    [TestMethod]
    [DataRow("string", false)]
    [DataRow("string", true)]
    [DataRow("message", false)]
    [DataRow("message", true)]
    [DataRow("builder", false)]
    [DataRow("builder", true)]
    public async Task AdapterProfile_AppliesBeforeTransportAndRestoresDefaults(string entry, bool replaceInput)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = new Adapter(http) { ReplaceInput = replaceInput, MaxTokens = 8192 };
        service.Forward = _ => new AIRequestProfile { Stateless = true, MaxTokens = 123, DisableFunctions = true };
        service.Functions.Add(new FunctionDefinition { Name = "unused", Description = "must be disabled", Handler = _ => Task.FromResult("unused") });
        var old = new Message(ActorRole.User, "PRIVATE_PREVIOUS_CONVERSATION");
        service.ActivateChat.Messages.Add(old);
        if (entry == "string") await service.GetCompletionAsync("input");
        else if (entry == "message") await service.GetCompletionAsync(new Message(ActorRole.User, "input"), context: null);
        else await service.CreateRequest("input").WithMaxTokens(3333).GetCompletionAsync();
        var body = Assert.ContainsSingle(wire.Bodies);
        Assert.AreEqual(123, body["max_tokens"]!.GetValue<int>());
        Assert.IsFalse(body.ToJsonString().Contains("PRIVATE_PREVIOUS_CONVERSATION"));
        Assert.IsFalse(body.ToJsonString().Contains("unused"));
        Assert.AreSame(old, Assert.ContainsSingle(service.ActivateChat.Messages));
        Assert.AreEqual(8192u, service.MaxTokens);
        Assert.IsFalse(service.StatelessMode);
        Assert.IsFalse(service.FunctionsDisabled);
        Assert.AreEqual(1, service.Applied);
        Assert.AreEqual(service.Applied, service.Restored);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnchangedForwardedProfile_IsAppliedOnce_EvenWhenCopied(bool copy)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = new Adapter(http);
        var profile = new AIRequestProfile { Stateless = true, MaxTokens = 234 };
        service.Forward = value => copy ? new AIRequestProfile { Stateless = value!.Stateless, MaxTokens = value.MaxTokens } : value;
        await service.GetCompletionAsync("input", profile);
        Assert.AreEqual(234, wire.Bodies.Single()["max_tokens"]!.GetValue<int>());
        Assert.AreEqual(1, service.Applied);
        Assert.AreEqual(1, service.Restored);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdapterChangedProfile_IsComparedByValue_AndRetainsUnchangedRequestFeatures(bool mutateSameInstance)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = new Adapter(http);
        var profile = new AIRequestProfile { MaxTokens = 999 };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["retained.example"] });
        service.Forward = value =>
        {
            var changed = mutateSameInstance ? value! : new AIRequestProfile();
            changed.MaxTokens = 123;
            changed.Stateless = true;
            return changed;
        };
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "PRIVATE_HISTORY"));
        await service.GetCompletionAsync("input", profile);
        var json = wire.Bodies.Single().ToJsonString();
        Assert.AreEqual(123, wire.Bodies.Single()["max_tokens"]!.GetValue<int>());
        StringAssert.Contains(json, "retained.example");
        Assert.IsFalse(json.Contains("PRIVATE_HISTORY"));
        Assert.AreEqual(2, service.Applied);
        Assert.AreEqual(2, service.Restored);
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("failure")]
    [DataRow("cancel")]
    public async Task AdapterAuxiliaryProfile_SuppressesParentOptionsWithoutLeakingThem(string outcome)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        using var cancellation = new CancellationTokenSource();
        var service = new Adapter(http) { MaxTokens = 3333 };
        service.Forward = _ => RequestProfiles.QueryRewrite;
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = ["parent.example"] });
        service.WithTurnInstruction("PARENT_INSTRUCTION");
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "PRIVATE_HISTORY"));
        wire.Before = token =>
        {
            if (outcome == "failure") throw new HttpRequestException("Synthetic failure");
            if (outcome == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
        };
        Task<string> Send() => service.CreateRequest("input").WithMaxTokens(1111).GetCompletionAsync(cancellation.Token);
        if (outcome == "failure") await Assert.ThrowsExactlyAsync<HttpRequestException>(Send);
        else if (outcome == "cancel") await Assert.ThrowsAsync<OperationCanceledException>(Send);
        else await Send();
        var json = Assert.ContainsSingle(wire.Bodies).ToJsonString();
        Assert.IsFalse(json.Contains("parent.example"));
        Assert.IsFalse(json.Contains("PARENT_INSTRUCTION"));
        Assert.IsFalse(json.Contains("PRIVATE_HISTORY"));
        Assert.HasCount(1, service.ActivateChat.Messages);
        Assert.AreEqual(service.Applied, service.Restored);
        service.Forward = value => value;
        wire.Before = null;
        await service.GetCompletionAsync("next");
        var next = wire.Bodies.Last().ToJsonString();
        Assert.IsFalse(next.Contains("parent.example"));
        Assert.IsFalse(next.Contains("PARENT_INSTRUCTION"));
        Assert.AreEqual(3333, wire.Bodies.Last()["max_tokens"]!.GetValue<int>());
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("builder")]
    public async Task AdapterReenablesReasoning_RestoresOriginallyCapturedNativeSettings(string entry)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = new Adapter(http);
        service.Forward = _ => new AIRequestProfile { DisableReasoning = false };
        var profile = new AIRequestProfile { DisableReasoning = true };
        if (entry == "string") await service.GetCompletionAsync("input", profile);
        else await service.CreateRequest("input").WithProfile(profile).GetCompletionAsync();
        var body = Assert.ContainsSingle(wire.Bodies);
        Assert.AreEqual("adaptive", body["thinking"]!["type"]!.GetValue<string>(),
            "The replacement profile must remove the original profile's native override.");
        Assert.IsTrue(profile.DisableReasoning);
        Assert.AreEqual(2, service.Applied);
        Assert.AreEqual(service.Applied, service.Restored);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("builder")]
    [DataRow("structured")]
    public async Task AdapterStatelessProfile_IsAppliedBeforeAutomaticSummary(string entry)
    {
        using var wire = new Wire { ResponseText = "{\"Value\":7}" };
        using var http = new HttpClient(wire);
        var service = new Adapter(http)
        {
            ConversationPolicy = new SummaryConversationPolicy
            { TriggerCount = 2, KeepRecentCount = 1, CurrentSummary = "EXISTING_SUMMARY" }
        };
        service.Forward = profile => profile?.Purpose == AIRequestPurpose.Summarization
            ? profile : new AIRequestProfile { Stateless = true, MaxTokens = 123 };
        for (var i = 0; i < 6; i++)
            service.ActivateChat.Messages.Add(new Message(i % 2 == 0 ? ActorRole.User : ActorRole.Assistant, "PRIVATE_HISTORY_" + i));
        var history = service.ActivateChat.Messages.ToArray();
        if (entry == "string") await service.GetCompletionAsync("input");
        else if (entry == "builder") await service.CreateRequest("input").GetCompletionAsync();
        else Assert.AreEqual(7, (await service.GetCompletionAsync<Answer>("input")).Value);
        var body = Assert.ContainsSingle(wire.Bodies);
        Assert.IsFalse(body.ToJsonString().Contains("PRIVATE_HISTORY_"));
        Assert.IsFalse(body.ToJsonString().Contains("EXISTING_SUMMARY"));
        CollectionAssert.AreEqual(history, service.ActivateChat.Messages.ToArray());
        Assert.AreEqual("EXISTING_SUMMARY", service.ConversationPolicy.CurrentSummary);
    }

    public sealed class Answer { public int Value { get; set; } }

    private sealed class Adapter(HttpClient http) : AnthropicService("offline", AIModels.Anthropic.ClaudeSonnet5_5, http)
    {
        public Func<AIRequestProfile?, AIRequestProfile?> Forward { get; set; } = value => value;
        public bool ReplaceInput { get; init; } = true;
        public int Applied;
        public int Restored;
        public override async Task<string> GetCompletionAsync(Message message, AIRequestProfile? profile = null,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var input = ReplaceInput ? new Message(message.Role, "PREFIX " + message.Content) : message;
            return await base.GetCompletionAsync(input, Forward(profile), context, cancellationToken);
        }
        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            Applied++;
            var restore = base.ApplyRequestProfile(profile);
            return () => { restore(); Restored++; };
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];
        public Action<CancellationToken>? Before { get; set; }
        public string ResponseText { get; init; } = "ok";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
            Before?.Invoke(token);
            var response = System.Text.Json.JsonSerializer.Serialize(new
            { id = "r", content = new[] { new { type = "text", text = ResponseText } }, stop_reason = "end_turn" });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
