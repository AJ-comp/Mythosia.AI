using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class PerplexityRequestPlanTests
{
    public enum Path { String, Message, Builder, Stream, Run }
    public enum Selection { Model, Override, Preset, Profile, Models, ModelsAndOverride }

    public static IEnumerable<object[]> AuxiliaryCases()
    {
        foreach (var path in Enum.GetValues<Path>())
        foreach (var selection in Enum.GetValues<Selection>())
        foreach (var purpose in new[] { AIRequestPurpose.QueryRewrite, AIRequestPurpose.Summarization })
            yield return [path, selection, purpose];
    }

    [TestMethod]
    [DynamicData(nameof(AuxiliaryCases))]
    public async Task AuxiliaryRouting_UsesItsOwnEffectivePlan_AndPreservesParentSettings(
        Path path, Selection selection, AIRequestPurpose purpose)
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new RoutedService(http);
        ConfigureSelection(service, selection);
        service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        service.AgentOptions.Tools.Add(new PerplexityHostedTool { Type = "sandbox" });
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "parent history"));
        var original = service.ActivateChat.Messages.ToArray();
        var options = JsonSerializer.Serialize(service.AgentOptions);
        var profile = purpose == AIRequestPurpose.QueryRewrite ? RequestProfiles.QueryRewrite : RequestProfiles.Summarization;

        Assert.AreEqual("ok", await Send(service, path, profile));

        var helper = transport.Requests.Single();
        Assert.AreEqual(AIModels.Perplexity.Sonar, helper["model"]!.GetValue<string>());
        Assert.IsFalse(helper.ContainsKey("reasoning"));
        Assert.HasCount(0, helper["tools"]!.AsArray());
        foreach (var field in new[] { "preset", "profile", "models" }) Assert.IsFalse(helper.ContainsKey(field), field);
        CollectionAssert.AreEqual(original, service.ActivateChat.Messages.ToArray());
        Assert.AreEqual(options, JsonSerializer.Serialize(service.AgentOptions));
        Assert.AreEqual("openai/gpt-5", service.Model);

        await service.GetCompletionAsync("parent continuation");
        var parent = transport.Requests.Last();
        Assert.AreEqual("high", parent["reasoning"]!["effort"]!.GetValue<string>());
        Assert.IsTrue(parent["tools"]!.AsArray().Any(tool => tool!["type"]!.GetValue<string>() == "sandbox"));
        Assert.AreEqual(2, transport.Requests.Count);
    }

    [TestMethod]
    public async Task AutomaticSummary_RestoresTheValidatedParentPlanAfterTheHelper()
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new RoutedService(http);
        service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        service.AgentOptions.Tools.Add(new PerplexityHostedTool { Type = "sandbox" });
        for (var i = 0; i < 6; i++)
            service.ActivateChat.Messages.Add(new Message(i % 2 == 0 ? ActorRole.User : ActorRole.Assistant, "old-" + i));
        service.ConversationPolicy = new SummaryConversationPolicy { TriggerCount = 2, KeepRecentCount = 2 };

        await service.GetCompletionAsync("current parent input");

        Assert.HasCount(2, transport.Requests);
        var helper = transport.Requests[0];
        Assert.AreEqual(AIModels.Perplexity.Sonar, helper["model"]!.GetValue<string>());
        Assert.IsNull(helper["reasoning"]);
        Assert.HasCount(0, helper["tools"]!.AsArray());
        var parent = transport.Requests[1];
        Assert.AreEqual("openai/gpt-5", parent["model"]!.GetValue<string>());
        Assert.AreEqual("high", parent["reasoning"]!["effort"]!.GetValue<string>());
        Assert.IsTrue(parent["tools"]!.AsArray().Any(tool => tool!["type"]!.GetValue<string>() == "sandbox"));
        Assert.AreEqual("ok", service.ConversationPolicy.CurrentSummary);
    }

    public static IEnumerable<object[]> DefaultProfileCases()
    {
        foreach (var path in new[] { Path.String, Path.Builder, Path.Run })
        foreach (var selection in Enum.GetValues<Selection>())
            yield return [path, selection];
    }

    [TestMethod]
    [DynamicData(nameof(DefaultProfileCases))]
    public async Task DefaultProfile_DisableReasoningUsesTheSelectedModel_WithoutInventingPresetOrFallbackModels(
        Path path, Selection selection)
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new PerplexityService("offline", "openai/gpt-5", http);
        ConfigureSelection(service, selection);
        service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        service.WithReasoning(ReasoningLevel.Max);

        await Send(service, path, new AIRequestProfile { DisableReasoning = true });

        var body = transport.Requests.Single();
        var expected = selection == Selection.Model ? "minimal" : selection == Selection.Override ? "low" : null;
        Assert.AreEqual(expected, body["reasoning"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(ReasoningLevel.High, service.AgentOptions.ReasoningEffort);
        if (selection is Selection.Models or Selection.ModelsAndOverride)
        {
            Assert.IsNull(body["model"]);
            Assert.AreEqual("google/gemini-3.8-flash", body["models"]![0]!.GetValue<string>());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitSonar_WithDisabledReasoningAcceptsSuppressedHighEffort(bool common)
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new PerplexityService("offline", http);
        if (common) service.WithReasoning(ReasoningLevel.High);
        else service.AgentOptions.ReasoningEffort = ReasoningLevel.High;

        await service.CreateRequest("input").WithProfile(new AIRequestProfile { DisableReasoning = true }).GetCompletionAsync();

        Assert.IsNull(transport.Requests.Single()["reasoning"]);
        Assert.AreEqual(AIModels.Perplexity.Sonar, transport.Requests.Single()["model"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FallbackModels_OverrideTheSingleModelForReasoningAndCapabilities(bool background)
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new PerplexityService("offline", http);
        service.AgentOptions.ModelOverride = AIModels.Perplexity.Sonar;
        service.AgentOptions.Models = ["openai/gpt-5", "google/gemini-3.8-flash"];
        service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        Assert.IsNull(service.GetCapabilities().Model);
        Assert.AreEqual(CapabilitySupport.Unknown, service.CreateRequest("capabilities").GetCapabilities().Reasoning);

        if (background) await service.StartBackgroundAsync("input");
        else await service.GetCompletionAsync("input");

        var body = transport.Requests.Single();
        Assert.IsNull(body["model"]);
        Assert.AreEqual("high", body["reasoning"]!["effort"]!.GetValue<string>());
        Assert.AreEqual("openai/gpt-5", body["models"]![0]!.GetValue<string>());
        if (background) Assert.IsTrue(body["background"]!.GetValue<bool>());
    }

    public static IEnumerable<object[]> InvalidNativeCases()
    {
        foreach (var path in new[] { Path.String, Path.Builder, Path.Run })
        foreach (var invalid in new[] { "enum", "none", "preset", "legacy-model" })
            yield return [path, invalid];
    }

    [TestMethod]
    [DynamicData(nameof(InvalidNativeCases))]
    public async Task AuxiliarySuppression_DoesNotMakeMalformedNativeConfigurationValid(Path path, string invalid)
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new RoutedService(http);
        if (invalid == "enum") service.AgentOptions.ReasoningEffort = (ReasoningLevel)999;
        else if (invalid == "none") service.AgentOptions.ReasoningEffort = ReasoningLevel.None;
        else if (invalid == "preset") service.AgentOptions.Preset = (PerplexityPreset)999;
        else service.AgentOptions.ModelOverride = "sonar";
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "keep"));

        if (invalid is "enum" or "preset")
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => Send(service, path, RequestProfiles.QueryRewrite));
        else
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => Send(service, path, RequestProfiles.QueryRewrite));

        Assert.IsEmpty(transport.Requests);
        Assert.HasCount(1, service.ActivateChat.Messages);
        Assert.AreEqual("keep", service.ActivateChat.Messages[0].Content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BackgroundAndCompletion_UseValidatedSnapshotAcrossDynamicContextChanges(bool background)
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new PerplexityService("offline", "openai/gpt-5", http);
        service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        service.AgentOptions.Store = true;
        service.AgentOptions.Tools.Add(new PerplexityHostedTool { Type = "sandbox" });
        service.WithReasoning(ReasoningLevel.Low);
        service.WithSystemMessageProvider(() =>
        {
            service.ChangeModel(AIModels.Perplexity.Sonar);
            service.AgentOptions.ReasoningEffort = ReasoningLevel.None;
            service.AgentOptions.Store = false;
            service.AgentOptions.Tools.Clear();
            return new AIRequestContext { SystemMessageSuffix = "captured context" };
        });

        if (background) await service.StartBackgroundAsync("input");
        else await service.GetCompletionAsync("input");

        var body = transport.Requests.Single();
        Assert.AreEqual("openai/gpt-5", body["model"]!.GetValue<string>());
        Assert.AreEqual("low", body["reasoning"]!["effort"]!.GetValue<string>());
        Assert.IsTrue(body["store"]!.GetValue<bool>());
        Assert.IsTrue(body["tools"]!.AsArray().Any(tool => tool!["type"]!.GetValue<string>() == "sandbox"));
        StringAssert.Contains(body["instructions"]!.GetValue<string>(), "captured context");
        if (background) Assert.IsEmpty(service.ActivateChat.Messages);
    }

    [TestMethod]
    public async Task CapabilityInspection_UsesProfileSelectionWithoutConsumingOrPreparingARequest()
    {
        using var transport = new Transport();
        using var http = new HttpClient(transport);
        var service = new RoutedService(http);
        service.AgentOptions.ModelOverride = "openai/gpt-5";
        service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        var builder = service.CreateRequest("input").WithProfile(RequestProfiles.QueryRewrite);

        Assert.AreEqual(AIModels.Perplexity.Sonar, builder.GetCapabilities().Model);
        Assert.AreEqual(CapabilitySupport.Unsupported, builder.GetCapabilities().Reasoning);
        Assert.AreEqual(0, service.ExecutionProfiles);
        Assert.IsEmpty(transport.Requests);
        Assert.IsEmpty(service.ActivateChat.Messages);

        await builder.GetCompletionAsync();
        Assert.AreEqual(1, service.ExecutionProfiles);
        Assert.AreEqual(AIModels.Perplexity.Sonar, transport.Requests.Single()["model"]!.GetValue<string>());
        Assert.IsNull(transport.Requests.Single()["reasoning"]);
    }

    private static void ConfigureSelection(PerplexityService service, Selection selection)
    {
        if (selection == Selection.Override) service.AgentOptions.ModelOverride = "google/gemini-3.8-flash";
        else if (selection == Selection.Preset) service.AgentOptions.Preset = PerplexityPreset.High;
        else if (selection == Selection.Profile) service.AgentOptions.Profile = new PerplexityProfile { Id = "profile-fixture" };
        else if (selection is Selection.Models or Selection.ModelsAndOverride)
        {
            service.AgentOptions.Models = ["google/gemini-3.8-flash", "openai/gpt-5"];
            if (selection == Selection.ModelsAndOverride) service.AgentOptions.ModelOverride = AIModels.Perplexity.Sonar;
        }
    }

    private static async Task<string> Send(PerplexityService service, Path path, AIRequestProfile profile)
    {
        if (path == Path.String) return await service.GetCompletionAsync("input", profile);
        if (path == Path.Message) return await service.GetCompletionAsync(new Message(ActorRole.User, "input"), profile);
        var request = service.CreateRequest("input").WithProfile(profile);
        if (path == Path.Builder) return await request.GetCompletionAsync();
        if (path == Path.Run)
        {
            await using var run = await request.StartRunAsync();
            return (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text;
        }
        var result = new StringBuilder();
        await foreach (var chunk in request.StreamAsync()) result.Append(chunk);
        return result.ToString();
    }

    private sealed class RoutedService(HttpClient http) : PerplexityService("offline", "openai/gpt-5", http)
    {
        public int ExecutionProfiles { get; private set; }
        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ExecutionProfiles++;
            var restore = base.ApplyProviderSpecificRequestProfile(profile);
            if (profile.Purpose != AIRequestPurpose.Default) SetExecutionSetting(nameof(Model), AIModels.Perplexity.Sonar);
            return restore;
        }

        protected override void ApplyCapabilityRequestProfile(AIRequestProfile profile)
        {
            base.ApplyCapabilityRequestProfile(profile);
            if (profile.Purpose != AIRequestPurpose.Default) SetExecutionSetting(nameof(Model), AIModels.Perplexity.Sonar);
        }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add(body);
            const string response = """{"id":"fixture","status":"completed","model":"perplexity/sonar","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}}""";
            var streaming = body["stream"]?.GetValue<bool>() == true && body["background"]?.GetValue<bool>() != true;
            var text = streaming
                ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"ok\"}\n\ndata: {\"type\":\"response.completed\",\"response\":" + response + "}\n\n"
                : response;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(text, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
