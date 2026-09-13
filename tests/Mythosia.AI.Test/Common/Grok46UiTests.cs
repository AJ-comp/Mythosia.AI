using Mythosia.AI.Models;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class Grok46UiTests
{
    [TestMethod]
    public void Catalogue_OffersAllSupportedEffortsAndContextCeiling()
    {
        Assert.AreEqual(AIModels.xAI.Grok4_6,
            ChatUiModelHelpers.FindModelValueByName(nameof(AIModels.xAI.Grok4_6)));
        Assert.AreEqual(AIModels.xAI.Grok4_6, ChatUiModelHelpers.FindModelValueByName("GROK-4.6"));
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var group = catalogue.EnumerateArray().Single(g => g.GetProperty("provider").GetString() == "xAI");
        var entry = group.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == nameof(AIModels.xAI.Grok4_6));
        Assert.AreEqual(AIModels.xAI.Grok4_6, entry.GetProperty("description").GetString());
        Assert.AreEqual(500000u, entry.GetProperty("maxOutputTokens").GetUInt32());
        var reasoning = entry.GetProperty("reasoning");
        Assert.AreEqual("grok_always", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(new[] { "Auto", "Low", "Medium", "High", "XHigh" },
            reasoning.GetProperty("levels").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.IsTrue(entry.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsTrue(entry.GetProperty("sampling").GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    [DataRow(true, "Auto", null)]
    [DataRow(true, "Low", "low")]
    [DataRow(true, "Medium", "medium")]
    [DataRow(true, "High", "high")]
    [DataRow(true, "XHigh", "xhigh")]
    [DataRow(false, "XHigh", "low")]
    public async Task Settings_ReachRequestStateAndCopiedCode(bool enabled, string level, string? wireLevel)
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new XAIService("offline-key", AIModels.xAI.Grok4_6, client)
        {
            ReasoningEffort = GrokReasoning.High,
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.5f
        };
        ChatUiSettingsHelpers.ApplyReasoningSettings(service, new SettingsRequest(
            Temperature: null, TopP: null, MaxTokens: null,
            FrequencyPenalty: null, PresencePenalty: null,
            StatelessMode: null, SystemMessage: null,
            ReasoningEnabled: enabled, ReasoningLevel: level, ReasoningType: "grok_always"));

        Assert.AreEqual("ok", await service.GetCompletionAsync("hello"));
        var request = JsonSerializer.Deserialize<JsonElement>(handler.Body!);
        Assert.AreEqual(AIModels.xAI.Grok4_6, request.GetProperty("model").GetString());
        if (wireLevel == null)
            Assert.IsFalse(request.TryGetProperty("reasoning_effort", out _));
        else
            Assert.AreEqual(wireLevel, request.GetProperty("reasoning_effort").GetString());
        Assert.IsFalse(request.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(request.TryGetProperty("presence_penalty", out _));
        Assert.IsFalse(request.TryGetProperty("stop", out _));

        var state = JsonSerializer.SerializeToElement(ChatUiSettingsHelpers.GetReasoningState(service));
        Assert.AreEqual("grok_always", state.GetProperty("type").GetString());
        Assert.IsTrue(state.GetProperty("alwaysOn").GetBoolean());
        Assert.AreEqual(service.ReasoningEffort.ToString(), state.GetProperty("effort").GetString());
        Assert.AreEqual("High", state.GetProperty("defaultEffort").GetString());
        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(service, "xAI", nameof(AIModels.xAI.Grok4_6), "hello");
        StringAssert.Contains(snippet, "service.ChangeModel(\"grok-4.6\");");
        StringAssert.Contains(snippet, $"service.ReasoningEffort = GrokReasoning.{service.ReasoningEffort};");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
