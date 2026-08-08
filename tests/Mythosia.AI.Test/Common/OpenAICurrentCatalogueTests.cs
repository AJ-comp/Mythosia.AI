using Mythosia.AI.Models;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Base;
using System.Reflection;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAICurrentCatalogueTests
{
    private static readonly string[] RetiredModelIds =
    {
        "gpt-4-vision-preview",
        "chatgpt-4o-latest",
        "gpt-5-chat-latest",
        "gpt-5.2-codex",
        "gpt-5-2025-08-07",
        "gpt-5-mini-2025-08-07",
        "gpt-5-nano-2025-08-07",
        "gpt-4.1-nano"
    };

    [TestMethod]
    public void OpenAiModelConstants_DoNotExposeRetiredModels()
    {
        var fields = typeof(AIModels.OpenAI).GetFields(BindingFlags.Public | BindingFlags.Static);
        var names = fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        var values = fields
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var retiredName in new[]
        {
            "Gpt4Vision",
            "Gpt4oLatest",
            "Gpt5ChatLatest",
            "Gpt5_2Codex",
            "Gpt5_250807",
            "Gpt5Mini_250807",
            "Gpt5Nano_250807",
            "Gpt4_1Nano"
        })
            Assert.IsFalse(names.Contains(retiredName), $"Retired model constant {retiredName} must not be public.");

        foreach (var retiredId in RetiredModelIds)
            Assert.IsFalse(values.Contains(retiredId), $"Retired model ID {retiredId} must not be public.");
    }

    [TestMethod]
    public void ChatUiCatalogue_ContainsGpt5_4MiniAndNano_WithoutRetiredModels()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var openAiModels = catalogue
            .EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "OpenAI")
            .GetProperty("models")
            .EnumerateArray()
            .ToArray();
        var names = openAiModels
            .Select(model => model.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var ids = openAiModels
            .Select(model => model.GetProperty("description").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsTrue(names.Contains(nameof(AIModels.OpenAI.Gpt5_4Mini)));
        Assert.IsTrue(names.Contains(nameof(AIModels.OpenAI.Gpt5_4Nano)));
        Assert.IsFalse(names.Contains(nameof(AIModels.OpenAI.GptImage2)));
        Assert.IsFalse(names.Contains(nameof(AIModels.OpenAI.GptImage2_260421)));
        foreach (var retiredId in RetiredModelIds)
            Assert.IsFalse(ids.Contains(retiredId), $"Chat UI must not expose retired model ID {retiredId}.");
    }

    [TestMethod]
    public void OpenAiModelConstants_ExposeCurrentGptImage2Models()
    {
        var fields = typeof(AIModels.OpenAI).GetFields(BindingFlags.Public | BindingFlags.Static);
        var values = fields.ToDictionary(
            field => field.Name,
            field => (string)field.GetRawConstantValue()!,
            StringComparer.Ordinal);

        Assert.AreEqual("gpt-image-2", values[nameof(AIModels.OpenAI.GptImage2)]);
        Assert.AreEqual("gpt-image-2-2026-04-21", values[nameof(AIModels.OpenAI.GptImage2_260421)]);
    }

    [TestMethod]
    public void QuickAskWithImage_DefaultsToGpt4_1()
    {
        var method = typeof(AIService)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate =>
                candidate.Name == nameof(AIService.QuickAskWithImageAsync) &&
                candidate.GetParameters().Length == 4);
        var modelParameter = method.GetParameters().Single(parameter => parameter.Name == "model");

        Assert.AreEqual(AIModels.OpenAI.Gpt4_1, modelParameter.DefaultValue);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5_6)]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol)]
    [DataRow(AIModels.OpenAI.O3)]
    [DataRow(AIModels.OpenAI.Gpt4_1)]
    public void ResponsesModels_DoNotAdvertiseUnsupportedSamplingControls(string model)
    {
        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetSamplingControls(model));

        Assert.IsFalse(controls.GetProperty("temperature").GetBoolean());
        Assert.IsFalse(controls.GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    public void LegacyGpt4o_AdvertisesSupportedSamplingControls()
    {
        var controls = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetSamplingControls(AIModels.OpenAI.Gpt4o));

        Assert.IsTrue(controls.GetProperty("temperature").GetBoolean());
        Assert.IsTrue(controls.GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    public void Gpt5Pro_AdvertisesIts272KOutputLimit()
    {
        Assert.AreEqual(272000u, ChatUiModelHelpers.GetDefaultMaxOutputTokens(AIModels.OpenAI.Gpt5Pro));
    }
}
