using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAICurrentModelContractTests
{
    private const string CompletedResponse = """
        {
          "status": "completed",
          "output_text": "ok",
          "output": [
            {
              "type": "message",
              "status": "completed",
              "content": [{ "type": "output_text", "text": "ok" }]
            }
          ]
        }
        """;

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5, "medium")]
    [DataRow(AIModels.OpenAI.Gpt5_1, "high")]
    [DataRow(AIModels.OpenAI.Gpt5_2, "high")]
    [DataRow(AIModels.OpenAI.Gpt5_3Codex, "high")]
    [DataRow(AIModels.OpenAI.Gpt5_4, "high")]
    [DataRow(AIModels.OpenAI.Gpt5_5, "high")]
    public async Task StructuredOutput_PreservesConfiguredVerbosity(
        string model,
        string expectedVerbosity)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        service.SetStructuredOutputSchema("""
            {
              "type": "object",
              "properties": { "value": { "type": "string" } },
              "required": ["value"],
              "additionalProperties": false
            }
            """);

        switch (model)
        {
            case AIModels.OpenAI.Gpt5_1:
                service.Gpt5_1Verbosity = Verbosity.High;
                break;
            case AIModels.OpenAI.Gpt5_2:
                service.Gpt5_2Verbosity = Verbosity.High;
                break;
            case AIModels.OpenAI.Gpt5_3Codex:
                service.Gpt5_3Verbosity = Verbosity.High;
                break;
            case AIModels.OpenAI.Gpt5_4:
                service.Gpt5_4Verbosity = Verbosity.High;
                break;
            case AIModels.OpenAI.Gpt5_5:
                service.Gpt5_5Verbosity = Verbosity.High;
                break;
        }

        await service.GetCompletionAsync("return structured output");

        using var document = JsonDocument.Parse(handler.SingleBody);
        var text = document.RootElement.GetProperty("text");
        Assert.AreEqual("json_schema", text.GetProperty("format").GetProperty("type").GetString());
        if (model == AIModels.OpenAI.Gpt5)
            Assert.IsFalse(text.TryGetProperty("verbosity", out _));
        else
            Assert.AreEqual(expectedVerbosity, text.GetProperty("verbosity").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5, 128)]
    [DataRow(AIModels.OpenAI.Gpt5_1, 128)]
    [DataRow(AIModels.OpenAI.Gpt5_2, 128)]
    [DataRow(AIModels.OpenAI.Gpt5_3Codex, 128)]
    [DataRow(AIModels.OpenAI.Gpt5_4, 128)]
    [DataRow(AIModels.OpenAI.Gpt5_5, 128)]
    public async Task Completion_PreservesCallerMaxOutputTokenLimit(string model, int maxTokens)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        service.MaxTokens = (uint)maxTokens;

        await service.GetCompletionAsync("short answer");

        using var document = JsonDocument.Parse(handler.SingleBody);
        Assert.AreEqual(maxTokens, document.RootElement.GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5_5, "medium")]
    [DataRow(AIModels.OpenAI.Gpt5_5Pro, "high")]
    public async Task Gpt5_5_AutoUsesOfficialDefaultReasoning(string model, string expected)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);

        await service.GetCompletionAsync("reason");

        using var document = JsonDocument.Parse(handler.SingleBody);
        Assert.AreEqual(
            expected,
            document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [TestMethod]
    public async Task Gpt5Pro_UsesIts272KOutputCeiling()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5Pro);
        service.MaxTokens = 300000;

        await service.GetCompletionAsync("reason");

        using var document = JsonDocument.Parse(handler.SingleBody);
        Assert.AreEqual(272000, document.RootElement.GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    public async Task Gpt5Pro_SummarizationProfile_ReservesReasoningBudgetAndRestoresCallerSetting()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5Pro);
        service.MaxTokens = 16000;

        await service.GetCompletionAsync("Summarize this conversation.", RequestProfiles.Summarization);

        using var document = JsonDocument.Parse(handler.SingleBody);
        Assert.AreEqual(
            4096,
            document.RootElement.GetProperty("max_output_tokens").GetInt32(),
            "gpt-5-pro needs room for mandatory high reasoning before the internal summary text.");
        Assert.AreEqual(
            "high",
            document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.AreEqual(
            16000u,
            service.MaxTokens,
            "The internal summary budget must not leak into later caller requests.");
    }

    [TestMethod]
    public async Task Gpt5Pro_QueryRewriteProfile_ReservesReasoningBudgetAndRestoresCallerSetting()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5Pro);
        service.MaxTokens = 16000;

        await service.GetCompletionAsync("Rewrite this query.", RequestProfiles.QueryRewrite);

        using var document = JsonDocument.Parse(handler.SingleBody);
        Assert.AreEqual(
            4096,
            document.RootElement.GetProperty("max_output_tokens").GetInt32(),
            "gpt-5-pro needs room for mandatory high reasoning before internal query-rewrite text.");
        Assert.AreEqual(
            "high",
            document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.AreEqual(
            16000u,
            service.MaxTokens,
            "The internal query-rewrite budget must not leak into later caller requests.");
    }

    [TestMethod]
    public async Task Gpt5Pro_InternalProfile_RestoresCallerBudgetAfterProviderFailure()
    {
        var service = new OpenAIService(
            "offline-test-key",
            new HttpClient(new FailingHandler()));
        service.ChangeModel(AIModels.OpenAI.Gpt5Pro);
        service.MaxTokens = 16000;

        await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GetCompletionAsync("Summarize this.", RequestProfiles.Summarization));

        Assert.AreEqual(
            16000u,
            service.MaxTokens,
            "The internal profile budget must be restored even when the provider rejects the request.");
    }

    [TestMethod]
    public async Task Gpt5Pro_CustomProfile_PreservesExplicitOutputBudget()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5Pro);

        await service.GetCompletionAsync(
            "Custom bounded request.",
            new AIRequestProfile
            {
                DisableReasoning = true,
                MaxTokens = 128
            });

        using var document = JsonDocument.Parse(handler.SingleBody);
        Assert.AreEqual(128, document.RootElement.GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    public async Task O3_UsesExplicitReasoningSelection()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.O3);
        service.Gpt5ReasoningEffort = Gpt5Reasoning.High;

        await service.GetCompletionAsync("reason");

        using var document = JsonDocument.Parse(handler.SingleBody);
        Assert.AreEqual(
            "high",
            document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.IsFalse(
            document.RootElement.GetProperty("reasoning").TryGetProperty("summary", out _),
            "o3 summaries are opt-in because they require a verified organization.");
    }

    [TestMethod]
    public async Task O3_ExplicitReasoningSummary_IsSerialized()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.O3);
        service.WithO3Parameters(Gpt5Reasoning.Low, ReasoningSummary.Detailed);

        await service.GetCompletionAsync("reason");

        using var document = JsonDocument.Parse(handler.SingleBody);
        var reasoning = document.RootElement.GetProperty("reasoning");
        Assert.AreEqual("low", reasoning.GetProperty("effort").GetString());
        Assert.AreEqual("detailed", reasoning.GetProperty("summary").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt4_1)]
    [DataRow(AIModels.OpenAI.O3)]
    public async Task VisionCapableModel_ImageCompletion_DoesNotSwitchModels(string model)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        var imagePath = Path.Combine(Path.GetTempPath(), $"mythosia-openai-{Guid.NewGuid():N}.png");

        try
        {
            await File.WriteAllBytesAsync(imagePath, new byte[] { 137, 80, 78, 71 });

            var result = await service.GetCompletionWithImageAsync("describe", imagePath);

            Assert.AreEqual("ok", result);
            Assert.AreEqual(model, service.Model);
            using var document = JsonDocument.Parse(handler.SingleBody);
            Assert.AreEqual(model, document.RootElement.GetProperty("model").GetString());
            Assert.AreEqual(
                "input_image",
                document.RootElement.GetProperty("input")[0].GetProperty("content")[1].GetProperty("type").GetString());
            Assert.AreEqual(
                "low",
                document.RootElement.GetProperty("input")[0].GetProperty("content")[1].GetProperty("detail").GetString());
        }
        finally
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
    }

    private static ProbeService CreateService(CaptureHandler handler, string model)
    {
        var service = new ProbeService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(model);
        return service;
    }

    private sealed class ProbeService : OpenAIService
    {
        public ProbeService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient)
        {
        }

        public void SetStructuredOutputSchema(string schema)
        {
            _structuredOutputSchemaJson = schema;
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly List<string> _bodies = new();

        public string SingleBody
        {
            get
            {
                Assert.AreEqual(1, _bodies.Count);
                return _bodies[0];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CompletedResponse, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":{"message":"synthetic failure","type":"invalid_request_error"}}""",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
