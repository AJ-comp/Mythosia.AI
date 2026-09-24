using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("Capabilities")]
public class OtherProviderCapabilitiesTests
{
    public static IEnumerable<object[]> CommonReasoningCases =>
        Catalog(typeof(AIModels.Google)).Select(model => ("Google", model))
            .Concat(Catalog(typeof(AIModels.xAI)).Where(model => !model.Contains("image")).Select(model => ("xAI", model)))
            .Concat(new[] { ("DeepSeek", AIModels.DeepSeek.Flash), ("DeepSeek", AIModels.DeepSeek.V4Pro) })
            .SelectMany(pair => Enum.GetValues<ReasoningLevel>().Select(level => new object[] { pair.Item1, pair.Item2, level }));

    [TestMethod]
    [DynamicData(nameof(CommonReasoningCases))]
    public async Task CommonReasoning_ReportMatchesActualRequestValidationAndSerialization(string provider, string model, ReasoningLevel level)
    {
        var handler = new CaptureHandler(provider);
        using var client = new HttpClient(handler);
        var service = Create(provider, model, client);
        var request = service.CreateRequest("question").WithReasoning(level);
        var capabilities = request.GetCapabilities();
        Assert.AreEqual(model, capabilities.Model);
        Assert.AreEqual(0, handler.Calls, "Inspection must never send a request.");
        if (capabilities.GetReasoningSupport(level) == CapabilitySupport.Unsupported)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => request.GetCompletionAsync());
            Assert.AreEqual(0, handler.Calls);
            return;
        }
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.GetReasoningSupport(level));
        Assert.AreEqual("answer", await request.GetCompletionAsync());
        Assert.AreEqual(1, handler.Calls);
        var body = handler.Body!;
        if (provider == "xAI")
            Assert.AreEqual(level == ReasoningLevel.Auto ? null : level.ToString().ToLowerInvariant(), body["reasoning_effort"]?.GetValue<string>());
        else if (provider == "Google" && level != ReasoningLevel.Auto)
        {
            var thinking = body["generationConfig"]!["thinkingConfig"]!;
            if (level == ReasoningLevel.None) Assert.AreEqual(0, thinking["thinkingBudget"]!.GetValue<int>());
            else Assert.AreEqual(level.ToString().ToUpperInvariant(), thinking["thinkingLevel"]!.GetValue<string>());
        }
        else if (provider == "DeepSeek")
        {
            var thinking = level != ReasoningLevel.Auto && level != ReasoningLevel.None;
            Assert.AreEqual(thinking ? "enabled" : "disabled", body["thinking"]!["type"]!.GetValue<string>());
            Assert.AreEqual(capabilities.Temperature == CapabilitySupport.Supported, body.ContainsKey("temperature"));
            Assert.AreEqual(capabilities.TopP == CapabilitySupport.Supported, body.ContainsKey("top_p"));
        }
    }

    public static IEnumerable<object[]> GeminiModels => Catalog(typeof(AIModels.Google)).Select(model => new object[] { model });

    [TestMethod]
    [DynamicData(nameof(GeminiModels))]
    public async Task Gemini_SamplingAndSuggestedBudgetPresetsMatchSerializedContract(string model)
    {
        var handler = new CaptureHandler("Google");
        using var client = new HttpClient(handler);
        var service = new GoogleAIService("offline", model, client);
        var caps = service.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, caps.WebSearch);
        Assert.AreEqual(CapabilitySupport.Supported, caps.FileSearch);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.ReasoningCachePreservation);
        Assert.AreEqual(65536u, caps.MaxOutputTokens);
        foreach (var budget in caps.ThinkingBudgetPresets.DefaultIfEmpty(-1))
        {
            service.ThinkingBudget = budget;
            await service.CreateRequest("question").GetCompletionAsync();
            var config = handler.Body!["generationConfig"]!.AsObject();
            Assert.AreEqual(caps.Temperature == CapabilitySupport.Supported, config.ContainsKey("temperature"));
            Assert.AreEqual(caps.TopP == CapabilitySupport.Supported, config.ContainsKey("topP"));
            if (caps.ThinkingBudgetPresets.Count > 0)
                Assert.AreEqual(budget, config["thinkingConfig"]!["thinkingBudget"]!.GetValue<int>());
        }
    }

    public static IEnumerable<object[]> NativeGrokCases => new[] { AIModels.xAI.Grok4_6, AIModels.xAI.Grok4_5, AIModels.xAI.Grok4_3 }
        .SelectMany(model => Enum.GetValues<GrokReasoning>().Select(effort => new object[] { model, effort }));

    [TestMethod]
    [DynamicData(nameof(NativeGrokCases))]
    public async Task Grok_NativeEffortsRemainDistinctFromCommonReasoning(string model, GrokReasoning effort)
    {
        var handler = new CaptureHandler("xAI");
        using var client = new HttpClient(handler);
        var service = new XAIService("offline", model, client) { ReasoningEffort = effort };
        var caps = service.GetCapabilities();
        Assert.AreEqual(model == AIModels.xAI.Grok4_6 ? CapabilitySupport.Supported : CapabilitySupport.Unsupported, caps.Reasoning);
        Assert.AreEqual(CapabilitySupport.Supported, caps.NativeReasoning);
        var level = Enum.Parse<ReasoningLevel>(effort.ToString());
        if (!caps.NativeReasoningLevels.Contains(level))
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateRequest("question").GetCompletionAsync());
            Assert.AreEqual(0, handler.Calls);
            return;
        }
        await service.CreateRequest("question").GetCompletionAsync();
        Assert.AreEqual(effort == GrokReasoning.Auto ? null : effort.ToString().ToLowerInvariant(), handler.Body!["reasoning_effort"]?.GetValue<string>());
        Assert.IsFalse(handler.Body.ContainsKey("frequency_penalty"));
        Assert.IsFalse(handler.Body.ContainsKey("presence_penalty"));
    }

    [TestMethod]
    public async Task DeepSeek_QueryUsesPendingAndCapturedThinkingWithoutConsumingOptionsOrRunningContext()
    {
        var handler = new CaptureHandler("DeepSeek");
        using var client = new HttpClient(handler);
        var service = new DeepSeekService("offline", client);
        var contexts = 0;
        service.WithSystemMessageProvider(() => { contexts++; return new AIRequestContext(); });
        service.WithReasoning(ReasoningLevel.High);
        Assert.AreEqual(CapabilitySupport.Unsupported, service.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, service.GetCapabilities().Temperature);
        Assert.AreEqual(0, contexts);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        var captured = service.CreateRequest("question");
        service.ThinkingEnabled = false;
        Assert.AreEqual(CapabilitySupport.Unsupported, captured.GetCapabilities().Temperature);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().Temperature);
        await captured.GetCompletionAsync();
        Assert.AreEqual("enabled", handler.Body!["thinking"]!["type"]!.GetValue<string>());
        Assert.IsTrue(contexts > 0);
    }

    [TestMethod]
    public async Task DeepSeek_ProfileDisabledThinkingMatchesRequestCapabilitiesAndRestoresNativeDefaults()
    {
        var handler = new CaptureHandler("DeepSeek");
        using var client = new HttpClient(handler);
        var service = new DeepSeekService("offline", client) { ThinkingEnabled = true };
        var request = service.CreateRequest("question").WithProfile(RequestProfiles.Summarization);
        var caps = request.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, caps.Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.TopP);
        Assert.IsTrue(service.ThinkingEnabled);
        Assert.AreEqual(CapabilitySupport.Unsupported, service.GetCapabilities().Temperature);
        await request.GetCompletionAsync();
        Assert.AreEqual("disabled", handler.Body!["thinking"]!["type"]!.GetValue<string>());
        Assert.IsTrue(handler.Body.ContainsKey("temperature"));
        Assert.IsFalse(handler.Body.ContainsKey("top_p"));
    }

    [TestMethod]
    [DataRow("Google", "gemini-3.99-flash")]
    [DataRow("xAI", "grok-4.99")]
    [DataRow("DeepSeek", "deepseek-future")]
    public void FutureModelNames_AreUnknownWithoutLosingDefinitiveAdapterRestrictions(string provider, string model)
    {
        using var client = new HttpClient(new CaptureHandler(provider));
        var caps = Create(provider, model, client).GetCapabilities();
        Assert.AreEqual(model, caps.Model);
        Assert.AreEqual(CapabilitySupport.Unknown, caps.Streaming);
        Assert.AreEqual(CapabilitySupport.Unknown, caps.FunctionCalling);
        Assert.AreEqual(CapabilitySupport.Unknown, caps.NativeReasoning);
        Assert.AreEqual(0, caps.ReasoningLevels.Count);
        Assert.AreEqual(0, caps.NativeReasoningLevels.Count);
        Assert.IsNull(caps.MaxOutputTokens);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.AsyncFunctionCalling);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.Steering);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.ReasoningCachePreservation);
    }

    [TestMethod]
    [DataRow("default")]
    [DataRow("override")]
    [DataRow("preset")]
    [DataRow("profile")]
    [DataRow("models")]
    [DataRow("models-override")]
    [DataRow("custom")]
    public async Task Perplexity_CapabilitiesFollowCapturedSerializedSelector(string selector)
    {
        var handler = new CaptureHandler("Perplexity");
        using var client = new HttpClient(handler);
        var options = new PerplexityAgentOptions { DisableWebSearch = true };
        if (selector == "override" || selector == "models-override") options.ModelOverride = AIModels.Perplexity.Grok4_6;
        if (selector == "preset") options.Preset = PerplexityPreset.High;
        if (selector == "profile") options.Profile = new PerplexityProfile { Id = "profile", Version = "1" };
        if (selector.StartsWith("models")) options.Models = new[] { AIModels.Perplexity.Grok4_6 };
        if (selector == "custom") options.ModelOverride = "vendor/future-model";
        var service = new PerplexityService("offline", client).WithPerplexityOptions(options);
        var direct = service.GetCapabilities();
        var request = service.CreateRequest("question");
        service.AgentOptions.ModelOverride = AIModels.Perplexity.Sonar;
        service.AgentOptions.Models = null;
        service.AgentOptions.Profile = null;
        service.AgentOptions.Preset = null;
        var captured = request.GetCapabilities();
        Assert.AreEqual(direct.Model, captured.Model);
        Assert.AreEqual(direct.Streaming, captured.Streaming);
        Assert.AreEqual(0, handler.Calls);
        var known = selector == "default" || selector == "override";
        Assert.AreEqual(known ? CapabilitySupport.Supported : CapabilitySupport.Unknown, captured.Streaming);
        Assert.AreEqual(selector == "default" ? CapabilitySupport.Unsupported : CapabilitySupport.Unknown, captured.Reasoning);
        Assert.AreEqual(CapabilitySupport.Unsupported, captured.FileSearch);
        await request.GetCompletionAsync();
        Assert.AreEqual(handler.Body!["model"]?.GetValue<string>(), captured.Model);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task Perplexity_InternalProfileSuppressesCapturedRoutingOptionsBeforeInspection()
    {
        var handler = new CaptureHandler("Perplexity");
        using var client = new HttpClient(handler);
        var service = new PerplexityService("offline", client).WithPerplexityOptions(new PerplexityAgentOptions
        {
            Profile = new PerplexityProfile { Id = "remote-profile" }, ModelOverride = AIModels.Perplexity.Grok4_6
        });
        var request = service.CreateRequest("question").WithProfile(RequestProfiles.Summarization);
        var caps = request.GetCapabilities();
        Assert.AreEqual(AIModels.Perplexity.Sonar, caps.Model);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.Reasoning);
        await request.GetCompletionAsync();
        Assert.AreEqual(caps.Model, handler.Body!["model"]!.GetValue<string>());
        Assert.IsFalse(handler.Body.ContainsKey("profile"));
        Assert.AreEqual(AIModels.Perplexity.Grok4_6, service.GetCapabilities().Model);
    }

    [TestMethod]
    public async Task Qwen_CommonReasoningUnsupported_NativeThinkingAndOverrideSnapshotsRemainIndependent()
    {
        var handler = new CaptureHandler("Qwen");
        using var client = new HttpClient(handler);
        var service = new QwenService("offline", AlibabaModels.Qwen3_8B, client) { ThinkingMode = QwenThinking.On };
        var original = service.CreateRequest("question");
        var caps = original.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.Reasoning);
        Assert.AreEqual(CapabilitySupport.Supported, caps.NativeReasoning);
        Assert.AreEqual(CapabilitySupport.Supported, caps.ThinkingToggle);
        service.ModelIdOverride = "deployment/custom";
        Assert.AreEqual(CapabilitySupport.Unknown, service.GetCapabilities().NativeReasoning);
        Assert.AreEqual("deployment/custom", service.GetCapabilities().Model);
        Assert.AreEqual(AlibabaModels.Qwen3_8B, original.GetCapabilities().Model);
        await original.GetCompletionAsync();
        Assert.IsTrue(handler.Body!["enable_thinking"]!.GetValue<bool>());
        Assert.AreEqual(caps.Model, handler.Body["model"]!.GetValue<string>());
        await Assert.ThrowsAsync<NotSupportedException>(() => original.WithReasoning(ReasoningLevel.Auto).GetCompletionAsync());
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow(EndpointPlatform.DashScope)]
    [DataRow(EndpointPlatform.Ollama)]
    [DataRow(EndpointPlatform.Vllm)]
    public void Qwen_CustomEndpointsDoNotInferRemoteCapabilitiesFromCatalogLikeServedNames(EndpointPlatform platform)
    {
        using var client = new HttpClient(new CaptureHandler("Qwen"));
        var service = new QwenService("http://localhost:9000", platform, AlibabaModels.Qwen3_8B, client);
        var caps = service.GetCapabilities();
        Assert.AreEqual(platform == EndpointPlatform.Ollama ? "qwen3:8b" : AlibabaModels.Qwen3_8B, caps.Model);
        Assert.AreEqual(CapabilitySupport.Unknown, caps.Streaming);
        Assert.AreEqual(CapabilitySupport.Unknown, caps.NativeReasoning);
        Assert.AreEqual(platform == EndpointPlatform.Ollama ? CapabilitySupport.Unsupported : CapabilitySupport.Unknown, caps.ThinkingToggle);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.Reasoning);
    }

    [TestMethod]
    [DataRow("Google")]
    [DataRow("xAI")]
    [DataRow("DeepSeek")]
    public async Task InspectionDoesNotValidateInvalidNativeDefaults(string provider)
    {
        var handler = new CaptureHandler(provider);
        using var client = new HttpClient(handler);
        AIService service = provider switch
        {
            "Google" => new GoogleAIService("offline", AIModels.Google.Gemini3_8Flash, client) { ThinkingLevel = (GeminiThinkingLevel)999 },
            "xAI" => new XAIService("offline", AIModels.xAI.Grok4_6, client) { ReasoningEffort = (GrokReasoning)999 },
            "DeepSeek" => new DeepSeekService("offline", client) { ReasoningEffort = (DeepSeekReasoning)999 },
            _ => throw new ArgumentException(provider)
        };
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().NativeReasoning);
        Assert.AreEqual(0, handler.Calls);
        var request = service.CreateRequest("question");
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().NativeReasoning);
        if (provider == "xAI")
            await Assert.ThrowsAsync<NotSupportedException>(() => request.GetCompletionAsync());
        else
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => request.GetCompletionAsync());
        Assert.AreEqual(0, handler.Calls);
    }

    private static IEnumerable<string> Catalog(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue()!);

    private static AIService Create(string provider, string model, HttpClient client) => provider switch
    {
        "Google" => new GoogleAIService("offline", model, client),
        "xAI" => new XAIService("offline", model, client),
        "DeepSeek" => new DeepSeekService("offline", model, client),
        _ => throw new ArgumentException(provider)
    };

    private sealed class CaptureHandler(string provider) : HttpMessageHandler
    {
        public JsonObject? Body { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var response = provider switch
            {
                "Google" => "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"answer\"}]},\"finishReason\":\"STOP\"}]}",
                "Perplexity" => "{\"id\":\"response\",\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"answer\"}]}]}",
                _ => "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
