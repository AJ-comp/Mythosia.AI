using Mythosia.AI.Models.Functions;
using Mythosia.AI.Providers.Alibaba;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Alibaba;

[TestClass]
[TestCategory("Unit")]
public class QwenRunRequestedModelTests
{
    [TestMethod]
    [DataRow(EndpointPlatform.DashScope)]
    [DataRow(EndpointPlatform.Vllm)]
    public async Task ModelOverride_IsRecordedAsTheModelActuallySent(EndpointPlatform platform)
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(platform, client);
        service.ModelIdOverride = "served-production-model";

        await using var run = await service.StartRunAsync("question");
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("served-production-model", handler.RequestedModel);
        Assert.AreEqual(handler.RequestedModel, result.RequestedModel);
        Assert.AreEqual("server-resolved-model", result.Model);
        Assert.AreEqual("qwen3-32b", service.Model);
    }

    [TestMethod]
    public async Task BuilderSnapshot_RecordsCapturedOverrideAfterServiceDefaultsChange()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(EndpointPlatform.Vllm, client);
        service.ModelIdOverride = "captured-served-model";
        var request = service.CreateRequest("question");
        service.ModelIdOverride = "later-served-model";
        service.ChangeModel("qwen3-235b-a22b");

        await using var run = await request.StartRunAsync();
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("captured-served-model", handler.RequestedModel);
        Assert.AreEqual(handler.RequestedModel, result.RequestedModel);
        Assert.AreEqual("later-served-model", service.ModelIdOverride);
        Assert.AreEqual("qwen3-235b-a22b", service.Model);
    }

    [TestMethod]
    public async Task Ollama_RecordsTheTranslatedWireModelId()
    {
        using var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(EndpointPlatform.Ollama, client);

        await using var run = await service.StartRunAsync("question");
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("qwen3:32b", handler.RequestedModel);
        Assert.AreEqual(handler.RequestedModel, result.RequestedModel);
        Assert.AreEqual("qwen3-32b", service.Model);
    }

    private static QwenService CreateService(EndpointPlatform platform, HttpClient client)
    {
        var service = new QwenService("https://offline.invalid/", platform, "qwen3-32b", client);
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, EnableLogging = false };
        return service;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestedModel { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestedModel = body.RootElement.GetProperty("model").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"model\":\"server-resolved-model\",\"choices\":[{\"delta\":{\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n",
                    Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
