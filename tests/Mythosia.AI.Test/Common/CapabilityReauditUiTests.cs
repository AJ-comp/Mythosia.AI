using Mythosia.AI.Models;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.DeepSeek;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class CapabilityReauditUiTests
{
    [TestMethod]
    [DataRow(EndpointPlatform.Ollama)]
    [DataRow(EndpointPlatform.Vllm)]
    [DataRow(EndpointPlatform.DashScope)]
    public void ConnectedCustomQwenControlsOverrideCloudCatalogue(EndpointPlatform platform)
    {
        using var client = new HttpClient(new NoNetwork());
        var cloud = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetReasoningLevels(AlibabaModels.Qwen3_8B));
        Assert.AreEqual("qwen_thinking", cloud.GetProperty("type").GetString());
        var local = new QwenService("http://localhost:11434", platform, AlibabaModels.Qwen3_8B, client)
            { ModelIdOverride = "private-deployment" };
        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(local));
        Assert.AreEqual(JsonValueKind.Null, controls.GetProperty("reasoning").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, controls.GetProperty("maxOutputTokens").ValueKind);
        Assert.IsFalse(controls.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(controls.GetProperty("sampling").GetProperty("topP").GetBoolean());
        Assert.AreEqual("Unknown", controls.GetProperty("sampling").GetProperty("temperatureSupport").GetString());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CurrentDeepSeekThinkingControlsDetermineSampling(bool thinking)
    {
        using var client = new HttpClient(new NoNetwork());
        var service = new DeepSeekService("offline", client) { ThinkingEnabled = thinking };
        var controls = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(service));
        Assert.AreEqual(!thinking, controls.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.AreEqual(thinking, controls.GetProperty("sampling").GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    public void EnablingClaudeThinkingUpdatesTemperatureAvailability()
    {
        using var client = new HttpClient(new NoNetwork());
        var service = new AnthropicService("offline", AIModels.Anthropic.ClaudeSonnet4_6, client);
        var before = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(service));
        Assert.IsTrue(before.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        service.ThinkingBudget = 4096;
        var after = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetModelControls(service));
        Assert.IsFalse(after.GetProperty("sampling").GetProperty("temperature").GetBoolean());
        Assert.IsFalse(after.GetProperty("sampling").GetProperty("topP").GetBoolean());
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("Inspecting UI controls must not call a provider.");
    }
}
