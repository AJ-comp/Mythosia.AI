using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class GoogleLatestFlashUiTests
{
    [TestMethod]
    [DataRow(nameof(AIModels.Google.Gemini3_7Flash), AIModels.Google.Gemini3_7Flash)]
    [DataRow(nameof(AIModels.Google.Gemini3_8Flash), AIModels.Google.Gemini3_8Flash)]
    public void Catalogue_OffersNewModelsWithTheirSupportedControls(string name, string model)
    {
        Assert.AreEqual(model, ChatUiModelHelpers.FindModelValueByName(name));
        Assert.AreEqual(model, ChatUiModelHelpers.FindModelValueByName(model));
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var group = catalogue.EnumerateArray().Single(g => g.GetProperty("provider").GetString() == "Google");
        var entry = group.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("name").GetString() == name);
        Assert.AreEqual(model, entry.GetProperty("description").GetString());
        Assert.AreEqual(65536u, entry.GetProperty("maxOutputTokens").GetUInt32());
        var reasoning = entry.GetProperty("reasoning");
        Assert.AreEqual("gemini3", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(new[] { "Auto", "Low", "Medium", "High" },
            reasoning.GetProperty("levels").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.IsFalse(entry.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(entry.GetProperty("sampling").GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, true, "Low", "LOW")]
    [DataRow(AIModels.Google.Gemini3_7Flash, true, "Medium", "MEDIUM")]
    [DataRow(AIModels.Google.Gemini3_7Flash, true, "High", "HIGH")]
    [DataRow(AIModels.Google.Gemini3_7Flash, false, "High", "LOW")]
    [DataRow(AIModels.Google.Gemini3_8Flash, true, "Low", "LOW")]
    [DataRow(AIModels.Google.Gemini3_8Flash, true, "Medium", "MEDIUM")]
    [DataRow(AIModels.Google.Gemini3_8Flash, true, "High", "HIGH")]
    [DataRow(AIModels.Google.Gemini3_8Flash, false, "High", "LOW")]
    public async Task Settings_ReachProviderRequestAndDisplayedState(
        string model, bool enabled, string level, string expectedWireLevel)
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new GoogleAIService("offline-key", model, client)
        {
            Temperature = 0.2f,
            TopP = 0.4f,
            ThinkingLevel = GeminiThinkingLevel.High
        };
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, new SettingsRequest(
            Temperature: null, TopP: null, MaxTokens: null,
            FrequencyPenalty: null, PresencePenalty: null,
            StatelessMode: null, SystemMessage: null,
            ReasoningEnabled: enabled, ReasoningLevel: level, ReasoningType: "gemini3"));

        Assert.AreEqual("ok", await service.GetCompletionAsync("hello"));
        var request = JsonSerializer.Deserialize<JsonElement>(handler.Body!);
        var generation = request.GetProperty("generationConfig");
        Assert.AreEqual(expectedWireLevel,
            generation.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.IsFalse(generation.TryGetProperty("temperature", out _));
        Assert.IsFalse(generation.TryGetProperty("topP", out _));
        Assert.IsFalse(generation.TryGetProperty("topK", out _));

        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));
        Assert.AreEqual("gemini3", state.GetProperty("type").GetString());
        Assert.IsTrue(state.GetProperty("alwaysOn").GetBoolean());
        Assert.AreEqual(service.ThinkingLevel.ToString(), state.GetProperty("effort").GetString());
        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "Google", model, "hello");
        StringAssert.Contains(snippet, $"service.ChangeModel(\"{model}\");");
        StringAssert.Contains(snippet, "using Mythosia.AI.Models.Enums;");
        StringAssert.Contains(snippet, $"service.ThinkingLevel = GeminiThinkingLevel.{service.ThinkingLevel};");
        Assert.IsFalse(snippet.Contains("service.Temperature =", StringComparison.Ordinal));
        Assert.IsFalse(snippet.Contains("service.TopP =", StringComparison.Ordinal));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"ok\"}]},\"finishReason\":\"STOP\"}]}",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
