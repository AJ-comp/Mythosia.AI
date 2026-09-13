using Mythosia.AI.Models;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.DeepSeek;
using System.Reflection;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ModelRetirementCatalogueTests
{
    [TestMethod]
    [DataRow(typeof(AIModels.OpenAI), "Gpt5", "gpt-5", "Gpt5_6Sol")]
    [DataRow(typeof(AIModels.OpenAI), "Gpt5Mini", "gpt-5-mini", "Gpt5_6Terra")]
    [DataRow(typeof(AIModels.OpenAI), "Gpt5Nano", "gpt-5-nano", "Gpt5_6Luna")]
    [DataRow(typeof(AIModels.OpenAI), "Gpt5Pro", "gpt-5-pro", "Gpt5_6Sol")]
    [DataRow(typeof(AIModels.OpenAI), "O3", "o3", "Gpt5_6Sol")]
    [DataRow(typeof(AIModels.OpenAI), "O3Pro", "o3-pro", "Gpt5_6Sol")]
    [DataRow(typeof(AIModels.DeepSeek), "Chat", "deepseek-chat", "Flash")]
    [DataRow(typeof(AIModels.DeepSeek), "Reasoner", "deepseek-reasoner", "Flash")]
    [DataRow(typeof(AIModels.DeepSeek), "V4Flash", "deepseek-v4-flash", "Flash")]
    public void DeprecatedConstants_PreserveWireValuesAndWarnWithoutBreakingCompilation(
        Type provider, string name, string wireId, string replacement)
    {
        var field = provider.GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(field);
        Assert.IsTrue(field.IsLiteral);
        Assert.AreEqual(wireId, field.GetRawConstantValue());
        var warning = field.GetCustomAttribute<ObsoleteAttribute>();
        Assert.IsNotNull(warning);
        Assert.IsFalse(warning.IsError);
        StringAssert.Contains(warning.Message!, replacement);

        Assert.IsNull(ChatUiModelHelpers.FindModelValueByName(name));
        Assert.IsNull(ChatUiModelHelpers.FindModelValueByName(wireId));
    }

    [TestMethod]
    public void ChatUiCatalogue_ExcludesDeprecatedConstantsAndKeepsSupportedOlderModels()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var ids = catalogue.EnumerateArray()
            .SelectMany(group => group.GetProperty("models").EnumerateArray())
            .Select(model => model.GetProperty("description").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deprecatedIds = typeof(AIModels).GetNestedTypes(BindingFlags.Public)
            .SelectMany(provider => provider.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) &&
                            field.IsDefined(typeof(ObsoleteAttribute)))
            .Select(field => (string)field.GetRawConstantValue()!);
        foreach (var id in deprecatedIds)
            Assert.IsFalse(ids.Contains(id), $"Deprecated ID {id} must not be offered for new sessions.");

        foreach (var id in new[]
        {
            AIModels.DeepSeek.Flash,
            AIModels.OpenAI.Gpt5_6Sol,
            AIModels.OpenAI.Gpt4_1,
            AIModels.OpenAI.Gpt4o,
            AIModels.Anthropic.ClaudeHaiku4_5_251001,
            AIModels.Google.Gemini2_5Flash,
            AIModels.Perplexity.Sonar
        })
            Assert.IsTrue(ids.Contains(id), $"Supported ID {id} should remain selectable.");
    }

    [TestMethod]
    public void DeepSeekThinkingToggle_ChangesServerModeWithoutChangingModel()
    {
        using var client = new HttpClient();
        var service = new DeepSeekService("test-key", client);
        var control = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetReasoningLevels(AIModels.DeepSeek.Flash));
        Assert.AreEqual("deepseek_thinking", control.GetProperty("type").GetString());
        CollectionAssert.AreEqual(new[] { "Auto", "Low", "High", "Max" },
            control.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
        Assert.IsFalse(service.ThinkingEnabled);

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, Settings(true));
        Assert.IsTrue(service.ThinkingEnabled);
        Assert.AreEqual(AIModels.DeepSeek.Flash, service.Model);
        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));
        Assert.IsTrue(state.GetProperty("enabled").GetBoolean());
        StringAssert.Contains(ChatUiUtilityHelpers.GenerateCodeSnippet(
            service, "DeepSeek", nameof(AIModels.DeepSeek.Flash), "Hello"),
            "service.ThinkingEnabled = true;");

        ChatUiSettingsHelpers.ApplyReasoningSettings(service, Settings(false));
        Assert.IsFalse(service.ThinkingEnabled);
        StringAssert.Contains(ChatUiUtilityHelpers.GenerateCodeSnippet(
            service, "DeepSeek", nameof(AIModels.DeepSeek.Flash), "Hello"),
            "service.ThinkingEnabled = false;");
    }

    private static SettingsRequest Settings(bool enabled)
        => new(Temperature: null, TopP: null, MaxTokens: null,
            FrequencyPenalty: null, PresencePenalty: null,
            StatelessMode: null, SystemMessage: null,
            ReasoningEnabled: enabled, ReasoningLevel: null,
            ReasoningType: "deepseek_thinking");
}
