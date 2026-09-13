using Mythosia.AI.Models;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Perplexity;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class PerplexityAgentUiTests
{
    [TestMethod]
    public void Catalogue_DistinguishesAgentModelsFromDirectProvidersAndRemovesLegacySonar()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var group = catalogue.EnumerateArray().Single(item => item.GetProperty("provider").GetString() == "Perplexity");
        var models = group.GetProperty("models").EnumerateArray().ToArray();
        Assert.IsTrue(models.All(item => item.GetProperty("description").GetString()!.Contains('/')));
        foreach (var entry in models)
        {
            var name = entry.GetProperty("name").GetString()!;
            var model = ChatUiModelHelpers.FindModelValueByName(name);
            Assert.IsNotNull(model);
            Assert.AreEqual(entry.GetProperty("description").GetString(), model);
            Assert.AreEqual("Perplexity", ChatUiModelHelpers.GetProviderForModel(model));
            // Gateway effort support is not established per selected model. Keep advanced
            // Agent controls separate and avoid advertising a union of every model's levels.
            Assert.AreEqual(JsonValueKind.Null, entry.GetProperty("reasoning").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, entry.GetProperty("maxOutputTokens").ValueKind);
        }
        Assert.AreEqual("openai/gpt-5.6-luna", ChatUiModelHelpers.FindModelValueByName("PerplexityGpt5_6Luna"));
        Assert.AreEqual("gpt-5.6-luna", ChatUiModelHelpers.FindModelValueByName("Gpt5_6Luna"));
    }

    [TestMethod]
    [DataRow("Model", "Auto", false)]
    [DataRow("Fast", "Auto", true)]
    [DataRow("WideResearch", "High", true)]
    public async Task Settings_PreservePresetEffortStepsAndCopiedCode(string preset, string effort, bool search)
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        var service = new PerplexityService("offline-key", client);
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, Request(preset, effort, 8, search));
        Assert.AreEqual("ok", await service.GetCompletionAsync("hello"));
        var body = handler.Body;
        if (preset == "Model")
        {
            Assert.IsFalse(body.TryGetProperty("preset", out _));
            Assert.AreEqual("perplexity/sonar", body.GetProperty("model").GetString());
            Assert.AreEqual(0, body.GetProperty("tools").GetArrayLength());
        }
        else
        {
            Assert.AreEqual(preset == "WideResearch" ? "wide-research" : "fast", body.GetProperty("preset").GetString());
            Assert.IsFalse(body.TryGetProperty("model", out _));
        }
        Assert.AreEqual(8, body.GetProperty("max_steps").GetInt32());
        if (effort == "High") Assert.AreEqual("high", body.GetProperty("reasoning").GetProperty("effort").GetString());
        else Assert.IsFalse(body.TryGetProperty("reasoning", out _));
        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));
        Assert.AreEqual(preset, state.GetProperty("preset").GetString());
        Assert.AreEqual(effort, state.GetProperty("effort").GetString());
        Assert.AreEqual(search, state.GetProperty("webSearch").GetBoolean());
        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "Perplexity", "Sonar", "hello");
        StringAssert.Contains(snippet, "service.AgentOptions.MaxSteps = 8;");
        if (preset != "Model") StringAssert.Contains(snippet, $"service.UsePreset(PerplexityPreset.{preset});");
        if (!search) StringAssert.Contains(snippet, "service.AgentOptions.DisableWebSearch = true;");
    }

    [TestMethod]
    public void HiddenUnknownReasoningControlsPreserveNativeEffortWhenOtherSettingsChange()
    {
        using var client = new HttpClient(new Handler());
        var service = new PerplexityService("offline-key", AIModels.Perplexity.Gpt5_6Luna, client);
        service.AgentOptions.ReasoningEffort = ReasoningLevel.High;
        var request = JsonSerializer.Deserialize<SettingsRequest>(
            "{\"reasoningEnabled\":null,\"perplexityMaxSteps\":9,\"perplexityWebSearch\":false}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, request);
        Assert.AreEqual(ReasoningLevel.High, service.AgentOptions.ReasoningEffort);
        Assert.AreEqual(9, service.AgentOptions.MaxSteps);
        Assert.IsTrue(service.AgentOptions.DisableWebSearch);
    }

    [TestMethod]
    [DataRow("Model", "High", 8)]
    [DataRow("Unknown", "Auto", 8)]
    [DataRow("Fast", "None", 8)]
    [DataRow("Fast", "Auto", 101)]
    public void InvalidSettings_DoNotPartiallyMutateAgentOptions(string preset, string effort, int steps)
    {
        using var client = new HttpClient(new Handler());
        var service = new PerplexityService("offline-key", client);
        Assert.Throws<ArgumentException>(() => ChatUiSettingsHelpers.ApplyReasoningSettings(service, Request(preset, effort, steps, false)));
        Assert.IsNull(service.AgentOptions.Preset);
        Assert.AreEqual(0, service.AgentOptions.MaxSteps);
        Assert.AreEqual(ReasoningLevel.Auto, service.AgentOptions.ReasoningEffort);
        Assert.IsFalse(service.AgentOptions.DisableWebSearch);
    }

    private static SettingsRequest Request(string preset, string effort, int steps, bool search)
        => new(null, null, null, null, null, null, null, true, effort, "perplexity", preset, steps, search);

    private sealed class Handler : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"id\":\"resp_ui\",\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"ok\"}]}]}", Encoding.UTF8, "application/json") };
        }
    }
}
