using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.xAI;

[TestClass]
[TestCategory("Unit")]
public class XAICurrentContractTests
{
    [TestMethod]
    public void CurrentModelsAndDefault_UseOfficialGrok45Identifiers()
    {
        Assert.AreEqual("grok-4.5", ReadModelId(nameof(AIModels.xAI.Grok4_5)));
        Assert.AreEqual("grok-4.5-latest", ReadModelId(nameof(AIModels.xAI.Grok4_5Latest)));
        Assert.AreEqual("grok-build-latest", ReadModelId(nameof(AIModels.xAI.GrokBuildLatest)));
        Assert.AreEqual("grok-4.3-latest", ReadModelId(nameof(AIModels.xAI.Grok4_3Latest)));
        Assert.AreEqual("grok-latest", ReadModelId(nameof(AIModels.xAI.GrokLatest)));

        var service = new XAIService("offline-test-key", new HttpClient());
        Assert.AreEqual(AIModels.xAI.Grok4_5, service.Model);
        Assert.AreEqual(GrokReasoning.Auto, service.ReasoningEffort);

        service.UseGrok4FastModel();
        Assert.AreEqual(AIModels.xAI.Grok4_3, service.Model);
        service.UseGrok4Model();
        Assert.AreEqual(AIModels.xAI.Grok4_5, service.Model);
    }

