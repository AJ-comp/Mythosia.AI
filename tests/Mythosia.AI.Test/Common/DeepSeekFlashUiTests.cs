using Mythosia.AI.Models;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.DeepSeek;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class DeepSeekFlashUiTests
{
    [TestMethod]
    public void Catalogue_OffersCurrentFlashEffortsAndOutputCeiling()
    {
        Assert.AreEqual(AIModels.DeepSeek.Flash, ChatUiModelHelpers.FindModelValueByName("Flash"));
        Assert.AreEqual(AIModels.DeepSeek.Flash, ChatUiModelHelpers.FindModelValueByName("DEEPSEEK-FLASH"));
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var group = catalogue.EnumerateArray().Single(g => g.GetProperty("provider").GetString() == "DeepSeek");
        var entry = group.GetProperty("models").EnumerateArray()
            .Single(model => model.GetProperty("name").GetString() == "Flash");
        Assert.AreEqual("Flash", entry.GetProperty("name").GetString());
        Assert.AreEqual(AIModels.DeepSeek.Flash, entry.GetProperty("description").GetString());
        Assert.AreEqual(393216u, entry.GetProperty("maxOutputTokens").GetUInt32());
        CollectionAssert.AreEqual(new[] { "Auto", "Low", "High", "Max" },
            entry.GetProperty("reasoning").GetProperty("levels").EnumerateArray()
                .Select(level => level.GetString()).ToArray());
        Assert.IsTrue(entry.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(entry.GetProperty("sampling").GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    [DataRow(true, "Auto", null)]
    [DataRow(true, "Low", "low")]
    [DataRow(true, "High", "high")]
    [DataRow(true, "Max", "max")]
    [DataRow(false, "Max", null)]
    public async Task SelectedEffort_ReachesRequestStateAndCopiedCode(bool enabled, string level, string? wireLevel)
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new DeepSeekService("offline-key", client) { ReasoningEffort = DeepSeekReasoning.Max };
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, Settings(enabled, level));
        Assert.AreEqual("ok", await service.GetCompletionAsync("hello"));
        var request = JsonSerializer.Deserialize<JsonElement>(handler.Body!);
        Assert.AreEqual(AIModels.DeepSeek.Flash, request.GetProperty("model").GetString());
        Assert.AreEqual(enabled ? "enabled" : "disabled", request.GetProperty("thinking").GetProperty("type").GetString());
        if (wireLevel == null)
            Assert.IsFalse(request.TryGetProperty("reasoning_effort", out _));
        else
            Assert.AreEqual(wireLevel, request.GetProperty("reasoning_effort").GetString());
        Assert.AreEqual(!enabled, request.TryGetProperty("temperature", out _));
        Assert.AreEqual(enabled, request.TryGetProperty("top_p", out _));
        Assert.IsFalse(request.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(request.TryGetProperty("presence_penalty", out _));

        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));
        Assert.AreEqual(enabled, state.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(service.ReasoningEffort.ToString(), state.GetProperty("effort").GetString());
        Assert.AreEqual("High", state.GetProperty("defaultEffort").GetString());
        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetSamplingControls(service.Model, enabled));
        Assert.AreEqual(!enabled, controls.GetProperty("temperature").GetBoolean());
        Assert.AreEqual(enabled, controls.GetProperty("topP").GetBoolean());

        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "DeepSeek", "Flash", "hello");
        StringAssert.Contains(snippet, "service.ChangeModel(\"deepseek-flash\");");
        StringAssert.Contains(snippet, $"service.ThinkingEnabled = {enabled.ToString().ToLowerInvariant()};");
        StringAssert.Contains(snippet, $"service.ReasoningEffort = DeepSeekReasoning.{service.ReasoningEffort};");
        Assert.AreEqual(!enabled, snippet.Contains("service.Temperature =", StringComparison.Ordinal));
        Assert.AreEqual(enabled, snippet.Contains("service.TopP =", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("999")]
    [DataRow("Medium")]
    [DataRow("invalid")]
    public void InvalidEffort_DoesNotOverwriteExistingSelection(string invalidLevel)
    {
        using var client = new HttpClient();
        var service = new DeepSeekService("offline-key", client) { ReasoningEffort = DeepSeekReasoning.Low };
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, Settings(true, invalidLevel));
        Assert.IsTrue(service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.Low, service.ReasoningEffort);
    }

    private static SettingsRequest Settings(bool enabled, string level)
        => new(Temperature: null, TopP: null, MaxTokens: null,
            FrequencyPenalty: null, PresencePenalty: null, StatelessMode: null, SystemMessage: null,
            ReasoningEnabled: enabled, ReasoningLevel: level, ReasoningType: "deepseek_thinking");

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\",\"reasoning_content\":\"checked\"},\"finish_reason\":\"stop\"}]}", Encoding.UTF8, "application/json")
            };
        }
    }
}
