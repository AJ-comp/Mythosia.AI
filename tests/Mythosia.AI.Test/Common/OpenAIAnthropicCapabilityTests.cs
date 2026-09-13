using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIAnthropicCapabilityTests
{
    public static IEnumerable<object[]> CatalogueModels =>
        new[] { ("OpenAI", typeof(AIModels.OpenAI)), ("Anthropic", typeof(AIModels.Anthropic)) }
            .SelectMany(provider => provider.Item2.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(modelField => modelField.IsLiteral && modelField.FieldType == typeof(string))
                .Select(modelField => (string)modelField.GetRawConstantValue()!)
                .Where(model => !model.StartsWith("gpt-image-", StringComparison.Ordinal))
                .Select(model => new object[] { provider.Item1, model }));

    [TestMethod]
    [DynamicData(nameof(CatalogueModels))]
    public void CatalogueReasoningCapabilities_MatchCommonRequestValidation(string provider, string model)
    {
        using var handler = new OfflineHandler();
        using var client = new HttpClient(handler);
        var openAI = provider == "OpenAI" ? new OpenAIProbe(model, client) : null;
        var claude = provider == "Anthropic" ? new ClaudeProbe(model, client) : null;
        var capabilities = openAI?.GetCapabilities() ?? claude!.GetCapabilities();

        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Streaming, model);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.FunctionCalling, model);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.StructuredOutput, model);
        Assert.IsNotNull(capabilities.MaxOutputTokens, model);
        foreach (var level in Enum.GetValues<ReasoningLevel>())
        {
            var features = new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = level } };
            Action validate = openAI != null ? () => openAI.Validate(features) : () => claude!.Validate(features);
            if (capabilities.GetReasoningSupport(level) == CapabilitySupport.Supported)
                validate();
            else
                Assert.Throws<NotSupportedException>(validate, $"{model}: {level}");
        }
        Assert.AreEqual(0, handler.Requests);
    }

    [TestMethod]
    [DataRow("gpt-6-unannounced")]
    [DataRow("gpt-6-astra-experimental")]
    [DataRow("gpt-6-astra-2026-99-99")]
    public void UnknownOpenAIModels_DoNotAcquireCapabilitiesFromPrefixes(string model)
    {
        using var client = new HttpClient(new OfflineHandler());
        var capabilities = new OpenAIProbe(model, client).GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Reasoning);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.AsyncFunctionCalling);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Steering);
        Assert.IsEmpty(capabilities.ReasoningLevels);
        Assert.IsNull(capabilities.MaxOutputTokens);
    }

    [TestMethod]
    [DataRow("claude-fable-99")]
    [DataRow("claude-fable-5-1-experimental")]
    [DataRow("claude-fable-5-1-20269999")]
    public void UnknownClaudeModels_KeepModelDependentCapabilitiesUnknown(string model)
    {
        using var client = new HttpClient(new OfflineHandler());
        var capabilities = new ClaudeProbe(model, client).GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Reasoning);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.WebSearch);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Steering);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.FileSearch);
        Assert.IsEmpty(capabilities.ReasoningLevels);
    }

    [TestMethod]
    public void KnownDatedSnapshots_UseTheirExplicitFamilyContract()
    {
        using var client = new HttpClient(new OfflineHandler());
        var openAI = new OpenAIProbe("gpt-6-astra-2026-09-01", client).GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, openAI.Steering);
        using var claudeClient = new HttpClient(new OfflineHandler());
        var claude = new ClaudeProbe("claude-fable-5-1-20260901", claudeClient).GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, claude.ReasoningCachePreservation);
    }

    [TestMethod]
    public void AstraMode_IsCapturedByBuilder_AndMatchesPreservationValidation()
    {
        using var client = new HttpClient(new OfflineHandler());
        var service = new OpenAIProbe(AIModels.OpenAI.Gpt6Astra, client);
        var request = service.CreateRequest("question");
        service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;

        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().ReasoningCachePreservation);
        Assert.AreEqual(CapabilitySupport.Unsupported, service.GetCapabilities().ReasoningCachePreservation);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().AsyncFunctionCalling);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().Steering);
        Assert.Throws<NotSupportedException>(() => service.Validate(new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = ReasoningLevel.High, Cache = CachePreservation.Required }
        }));
    }

    [TestMethod]
    public void SearchCapabilities_MatchAdaptersAndKeepSearchDisabledDifferentFromUnsupported()
    {
        using var client = new HttpClient(new OfflineHandler());
        var capable = new OpenAIProbe(AIModels.OpenAI.Gpt4_1, client);
        Assert.AreEqual(CapabilitySupport.Supported, capable.GetCapabilities().WebSearch);
        capable.Validate(new AIRequestFeatures { WebSearch = new WebSearchOptions() });
        using var nanoClient = new HttpClient(new OfflineHandler());
        var nano = new OpenAIProbe(AIModels.OpenAI.Gpt5_4Nano, nanoClient);
        Assert.AreEqual(CapabilitySupport.Unsupported, nano.GetCapabilities().WebSearch);
        Assert.Throws<NotSupportedException>(() => nano.Validate(new AIRequestFeatures { WebSearch = new WebSearchOptions() }));
    }

    [TestMethod]
    public void ClaudeManualAndAdaptiveControls_ReportDifferentChoicesAndCapturedSampling()
    {
        using var client = new HttpClient(new OfflineHandler());
        var manual = new ClaudeProbe(AIModels.Anthropic.ClaudeHaiku4_5_251001, client);
        var captured = manual.CreateRequest("question");
        Assert.AreEqual(CapabilitySupport.Supported, captured.GetCapabilities().Temperature);
        manual.ThinkingBudget = 4096;
        var manualCapabilities = manual.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unsupported, manualCapabilities.Temperature);
        Assert.AreEqual(CapabilitySupport.Supported, captured.GetCapabilities().Temperature);
        CollectionAssert.AreEqual(new[] { 1024, 2048, 4096, 8192, 16384 }, manualCapabilities.ThinkingBudgetPresets.ToArray());
        Assert.IsEmpty(manualCapabilities.NativeReasoningLevels);
        Assert.AreEqual(CapabilitySupport.Unsupported, manualCapabilities.GetReasoningSupport(ReasoningLevel.High));

        using var adaptiveClient = new HttpClient(new OfflineHandler());
        var adaptive = new ClaudeProbe(AIModels.Anthropic.ClaudeFable5_1, adaptiveClient).GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unsupported, adaptive.ThinkingToggle);
        Assert.AreEqual(CapabilitySupport.Unsupported, adaptive.GetReasoningSupport(ReasoningLevel.None));
        Assert.AreEqual(CapabilitySupport.Supported, adaptive.GetReasoningSupport(ReasoningLevel.Max));
        Assert.IsEmpty(adaptive.ThinkingBudgetPresets);
        Assert.AreEqual(CapabilitySupport.Supported, adaptive.ReasoningCachePreservation);
    }

    [TestMethod]
    public async Task ClaudeBuilderCapabilityQuery_DoesNotResetPreviousResponseDiagnostics()
    {
        using var handler = new OfflineHandler
        {
            ResponseBody = """
                {"id":"msg-diagnostics","type":"message","model":"claude-fable-5-1","role":"assistant","content":[{"type":"thinking","thinking":"previous progress","signature":"signature"},{"type":"text","text":"answer"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":3},"input_transformations":[{"type":"thinking_dropped","path":"messages.1.content.0","reason":"prefix_binding_mismatch"}]}
                """
        };
        using var client = new HttpClient(handler);
        var service = new ClaudeProbe(AIModels.Anthropic.ClaudeFable5_1, client);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        var earlierBuilder = service.CreateRequest("another request");
        await service.GetCompletionAsync("produce diagnostics");
        Assert.AreEqual("previous progress", service.LastThinkingContent);
        Assert.AreEqual("prefix_binding_mismatch", service.LastInputTransformations.Single().Reason);

        earlierBuilder.GetCapabilities();
        service.GetCapabilities();

        Assert.AreEqual("previous progress", service.LastThinkingContent);
        Assert.AreEqual("prefix_binding_mismatch", service.LastInputTransformations.Single().Reason);
        Assert.AreEqual(1, handler.Requests);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.GptImage2)]
    [DataRow(AIModels.OpenAI.GptImage2_5Sunburst)]
    [DataRow(AIModels.OpenAI.GptImage2_5Flare)]
    public async Task ImageQualityChoices_MatchActualValidationAndWireValues(string model)
    {
        using var handler = new OfflineHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIProbe(AIModels.OpenAI.Gpt4_1, client);
        var capabilities = service.GetImageCapabilities(model);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Generation);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Editing);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Mask);
        foreach (var quality in Enum.GetValues<ImageQuality>())
        {
            var request = new ImageGenerationRequest { Prompt = "one shape", Model = model, Quality = quality };
            if (!capabilities.Qualities.Contains(quality))
            {
                var before = handler.Requests;
                await Assert.ThrowsAsync<NotSupportedException>(() => service.GenerateImagesAsync(request));
                Assert.AreEqual(before, handler.Requests);
            }
            else
            {
                await service.GenerateImagesAsync(request);
                using var body = JsonDocument.Parse(handler.LastBody!);
                Assert.AreEqual(quality.ToString().ToLowerInvariant(), body.RootElement.GetProperty("quality").GetString());
            }
        }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "one shape", Model = model, Count = capabilities.MaxImages!.Value + 1
        }));
    }

    [TestMethod]
    public async Task ImageCapabilities_ExposeDefaultsUnknownAndReferenceImageLimit()
    {
        using var handler = new OfflineHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIProbe(AIModels.OpenAI.Gpt4_1, client);
        Assert.AreEqual(AIModels.OpenAI.GptImage2, service.GetImageCapabilities().Model);
        var unknown = service.GetImageCapabilities("custom-image-deployment");
        Assert.AreEqual(CapabilitySupport.Unknown, unknown.Generation);
        Assert.IsEmpty(unknown.Qualities);
        var known = service.GetImageCapabilities(AIModels.OpenAI.GptImage2_5Sunburst);
        await Assert.ThrowsAsync<ArgumentException>(() => service.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "edit", Model = known.Model,
            InputImages = Enumerable.Range(0, known.MaxInputImages!.Value + 1)
                .Select(_ => new ImageInput(new byte[] { 1 }, "image/png")).ToArray()
        }));
        Assert.AreEqual(0, handler.Requests);
    }

    private sealed class OpenAIProbe(string model, HttpClient client) : OpenAIService("offline", model, client)
    {
        public void Validate(AIRequestFeatures features) => ValidateRequestFeatures(features);
    }

    private sealed class ClaudeProbe(string model, HttpClient client) : AnthropicService("offline", model, client)
    {
        public void Validate(AIRequestFeatures features) => ValidateRequestFeatures(features);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string? LastBody { get; private set; }
        public string ResponseBody { get; set; } = "{\"data\":[{\"b64_json\":\"AQ==\"}]}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
