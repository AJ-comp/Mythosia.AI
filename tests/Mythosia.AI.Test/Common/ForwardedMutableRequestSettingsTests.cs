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
public class ForwardedMutableRequestSettingsTests
{
    [TestMethod]
    [DataRow("string", "clear", false)]
    [DataRow("builder", "clear", false)]
    [DataRow("string", "add", false)]
    [DataRow("builder", "add", false)]
    [DataRow("string", "description", false)]
    [DataRow("builder", "description", false)]
    [DataRow("string", "schema", false)]
    [DataRow("builder", "schema", false)]
    [DataRow("string", "clear", true)]
    [DataRow("builder", "clear", true)]
    [DataRow("string", "add", true)]
    [DataRow("builder", "add", true)]
    [DataRow("string", "description", true)]
    [DataRow("builder", "description", true)]
    [DataRow("string", "schema", true)]
    [DataRow("builder", "schema", true)]
    public async Task ChangedProfile_PreservesLaterMutableToolChanges(string entry, string mutation, bool profileAssignsTools)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        if (profileAssignsTools) service.DuringInitialProfile = adapter => adapter.AssignTools(adapter.RequestTools.ToList());
        service.BeforeForward = adapter =>
        {
            if (mutation == "clear") adapter.RequestTools.Clear();
            if (mutation == "add") adapter.RequestTools.Add(Function("adapter_tool"));
            if (mutation == "description") adapter.RequestTools[0].Description = "ADAPTER_DESCRIPTION";
            if (mutation == "schema") adapter.RequestTools[0].Parameters.Properties["items"].Items!.Enum![0] = "adapter";
        };
        await Send(service, entry);
        var body = Assert.ContainsSingle(wire.Bodies);
        Assert.AreEqual(123, body["max_tokens"]!.GetValue<int>());
        if (mutation == "clear") Assert.IsNull(body["tools"]);
        if (mutation == "add") Assert.AreEqual(2, body["tools"]!.AsArray().Count);
        if (mutation == "description") Assert.AreEqual("ADAPTER_DESCRIPTION", body["tools"]![0]!["description"]!.GetValue<string>());
        if (mutation == "schema") Assert.AreEqual("adapter", body["tools"]![0]!["input_schema"]!["properties"]!["items"]!["items"]!["enum"]![0]!.GetValue<string>());
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("string", false)]
    [DataRow("builder", false)]
    [DataRow("string", true)]
    [DataRow("builder", true)]
    public async Task RemovedTool_IsNeitherAdvertisedNorExecutedAfterProfileChange(string entry, bool profileAssignsTools)
    {
        using var wire = new Wire { OfferTool = true };
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        var calls = 0;
        service.Functions[0].Handler = _ => { calls++; return Task.FromResult("executed"); };
        if (profileAssignsTools) service.DuringInitialProfile = adapter => adapter.AssignTools(adapter.RequestTools.ToList());
        service.BeforeForward = adapter => adapter.RequestTools.Clear();
        await Send(service, entry);
        Assert.AreEqual(0, calls);
        Assert.IsNull(Assert.ContainsSingle(wire.Bodies)["tools"]);
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("builder")]
    public async Task ChangedProfile_PreservesReplacementHandler(string entry)
    {
        using var wire = new Wire { OfferTool = true };
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        var oldCalls = 0;
        var newCalls = 0;
        service.Functions[0].Handler = _ => { oldCalls++; return Task.FromResult("old"); };
        service.DuringInitialProfile = adapter => adapter.AssignTools(adapter.RequestTools.ToList());
        service.BeforeForward = adapter => adapter.RequestTools[0].Handler = _ => { newCalls++; return Task.FromResult("new"); };
        await Send(service, entry);
        Assert.AreEqual(0, oldCalls);
        Assert.AreEqual(1, newCalls);
        Assert.HasCount(2, wire.Bodies);
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("string", false)]
    [DataRow("builder", false)]
    [DataRow("string", true)]
    [DataRow("builder", true)]
    public async Task InPlaceChangesMadeByOldProfile_AreRemovedWithoutAdapterChanges(string entry, bool beforeBase)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        service.MutateProfileBeforeBase = beforeBase;
        service.DuringInitialProfile = adapter =>
        {
            adapter.RequestTools.Clear();
            adapter.RequestPolicy.MaxRounds = 3;
        };
        await Send(service, entry);
        Assert.AreEqual("baseline_tool", Assert.ContainsSingle(wire.Bodies)["tools"]![0]!["name"]!.GetValue<string>());
        Assert.AreEqual(9, service.ObservedRounds);
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("builder")]
    public async Task ProfileAssignedPolicyAndCyclicCollections_PreserveLaterInPlaceChanges(string entry)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        service.DuringInitialProfile = adapter =>
        {
            adapter.AssignPolicy(new FunctionCallingPolicy { MaxRounds = 4 });
            var list = new List<object>();
            var dictionary = new Dictionary<string, object> { ["self"] = list, ["phase"] = "profile" };
            list.Add(dictionary);
            adapter.AssignNative(list);
        };
        service.BeforeForward = adapter =>
        {
            adapter.RequestPolicy.MaxRounds = 2;
            ((Dictionary<string, object>)adapter.RequestNative[0])["phase"] = "adapter";
        };
        await Send(service, entry);
        Assert.AreEqual(2, service.ObservedRounds);
        Assert.AreEqual("adapter", service.ObservedNativePhase);
        Assert.IsTrue(service.ObservedNativeCycle);
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("builder")]
    public async Task ProfileInPlaceCustomCollectionChange_RestoresDetachedCapturedBaseline(string entry)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        service.DuringInitialProfile = adapter => ((Dictionary<string, object>)adapter.RequestNative[0])["phase"] = "profile";
        await Send(service, entry);
        Assert.AreEqual("baseline", service.ObservedNativePhase);
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("success")]
    [DataRow("failure")]
    [DataRow("cancel")]
    public async Task MutableChanges_DoNotLeakAfterForwardedRequestEnds(string outcome)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        using var cancellation = new CancellationTokenSource();
        var service = CreateAdapter(http);
        service.DuringInitialProfile = adapter => adapter.AssignTools(adapter.RequestTools.ToList());
        service.BeforeForward = adapter => adapter.RequestTools.Clear();
        var request = service.CreateRequest("request").WithProfile(new AIRequestProfile { MaxTokens = 555 });
        wire.Before = token =>
        {
            if (outcome == "failure") throw new HttpRequestException("synthetic failure");
            if (outcome == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
        };
        if (outcome == "failure") await Assert.ThrowsExactlyAsync<HttpRequestException>(() => request.GetCompletionAsync(cancellation.Token));
        else if (outcome == "cancel") await Assert.ThrowsAsync<OperationCanceledException>(() => request.GetCompletionAsync(cancellation.Token));
        else await request.GetCompletionAsync(cancellation.Token);
        Assert.IsNull(Assert.ContainsSingle(wire.Bodies)["tools"]);
        AssertDefaults(service);
        service.BeforeForward = null;
        wire.Before = null;
        await request.GetCompletionAsync();
        Assert.AreEqual("baseline_tool", wire.Bodies.Last()["tools"]![0]!["name"]!.GetValue<string>());
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("builder")]
    public async Task ExplicitLaterAssignmentOfEqualScalar_IsNotReverted(string entry)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        service.BeforeForward = adapter => adapter.AssignMaxTokens(555);
        service.ForwardProfile = new AIRequestProfile();
        await Send(service, entry);
        Assert.AreEqual(555, Assert.ContainsSingle(wire.Bodies)["max_tokens"]!.GetValue<int>());
        AssertDefaults(service);
    }

    [TestMethod]
    public async Task DeepCustomCollectionAndOpaqueObject_DoNotInvokeCustomSerialization()
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        service.DuringInitialProfile = adapter =>
        {
            var root = new List<object>();
            var tail = root;
            for (var i = 0; i < 256; i++) { var next = new List<object>(); tail.Add(next); tail = next; }
            tail.Add(root);
            adapter.AssignNative(root);
        };
        await Send(service, "string");
        Assert.HasCount(1, wire.Bodies);
        AssertDefaults(service);
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("builder")]
    public async Task RebasedCustomSettings_PreserveReferenceKeysAndOpaqueSubtypes(string entry)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = CreateAdapter(http);
        service.DuringInitialProfile = adapter => adapter.AssignOpaqueProfileSettings();
        await Send(service, entry);
        Assert.AreEqual("baseline", service.ObservedKeyedValue);
        Assert.AreEqual("baseline", service.ObservedDerivedValue);
        AssertDefaults(service);
    }

    private static Adapter CreateAdapter(HttpClient http)
    {
        var service = new Adapter(http) { MaxTokens = 3333, DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 9 } };
        service.Functions.Add(Function("baseline_tool"));
        return service;
    }

    private static FunctionDefinition Function(string name) => new()
    {
        Name = name, Description = "original", Handler = _ => Task.FromResult("ok"),
        Parameters = new FunctionParameters
        {
            Properties = new Dictionary<string, ParameterProperty>
            {
                ["items"] = new() { Type = "array", Items = new() { Type = "string", Enum = ["original"] } }
            }
        }
    };

    private static Task<string> Send(Adapter service, string entry)
        => entry == "string" ? service.GetCompletionAsync("request", new AIRequestProfile { MaxTokens = 555 })
            : service.CreateRequest("request").WithProfile(new AIRequestProfile { MaxTokens = 555 }).GetCompletionAsync();

    private static void AssertDefaults(Adapter service)
    {
        Assert.AreEqual(3333u, service.MaxTokens);
        Assert.AreEqual(9, service.DefaultPolicy.MaxRounds);
        var function = Assert.ContainsSingle(service.Functions);
        Assert.AreEqual("original", function.Description);
        Assert.AreEqual("original", function.Parameters.Properties["items"].Items!.Enum![0]);
        Assert.AreEqual(service.Applied, service.Restored);
    }

    private sealed class OpaqueSetting
    {
        public string UnexpectedGetter => throw new InvalidOperationException("Do not serialize provider objects.");
    }

    private sealed class DerivedProperty : ParameterProperty
    {
        public string Extra { get; set; } = "baseline";
    }

    private sealed class Adapter(HttpClient http) : AnthropicService("offline", AIModels.Anthropic.ClaudeSonnet4_6, http)
    {
        private const string NativeSetting = "NativeCollection";
        public Action<Adapter>? BeforeForward;
        public Action<Adapter>? DuringInitialProfile;
        public bool MutateProfileBeforeBase;
        public AIRequestProfile ForwardProfile = new() { MaxTokens = 123 };
        public int Applied;
        public int Restored;
        public int ObservedRounds;
        public string? ObservedNativePhase;
        public bool ObservedNativeCycle;
        public string? ObservedKeyedValue;
        public string? ObservedDerivedValue;
        private readonly List<object> _referenceKey = ["key"];
        public List<FunctionDefinition> RequestTools => RequestFunctions;
        public FunctionCallingPolicy RequestPolicy => RequestSetting(nameof(DefaultPolicy), DefaultPolicy);
        public List<object> RequestNative => RequestSetting(NativeSetting, new List<object>());
        public void AssignTools(List<FunctionDefinition> functions) => SetExecutionSetting(nameof(Functions), functions);
        public void AssignPolicy(FunctionCallingPolicy policy) => SetExecutionSetting(nameof(DefaultPolicy), policy);
        public void AssignNative(List<object> list) => SetExecutionSetting(NativeSetting, list);
        public void AssignMaxTokens(uint maxTokens) => SetExecutionSetting(nameof(MaxTokens), maxTokens);
        public void AssignOpaqueProfileSettings()
        {
            SetExecutionSetting("Keyed", new Dictionary<List<object>, string> { [_referenceKey] = "profile" });
            SetExecutionSetting("Derived", new List<DerivedProperty> { new() { Extra = "profile" } });
        }
        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[NativeSetting] = new List<object> { new Dictionary<string, object> { ["phase"] = "baseline" } };
            settings["OpaqueSetting"] = new OpaqueSetting();
            settings["Keyed"] = new Dictionary<List<object>, string> { [_referenceKey] = "baseline" };
            settings["Derived"] = new List<DerivedProperty> { new() };
        }
        public override async Task<string> GetCompletionAsync(Message message, AIRequestProfile? profile = null,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            BeforeForward?.Invoke(this);
            return await base.GetCompletionAsync(message, ForwardProfile, context, cancellationToken);
        }
        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            Applied++;
            if (MutateProfileBeforeBase && profile.MaxTokens == 555) DuringInitialProfile?.Invoke(this);
            var restore = base.ApplyRequestProfile(profile);
            if (!MutateProfileBeforeBase && profile.MaxTokens == 555) DuringInitialProfile?.Invoke(this);
            return () => { restore(); Restored++; };
        }
        public override Task<string> GetCompletionAsync(Message message)
        {
            ObservedRounds = RequestPolicy.MaxRounds;
            ObservedKeyedValue = RequestSetting("Keyed", new Dictionary<List<object>, string>())[_referenceKey];
            ObservedDerivedValue = RequestSetting("Derived", new List<DerivedProperty>())[0].Extra;
            if (RequestNative.FirstOrDefault() is Dictionary<string, object> dictionary)
            {
                ObservedNativePhase = dictionary["phase"].ToString();
                ObservedNativeCycle = dictionary.TryGetValue("self", out var self) && ReferenceEquals(self, RequestNative);
            }
            return base.GetCompletionAsync(message);
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];
        public bool OfferTool;
        public Action<CancellationToken>? Before;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            Bodies.Add(body);
            Before?.Invoke(token);
            var callTool = OfferTool && Bodies.Count == 1 && body["tools"] is JsonArray { Count: > 0 };
            var response = callTool
                ? "{\"id\":\"r\",\"content\":[{\"type\":\"tool_use\",\"id\":\"call\",\"name\":\"baseline_tool\",\"input\":{}}],\"stop_reason\":\"tool_use\"}"
                : "{\"id\":\"r\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"stop_reason\":\"end_turn\"}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
