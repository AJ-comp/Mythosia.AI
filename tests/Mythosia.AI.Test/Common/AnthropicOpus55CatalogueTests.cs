using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Anthropic;
using System.Reflection;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicOpus55CatalogueTests
{
    [TestMethod]
    public void Catalogue_RegistersFixedIdWithAlwaysOnControlsAndMediumDefault()
    {
        var field = typeof(AIModels.Anthropic).GetField(nameof(AIModels.Anthropic.ClaudeOpus5_5),
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(field);
        Assert.AreEqual("claude-opus-5-5", field.GetRawConstantValue());
        Assert.AreEqual(AIModels.Anthropic.ClaudeOpus5_5,
            ChatUiModelHelpers.FindModelValueByName(nameof(AIModels.Anthropic.ClaudeOpus5_5)));
        Assert.AreEqual(AIModels.Anthropic.ClaudeOpus5_5,
            ChatUiModelHelpers.FindModelValueByName("claude-opus-5-5"));

        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var entry = catalogue.EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "Anthropic")
            .GetProperty("models").EnumerateArray()
            .Single(model => model.GetProperty("name").GetString() == nameof(AIModels.Anthropic.ClaudeOpus5_5));
        Assert.AreEqual("claude-opus-5-5", entry.GetProperty("description").GetString());
        Assert.AreEqual(128000u, entry.GetProperty("maxOutputTokens").GetUInt32());
        var reasoning = entry.GetProperty("reasoning");
        Assert.AreEqual("claude_always", reasoning.GetProperty("type").GetString());
        Assert.AreEqual("Medium", reasoning.GetProperty("defaultLevel").GetString());
        CollectionAssert.AreEqual(new[] { "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
        Assert.IsFalse(entry.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(entry.GetProperty("sampling").GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    public void Capabilities_ExposeSupportedReasoningWithoutThinkingToggleOrManualBudget()
    {
        using var client = new HttpClient();
        var service = new AnthropicService("offline-test-key", AIModels.Anthropic.ClaudeOpus5_5, client);
        var capabilities = service.GetCapabilities();
        var levels = new[] { ReasoningLevel.Auto, ReasoningLevel.Low, ReasoningLevel.Medium,
            ReasoningLevel.High, ReasoningLevel.XHigh, ReasoningLevel.Max };
        CollectionAssert.AreEqual(levels, capabilities.ReasoningLevels.ToArray());
        CollectionAssert.AreEqual(levels, capabilities.NativeReasoningLevels.ToArray());
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.GetReasoningSupport(ReasoningLevel.None));
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.GetReasoningSupport(ReasoningLevel.Minimal));
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.ThinkingToggle);
        Assert.IsEmpty(capabilities.ThinkingBudgetPresets);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.FunctionCalling);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Streaming);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.WebSearch);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.ReasoningCachePreservation);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.AsyncFunctionCalling);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Steering);
        Assert.AreEqual(128000u, capabilities.MaxOutputTokens);
    }

    [TestMethod]
    [DataRow("claude-opus-5-5-20260922")]
    [DataRow("claude-opus-5-5-2026-09-22")]
    [DataRow("claude-opus-5-5-experimental")]
    public void UnpublishedVariants_DoNotAcquireKnownModelCapabilities(string model)
    {
        using var client = new HttpClient();
        var service = new AnthropicService("offline-test-key", model, client);
        var capabilities = service.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Reasoning);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.WebSearch);
        Assert.IsEmpty(capabilities.ReasoningLevels);
        Assert.IsNull(capabilities.MaxOutputTokens);
        Assert.IsNull(ChatUiModelHelpers.FindModelValueByName(model));
    }

    [TestMethod]
    public void ExplicitUiDisable_UsesLowAndOmittedWithoutChangingProviderDefaults()
    {
        using var defaultClient = new HttpClient();
        var defaultService = new AnthropicService("offline-test-key", defaultClient);
        Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet4_6, defaultService.Model);

        using var client = new HttpClient();
        var service = new AnthropicService("offline-test-key", AIModels.Anthropic.ClaudeOpus5_5, client);
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, new SettingsRequest(
            Temperature: null, TopP: null, MaxTokens: null, FrequencyPenalty: null,
            PresencePenalty: null, StatelessMode: null, SystemMessage: null,
            ReasoningEnabled: null, ReasoningLevel: null, ReasoningType: null));
        Assert.AreEqual(ClaudeReasoningEffort.Auto, service.AdaptiveThinkingEffort);
        Assert.AreEqual(-1, service.ThinkingBudget);

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, new SettingsRequest(
            Temperature: null, TopP: null, MaxTokens: null, FrequencyPenalty: null,
            PresencePenalty: null, StatelessMode: null, SystemMessage: null,
            ReasoningEnabled: false, ReasoningLevel: null, ReasoningType: "claude_always"));
        Assert.AreEqual(ClaudeReasoningEffort.Low, service.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingDisplay.Omitted, service.AdaptiveThinkingDisplay);
        Assert.AreEqual(AIModels.Anthropic.ClaudeOpus5_5, service.Model);
    }
}
