using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
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
    [DataRow(nameof(AIModels.OpenAI.Gpt6Astra), AIModels.OpenAI.Gpt6Astra)]
    [DataRow(nameof(AIModels.OpenAI.Gpt6_1Sol), AIModels.OpenAI.Gpt6_1Sol)]
    public void ModelCatalogue_ExposesMandatoryReasoningModelsWithSupportedControls(string name, string id)
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var model = catalogue.EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "OpenAI")
            .GetProperty("models").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);

        Assert.AreEqual(id, model.GetProperty("description").GetString());
        Assert.AreEqual(128000, model.GetProperty("maxOutputTokens").GetInt32());
        Assert.IsFalse(model.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(model.GetProperty("sampling").GetProperty("topP").GetBoolean());
        Assert.AreEqual("Supported", model.GetProperty("speed").GetProperty("fast").GetString());
        foreach (var feature in new[] { "streaming", "functionCalling", "asyncFunctionCalling", "steering", "reasoning", "imageInput" })
            Assert.AreEqual("Supported", model.GetProperty("capabilities").GetProperty(feature).GetString(), feature);

        var reasoning = model.GetProperty("reasoning");
        Assert.AreEqual("gpt6", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(
            new[] { "Auto", "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow(nameof(AIModels.OpenAI.Gpt6Astra), AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, AIModels.OpenAI.Gpt6Astra)]
    [DataRow(nameof(AIModels.OpenAI.Gpt6_1Sol), AIModels.OpenAI.Gpt6_1Sol)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, AIModels.OpenAI.Gpt6_1Sol)]
    public void FindModelValueByName_ResolvesMandatoryReasoningModels(string lookup, string expected)
    {
        Assert.AreEqual(expected, ChatUiModelHelpers.FindModelValueByName($"  {lookup}  "));
    }

    [TestMethod]
    [DataRow("gpt-6")]
    [DataRow("gpt-6-pro")]
    [DataRow("gpt-6-mini")]
    [DataRow("gpt-6.1")]
    [DataRow("gpt-6.1-pro")]
    [DataRow("gpt-6.1-astra")]
    [DataRow("gpt-6.1-luna")]
    public void FindModelValueByName_DoesNotOfferUnpublishedAliases(string lookup)
    {
        Assert.IsNull(ChatUiModelHelpers.FindModelValueByName(lookup));
    }

    public static IEnumerable<object[]> MandatoryModelEfforts =>
        from model in new[] { AIModels.OpenAI.Gpt6Astra, AIModels.OpenAI.Gpt6_1Sol }
        from effort in Enum.GetValues<Gpt6Reasoning>().Where(effort => effort != Gpt6Reasoning.None)
        select new object[] { model, effort.ToString() };

    [TestMethod]
    [DynamicData(nameof(MandatoryModelEfforts))]
    public void ApplyReasoningSettings_AppliesSupportedEffort(string model, string level)
    {
        var service = CreateService(model);

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(true, level));

        Assert.AreEqual(Enum.Parse<Gpt6Reasoning>(level), service.Gpt6ReasoningEffort);
        Assert.AreEqual(ReasoningSummary.Detailed, service.Gpt6ReasoningSummary);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra, "None")]
    [DataRow(AIModels.OpenAI.Gpt6Astra, "Minimal")]
    [DataRow(AIModels.OpenAI.Gpt6Astra, "999")]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, "None")]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, "Minimal")]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol, "999")]
    public void ApplyReasoningSettings_UnsupportedEffortDoesNotReplaceValidEffort(string model, string level)
    {
        var service = CreateService(model);
        service.Gpt6ReasoningEffort = Gpt6Reasoning.High;

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(true, level));

        Assert.AreEqual(Gpt6Reasoning.High, service.Gpt6ReasoningEffort);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol)]
    public void ApplyReasoningSettings_DisableUsesLowEffortWithoutSummaryOrProMode(string model)
    {
        var service = CreateService(model);
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
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol)]
    public void GetReasoningState_ReportsMandatoryReasoningParameters(string model)
    {
        var service = CreateService(model);
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
    [DataRow(nameof(AIModels.OpenAI.Gpt6Astra), AIModels.OpenAI.Gpt6Astra)]
    [DataRow(nameof(AIModels.OpenAI.Gpt6_1Sol), AIModels.OpenAI.Gpt6_1Sol)]
    public void GenerateCodeSnippet_PreservesMandatoryReasoningSettingsAndOmitsUnsupportedSampling(string name, string model)
    {
        var service = CreateService(model);
        service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;
        service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;
        service.Gpt6Verbosity = Verbosity.High;

        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(
            service, "OpenAI", name, "Hello!");

        StringAssert.Contains(snippet, $"service.ChangeModel(\"{model}\");");
        StringAssert.Contains(snippet, "service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;");
        StringAssert.Contains(snippet, "service.Gpt6ReasoningSummary = ReasoningSummary.Detailed;");
        StringAssert.Contains(snippet, "service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;");
        StringAssert.Contains(snippet, "service.Gpt6Verbosity = Verbosity.High;");
        Assert.IsFalse(snippet.Contains("service.Temperature =", StringComparison.Ordinal));
        Assert.IsFalse(snippet.Contains("service.TopP =", StringComparison.Ordinal));
        Assert.IsFalse(snippet.Contains("offline-test-key", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6_1Sol)]
    public void GenerateCodeSnippet_PreservesOmittedSummaryAndDefaultVerbosity(string model)
    {
        var service = CreateService(model);
        service.Gpt6ReasoningSummary = null;
        service.Gpt6Verbosity = null;

        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(
            service, "OpenAI", model, null);

        StringAssert.Contains(snippet, "service.Gpt6ReasoningSummary = null;");
        StringAssert.Contains(snippet, "service.Gpt6Verbosity = null;");
    }

    [TestMethod]
    [DataRow(true, "Auto", "medium")]
    [DataRow(true, "Max", "max")]
    [DataRow(true, "None", "medium")]
    [DataRow(true, "Minimal", "medium")]
    [DataRow(false, null, "low")]
    public async Task Gpt6_1Sol_ChatSettingsReachResponsesWithMandatoryReasoningAndTokenLimit(
        bool enabled, string? level, string expectedEffort)
    {
        using var handler = new CompletionCaptureHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-test-key", client);
        service.ChangeModel(ChatUiModelHelpers.FindModelValueByName(nameof(AIModels.OpenAI.Gpt6_1Sol))!);
        service.MaxTokens = 200000;
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, CreateSettingsRequest(enabled, level));

        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(service));
        Assert.AreEqual(128000, controls.GetProperty("maxOutputTokens").GetInt32());
        Assert.IsFalse(controls.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(controls.GetProperty("sampling").GetProperty("topP").GetBoolean());

        var request = ChatUiSettingsHelpers.CreateChatRequest(
            service, new Message(ActorRole.User, "hello"), InferenceSpeed.Fast);
        Assert.AreEqual("ok", await request.GetCompletionAsync());

        Assert.AreEqual("/v1/responses", handler.Path);
        var body = handler.Body;
        Assert.AreEqual(AIModels.OpenAI.Gpt6_1Sol, body.GetProperty("model").GetString());
        Assert.AreEqual(expectedEffort, body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.AreEqual(enabled, body.GetProperty("reasoning").TryGetProperty("summary", out _));
        Assert.AreEqual(128000, body.GetProperty("max_output_tokens").GetInt32());
        Assert.AreEqual("fast", body.GetProperty("service_tier").GetString());
        Assert.IsFalse(body.TryGetProperty("temperature", out _));
        Assert.IsFalse(body.TryGetProperty("top_p", out _));
    }

    private sealed class CompletionCaptureHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public JsonElement Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"resp_ui","status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}]}
                    """, Encoding.UTF8, "application/json")
            };
        }
    }

    private static OpenAIService CreateService(string model = AIModels.OpenAI.Gpt6Astra)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient());
        service.ChangeModel(model);
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
