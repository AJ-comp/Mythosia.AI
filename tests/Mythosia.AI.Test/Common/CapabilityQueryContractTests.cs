using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class CapabilityQueryContractTests
{
    [TestMethod]
    public void CustomProviderWithoutOverrideRemainsCompatibleAndUnknown()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new UnregisteredService(client);
        Assert.AreSame(AIModelCapabilities.Unknown, service.GetCapabilities());
        Assert.AreSame(AIModelCapabilities.Unknown, service.CreateRequest("question").GetCapabilities());
        Assert.AreSame(ImageModelCapabilities.Unknown, service.GetImageCapabilities("custom-image"));
    }

    [TestMethod]
    public void ServiceQueryDoesNotConsumePendingFeaturesPolicyOrNativeOptions()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProbeService(client) { PendingNative = "native-original" };
        service.NextPolicy = new FunctionCallingPolicy { MaxRounds = 3 };
        service.ConfigureRequestFeatures(new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = ReasoningLevel.High }
        });
        for (var i = 0; i < 3; i++)
        {
            Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().Reasoning);
            Assert.AreEqual(0, service.CaptureCalls);
            Assert.AreEqual("native-original", service.PendingNative);
            Assert.AreEqual(3, service.NextPolicy!.MaxRounds);
        }
        var request = service.CreateRequest("question");
        Assert.AreEqual(1, service.CaptureCalls);
        Assert.IsNull(service.PendingNative);
        Assert.IsNull(service.NextPolicy);
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().Reasoning);
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().Steering);
        Assert.AreEqual(CapabilitySupport.Unsupported, service.GetCapabilities().Reasoning);
        Assert.AreEqual(1, service.CaptureCalls);
    }

    [TestMethod]
    public void BuilderQueryUsesSnapshotAndSiblingOverridesWithoutChangingDefaults()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProbeService(client) { Temperature = 0.2f };
        var first = service.CreateRequest("first");
        var second = first.WithTemperature(0.8f);
        service.SelectModel("later-model");
        service.Temperature = 0.9f;
        Assert.AreEqual("original-model", first.GetCapabilities().Model);
        Assert.AreEqual("original-model", second.GetCapabilities().Model);
        Assert.AreEqual("later-model", service.GetCapabilities().Model);
        Assert.AreEqual(CapabilitySupport.Supported, first.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, second.GetCapabilities().Temperature);
        Assert.AreEqual(0.9f, service.Temperature);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public void UnsupportedQueryDoesNotValidateRequestOrInvokeCallbacks()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProbeService(client) { RejectValidation = true };
        var callbacks = 0;
        service.WithSystemMessageProvider(() => { callbacks++; return new AIRequestContext(); });
        var request = service.CreateRequest("input").WithWebSearch()
            .WithFunctions(new FunctionDefinition { Name = "unused" })
            .WithContext(new AIRequestContext { SystemMessagePrefix = "context" });
        Assert.AreEqual(CapabilitySupport.Unsupported, request.GetCapabilities().WebSearch);
        Assert.AreEqual(0, service.ValidationCalls);
        Assert.AreEqual(0, callbacks);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.AreEqual(0, service.LastCitations.Count);
    }

    [TestMethod]
    public async Task QueriesLeavePreviousCitationsAndNextExecutionIntact()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProbeService(client);
        await service.GetCompletionAsync(new Message(ActorRole.User, "previous"));
        Assert.AreEqual("previous-source", service.LastCitations.Single().Title);
        var request = service.CreateRequest("next").WithReasoning(ReasoningLevel.High);
        request.GetCapabilities();
        service.GetCapabilities();
        Assert.AreEqual("previous-source", service.LastCitations.Single().Title);
        Assert.AreEqual("reasoning", await request.GetCompletionAsync());
        Assert.AreEqual(2, service.CompletionCalls);
    }

    [TestMethod]
    public void ResolverFailureRestoresScopes()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProbeService(client) { Temperature = 0.2f };
        var request = service.CreateRequest("question").WithTemperature(0.8f);
        service.ThrowOnResolve = true;
        Assert.Throws<InvalidOperationException>(() => request.GetCapabilities());
        service.ThrowOnResolve = false;
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, request.GetCapabilities().Temperature);
    }

    [TestMethod]
    public async Task ConcurrentQueriesKeepIndependentScopes()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProbeService(client);
        var first = service.CreateRequest("first").WithTemperature(0.2f);
        var second = service.CreateRequest("second").WithTemperature(0.8f);
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() =>
            (i, result: (i % 2 == 0 ? first : second).GetCapabilities().Temperature))));
        foreach (var result in results)
            Assert.AreEqual(result.i % 2 == 0 ? CapabilitySupport.Supported : CapabilitySupport.Unsupported, result.result);
    }

    [TestMethod]
    public void InternalProfileSuppressesFeaturesAndNativeOptionsOnlyWithinQuery()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new ProbeService(client) { PendingNative = "native-original" };
        var request = service.CreateRequest("question").WithReasoning(ReasoningLevel.High);
        var internalRequest = request.WithProfile(new AIRequestProfile
        {
            Purpose = AIRequestPurpose.Summarization, DisableReasoning = true, Temperature = 0.2f
        });
        var capabilities = internalRequest.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Reasoning);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Steering);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Temperature);
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().Reasoning);
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().Steering);
    }

    [TestMethod]
    public void ProviderProfileAndPendingReasoningAffectSamplingWithoutEnablingTheFeature()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new DeepSeekService("offline", client) { ThinkingEnabled = false };
        var plain = service.CreateRequest("question");
        var reasoning = plain.WithReasoning(ReasoningLevel.High);
        var internalRequest = reasoning.WithProfile(new AIRequestProfile
        {
            Purpose = AIRequestPurpose.Summarization, DisableReasoning = true
        });
        Assert.AreEqual(CapabilitySupport.Supported, plain.GetCapabilities().Reasoning);
        Assert.AreEqual(CapabilitySupport.Supported, plain.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, reasoning.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Supported, reasoning.GetCapabilities().TopP);
        Assert.AreEqual(CapabilitySupport.Supported, internalRequest.GetCapabilities().Temperature);
        Assert.IsFalse(service.ThinkingEnabled);
    }

    [TestMethod]
    [DataRow(CapabilitySupport.Unknown, ReasoningLevel.High, CapabilitySupport.Unknown)]
    [DataRow(CapabilitySupport.Unsupported, ReasoningLevel.High, CapabilitySupport.Unsupported)]
    [DataRow(CapabilitySupport.Supported, ReasoningLevel.High, CapabilitySupport.Supported)]
    [DataRow(CapabilitySupport.Supported, ReasoningLevel.Max, CapabilitySupport.Unsupported)]
    [DataRow(CapabilitySupport.Unknown, (ReasoningLevel)999, CapabilitySupport.Unsupported)]
    public void ReasoningSupportDoesNotConflateUnknownWithUnsupported(
        CapabilitySupport support, ReasoningLevel level, CapabilitySupport expected)
    {
        var capabilities = new AIModelCapabilities(reasoning: support, reasoningLevels: [ReasoningLevel.High]);
        Assert.AreEqual(expected, capabilities.GetReasoningSupport(level));
    }

    [TestMethod]
    public void ChatCapabilityCollectionsAreDefensivelyCopiedAndReadOnly()
    {
        var levels = new List<ReasoningLevel> { ReasoningLevel.Low, ReasoningLevel.High };
        var budgets = new[] { 1024, 4096 };
        var result = new AIModelCapabilities(reasoningLevels: levels, nativeReasoningLevels: levels, thinkingBudgetPresets: budgets);
        levels.Clear();
        budgets[0] = 99;
        CollectionAssert.AreEqual(new[] { ReasoningLevel.Low, ReasoningLevel.High }, result.ReasoningLevels.ToArray());
        Assert.AreEqual(2, result.NativeReasoningLevels.Count);
        Assert.AreEqual(1024, result.ThinkingBudgetPresets[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<ReasoningLevel>)result.ReasoningLevels)[0] = ReasoningLevel.Max);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)result.ThinkingBudgetPresets).Clear());
    }

    [TestMethod]
    public void ImageCapabilityCollectionsAreDefensivelyCopiedAndReadOnly()
    {
        var qualities = new[] { ImageQuality.High };
        var backgrounds = new[] { ImageBackground.Transparent };
        var formats = new[] { ImageOutputFormat.WebP };
        var sizes = new[] { ImageSizeKind.Preset };
        var resolutions = new[] { ImageResolution.TwoK };
        var ratios = new[] { ImageAspectRatio.OneByOne };
        var result = new ImageModelCapabilities(qualities: qualities, backgrounds: backgrounds, outputFormats: formats,
            sizeKinds: sizes, resolutions: resolutions, aspectRatios: ratios);
        qualities[0] = ImageQuality.Low;
        backgrounds[0] = ImageBackground.Opaque;
        formats[0] = ImageOutputFormat.Png;
        sizes[0] = ImageSizeKind.Auto;
        resolutions[0] = ImageResolution.OneK;
        ratios[0] = ImageAspectRatio.SixteenByNine;
        Assert.AreEqual(ImageQuality.High, result.Qualities.Single());
        Assert.AreEqual(ImageBackground.Transparent, result.Backgrounds.Single());
        Assert.AreEqual(ImageOutputFormat.WebP, result.OutputFormats.Single());
        Assert.AreEqual(ImageSizeKind.Preset, result.SizeKinds.Single());
        Assert.AreEqual(ImageResolution.TwoK, result.Resolutions.Single());
        Assert.AreEqual(ImageAspectRatio.OneByOne, result.AspectRatios.Single());
        Assert.Throws<NotSupportedException>(() => ((IList<ImageQuality>)result.Qualities).Clear());
    }

    [TestMethod]
    public void CatalogueControlsUseProviderDefinitionsAndHideUnknownModels()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        foreach (var model in catalogue.EnumerateArray().SelectMany(group => group.GetProperty("models").EnumerateArray()))
        {
            var value = ChatUiModelHelpers.FindModelValueByName(model.GetProperty("name").GetString())!;
            var capabilities = ChatUiModelHelpers.GetModelCapabilities(value);
            Assert.AreEqual(capabilities.Temperature == CapabilitySupport.Supported,
                model.GetProperty("sampling").GetProperty("temperature").GetBoolean(), value);
            Assert.AreEqual(capabilities.TopP == CapabilitySupport.Supported,
                model.GetProperty("sampling").GetProperty("topP").GetBoolean(), value);
            Assert.AreEqual(capabilities.MaxOutputTokens,
                model.GetProperty("maxOutputTokens").ValueKind == JsonValueKind.Null
                    ? (uint?)null : model.GetProperty("maxOutputTokens").GetUInt32(), value);
        }
        Assert.IsNull(ChatUiModelHelpers.GetReasoningLevels("gpt-99-unregistered"));
        var unknown = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetSamplingControls("deployment-private"));
        Assert.IsFalse(unknown.GetProperty("temperature").GetBoolean());
        Assert.IsFalse(unknown.GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    [DataRow("OpenAI", AIModels.OpenAI.Gpt5_6)]
    [DataRow("OpenAI", AIModels.OpenAI.Gpt4o)]
    [DataRow("Anthropic", AIModels.Anthropic.ClaudeOpus5)]
    [DataRow("Google", AIModels.Google.Gemini3_8Flash)]
    public void ExportedSnippetUsesTheActiveSamplingCapabilities(string provider, string model)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        AIService service = provider switch
        {
            "OpenAI" => new Mythosia.AI.Services.OpenAI.OpenAIService("offline", model, client),
            "Anthropic" => new Mythosia.AI.Services.Anthropic.AnthropicService("offline", model, client),
            _ => new Mythosia.AI.Services.Google.GoogleAIService("offline", model, client)
        };
        var capabilities = service.GetCapabilities();
        var code = ChatUiUtilityHelpers.GenerateCodeSnippet(service, provider, model, "question");
        Assert.AreEqual(capabilities.Temperature == CapabilitySupport.Supported, code.Contains("service.Temperature ="));
        Assert.AreEqual(capabilities.TopP == CapabilitySupport.Supported, code.Contains("service.TopP ="));
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("Capability inspection must not send HTTP.");
    }

    private class UnregisteredService : AIService
    {
        public UnregisteredService(HttpClient client) : base("offline", "https://localhost/", client)
        { Model = "original-model"; AddNewChat(); }
        public override string Provider => "Custom";
        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult("plain");
        public override Task StreamCompletionAsync(Message message, Func<string, Task> callback) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No request expected");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No request expected");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new());
        protected override string ExtractResponseContent(string response) => response;
        protected override string StreamParseJson(string response) => response;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }

    private sealed class ProbeService : UnregisteredService
    {
        public ProbeService(HttpClient client) : base(client) { }
        public string? PendingNative { get; set; }
        public int CaptureCalls { get; private set; }
        public int ValidationCalls { get; private set; }
        public int CompletionCalls { get; private set; }
        public bool RejectValidation { get; set; }
        public bool ThrowOnResolve { get; set; }
        public FunctionCallingPolicy? NextPolicy { get => CurrentPolicy; set => CurrentPolicy = value; }
        public void SelectModel(string model) => Model = model;
        protected override object? CaptureProviderRequestOptions(Message message)
        {
            CaptureCalls++;
            var value = PendingNative;
            PendingNative = null;
            return value;
        }
        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            if (ThrowOnResolve) throw new InvalidOperationException("resolver failure");
            return new AIModelCapabilities(provider: Provider, model: RequestModel,
                temperature: RequestTemperature < 0.5f ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                reasoning: CurrentRequestFeatures.Reasoning != null ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                steering: CurrentProviderRequestOptions is string ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                webSearch: CapabilitySupport.Unsupported);
        }
        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            ValidationCalls++;
            if (RejectValidation) throw new NotSupportedException("validation called");
        }
        public override Task<string> GetCompletionAsync(Message message)
        {
            using var scope = BeginRequestFeaturesScope(message);
            CompletionCalls++;
            RecordCitation(new AICitation { Provider = Provider, Title = "previous-source" });
            return Task.FromResult(CurrentRequestFeatures.Reasoning != null ? "reasoning" : "plain");
        }
    }
}
