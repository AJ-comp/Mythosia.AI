using Mythosia.AI.Models;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.OpenAI;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ChatUiGpt6Tests
{
    [TestMethod]
    [DataRow(nameof(AIModels.OpenAI.Gpt6Sol), AIModels.OpenAI.Gpt6Sol)]
    [DataRow(nameof(AIModels.OpenAI.Gpt6Luna), AIModels.OpenAI.Gpt6Luna)]
    public void ModelCatalogue_ExposesSolAndLunaWithNoneWithoutInventingAliases(string name, string id)
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var model = catalogue.EnumerateArray().Single(group => group.GetProperty("provider").GetString() == "OpenAI")
            .GetProperty("models").EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);
        Assert.AreEqual(id, model.GetProperty("description").GetString());
        Assert.AreEqual(128000, model.GetProperty("maxOutputTokens").GetInt32());
        Assert.IsFalse(model.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(model.GetProperty("sampling").GetProperty("topP").GetBoolean());
        var reasoning = model.GetProperty("reasoning");
        Assert.AreEqual("gpt6", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(new[] { "Auto", "None", "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
        Assert.AreEqual(id, ChatUiModelHelpers.FindModelValueByName($"  {name}  "));
        Assert.AreEqual(id, ChatUiModelHelpers.FindModelValueByName(id));
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public void SolAndLuna_NoneSettingsDisableReasoningAndRefreshSamplingAndSnippet(string model)
    {
        var service = CreateService();
        service.ChangeModel(model);
        service.Gpt6ReasoningEffort = Gpt6Reasoning.High;
        service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(true, "None"));
        Assert.AreEqual(Gpt6Reasoning.None, service.Gpt6ReasoningEffort);
        Assert.IsNull(service.Gpt6ReasoningSummary);
        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));
        Assert.IsFalse(state.GetProperty("alwaysOn").GetBoolean());
        Assert.AreEqual("None", state.GetProperty("effort").GetString());
        var sampling = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetSamplingControls(service.GetCapabilities()));
        Assert.IsTrue(sampling.GetProperty("temperature").GetBoolean());
        Assert.IsTrue(sampling.GetProperty("topP").GetBoolean());
        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "OpenAI", model, "hello");
        StringAssert.Contains(snippet, "service.Gpt6ReasoningEffort = Gpt6Reasoning.None;");
        StringAssert.Contains(snippet, "service.Temperature =");
        StringAssert.Contains(snippet, "service.TopP =");

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(true, "High"));
        Assert.AreEqual(Gpt6Reasoning.High, service.Gpt6ReasoningEffort);
        sampling = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetSamplingControls(service.GetCapabilities()));
        Assert.IsFalse(sampling.GetProperty("temperature").GetBoolean());
        Assert.IsFalse(sampling.GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public void SolAndLuna_DisableUsesNoneAndClearsProAndSummary(string model)
    {
        var service = CreateService();
        service.ChangeModel(model);
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;
        service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;
        service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(false, null));
        Assert.AreEqual(Gpt6Reasoning.None, service.Gpt6ReasoningEffort);
        Assert.AreEqual(Gpt6ReasoningMode.Standard, service.Gpt6ReasoningMode);
        Assert.IsNull(service.Gpt6ReasoningSummary);
    }

    [TestMethod]
    public void ModelCatalogue_ExposesAstraWithSupportedControls()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var astra = catalogue.EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "OpenAI")
            .GetProperty("models").EnumerateArray()
            .Single(model => model.GetProperty("name").GetString() == nameof(AIModels.OpenAI.Gpt6Astra));

        Assert.AreEqual(AIModels.OpenAI.Gpt6Astra, astra.GetProperty("description").GetString());
        Assert.AreEqual(128000, astra.GetProperty("maxOutputTokens").GetInt32());
        Assert.IsFalse(astra.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(astra.GetProperty("sampling").GetProperty("topP").GetBoolean());

        var reasoning = astra.GetProperty("reasoning");
        Assert.AreEqual("gpt6", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(
            new[] { "Auto", "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow(nameof(AIModels.OpenAI.Gpt6Astra))]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    public void FindModelValueByName_ResolvesAstra(string lookup)
    {
        Assert.AreEqual(AIModels.OpenAI.Gpt6Astra, ChatUiModelHelpers.FindModelValueByName($"  {lookup}  "));
    }

    [TestMethod]
    [DataRow("gpt-6")]
    [DataRow("gpt-6-pro")]
    [DataRow("gpt-6-mini")]
    public void FindModelValueByName_DoesNotOfferUnpublishedAliases(string lookup)
    {
        Assert.IsNull(ChatUiModelHelpers.FindModelValueByName(lookup));
    }

    [TestMethod]
    [DataRow(nameof(Gpt6Reasoning.Auto))]
    [DataRow(nameof(Gpt6Reasoning.Low))]
    [DataRow(nameof(Gpt6Reasoning.Medium))]
    [DataRow(nameof(Gpt6Reasoning.High))]
    [DataRow(nameof(Gpt6Reasoning.XHigh))]
    [DataRow(nameof(Gpt6Reasoning.Max))]
    public void ApplyReasoningSettings_AppliesSupportedEffort(string level)
    {
        var service = CreateService();

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(true, level));

        Assert.AreEqual(Enum.Parse<Gpt6Reasoning>(level), service.Gpt6ReasoningEffort);
        Assert.AreEqual(ReasoningSummary.Detailed, service.Gpt6ReasoningSummary);
    }

    [TestMethod]
    [DataRow("None")]
    [DataRow("Minimal")]
    [DataRow("999")]
    public void ApplyReasoningSettings_UnsupportedEffortDoesNotReplaceValidEffort(string level)
    {
        var service = CreateService();
        service.Gpt6ReasoningEffort = Gpt6Reasoning.High;

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(true, level));

        Assert.AreEqual(Gpt6Reasoning.High, service.Gpt6ReasoningEffort);
    }

    [TestMethod]
    public void ApplyReasoningSettings_DisableUsesLowEffortWithoutSummaryOrProMode()
    {
        var service = CreateService();
        service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;
        service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(false, null));

        Assert.AreEqual(Gpt6Reasoning.Low, service.Gpt6ReasoningEffort);
        Assert.IsNull(service.Gpt6ReasoningSummary);
        Assert.AreEqual(Gpt6ReasoningMode.Standard, service.Gpt6ReasoningMode);

        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));
        Assert.IsTrue(state.GetProperty("alwaysOn").GetBoolean());
        Assert.AreEqual("Low", state.GetProperty("effort").GetString());
        Assert.AreEqual(JsonValueKind.Null, state.GetProperty("summary").ValueKind);
        Assert.AreEqual("Standard", state.GetProperty("mode").GetString());
    }

    [TestMethod]
    public void GetReasoningState_ReportsAstraParameters()
    {
        var service = CreateService();
        service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;
        service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;
        service.Gpt6Verbosity = Verbosity.High;

        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));

        Assert.AreEqual("gpt6", state.GetProperty("type").GetString());
        Assert.AreEqual("Max", state.GetProperty("effort").GetString());
        Assert.AreEqual("Detailed", state.GetProperty("summary").GetString());
        Assert.AreEqual("Pro", state.GetProperty("mode").GetString());
        Assert.AreEqual("High", state.GetProperty("verbosity").GetString());
    }

    [TestMethod]
    public void GetReasoningState_DoesNotReportAstraSettingsForOtherModels()
    {
        var service = CreateService();
        service.ChangeModel(AIModels.OpenAI.Gpt5_6);

        Assert.IsNull(ChatUiSettingsHelpers.GetReasoningState(service));
    }

    [TestMethod]
    public void GenerateCodeSnippet_PreservesAstraSettingsAndOmitsUnsupportedSampling()
    {
        var service = CreateService();
        service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;
        service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;
        service.Gpt6Verbosity = Verbosity.High;

        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(
            service, "OpenAI", nameof(AIModels.OpenAI.Gpt6Astra), "Hello!");

        StringAssert.Contains(snippet, "service.ChangeModel(\"gpt-6-astra\");");
        StringAssert.Contains(snippet, "service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;");
        StringAssert.Contains(snippet, "service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;");
        StringAssert.Contains(snippet, "service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;");
        StringAssert.Contains(snippet, "service.Gpt6Verbosity = Verbosity.High;");
        Assert.IsFalse(snippet.Contains("service.Temperature =", StringComparison.Ordinal));
        Assert.IsFalse(snippet.Contains("service.TopP =", StringComparison.Ordinal));
        Assert.IsFalse(snippet.Contains("offline-test-key", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GenerateCodeSnippet_PreservesOmittedSummaryAndDefaultVerbosity()
    {
        var service = CreateService();
        service.Gpt6ReasoningSummary = null;
        service.Gpt6Verbosity = null;

        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(
            service, "OpenAI", AIModels.OpenAI.Gpt6Astra, null);

        StringAssert.Contains(snippet, "service.Gpt6ReasoningSummary = null;");
        StringAssert.Contains(snippet, "service.Gpt6Verbosity = null;");
    }

    private static OpenAIService CreateService()
    {
        var service = new OpenAIService("offline-test-key", new HttpClient());
        service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
        return service;
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
            ReasoningType: "gpt6");
}
