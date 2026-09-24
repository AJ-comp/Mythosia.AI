using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIGpt6RequestShapeTests
{
    private const string CompletedResponse = """
        {"id":"resp_final","status":"completed","output_text":"ok","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}]}
        """;

    private const string ToolResponse = """
        {"id":"resp_tools","status":"completed","output":[
          {"id":"rs_gpt6","type":"reasoning","status":"completed","summary":[],"encrypted_content":"gpt6-reasoning-state"},
          {"id":"fc_first","type":"function_call","status":"completed","call_id":"call_first","name":"first","arguments":"{\"value\":1}"},
          {"id":"fc_second","type":"function_call","status":"completed","call_id":"call_second","name":"second","arguments":"{\"value\":2}"}
        ]}
        """;

    [TestMethod]
    [DataRow(nameof(Gpt6Reasoning.Auto), 0)]
    [DataRow(nameof(Gpt6Reasoning.Low), 1)]
    [DataRow(nameof(Gpt6Reasoning.Medium), 2)]
    [DataRow(nameof(Gpt6Reasoning.High), 3)]
    [DataRow(nameof(Gpt6Reasoning.XHigh), 4)]
    [DataRow(nameof(Gpt6Reasoning.Max), 5)]
    [DataRow(nameof(Gpt6Reasoning.None), 6)]
    public void ReasoningOptions_PreserveExistingValuesWhenAddingNone(string name, int value)
    {
        Assert.AreEqual(value, (int)Enum.Parse<Gpt6Reasoning>(name));
    }

    [TestMethod]
    public async Task Completion_UsesResponsesDefaultsAndOmitsUnsupportedSamplingParameters()
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);
        service.MaxTokens = 200000;
        service.Temperature = 0.3f;
        service.TopP = 0.5f;
        service.FrequencyPenalty = 0.2f;
        service.PresencePenalty = 0.2f;
        service.WithGpt5_6Parameters(Gpt5_6Reasoning.None, Verbosity.High);

        Assert.AreEqual("ok", await service.GetCompletionAsync("hello"));

        using var document = ParseSingleRequest(handler);
        var root = document.RootElement;
        AssertGpt6Request(root);
        var reasoning = root.GetProperty("reasoning");
        Assert.AreEqual("medium", reasoning.GetProperty("effort").GetString());
        Assert.AreEqual("auto", reasoning.GetProperty("summary").GetString());
        Assert.IsFalse(reasoning.TryGetProperty("mode", out _));
        Assert.AreEqual("medium", root.GetProperty("text").GetProperty("verbosity").GetString());
        Assert.AreEqual(128000, root.GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    [DataRow(Gpt6Reasoning.Auto, "medium")]
    [DataRow(Gpt6Reasoning.Low, "low")]
    [DataRow(Gpt6Reasoning.Medium, "medium")]
    [DataRow(Gpt6Reasoning.High, "high")]
    [DataRow(Gpt6Reasoning.XHigh, "xhigh")]
    [DataRow(Gpt6Reasoning.Max, "max")]
    public async Task Completion_SerializesSupportedReasoningEfforts(Gpt6Reasoning effort, string expected)
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);
        service.WithGpt6Parameters(reasoningEffort: effort);

        await service.GetCompletionAsync("reason");

        using var document = ParseSingleRequest(handler);
        Assert.AreEqual(expected, document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [TestMethod]
    public async Task ProMode_UsesSameModelWithConfiguredSettingsAndCallerTokenBudget()
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);
        service.MaxTokens = 128;
        ConfigurePro(service);

        await service.GetCompletionAsync("solve this");

        using var document = ParseSingleRequest(handler);
        AssertProRequest(document.RootElement);
        Assert.AreEqual(128, document.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.AreEqual(AIModels.OpenAI.Gpt6Astra, service.Model);
    }

    [TestMethod]
    public async Task NullableSettings_OmitSummaryAndDefaultVerbosityToMedium()
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);
        service.WithGpt6Parameters(reasoningSummary: null);
        service.Gpt6Verbosity = null;

        await service.GetCompletionAsync("answer");

        using var document = ParseSingleRequest(handler);
        Assert.IsFalse(document.RootElement.GetProperty("reasoning").TryGetProperty("summary", out _));
        Assert.AreEqual("medium", document.RootElement.GetProperty("text").GetProperty("verbosity").GetString());
    }

    [TestMethod]
    public async Task DisableReasoning_UsesLowForOneRequestAndRestoresProSettings()
    {
        var handler = new CaptureHandler(CompletedResponse, CompletedResponse);
        var service = CreateService(handler);
        ConfigurePro(service);

        await service.GetCompletionAsync("fast request", new AIRequestProfile { DisableReasoning = true });
        await service.GetCompletionAsync("reasoned request");

        Assert.AreEqual(2, handler.Requests.Count);
        using var first = JsonDocument.Parse(handler.Requests[0].Body);
        AssertLowReasoningWithoutSummary(first.RootElement);
        using var second = JsonDocument.Parse(handler.Requests[1].Body);
        AssertProRequest(second.RootElement);
    }

    [TestMethod]
    [DataRow(AIRequestPurpose.Summarization, 256, 4096)]
    [DataRow(AIRequestPurpose.QueryRewrite, 128, 4096)]
    [DataRow(AIRequestPurpose.Summarization, 8192, 8192)]
    [DataRow(AIRequestPurpose.QueryRewrite, 200000, 128000)]
    public async Task InternalProfiles_ReserveReasoningBudgetAndRestoreCallerSettings(AIRequestPurpose purpose, int requestedTokens, int expectedTokens)
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);
        service.MaxTokens = 16000;
        ConfigurePro(service);
        var profile = purpose == AIRequestPurpose.Summarization
            ? RequestProfiles.Summarization
            : RequestProfiles.QueryRewrite;
        profile.MaxTokens = (uint)requestedTokens;

        await service.GetCompletionAsync("internal request", profile);

        using var document = ParseSingleRequest(handler);
        AssertLowReasoningWithoutSummary(document.RootElement);
        Assert.AreEqual(expectedTokens, document.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.AreEqual(16000u, service.MaxTokens);
        AssertProSettings(service);
        Assert.IsFalse(service.StatelessMode);
        Assert.IsFalse(service.FunctionsDisabled);
    }

    [TestMethod]
    public async Task CustomProfile_PreservesExplicitTokenBudget()
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);

        await service.GetCompletionAsync("bounded request", new AIRequestProfile
        {
            DisableReasoning = true,
            MaxTokens = 128
        });

        using var document = ParseSingleRequest(handler);
        AssertLowReasoningWithoutSummary(document.RootElement);
        Assert.AreEqual(128, document.RootElement.GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    public async Task InternalProfile_ProviderFailureRestoresReasoningAndTokenBudget()
    {
        var handler = new CaptureHandler("{\"error\":{\"message\":\"rejected\",\"type\":\"invalid_request_error\"}}")
        {
            StatusCode = HttpStatusCode.BadRequest
        };
        var service = CreateService(handler);
        service.MaxTokens = 16000;
        ConfigurePro(service);

        await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GetCompletionAsync("internal request", RequestProfiles.Summarization));

        Assert.AreEqual(16000u, service.MaxTokens);
        AssertProSettings(service);
        Assert.IsFalse(service.StatelessMode);
        Assert.IsFalse(service.FunctionsDisabled);
    }

    [TestMethod]
    [DataRow(Gpt6ReasoningMode.Standard, 100, 300)]
    [DataRow(Gpt6ReasoningMode.Pro, 100, 600)]
    [DataRow(Gpt6ReasoningMode.Standard, 45, 45)]
    [DataRow(Gpt6ReasoningMode.Pro, 45, 45)]
    public void Timeout_ExtendsDefaultButPreservesCallerOverride(Gpt6ReasoningMode mode, int timeout, int expected)
    {
        var service = CreateService(new CaptureHandler());
        service.Gpt6ReasoningMode = mode;

        Assert.AreEqual(expected, service.ResolveTimeout(new FunctionCallingPolicy { TimeoutSeconds = timeout }));
    }

    [TestMethod]
    public async Task StructuredOutput_PreservesSchemaAndGpt6Verbosity()
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);
        service.WithGpt6Parameters(verbosity: Verbosity.High);
        service.SetStructuredOutputSchema("""
            {"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}
            """);

        await service.GetCompletionAsync("return structured output");

        using var document = ParseSingleRequest(handler);
        AssertGpt6Request(document.RootElement);
        var text = document.RootElement.GetProperty("text");
        Assert.AreEqual("high", text.GetProperty("verbosity").GetString());
        var format = text.GetProperty("format");
        Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
        Assert.AreEqual("structured_output", format.GetProperty("name").GetString());
        Assert.IsTrue(format.GetProperty("strict").GetBoolean());
        Assert.AreEqual("string", format.GetProperty("schema").GetProperty("properties").GetProperty("value").GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task ImageCompletion_PreservesGpt6ModelAndSendsResponsesImageInput()
    {
        var handler = new CaptureHandler(CompletedResponse);
        var service = CreateService(handler);
        var imagePath = Path.Combine(Path.GetTempPath(), $"mythosia-gpt6-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(imagePath, new byte[] { 137, 80, 78, 71 });

            Assert.AreEqual("ok", await service.GetCompletionWithImageAsync("describe", imagePath));

            using var document = ParseSingleRequest(handler);
            AssertGpt6Request(document.RootElement);
            Assert.AreEqual(AIModels.OpenAI.Gpt6Astra, service.Model);
            var image = document.RootElement.GetProperty("input")[0].GetProperty("content")[1];
            Assert.AreEqual("input_image", image.GetProperty("type").GetString());
            StringAssert.StartsWith(image.GetProperty("image_url").GetString()!, "data:image/png;base64,");
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [TestMethod]
    public async Task Streaming_UsesResponsesApiAndEmitsReasoningAndText()
    {
        const string response = """
            data: {"type":"response.reasoning_summary_text.delta","item_id":"rs_answer","output_index":0,"summary_index":0,"delta":"summary"}

            data: {"type":"response.output_text.delta","delta":"ok"}

            data: {"type":"response.completed","response":{"id":"resp_answer","status":"completed","output":[{"id":"rs_answer","type":"reasoning","summary":[{"type":"summary_text","text":"summary"}]},{"type":"message","role":"assistant","content":[{"type":"output_text","text":"ok"}]}]}}

            """;
        var handler = new CaptureHandler(response);
        var service = CreateService(handler);
        var events = new List<StreamingContent>();

        await foreach (var item in service.StreamAsync("reason", StreamOptions.Default.WithFunctionCalls(false).WithReasoning()))
            events.Add(item);

        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
        Assert.AreEqual("summary", string.Concat(events.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content)));
        Assert.AreEqual("ok", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        using var document = ParseSingleRequest(handler);
        AssertGpt6Request(document.RootElement);
        Assert.IsTrue(document.RootElement.GetProperty("stream").GetBoolean());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FunctionBatch_ReplaysReasoningAndBothCallsOnceWithGpt6Settings(bool streaming)
    {
        var handler = new CaptureHandler(
            streaming ? ToCompletedStream(ToolResponse) : ToolResponse,
            streaming ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"ok\"}\n\n" + ToCompletedStream(CompletedResponse) : CompletedResponse);
        var service = CreateService(handler);
        ConfigurePro(service);
        service.ForceFunctionName = "first";
        service.SystemMessage = "Use both tools.";
        var invocations = new List<string>();
        foreach (var name in new[] { "first", "second" })
        {
            var function = new FunctionDefinition
            {
                Name = name,
                Description = name,
                Handler = arguments =>
                {
                    invocations.Add(name);
                    Assert.AreEqual(name == "first" ? "1" : "2", arguments["value"].ToString());
                    return Task.FromResult($"result-{name}");
                }
            };
            function.Parameters.Properties["value"] = new ParameterProperty { Type = "integer" };
            function.Parameters.Required.Add("value");
            service.Functions.Add(function);
        }

        if (streaming)
        {
            var events = new List<StreamingContent>();
            await foreach (var item in service.StreamAsync("run both", StreamOptions.WithFunctions))
                events.Add(item);
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
            Assert.AreEqual("ok", string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
            Assert.AreEqual(2, events.Count(item => item.Type == StreamingContentType.FunctionCall));
            Assert.AreEqual(2, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        }
        else
        {
            Assert.AreEqual("ok", await service.GetCompletionAsync("run both"));
        }

        CollectionAssert.AreEqual(new[] { "first", "second" }, invocations);
        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var captured in handler.Requests)
        {
            Assert.AreEqual("/v1/responses", captured.Uri.AbsolutePath);
            using var document = JsonDocument.Parse(captured.Body);
            AssertProRequest(document.RootElement);
            Assert.AreEqual("Use both tools.", document.RootElement.GetProperty("instructions").GetString());
            Assert.IsTrue(document.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
            Assert.AreEqual(2, document.RootElement.GetProperty("tools").GetArrayLength());
        }

        using var first = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.AreEqual("first", first.RootElement.GetProperty("tool_choice").GetProperty("name").GetString());
        using var continuation = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.AreEqual("auto", continuation.RootElement.GetProperty("tool_choice").GetString());
        var input = continuation.RootElement.GetProperty("input");
        Assert.AreEqual(6, input.GetArrayLength());
        Assert.AreEqual("rs_gpt6", input[1].GetProperty("id").GetString());
        Assert.AreEqual("gpt6-reasoning-state", input[1].GetProperty("encrypted_content").GetString());
        for (var i = 0; i < 2; i++)
        {
            var name = i == 0 ? "first" : "second";
            Assert.AreEqual($"fc_{name}", input[i + 2].GetProperty("id").GetString());
            Assert.AreEqual($"call_{name}", input[i + 2].GetProperty("call_id").GetString());
            Assert.AreEqual("function_call_output", input[i + 4].GetProperty("type").GetString());
            Assert.AreEqual($"call_{name}", input[i + 4].GetProperty("call_id").GetString());
            Assert.AreEqual($"result-{name}", input[i + 4].GetProperty("output").GetString());
        }
    }

    private static void ConfigurePro(OpenAIService service) => service.WithGpt6Parameters(
        reasoningEffort: Gpt6Reasoning.Max,
        verbosity: Verbosity.High,
        reasoningSummary: ReasoningSummary.Detailed,
        reasoningMode: Gpt6ReasoningMode.Pro);

    private static void AssertProSettings(OpenAIService service)
    {
        Assert.AreEqual(Gpt6Reasoning.Max, service.Gpt6ReasoningEffort);
        Assert.AreEqual(ReasoningSummary.Detailed, service.Gpt6ReasoningSummary);
        Assert.AreEqual(Gpt6ReasoningMode.Pro, service.Gpt6ReasoningMode);
        Assert.AreEqual(Verbosity.High, service.Gpt6Verbosity);
    }

    private static void AssertProRequest(JsonElement root)
    {
        AssertGpt6Request(root);
        var reasoning = root.GetProperty("reasoning");
        Assert.AreEqual("max", reasoning.GetProperty("effort").GetString());
        Assert.AreEqual("detailed", reasoning.GetProperty("summary").GetString());
        Assert.AreEqual("pro", reasoning.GetProperty("mode").GetString());
        Assert.AreEqual("high", root.GetProperty("text").GetProperty("verbosity").GetString());
    }

    private static void AssertLowReasoningWithoutSummary(JsonElement root)
    {
        AssertGpt6Request(root);
        var reasoning = root.GetProperty("reasoning");
        Assert.AreEqual("low", reasoning.GetProperty("effort").GetString());
        Assert.IsFalse(reasoning.TryGetProperty("summary", out _));
        Assert.IsFalse(reasoning.TryGetProperty("mode", out _));
    }

    private static void AssertGpt6Request(JsonElement root)
    {
        Assert.AreEqual("gpt-6-astra", root.GetProperty("model").GetString());
        Assert.AreEqual("current_turn", root.GetProperty("reasoning").GetProperty("context").GetString());
        foreach (var unsupported in new[]
        {
            "temperature", "top_p", "logprobs", "top_logprobs", "frequency_penalty",
            "presence_penalty", "max_tokens", "max_completion_tokens"
        })
            Assert.IsFalse(root.TryGetProperty(unsupported, out _), $"GPT-6 must omit {unsupported}.");
    }

    private static string ToCompletedStream(string response)
    {
        using var document = JsonDocument.Parse(response);
        return $"data: {{\"type\":\"response.completed\",\"response\":{JsonSerializer.Serialize(document.RootElement)}}}\n\n";
    }

    private static ProbeService CreateService(CaptureHandler handler)
    {
        var service = new ProbeService(new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
        return service;
    }

    private static JsonDocument ParseSingleRequest(CaptureHandler handler)
    {
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual("/v1/responses", handler.Requests[0].Uri.AbsolutePath);
        return JsonDocument.Parse(handler.Requests[0].Body);
    }

    private sealed class ProbeService : OpenAIService
    {
        public ProbeService(HttpClient client) : base("offline-test-key", client) { }

        public void SetStructuredOutputSchema(string schema) => _structuredOutputSchemaJson = schema;

        public int? ResolveTimeout(FunctionCallingPolicy policy) => ResolveRequestTimeoutSeconds(policy);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public CaptureHandler(params string[] responses) => _responses = new Queue<string>(responses);

        public List<CapturedRequest> Requests { get; } = new();
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued GPT-6 response remains.");
            var response = _responses.Dequeue();
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(response, Encoding.UTF8,
                    response.StartsWith("data:", StringComparison.Ordinal) ? "text/event-stream" : "application/json")
            };
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Body);
}
