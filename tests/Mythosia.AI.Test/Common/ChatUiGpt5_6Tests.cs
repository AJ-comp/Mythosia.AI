using Mythosia.AI.Models;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.OpenAI;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ChatUiGpt5_6Tests
{
    [TestMethod]
    public void ModelCatalogue_ContainsEveryGpt5_6Model()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var openAiModels = catalogue
            .EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "OpenAI")
            .GetProperty("models")
            .EnumerateArray()
            .ToDictionary(
                model => model.GetProperty("name").GetString()!,
                model => model.GetProperty("description").GetString()!);

        Assert.AreEqual(AIModels.OpenAI.Gpt5_6, openAiModels[nameof(AIModels.OpenAI.Gpt5_6)]);
        Assert.AreEqual(AIModels.OpenAI.Gpt5_6Sol, openAiModels[nameof(AIModels.OpenAI.Gpt5_6Sol)]);
        Assert.AreEqual(AIModels.OpenAI.Gpt5_6Terra, openAiModels[nameof(AIModels.OpenAI.Gpt5_6Terra)]);
        Assert.AreEqual(AIModels.OpenAI.Gpt5_6Luna, openAiModels[nameof(AIModels.OpenAI.Gpt5_6Luna)]);
    }

    [TestMethod]
    [DataRow(nameof(AIModels.OpenAI.Gpt5_6), AIModels.OpenAI.Gpt5_6)]
    [DataRow(nameof(AIModels.OpenAI.Gpt5_6Sol), AIModels.OpenAI.Gpt5_6Sol)]
    [DataRow(nameof(AIModels.OpenAI.Gpt5_6Terra), AIModels.OpenAI.Gpt5_6Terra)]
    [DataRow(nameof(AIModels.OpenAI.Gpt5_6Luna), AIModels.OpenAI.Gpt5_6Luna)]
    [DataRow(AIModels.OpenAI.Gpt5_6, AIModels.OpenAI.Gpt5_6)]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol, AIModels.OpenAI.Gpt5_6Sol)]
    [DataRow(AIModels.OpenAI.Gpt5_6Terra, AIModels.OpenAI.Gpt5_6Terra)]
    [DataRow(AIModels.OpenAI.Gpt5_6Luna, AIModels.OpenAI.Gpt5_6Luna)]
    public void FindModelValueByName_ResolvesGpt5_6NamesAndIds(string lookup, string expected)
    {
        Assert.AreEqual(expected, ChatUiModelHelpers.FindModelValueByName($"  {lookup}  "));
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5_6)]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol)]
    [DataRow(AIModels.OpenAI.Gpt5_6Terra)]
    [DataRow(AIModels.OpenAI.Gpt5_6Luna)]
    public void GetReasoningLevels_ReturnsEveryGpt5_6Effort(string model)
    {
        var reasoning = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetReasoningLevels(model));

        Assert.AreEqual("gpt5_6", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(
            new[] { "Auto", "None", "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow(nameof(Gpt5_6Reasoning.Auto))]
    [DataRow(nameof(Gpt5_6Reasoning.None))]
    [DataRow(nameof(Gpt5_6Reasoning.Low))]
    [DataRow(nameof(Gpt5_6Reasoning.Medium))]
    [DataRow(nameof(Gpt5_6Reasoning.High))]
    [DataRow(nameof(Gpt5_6Reasoning.XHigh))]
    [DataRow(nameof(Gpt5_6Reasoning.Max))]
    public void ApplyReasoningSettings_MapsEveryGpt5_6Effort(string level)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient());
        var request = CreateSettingsRequest(reasoningEnabled: true, reasoningLevel: level);

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, request);

        Assert.AreEqual(Enum.Parse<Gpt5_6Reasoning>(level), service.Gpt5_6ReasoningEffort);
        Assert.AreEqual(ReasoningSummary.Detailed, service.Gpt5_6ReasoningSummary);
    }

    [TestMethod]
    public void ApplyReasoningSettings_DisablesAndResetsGpt5_6Settings()
    {
        var service = new OpenAIService("offline-test-key", new HttpClient())
        {
            Gpt5_6ReasoningEffort = Gpt5_6Reasoning.Max,
            Gpt5_6ReasoningSummary = ReasoningSummary.Detailed,
            Gpt5_6ReasoningMode = Gpt5_6ReasoningMode.Pro
        };

        ChatUiSettingsHelpers.ApplyReasoningSettings(
            service,
            CreateSettingsRequest(reasoningEnabled: false, reasoningLevel: null));

        Assert.AreEqual(Gpt5_6Reasoning.None, service.Gpt5_6ReasoningEffort);
        Assert.IsNull(service.Gpt5_6ReasoningSummary);
        Assert.AreEqual(Gpt5_6ReasoningMode.Standard, service.Gpt5_6ReasoningMode);
    }

    private static SettingsRequest CreateSettingsRequest(bool reasoningEnabled, string? reasoningLevel)
        => new(
            Temperature: null,
            TopP: null,
            MaxTokens: null,
            FrequencyPenalty: null,
            PresencePenalty: null,
            StatelessMode: null,
            SystemMessage: null,
            ReasoningEnabled: reasoningEnabled,
            ReasoningLevel: reasoningLevel,
            ReasoningType: "gpt5_6");
}