    [TestMethod]
    public void UnavailableGrok3Mini_IsNotPubliclyExposed()
    {
        Assert.IsNull(typeof(AIModels.xAI).GetField("Grok3Mini"));
        Assert.IsNull(typeof(XAIService).GetMethod("UseMiniModel"));

        var catalogue = JsonSerializer.Serialize(
            ChatUiModelHelpers.BuildModelCatalogue());
        Assert.IsFalse(catalogue.Contains("Grok3Mini", StringComparison.Ordinal));
        Assert.IsFalse(catalogue.Contains("grok-3-mini", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ChatUi_ExposesModelSpecificReasoningContracts()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var models = catalogue
            .EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "xAI")
            .GetProperty("models")
            .EnumerateArray()
            .ToDictionary(model => model.GetProperty("name").GetString()!, model => model);

        Assert.AreEqual(
            AIModels.xAI.Grok4_5,
            models[nameof(AIModels.xAI.Grok4_5)].GetProperty("description").GetString());

        AssertReasoningLevels(
            AIModels.xAI.Grok4_5,
            "grok_always",
            "Low", "Medium", "High");
        AssertReasoningLevels(
            AIModels.xAI.Grok4_5Latest,
            "grok_always",
            "Low", "Medium", "High");
        AssertReasoningLevels(
            AIModels.xAI.GrokBuildLatest,
            "grok_always",
            "Low", "Medium", "High");
        AssertReasoningLevels(
            AIModels.xAI.Grok4_3,
            "grok",
            "None", "Low", "Medium", "High");
        AssertReasoningLevels(
            AIModels.xAI.Grok4_3Latest,
            "grok",
            "None", "Low", "Medium", "High");
        AssertReasoningLevels(
            AIModels.xAI.GrokLatest,
            "grok",
            "None", "Low", "Medium", "High");
        Assert.IsNull(ChatUiModelHelpers.GetReasoningLevels(AIModels.xAI.Grok4_20NonReasoning));
    }

    [TestMethod]
    public void ChatUi_DisableUsesNoneFor43AndTheLowestValidEffortFor45()
    {
        var grok43 = new XAIService(
            "offline-test-key",
            AIModels.xAI.Grok4_3,
            new HttpClient());
        ChatUiSettingsHelpers.ApplyReasoningSettings(
            grok43,
            CreateSettingsRequest(false, null, "grok"));
        Assert.AreEqual(GrokReasoning.None, grok43.ReasoningEffort);

        var grok45 = new XAIService(
            "offline-test-key",
            AIModels.xAI.Grok4_5,
            new HttpClient());
        ChatUiSettingsHelpers.ApplyReasoningSettings(
            grok45,
            CreateSettingsRequest(false, null, "grok_always"));
        Assert.AreEqual(GrokReasoning.Low, grok45.ReasoningEffort);

        ChatUiSettingsHelpers.ApplyReasoningSettings(
            grok45,
            CreateSettingsRequest(true, "medium", "grok_always"));
        Assert.AreEqual(GrokReasoning.Medium, grok45.ReasoningEffort);
    }

    [TestMethod]
    [DataRow(GrokReasoning.None, "none")]
    [DataRow(GrokReasoning.Low, "low")]
    [DataRow(GrokReasoning.Medium, "medium")]
    [DataRow(GrokReasoning.High, "high")]
    public async Task Grok43_SerializesEverySupportedEffort(
        GrokReasoning effort,
        string expected)
    {
        var service = CreateProbe(AIModels.xAI.Grok4_3, effort);

        using var request = service.BuildRegularRequest();
        var body = await ParseBodyAsync(request);

        Assert.AreEqual("chat/completions", request.RequestUri!.OriginalString);
        Assert.AreEqual(expected, body.GetProperty("reasoning_effort").GetString());
        Assert.IsFalse(body.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(body.TryGetProperty("presence_penalty", out _));
        Assert.IsTrue(body.TryGetProperty("temperature", out _));
        Assert.IsTrue(body.TryGetProperty("top_p", out _));
    }

    [TestMethod]
    [DataRow(AIModels.xAI.Grok4_5)]
    [DataRow(AIModels.xAI.Grok4_5Latest)]
    [DataRow(AIModels.xAI.GrokBuildLatest)]
    public async Task Grok45AndAliases_SerializeEffortAndOmitForbiddenPenalties(string model)
    {
        var service = CreateProbe(model, GrokReasoning.Medium);

        using var request = service.BuildRegularRequest();
        var body = await ParseBodyAsync(request);

        Assert.AreEqual("medium", body.GetProperty("reasoning_effort").GetString());
        Assert.IsFalse(body.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(body.TryGetProperty("presence_penalty", out _));
        Assert.IsTrue(body.TryGetProperty("temperature", out _));
        Assert.IsTrue(body.TryGetProperty("top_p", out _));
    }

    [TestMethod]
    [DataRow(AIModels.xAI.Grok4_3Latest)]
    [DataRow(AIModels.xAI.GrokLatest)]
    public async Task Grok43Aliases_SerializeConfigurableReasoning(string model)
    {
        var service = CreateProbe(model, GrokReasoning.Medium);

        using var request = service.BuildRegularRequest();
        var body = await ParseBodyAsync(request);

        Assert.AreEqual("medium", body.GetProperty("reasoning_effort").GetString());
        Assert.IsFalse(body.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(body.TryGetProperty("presence_penalty", out _));
    }

    [TestMethod]
    public async Task Grok45_AutoUsesProviderDefaultAndNoneIsRejected()
    {
        var automatic = CreateProbe(AIModels.xAI.Grok4_5, GrokReasoning.Auto);
        using var automaticRequest = automatic.BuildRegularRequest();
        var automaticBody = await ParseBodyAsync(automaticRequest);

        Assert.IsFalse(automaticBody.TryGetProperty("reasoning_effort", out _));
        Assert.IsFalse(automaticBody.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(automaticBody.TryGetProperty("presence_penalty", out _));

        var disabled = CreateProbe(AIModels.xAI.Grok4_5, GrokReasoning.None);
        var exception = Assert.ThrowsExactly<NotSupportedException>(
            () => disabled.BuildRegularRequest());
        StringAssert.Contains(exception.Message, "cannot disable reasoning");
    }

    [TestMethod]
    public async Task Grok45_FunctionRequestUsesSameEffortAndKeepsAllowedTemperature()
    {
        var service = CreateProbe(AIModels.xAI.Grok4_5Latest, GrokReasoning.High);

        using var request = service.BuildFunctionRequest();
        var body = await ParseBodyAsync(request);

        Assert.AreEqual("chat/completions", request.RequestUri!.OriginalString);
        Assert.AreEqual("high", body.GetProperty("reasoning_effort").GetString());
        Assert.IsTrue(body.TryGetProperty("temperature", out _));
        Assert.AreEqual(1, body.GetProperty("tools").GetArrayLength());
    }

    [TestMethod]
    public async Task ForcedFunctionName_IsSerializedOnlyForTheInitialToolRound()
    {
        var service = CreateProbe(AIModels.xAI.Grok4_20NonReasoning, GrokReasoning.Auto);
        service.ForceFunctionName = "lookup";

        using var initialRequest = service.BuildFunctionRequest();
        var initialBody = await ParseBodyAsync(initialRequest);
        Assert.AreEqual(
            "lookup",
            initialBody.GetProperty("tool_choice")
                .GetProperty("function")
                .GetProperty("name")
                .GetString());

        service.AddFunctionResultForProbe();
        using var continuationRequest = service.BuildFunctionRequest();
        var continuationBody = await ParseBodyAsync(continuationRequest);
        Assert.AreEqual("auto", continuationBody.GetProperty("tool_choice").GetString());
    }

    [TestMethod]
    public void Grok45_ChatCompletionsReasoningDeltaIsParsed()
    {
        var service = new XAIRequestProbe(AIModels.xAI.Grok4_5);

        var reasoning = service.ParseReasoning(
            """
            {
              "choices": [
                {
                  "delta": {
                    "reasoning_content": "checking the constraints"
                  }
                }
              ]
            }
            """);

        Assert.AreEqual("checking the constraints", reasoning);
    }

    [TestMethod]
    public async Task Grok420_OmitsRejectedPenaltiesForBothVariants()
    {
        var reasoning = CreateProbe(AIModels.xAI.Grok4_20Reasoning, GrokReasoning.Auto);
        using var reasoningRequest = reasoning.BuildRegularRequest();
        var reasoningBody = await ParseBodyAsync(reasoningRequest);
        Assert.IsFalse(reasoningBody.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(reasoningBody.TryGetProperty("presence_penalty", out _));

        var nonReasoning = CreateProbe(AIModels.xAI.Grok4_20NonReasoning, GrokReasoning.Auto);
        using var nonReasoningRequest = nonReasoning.BuildRegularRequest();
        var nonReasoningBody = await ParseBodyAsync(nonReasoningRequest);
        Assert.IsFalse(nonReasoningBody.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(nonReasoningBody.TryGetProperty("presence_penalty", out _));
    }

    [TestMethod]
    [DataRow(AIModels.xAI.Grok4_3)]
    [DataRow(AIModels.xAI.Grok4_20NonReasoning)]
    public async Task LiveRejectedPenaltyModels_OmitPenaltiesFromRegularAndFunctionRequests(string model)
    {
        var service = CreateProbe(model, GrokReasoning.Auto);

        using var regularRequest = service.BuildRegularRequest();
        var regularBody = await ParseBodyAsync(regularRequest);
        Assert.IsFalse(regularBody.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(regularBody.TryGetProperty("presence_penalty", out _));

        using var functionRequest = service.BuildFunctionRequest();
        var functionBody = await ParseBodyAsync(functionRequest);
        Assert.IsFalse(functionBody.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(functionBody.TryGetProperty("presence_penalty", out _));
    }

    [TestMethod]
    public async Task NonStreamingHttpFailure_PreservesProviderDiagnosticInMessageAndDetails()
    {
        const string errorBody =
            "{\"code\":\"invalid-argument\",\"error\":\"Model does not support parameter presencePenalty.\"}";
        var handler = new ErrorHandler(errorBody);
        var service = new XAIService(
            "offline-test-key",
            AIModels.xAI.Grok4_3,
            new HttpClient(handler));

        var exception = await Assert.ThrowsExactlyAsync<Mythosia.AI.Exceptions.AIServiceException>(
            () => service.GetCompletionAsync("test"));

        StringAssert.Contains(exception.Message, "xAI API request failed (400): Bad Request");
        StringAssert.Contains(exception.Message, "presencePenalty");
        Assert.AreEqual(errorBody, exception.ErrorDetails);
    }

    [TestMethod]
    public async Task DisableReasoningProfile_UsesEachModelsSupportedFloorAndRestoresState()
    {
        var handler45 = new CaptureHandler();
        var grok45 = new XAIService(
            "offline-test-key",
            AIModels.xAI.Grok4_5,
            new HttpClient(handler45));
        await grok45.GetCompletionAsync(
            "summarize",
            new AIRequestProfile { DisableReasoning = true });

        using (var request45 = JsonDocument.Parse(handler45.Body))
        {
            Assert.AreEqual(
                "low",
                request45.RootElement.GetProperty("reasoning_effort").GetString());
        }
        Assert.AreEqual(GrokReasoning.Auto, grok45.ReasoningEffort);

        var handler43 = new CaptureHandler();
        var grok43 = new XAIService(
            "offline-test-key",
            AIModels.xAI.Grok4_3,
            new HttpClient(handler43));
        await grok43.GetCompletionAsync(
            "summarize",
            new AIRequestProfile { DisableReasoning = true });

        using (var request43 = JsonDocument.Parse(handler43.Body))
        {
            Assert.AreEqual(
                "none",
                request43.RootElement.GetProperty("reasoning_effort").GetString());
        }
        Assert.AreEqual(GrokReasoning.Auto, grok43.ReasoningEffort);
    }

    private static XAIRequestProbe CreateProbe(string model, GrokReasoning effort)
    {
        return new XAIRequestProbe(model)
        {
            ReasoningEffort = effort,
            FrequencyPenalty = 0.25f,
            PresencePenalty = 0.5f
        };
    }

    private static string ReadModelId(string fieldName)
    {
        return (string)typeof(AIModels.xAI)
            .GetField(fieldName)!
            .GetRawConstantValue()!;
    }

    private static async Task<JsonElement> ParseBodyAsync(HttpRequestMessage request)
    {
        var json = await request.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void AssertReasoningLevels(
        string model,
        string expectedType,
        params string[] expectedLevels)
    {
        var reasoning = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetReasoningLevels(model));
        Assert.AreEqual(expectedType, reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(
            expectedLevels,
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    private static SettingsRequest CreateSettingsRequest(
        bool reasoningEnabled,
        string? reasoningLevel,
        string reasoningType)
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
            ReasoningType: reasoningType);

    private sealed class XAIRequestProbe : XAIService
    {
        public XAIRequestProbe(string model)
            : base("offline-test-key", model, new HttpClient())
        {
            ActivateChat.Messages.Add(new Message(ActorRole.User, "test"));
        }

        public HttpRequestMessage BuildRegularRequest() => CreateMessageRequest();

        public string? ParseReasoning(string json) =>
            ParseStreamChunk(json, StreamOptions.FullOptions).Reasoning;

        public HttpRequestMessage BuildFunctionRequest()
        {
            Functions.Add(new FunctionDefinition
            {
                Name = "lookup",
                Description = "Looks up a value.",
                Handler = _ => Task.FromResult("ok")
            });

            return CreateFunctionMessageRequest();
        }

        public void AddFunctionResultForProbe()
        {
            ActivateChat.Messages.Add(new Message(ActorRole.Function, "ok")
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_result",
                    [MessageMetadataKeys.FunctionId] = "call_1",
                    [MessageMetadataKeys.FunctionName] = "lookup"
                }
            });
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        private readonly string errorBody;

        public ErrorHandler(string errorBody)
        {
            this.errorBody = errorBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad Request",
                Content = new StringContent(errorBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
