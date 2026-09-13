using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("Capabilities")]
public class CapabilityReauditPrimaryProvidersTests
{
    [TestMethod]
    [DataRow("OpenAI", "gpt-5.5-2026-04-23-2026-09-01")]
    [DataRow("OpenAI", "gpt-4o-2024-11-20-2026-09-01")]
    [DataRow("Anthropic", "claude-haiku-4-5-20251001-20260901")]
    [DataRow("Anthropic", "claude-opus-4-5-20251101-20260901")]
    public void AppendingDateToExistingSnapshot_DoesNotInventKnownModel(string provider, string model)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        AIService service = provider == "OpenAI" ? new OpenAIService("offline", model, client)
            : new AnthropicService("offline", model, client);
        var direct = service.GetCapabilities();
        var captured = service.CreateRequest("question").GetCapabilities();
        foreach (var caps in new[] { direct, captured })
        {
            Assert.AreEqual(model, caps.Model);
            Assert.AreEqual(CapabilitySupport.Unknown, caps.Streaming);
            Assert.AreEqual(CapabilitySupport.Unknown, caps.FunctionCalling);
            Assert.AreEqual(CapabilitySupport.Unknown, caps.Reasoning);
            Assert.IsEmpty(caps.ReasoningLevels);
            Assert.IsNull(caps.MaxOutputTokens);
        }
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("OpenAI", AIModels.OpenAI.Gpt5_5_260423)]
    [DataRow("OpenAI", "gpt-6-astra-2026-09-01")]
    [DataRow("Anthropic", AIModels.Anthropic.ClaudeHaiku4_5_251001)]
    [DataRow("Anthropic", "claude-fable-5-1-20260901")]
    public void ExactCatalogAndSingleDateAliasSnapshotsRemainKnown(string provider, string model)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        AIService service = provider == "OpenAI" ? new OpenAIService("offline", model, client)
            : new AnthropicService("offline", model, client);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().Streaming);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5_2Pro, nameof(OpenAIService.Gpt5_2ReasoningEffort))]
    [DataRow(AIModels.OpenAI.Gpt5_3Codex, nameof(OpenAIService.Gpt5_3ReasoningEffort))]
    [DataRow(AIModels.OpenAI.Gpt5_5Pro, nameof(OpenAIService.Gpt5_5ReasoningEffort))]
    [DataRow(AIModels.OpenAI.Gpt6Astra, nameof(OpenAIService.Gpt6ReasoningEffort))]
    public async Task OpenAINativeChoicesSurviveWireCoercionWithoutMisrepresentingSupportedLevels(string model, string propertyName)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline", model, client);
        var property = typeof(OpenAIService).GetProperty(propertyName)!;
        var caps = service.GetCapabilities();
        foreach (var level in caps.NativeReasoningLevels)
        {
            property.SetValue(service, Enum.Parse(property.PropertyType, level.ToString()));
            var builder = service.CreateRequest("question");
            builder.GetCapabilities();
            await builder.GetCompletionAsync();
            var effort = handler.Body!["reasoning"]!["effort"]!.GetValue<string>();
            Assert.AreNotEqual("auto", effort);
            if (level != ReasoningLevel.Auto)
                Assert.AreEqual(level.ToString().ToLowerInvariant(), effort);
        }
        Assert.IsFalse(caps.NativeReasoningLevels.Contains(ReasoningLevel.None));
        if (model.Contains("-pro")) Assert.IsFalse(caps.NativeReasoningLevels.Contains(ReasoningLevel.Low));
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6)]
    [DataRow(AIModels.Anthropic.ClaudeFable5_1)]
    public async Task ClaudeNativeEffortChoicesRoundTripThroughAdaptiveSerialization(string model)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new AnthropicService("offline", model, client);
        foreach (var level in service.GetCapabilities().NativeReasoningLevels)
        {
            service.WithAdaptiveThinkingParameters(Enum.Parse<ClaudeReasoningEffort>(level.ToString()));
            var request = service.CreateRequest("question");
            Assert.AreEqual(CapabilitySupport.Unsupported, request.GetCapabilities().Temperature);
            await request.GetCompletionAsync();
            Assert.AreEqual("adaptive", handler.Body!["thinking"]!["type"]!.GetValue<string>());
            Assert.IsFalse(handler.Body.ContainsKey("temperature"));
            var effort = handler.Body["output_config"]!["effort"]!.GetValue<string>();
            if (level != ReasoningLevel.Auto) Assert.AreEqual(level.ToString().ToLowerInvariant(), effort);
            else Assert.AreNotEqual("auto", effort);
        }
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.GptImage2)]
    [DataRow("custom-image-model")]
    public async Task ImageCapabilitiesUseTheSameProviderIdentityAsTheServiceAndImageResult(string model)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new BrandedOpenAIService(client);
        var caps = service.GetImageCapabilities(model);
        Assert.AreEqual(service.Provider, caps.Provider);
        Assert.AreEqual(0, handler.Calls);
        var result = await service.GenerateImagesAsync(new ImageGenerationRequest { Model = model, Prompt = "shape" });
        Assert.AreEqual(result.Provider, caps.Provider);
        Assert.AreEqual(1, handler.Calls);
    }

    private sealed class BrandedOpenAIService(HttpClient client) : OpenAIService("offline", AIModels.OpenAI.Gpt4o, client)
    {
        public override string Provider => "BrandedOpenAIAdapter";
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6, false)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6, false)]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_6, true)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet4_6, true)]
    public async Task ClaudeBindingCanEnableAdaptiveThinkingAndMustDisableCustomTemperature(string model, bool profiled)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new AnthropicService("offline", model, client) { Temperature = 0.3f };
        service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error);
        var direct = service.GetCapabilities();
        var request = service.CreateRequest("question");
        if (profiled) request = request.WithProfile(RequestProfiles.Summarization);
        var captured = request.GetCapabilities();
        service.ThinkingPrefixMismatchBehavior = null;
        await request.GetCompletionAsync();
        Assert.AreEqual("adaptive", handler.Body!["thinking"]!["type"]!.GetValue<string>());
        Assert.IsFalse(handler.Body.ContainsKey("temperature"), "Binding controls activate adaptive thinking even without a legacy thinking budget.");
        Assert.AreEqual(CapabilitySupport.Unsupported, direct.Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, captured.Temperature);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().Temperature);
    }

    [TestMethod]
    [DataRow("regular")]
    [DataRow("tools")]
    [DataRow("tools-none")]
    [DataRow("tools-disabled")]
    public async Task OpenAILegacySamplingTracksActualFunctionAndDisabledFunctionRequestPaths(string path)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline", AIModels.OpenAI.Gpt4o, client);
        if (path != "regular") service.Functions.Add(new FunctionDefinition { Name = "unused" });
        if (path == "tools-none") service.FunctionCallMode = FunctionCallMode.None;
        var request = service.CreateRequest("question").WithFunctionsDisabled(path == "tools-disabled");
        var caps = request.GetCapabilities();
        await request.GetCompletionAsync();
        var body = handler.Body!;
        Assert.AreEqual(CapabilitySupport.Supported, caps.Temperature);
        Assert.IsTrue(body.ContainsKey("temperature"));
        Assert.AreEqual(caps.TopP == CapabilitySupport.Supported, body.ContainsKey("top_p"));
        Assert.AreEqual(caps.FrequencyPenalty == CapabilitySupport.Supported, body.ContainsKey("frequency_penalty"));
        Assert.AreEqual(caps.PresencePenalty == CapabilitySupport.Supported, body.ContainsKey("presence_penalty"));
        Assert.AreEqual(path == "tools" || path == "tools-none", body.ContainsKey("tools"));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public JsonObject? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var response = request.RequestUri!.AbsolutePath.Contains("/images/")
                ? "{\"data\":[{\"b64_json\":\"AQ==\"}]}"
                : request.RequestUri.AbsolutePath.Contains("/chat/completions")
                ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}"
                : request.RequestUri.Host.Contains("anthropic")
                ? "{\"id\":\"msg\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"answer\"}],\"stop_reason\":\"end_turn\"}"
                : "{\"id\":\"response\",\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"answer\"}]}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
