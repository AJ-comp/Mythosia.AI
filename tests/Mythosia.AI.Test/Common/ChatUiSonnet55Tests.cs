using Mythosia.AI.Models;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ChatUiSonnet55Tests
{
    [TestMethod]
    public void Catalogue_ExposesFixedIdWithOptionalAdaptiveHighAndNoSamplingOrFast()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var model = catalogue.EnumerateArray().Single(group => group.GetProperty("provider").GetString() == "Anthropic")
            .GetProperty("models").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == nameof(AIModels.Anthropic.ClaudeSonnet5_5));
        Assert.AreEqual("claude-sonnet-5-5", model.GetProperty("description").GetString());
        Assert.AreEqual(128000, model.GetProperty("maxOutputTokens").GetInt32());
        var reasoning = model.GetProperty("reasoning");
        Assert.AreEqual("claude_adaptive", reasoning.GetProperty("type").GetString());
        Assert.AreEqual("High", reasoning.GetProperty("defaultLevel").GetString());
        Assert.IsTrue(reasoning.GetProperty("defaultEnabled").GetBoolean());
        CollectionAssert.AreEqual(new[] { "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
        Assert.IsFalse(model.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(model.GetProperty("sampling").GetProperty("topP").GetBoolean());
        Assert.AreEqual("Unsupported", model.GetProperty("speed").GetProperty("fast").GetString());
        Assert.AreEqual("Unsupported", model.GetProperty("capabilities").GetProperty("asyncFunctionCalling").GetString());
        Assert.AreEqual("Unsupported", model.GetProperty("capabilities").GetProperty("steering").GetString());
        Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet5_5,
            ChatUiModelHelpers.FindModelValueByName("  ClaudeSonnet5_5  "));
        Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet5_5,
            ChatUiModelHelpers.FindModelValueByName("claude-sonnet-5-5"));
        Assert.IsNull(ChatUiModelHelpers.FindModelValueByName("claude-sonnet-5-5-latest"));
    }

    [TestMethod]
    public async Task UnchangedSettings_PreserveAdaptiveHighAndSnippetWithoutChangingProviderDefault()
    {
        using var fixture = new Fixture();
        ChatUiSettingsHelpers.ApplyReasoningSettings(fixture.Service, Settings(null, null));
        await fixture.Service.GetCompletionAsync("default settings");
        AssertRequest(fixture.Requests.Single(), "adaptive", "high");
        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(fixture.Service, "Anthropic", AIModels.Anthropic.ClaudeSonnet5_5, "hello");
        StringAssert.Contains(snippet, "service.ThinkingMode = ClaudeThinkingMode.Auto;");
        Assert.IsFalse(snippet.Contains("service.ThinkingBudget =", StringComparison.Ordinal));
        Assert.IsFalse(snippet.Contains("service.Temperature =", StringComparison.Ordinal));
        Assert.IsFalse(snippet.Contains("service.TopP =", StringComparison.Ordinal));
        using var client = new HttpClient();
        var defaultService = new AnthropicService("offline-test-key", client);
        Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet4_6, defaultService.Model);
    }

    [TestMethod]
    [DataRow("Low", "low")]
    [DataRow("Medium", "medium")]
    [DataRow("High", "high")]
    [DataRow("XHigh", "xhigh")]
    [DataRow("Max", "max")]
    public async Task EnabledSettings_SendSelectedNativeEffortWithoutManualBudgets(string level, string wire)
    {
        using var fixture = new Fixture();
        ChatUiSettingsHelpers.ApplyReasoningSettings(fixture.Service, Settings(true, level));
        await fixture.Service.GetCompletionAsync("selected effort");
        AssertRequest(fixture.Requests.Single(), "adaptive", wire);
    }

    [TestMethod]
    public async Task DisableThenEnable_SwitchesThinkingPhaseAndKeepsSamplingUnavailable()
    {
        using var fixture = new Fixture();
        ChatUiSettingsHelpers.ApplyReasoningSettings(fixture.Service, Settings(true, "Max"));
        ChatUiSettingsHelpers.ApplyReasoningSettings(fixture.Service, Settings(false, null));
        await fixture.Service.GetCompletionAsync("skip upfront thinking");
        AssertRequest(fixture.Requests[0], "between_tools", "high");
        var snippet = ChatUiUtilityHelpers.GenerateCodeSnippet(fixture.Service, "Anthropic", AIModels.Anthropic.ClaudeSonnet5_5, "hello");
        StringAssert.Contains(snippet, "service.ThinkingMode = ClaudeThinkingMode.BetweenTools;");
        Assert.IsFalse(snippet.Contains("service.ThinkingBudget =", StringComparison.Ordinal));
        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(fixture.Service));
        Assert.IsFalse(controls.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(controls.GetProperty("sampling").GetProperty("topP").GetBoolean());
        ChatUiSettingsHelpers.ApplyReasoningSettings(fixture.Service, Settings(true, "XHigh"));
        await fixture.Service.GetCompletionAsync("restore upfront thinking");
        AssertRequest(fixture.Requests[1], "adaptive", "xhigh");
    }

    [TestMethod]
    [DataRow("Minimal", "claude_adaptive")]
    [DataRow("None", "claude_adaptive")]
    [DataRow("1024", "claude")]
    [DataRow("999", "claude_adaptive")]
    public void UnsupportedSettings_DoNotMutateEffortOrSendRequests(string level, string type)
    {
        using var fixture = new Fixture();
        ChatUiSettingsHelpers.ApplyReasoningSettings(fixture.Service, Settings(true, "High"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ChatUiSettingsHelpers.ApplyReasoningSettings(fixture.Service, Settings(true, level, type)));
        Assert.AreEqual(ClaudeReasoningEffort.High, fixture.Service.AdaptiveThinkingEffort);
        Assert.IsEmpty(fixture.Requests);
    }

    private static SettingsRequest Settings(bool? enabled, string? level, string type = "claude_adaptive")
        => new(Temperature: null, TopP: null, MaxTokens: null, FrequencyPenalty: null,
            PresencePenalty: null, StatelessMode: null, SystemMessage: null,
            ReasoningEnabled: enabled, ReasoningLevel: level, ReasoningType: type);

    private static void AssertRequest(JsonElement body, string thinking, string effort)
    {
        Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet5_5, body.GetProperty("model").GetString());
        Assert.AreEqual(thinking, body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.AreEqual(effort, body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.IsFalse(body.GetProperty("thinking").TryGetProperty("budget_tokens", out _));
        Assert.IsFalse(body.TryGetProperty("temperature", out _));
        Assert.IsFalse(body.TryGetProperty("top_p", out _));
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _client;
        public AnthropicService Service { get; }
        public List<JsonElement> Requests { get; } = new();

        public Fixture()
        {
            _client = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline-test-key", AIModels.Anthropic.ClaudeSonnet5_5, _client);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(document.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"msg_offline","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}
                    """, Encoding.UTF8, "application/json")
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _client.Dispose();
            base.Dispose(disposing);
        }
    }
}
