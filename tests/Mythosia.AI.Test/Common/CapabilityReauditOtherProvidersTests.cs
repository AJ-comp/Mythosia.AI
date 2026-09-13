using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("Capabilities")]
public class CapabilityReauditOtherProvidersTests
{
    [TestMethod]
    [DataRow(AIModels.xAI.Grok4_6, true)]
    [DataRow(AIModels.xAI.Grok4_5, false)]
    [DataRow(AIModels.xAI.Grok4_3, false)]
    [DataRow(AIModels.xAI.Grok4_20NonReasoning, false)]
    [DataRow(AIModels.xAI.GrokBuild0_1, false)]
    public async Task Grok_FunctionRequestSamplingCapabilitiesFollowActualAdapterPath(string model, bool sendsToolTopP)
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new XAIService("offline", model, client);
        service.Functions.Add(new FunctionDefinition { Name = "unused" });
        var request = service.CreateRequest("question").WithTopP(0.42f);
        var disabled = request.WithFunctionsDisabled();
        var summary = request.WithProfile(RequestProfiles.Summarization);
        service.Functions.Clear();

        var caps = request.GetCapabilities();
        Assert.AreEqual(sendsToolTopP ? CapabilitySupport.Supported : CapabilitySupport.Unsupported, caps.TopP);
        Assert.AreEqual(CapabilitySupport.Supported, disabled.GetCapabilities().TopP);
        Assert.AreEqual(CapabilitySupport.Supported, summary.GetCapabilities().TopP);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().TopP);
        Assert.AreEqual(0, handler.Calls);

        Assert.AreEqual("answer", await request.GetCompletionAsync());
        Assert.IsTrue(handler.Body!.ContainsKey("tools"));
        Assert.AreEqual(sendsToolTopP, handler.Body.ContainsKey("top_p"));
        if (sendsToolTopP) Assert.AreEqual(0.42f, handler.Body["top_p"]!.GetValue<float>());
        Assert.IsTrue(handler.Body.ContainsKey("temperature"));

        Assert.AreEqual("answer", await disabled.GetCompletionAsync());
        Assert.IsFalse(handler.Body!.ContainsKey("tools"));
        Assert.AreEqual(0.42f, handler.Body["top_p"]!.GetValue<float>());

        Assert.AreEqual("answer", await summary.GetCompletionAsync());
        Assert.IsFalse(handler.Body!.ContainsKey("tools"));
        Assert.AreEqual(0.42f, handler.Body["top_p"]!.GetValue<float>());
    }

    [TestMethod]
    [DataRow(AlibabaModels.QwenMax)]
    [DataRow(AlibabaModels.Qwen3_8B)]
    public async Task Qwen_FunctionRequestSamplingCapabilitiesFollowCapturedFunctionsAndDisabledBranches(string model)
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new QwenService("offline", model, client);
        service.Functions.Add(new FunctionDefinition { Name = "unused" });
        var request = service.CreateRequest("question")
            .WithTopP(0.42f).WithFrequencyPenalty(0.3f).WithPresencePenalty(0.4f);
        var disabled = request.WithFunctionsDisabled();
        var summary = request.WithProfile(RequestProfiles.Summarization);
        service.Functions.Clear();

        var caps = request.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, caps.Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.TopP);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.FrequencyPenalty);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.PresencePenalty);
        var disabledCaps = disabled.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Supported, disabledCaps.TopP);
        Assert.AreEqual(CapabilitySupport.Supported, disabledCaps.FrequencyPenalty);
        Assert.AreEqual(CapabilitySupport.Supported, disabledCaps.PresencePenalty);
        Assert.AreEqual(CapabilitySupport.Supported, summary.GetCapabilities().TopP);
        Assert.AreEqual(CapabilitySupport.Supported, summary.GetCapabilities().FrequencyPenalty);
        Assert.AreEqual(CapabilitySupport.Supported, summary.GetCapabilities().PresencePenalty);
        Assert.AreEqual(CapabilitySupport.Supported, service.GetCapabilities().TopP);
        Assert.AreEqual(0, handler.Calls);

        Assert.AreEqual("answer", await request.GetCompletionAsync());
        Assert.IsTrue(handler.Body!.ContainsKey("tools"));
        Assert.IsTrue(handler.Body.ContainsKey("temperature"));
        Assert.IsFalse(handler.Body.ContainsKey("top_p"));
        Assert.IsFalse(handler.Body.ContainsKey("frequency_penalty"));
        Assert.IsFalse(handler.Body.ContainsKey("presence_penalty"));

        Assert.AreEqual("answer", await disabled.GetCompletionAsync());
        Assert.IsFalse(handler.Body!.ContainsKey("tools"));
        Assert.AreEqual(0.42f, handler.Body["top_p"]!.GetValue<float>());
        Assert.AreEqual(0.3f, handler.Body["frequency_penalty"]!.GetValue<float>());
        Assert.AreEqual(0.4f, handler.Body["presence_penalty"]!.GetValue<float>());

        Assert.AreEqual("answer", await summary.GetCompletionAsync());
        Assert.IsFalse(handler.Body!.ContainsKey("tools"));
        Assert.AreEqual(0.42f, handler.Body["top_p"]!.GetValue<float>());
        Assert.AreEqual(0.3f, handler.Body["frequency_penalty"]!.GetValue<float>());
        Assert.AreEqual(0.4f, handler.Body["presence_penalty"]!.GetValue<float>());
    }

    [TestMethod]
    public async Task Qwen_CustomDeploymentKeepsUnknownModelTraitsButReportsDefiniteFunctionPathOmissions()
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new QwenService("http://localhost:9000", EndpointPlatform.Vllm, "deployment", client);
        var request = service.CreateRequest("question")
            .WithFunctions(new FunctionDefinition { Name = "unused" });
        var caps = request.GetCapabilities();
        Assert.AreEqual(CapabilitySupport.Unknown, caps.FunctionCalling);
        Assert.AreEqual(CapabilitySupport.Unknown, caps.Temperature);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.TopP);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.FrequencyPenalty);
        Assert.AreEqual(CapabilitySupport.Unsupported, caps.PresencePenalty);
        Assert.AreEqual(CapabilitySupport.Unknown, request.WithFunctionsDisabled().GetCapabilities().TopP);

        Assert.AreEqual("answer", await request.GetCompletionAsync());
        Assert.IsTrue(handler.Body!.ContainsKey("tools"));
        Assert.IsFalse(handler.Body.ContainsKey("top_p"));
        Assert.IsFalse(handler.Body.ContainsKey("frequency_penalty"));
        Assert.IsFalse(handler.Body.ContainsKey("presence_penalty"));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public JsonObject? Body { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
