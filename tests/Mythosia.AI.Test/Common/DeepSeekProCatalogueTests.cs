using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.DeepSeek;
using System.Reflection;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class DeepSeekProCatalogueTests
{
    [TestMethod]
    public void Catalogue_OffersBothCurrentModelsWithProThinkingControls()
    {
        var field = typeof(AIModels.DeepSeek).GetField(nameof(AIModels.DeepSeek.V4Pro));
        Assert.IsNotNull(field);
        Assert.AreEqual("deepseek-v4-pro", field.GetRawConstantValue());
        Assert.IsNull(field.GetCustomAttribute<ObsoleteAttribute>());
        Assert.AreEqual(AIModels.DeepSeek.V4Pro, ChatUiModelHelpers.FindModelValueByName("V4Pro"));
        Assert.AreEqual(AIModels.DeepSeek.V4Pro, ChatUiModelHelpers.FindModelValueByName("DEEPSEEK-V4-PRO"));
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var models = catalogue.EnumerateArray().Single(group => group.GetProperty("provider").GetString() == "DeepSeek")
            .GetProperty("models").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(new[] { "Flash", "V4Pro" },
            models.Select(model => model.GetProperty("name").GetString()).ToArray());
        var pro = models.Single(model => model.GetProperty("name").GetString() == "V4Pro");
        Assert.AreEqual(AIModels.DeepSeek.V4Pro, pro.GetProperty("description").GetString());
        Assert.AreEqual(393216u, pro.GetProperty("maxOutputTokens").GetUInt32());
        Assert.AreEqual("deepseek_thinking", pro.GetProperty("reasoning").GetProperty("type").GetString());
        CollectionAssert.AreEqual(new[] { "Auto", "Low", "High", "Max" },
            pro.GetProperty("reasoning").GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow(AIModels.DeepSeek.Flash, false)]
    [DataRow(AIModels.DeepSeek.Flash, true)]
    [DataRow(AIModels.DeepSeek.V4Pro, false)]
    [DataRow(AIModels.DeepSeek.V4Pro, true)]
    [DataRow("DEEPSEEK-V4-PRO", true)]
    public void Capabilities_CurrentModelsExposeThinkingAndSamplingButProIsTextOnly(string model, bool thinking)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new DeepSeekService("offline-test-key", model, client) { ThinkingEnabled = thinking };
        var capabilities = service.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Streaming);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.FunctionCalling);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Reasoning);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.ThinkingToggle);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.StructuredOutput);
        Assert.AreEqual(model.Equals(AIModels.DeepSeek.Flash, StringComparison.OrdinalIgnoreCase)
            ? CapabilitySupport.Supported : CapabilitySupport.Unsupported, capabilities.ImageInput);
        Assert.AreEqual(thinking ? CapabilitySupport.Unsupported : CapabilitySupport.Supported, capabilities.Temperature);
        Assert.AreEqual(thinking ? CapabilitySupport.Supported : CapabilitySupport.Unsupported, capabilities.TopP);
        Assert.AreEqual(393216u, capabilities.MaxOutputTokens);
        CollectionAssert.AreEqual(new[] { ReasoningLevel.Auto, ReasoningLevel.Low, ReasoningLevel.High, ReasoningLevel.Max },
            capabilities.NativeReasoningLevels.ToArray());
    }

    [TestMethod]
    [DataRow("deepseek-v4-pro-experimental")]
    [DataRow("deepseek-v4-pro-0813")]
    [DataRow("deepseek-custom-deployment")]
    [DataRow("deepseek-chat")]
    [DataRow("deepseek-reasoner")]
    [DataRow("deepseek-v4-flash")]
    public void UnknownAndRetiredModels_PreserveRequestedIdWithoutAdvertisingKnownCapabilities(string model)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new DeepSeekService("offline-test-key", model, client);
        var capabilities = service.GetCapabilities();
        Assert.AreEqual(model, service.Model);
        Assert.AreEqual(model, capabilities.Model);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Reasoning);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.ImageInput);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.FunctionCalling);
        Assert.IsNull(capabilities.MaxOutputTokens);
        Assert.IsEmpty(capabilities.NativeReasoningLevels);
    }

    [TestMethod]
    [DataRow("DeepSeekChat", AIModels.DeepSeek.Flash)]
    [DataRow(" deepseekchat ", AIModels.DeepSeek.Flash)]
    [DataRow("Flash", AIModels.DeepSeek.Flash)]
    [DataRow("V4Pro", AIModels.DeepSeek.V4Pro)]
    [DataRow("deepseek-v4-pro", AIModels.DeepSeek.V4Pro)]
    [DataRow("deepseek-chat", "deepseek-chat")]
    [DataRow("deepseek-custom-deployment", "deepseek-custom-deployment")]
    [DataRow(" custom/model-name ", "custom/model-name")]
    public void RewriterResolution_MigratesOnlyTheFormerUiLabelAndPreservesCustomModels(string saved, string expected)
    {
        Assert.AreEqual(expected, ChatUiModelHelpers.ResolveRewriterModelValue(saved));
    }

    [TestMethod]
    public void LegacyRewriterLabel_UsesCurrentDeepSeekServiceAndSharesItsCache()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var fallback = new DeepSeekService("offline-fallback", "deepseek-custom", client);
        var state = new ChatUiRagEndpointState { RewriterApiKey = "offline-test-key" };
        var actual = state.GetOrCreateRewriterService("DeepSeekChat", fallback);
        Assert.IsInstanceOfType<DeepSeekService>(actual);
        Assert.AreNotSame(fallback, actual);
        Assert.AreEqual(AIModels.DeepSeek.Flash, actual.Model);
        Assert.AreSame(state.GetOrCreateRewriterService("Flash", fallback), actual);
        Assert.AreEqual("deepseek-custom", fallback.Model);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("Capability and catalogue inspection must never send HTTP requests.");
    }
}
