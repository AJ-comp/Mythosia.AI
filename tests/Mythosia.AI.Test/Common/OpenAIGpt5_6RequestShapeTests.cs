using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIGpt5_6RequestShapeTests
{
    [TestMethod]
    public void ModelConstants_MatchOfficialModelIds()
    {
        CollectionAssert.AreEqual(
            new[] { "gpt-5.6", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna" },
            new[]
            {
                AIModels.OpenAI.Gpt5_6,
                AIModels.OpenAI.Gpt5_6Sol,
                AIModels.OpenAI.Gpt5_6Terra,
                AIModels.OpenAI.Gpt5_6Luna
            });
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt5_6)]
    [DataRow(AIModels.OpenAI.Gpt5_6Sol)]
    [DataRow(AIModels.OpenAI.Gpt5_6Terra)]
    [DataRow(AIModels.OpenAI.Gpt5_6Luna)]
    public async Task Completion_UsesResponsesApiAndGpt5_6Defaults(string model)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, model);
        service.MaxTokens = 200000;

        var result = await service.GetCompletionAsync("hello");

        Assert.AreEqual("ok", result);
        var captured = AssertSingleRequest(handler);
        Assert.AreEqual("/v1/responses", captured.Uri.AbsolutePath);

        using var document = JsonDocument.Parse(captured.Body);
        var root = document.RootElement;
        Assert.AreEqual(model, root.GetProperty("model").GetString());
        Assert.AreEqual("medium", root.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.AreEqual("current_turn", root.GetProperty("reasoning").GetProperty("context").GetString());
        Assert.AreEqual("auto", root.GetProperty("reasoning").GetProperty("summary").GetString());
        Assert.IsFalse(root.GetProperty("reasoning").TryGetProperty("mode", out _));
        Assert.AreEqual("medium", root.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.AreEqual(128000, root.GetProperty("max_output_tokens").GetInt32());
        Assert.IsFalse(root.TryGetProperty("max_tokens", out _));
        Assert.IsFalse(root.TryGetProperty("max_completion_tokens", out _));
        Assert.IsFalse(root.TryGetProperty("frequency_penalty", out _));
        Assert.IsFalse(root.TryGetProperty("presence_penalty", out _));
    }

    [TestMethod]
    public async Task Completion_SendsMaxEffortHighVerbosityAndProMode()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5_6Sol);
        service.MaxTokens = 128;
        service.WithGpt5_6Parameters(
            reasoningEffort: Gpt5_6Reasoning.Max,
            verbosity: Verbosity.High,
            reasoningSummary: ReasoningSummary.Detailed,
            reasoningMode: Gpt5_6ReasoningMode.Pro);

        await service.GetCompletionAsync("solve this");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var root = document.RootElement;
        var reasoning = root.GetProperty("reasoning");
        Assert.AreEqual("max", reasoning.GetProperty("effort").GetString());
        Assert.AreEqual("current_turn", reasoning.GetProperty("context").GetString());
        Assert.AreEqual("detailed", reasoning.GetProperty("summary").GetString());
        Assert.AreEqual("pro", reasoning.GetProperty("mode").GetString());
        Assert.AreEqual("high", root.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.AreEqual(128, root.GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    [DataRow(nameof(Gpt5_6Reasoning.Auto), "medium")]
    [DataRow(nameof(Gpt5_6Reasoning.None), "none")]
    [DataRow(nameof(Gpt5_6Reasoning.Low), "low")]
    [DataRow(nameof(Gpt5_6Reasoning.Medium), "medium")]
    [DataRow(nameof(Gpt5_6Reasoning.High), "high")]
    [DataRow(nameof(Gpt5_6Reasoning.XHigh), "xhigh")]
    [DataRow(nameof(Gpt5_6Reasoning.Max), "max")]
    public async Task Completion_SerializesEveryGpt5_6ReasoningEffort(
        string effortName,
        string expected)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5_6Sol);
        service.WithGpt5_6Parameters(
            reasoningEffort: Enum.Parse<Gpt5_6Reasoning>(effortName));

        await service.GetCompletionAsync("reason");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        Assert.AreEqual(
            expected,
            document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [TestMethod]
    [DataRow(nameof(Verbosity.Low), "low")]
    [DataRow(nameof(Verbosity.Medium), "medium")]
    [DataRow(nameof(Verbosity.High), "high")]
    public async Task Completion_SerializesEveryGpt5_6Verbosity(
        string verbosityName,
        string expected)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5_6Sol);
        service.WithGpt5_6Parameters(
            verbosity: Enum.Parse<Verbosity>(verbosityName));

        await service.GetCompletionAsync("answer");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        Assert.AreEqual(
            expected,
            document.RootElement.GetProperty("text").GetProperty("verbosity").GetString());
    }

    [TestMethod]
    public async Task Completion_OmitsReasoningSummaryWhenDisabledExplicitly()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5_6Sol);
        service.WithGpt5_6Parameters(reasoningSummary: null);

        await service.GetCompletionAsync("reason without a summary");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        Assert.IsFalse(
            document.RootElement.GetProperty("reasoning").TryGetProperty("summary", out _));
    }

    [TestMethod]
    [DataRow(nameof(ReasoningSummary.Auto), "auto")]
    [DataRow(nameof(ReasoningSummary.Concise), "concise")]
    [DataRow(nameof(ReasoningSummary.Detailed), "detailed")]
    public async Task Completion_SerializesEveryGpt5_6ReasoningSummary(
        string summaryName,
        string expected)
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5_6Sol);
        service.WithGpt5_6Parameters(
            reasoningSummary: Enum.Parse<ReasoningSummary>(summaryName));

        await service.GetCompletionAsync("summarize reasoning");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        Assert.AreEqual(
            expected,
            document.RootElement.GetProperty("reasoning").GetProperty("summary").GetString());
    }

    [TestMethod]
    public async Task Completion_MergesVerbosityWithStructuredOutputFormat()
    {
        var handler = new CaptureHandler();
        var service = new RequestProbeService(
            "offline-test-key",
            new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        service.WithGpt5_6Parameters(verbosity: Verbosity.High);
        service.SetStructuredOutputSchema("""
            {
              "type": "object",
              "properties": { "value": { "type": "string" } },
              "required": ["value"],
              "additionalProperties": false
            }
            """);

        await service.GetCompletionAsync("return structured output");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var text = document.RootElement.GetProperty("text");
        Assert.AreEqual("high", text.GetProperty("verbosity").GetString());
        Assert.AreEqual("json_schema", text.GetProperty("format").GetProperty("type").GetString());
        Assert.AreEqual(
            "structured_output",
            text.GetProperty("format").GetProperty("name").GetString());
        Assert.IsTrue(text.GetProperty("format").GetProperty("strict").GetBoolean());
    }

    [TestMethod]
    public async Task DisableReasoning_AppliesOnceAndRestoresGpt5_6Settings()
    {
        var handler = new CaptureHandler();
        var service = CreateService(handler, AIModels.OpenAI.Gpt5_6Terra);
        service.WithGpt5_6Parameters(
            reasoningEffort: Gpt5_6Reasoning.Max,
            verbosity: Verbosity.High,
            reasoningSummary: ReasoningSummary.Detailed,
            reasoningMode: Gpt5_6ReasoningMode.Pro);

        await service.GetCompletionAsync(
            "fast request",
            new AIRequestProfile { DisableReasoning = true });
        await service.GetCompletionAsync("reasoned request");

        Assert.AreEqual(2, handler.Requests.Count);
        using var disabledDocument = JsonDocument.Parse(handler.Requests[0].Body);
        var disabledReasoning = disabledDocument.RootElement.GetProperty("reasoning");
        Assert.AreEqual("none", disabledReasoning.GetProperty("effort").GetString());
        Assert.IsFalse(disabledReasoning.TryGetProperty("summary", out _));
        Assert.IsFalse(disabledReasoning.TryGetProperty("mode", out _));

        using var restoredDocument = JsonDocument.Parse(handler.Requests[1].Body);
        var restoredReasoning = restoredDocument.RootElement.GetProperty("reasoning");
        Assert.AreEqual("max", restoredReasoning.GetProperty("effort").GetString());
        Assert.AreEqual("detailed", restoredReasoning.GetProperty("summary").GetString());
        Assert.AreEqual("pro", restoredReasoning.GetProperty("mode").GetString());
    }

    [TestMethod]
    public void ProMode_ExtendsOnlyTheDefaultRequestTimeout()
    {
        var service = new TimeoutProbeService(
            "offline-test-key",
            new HttpClient(new CaptureHandler()));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Luna);

        Assert.AreEqual(100, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = 100 }));

        service.Gpt5_6ReasoningMode = Gpt5_6ReasoningMode.Pro;
        Assert.AreEqual(600, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = 100 }));
        Assert.AreEqual(45, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = 45 }));

        service.ChangeModel(AIModels.OpenAI.Gpt5Pro);
        Assert.AreEqual(600, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = 100 }));
        Assert.AreEqual(45, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = 45 }));
    }

    [TestMethod]
    public void Gpt5_ExtendsOnlyTheDefaultRequestTimeout()
    {
        var service = new TimeoutProbeService(
            "offline-test-key",
            new HttpClient(new CaptureHandler()));
        service.ChangeModel(AIModels.OpenAI.Gpt5);

        Assert.AreEqual(300, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = 100 }));
        Assert.AreEqual(45, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = 45 }));
    }

    private static OpenAIService CreateService(CaptureHandler handler, string model)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(model);
        return service;
    }

    private static CapturedRequest AssertSingleRequest(CaptureHandler handler)
    {
        Assert.AreEqual(1, handler.Requests.Count);
        return handler.Requests[0];
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                await request.Content!.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"status\":\"completed\",\"output_text\":\"ok\",\"output\":[]}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class RequestProbeService : OpenAIService
    {
        public RequestProbeService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient)
        {
        }

        public void SetStructuredOutputSchema(string schema)
        {
            _structuredOutputSchemaJson = schema;
        }
    }

    private sealed class TimeoutProbeService : OpenAIService
    {
        public TimeoutProbeService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient)
        {
        }

        public int? ResolveTimeout(FunctionCallingPolicy policy)
        {
            return ResolveRequestTimeoutSeconds(policy);
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Body);
}
