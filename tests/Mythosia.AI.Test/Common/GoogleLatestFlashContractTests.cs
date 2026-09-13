using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class GoogleLatestFlashContractTests
{
    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, "completion")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "completion")]
    [DataRow(AIModels.Google.Gemini3_7Flash, "stream")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "stream")]
    [DataRow(AIModels.Google.Gemini3_7Flash, "run")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "run")]
    public async Task TextPaths_UseDocumentedThinkingAndGenerationFields(string model, string path)
    {
        var handler = new CaptureHandler();
        var service = Create(model, handler);
        service.MaxTokens = 65536;

        foreach (var level in new[] { GeminiThinkingLevel.Auto, GeminiThinkingLevel.Low,
                     GeminiThinkingLevel.Medium, GeminiThinkingLevel.High })
        {
            service.ThinkingLevel = level;
            Assert.AreEqual("answer", await InvokeAsync(service, path));
            var request = handler.Requests.Last();
            var action = path == "completion" ? "generateContent" : "streamGenerateContent";
            Assert.AreEqual($"/v1beta/models/{model}:{action}", request.Uri.AbsolutePath);
            Assert.AreEqual("offline-google-key", request.ApiKey);
            Assert.IsFalse(request.Uri.Query.Contains("key="));
            var config = Config(request);
            AssertContract(config, level == GeminiThinkingLevel.Auto ? null : level.ToString().ToUpperInvariant());
            Assert.AreEqual(65536, config.GetProperty("maxOutputTokens").GetInt32());
        }
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash)]
    [DataRow(AIModels.Google.Gemini3_8Flash)]
    [DataRow("gemini-3.7-flash-2026-08-13")]
    [DataRow("gemini-3.8-flash-2026-09-02")]
    public async Task DirectThinking_RejectsMinimalAndUndefinedLevelsBeforeHttp(string model)
    {
        var handler = new CaptureHandler();
        var service = Create(model, handler);
        foreach (var level in new[] { GeminiThinkingLevel.Minimal, (GeminiThinkingLevel)999 })
        {
            service.ThinkingLevel = level;
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                () => service.GetCompletionAsync("invalid"));
        }
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash)]
    [DataRow(AIModels.Google.Gemini3_8Flash)]
    public async Task CommonReasoning_RejectsUnsupportedLevelsBeforeHistoryAndHttp(string model)
    {
        var handler = new CaptureHandler();
        var service = Create(model, handler);
        foreach (var level in new[] { ReasoningLevel.None, ReasoningLevel.Minimal, ReasoningLevel.XHigh, ReasoningLevel.Max })
            await Assert.ThrowsExactlyAsync<NotSupportedException>(
                () => service.WithReasoning(level).GetCompletionAsync("invalid"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, "completion")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "completion")]
    [DataRow(AIModels.Google.Gemini3_7Flash, "stream")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "stream")]
    [DataRow(AIModels.Google.Gemini3_7Flash, "run")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "run")]
    public async Task FunctionRounds_PreserveRestIdsSignaturesAndReasoningSnapshot(string model, string path)
    {
        var handler = new CaptureHandler(Response(FunctionAnswer));
        var service = Create(model, handler);
        var calls = 0;
        var options = new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = ReasoningLevel.Low } };
        service.ConfigureRequestFeatures(options);
        options.Reasoning.Level = ReasoningLevel.Minimal;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Description = "Returns the requested value.",
            Handler = _ => { calls++; return Task.FromResult("value"); }
        });

        Assert.AreEqual("answer", await InvokeAsync(service, path));

        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
            AssertContract(Config(request), "LOW");
        var contents = Body(handler.Requests[1]).GetProperty("contents");
        var callPart = contents[1].GetProperty("parts")[0];
        Assert.AreEqual("native-call-id", callPart.GetProperty("functionCall").GetProperty("id").GetString());
        Assert.AreEqual("native-signature", callPart.GetProperty("thoughtSignature").GetString());
        Assert.AreEqual("native-call-id", contents[2].GetProperty("parts")[0]
            .GetProperty("functionResponse").GetProperty("id").GetString());
        Assert.IsFalse(callPart.GetProperty("functionCall").TryGetProperty("call_id", out _));
        Assert.AreEqual(GeminiThinkingLevel.High, service.ThinkingLevel);

        await InvokeAsync(service, path);
        AssertContract(Config(handler.Requests[2]), "HIGH");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, "completion")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "completion")]
    [DataRow(AIModels.Google.Gemini3_7Flash, "run")]
    [DataRow(AIModels.Google.Gemini3_8Flash, "run")]
    public async Task FailedRequest_ConsumesOneShotReasoningAndRestoresNextRequest(string model, string path)
    {
        var handler = new CaptureHandler(Response("{\"error\":{\"message\":\"offline failure\"}}", HttpStatusCode.BadRequest));
        var service = Create(model, handler);
        service.WithReasoning(ReasoningLevel.Medium);
        await Assert.ThrowsExactlyAsync<AIServiceException>(async () => await InvokeAsync(service, path));
        AssertContract(Config(handler.Requests[0]), "MEDIUM");
        Assert.AreEqual(GeminiThinkingLevel.High, service.ThinkingLevel);

        Assert.AreEqual("answer", await InvokeAsync(service, path));
        AssertContract(Config(handler.Requests[1]), "HIGH");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, false, false)]
    [DataRow(AIModels.Google.Gemini3_8Flash, false, false)]
    [DataRow(AIModels.Google.Gemini3_7Flash, true, false)]
    [DataRow(AIModels.Google.Gemini3_8Flash, true, false)]
    [DataRow(AIModels.Google.Gemini3_7Flash, false, true)]
    [DataRow(AIModels.Google.Gemini3_8Flash, false, true)]
    [DataRow(AIModels.Google.Gemini3_7Flash, true, true)]
    [DataRow(AIModels.Google.Gemini3_8Flash, true, true)]
    public async Task InternalProfiles_UseLowAndRestoreStateEvenOnFailure(string model, bool summarize, bool fail)
    {
        var handler = fail
            ? new CaptureHandler(Response("{\"error\":{\"message\":\"offline failure\"}}", HttpStatusCode.BadRequest))
            : new CaptureHandler();
        var service = Create(model, handler);
        service.ThinkingBudget = 2048;
        service.WithReasoning(ReasoningLevel.Medium).WithWebSearch();
        var profile = summarize ? RequestProfiles.Summarization : RequestProfiles.QueryRewrite;

        if (fail)
            await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.GetCompletionAsync("internal request", profile));
        else
            Assert.AreEqual("answer", await service.GetCompletionAsync("internal request", profile));

        var config = Config(handler.Requests[0]);
        AssertContract(config, "LOW");
        Assert.AreEqual(1024, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.IsFalse(Body(handler.Requests[0]).TryGetProperty("tools", out _));
        Assert.AreEqual(GeminiThinkingLevel.High, service.ThinkingLevel);
        Assert.AreEqual(2048, service.ThinkingBudget);
        Assert.AreEqual(8192u, service.MaxTokens);
        Assert.AreEqual(0.25f, service.Temperature);
        Assert.AreEqual(0.75f, service.TopP);
        Assert.IsFalse(service.StatelessMode);
        Assert.IsFalse(service.FunctionsDisabled);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);

        await service.GetCompletionAsync("ordinary request");
        AssertContract(Config(handler.Requests[1]), "MEDIUM");
        Assert.IsTrue(Body(handler.Requests[1]).GetProperty("tools")[0].TryGetProperty("googleSearch", out _));
        await service.GetCompletionAsync("next request");
        AssertContract(Config(handler.Requests[2]), "HIGH");
        Assert.IsFalse(Body(handler.Requests[2]).TryGetProperty("tools", out _));
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini3_7Flash, false)]
    [DataRow(AIModels.Google.Gemini3_8Flash, false)]
    [DataRow(AIModels.Google.Gemini3_7Flash, true)]
    [DataRow(AIModels.Google.Gemini3_8Flash, true)]
    public async Task StructuredRepair_PreservesContractAndCleansUpAfterSuccessOrFailure(string model, bool fail)
    {
        var handler = new CaptureHandler(Response(TextAnswer("invalid JSON")),
            Response(TextAnswer(fail ? "still invalid JSON" : "{\"Answer\":\"repaired\"}")));
        var service = Create(model, handler);
        service.StructuredOutputMaxRetries = 1;
        service.WithReasoning(ReasoningLevel.Low);

        if (fail)
            await Assert.ThrowsExactlyAsync<StructuredOutputException>(() => service.GetCompletionAsync<StructuredAnswer>("return JSON"));
        else
            Assert.AreEqual("repaired", (await service.GetCompletionAsync<StructuredAnswer>("return JSON")).Answer);

        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            var config = Config(request);
            AssertContract(config, "LOW");
            var format = config.GetProperty("responseFormat").GetProperty("text");
            Assert.AreEqual("APPLICATION_JSON", format.GetProperty("mimeType").GetString());
            Assert.IsTrue(format.GetProperty("schema").GetProperty("properties").TryGetProperty("Answer", out _));
        }
        StringAssert.Contains(handler.Requests[1].Body, "STRUCTURED OUTPUT CORRECTION");
        await service.GetCompletionAsync("ordinary request");
        AssertContract(Config(handler.Requests[2]), "HIGH");
        Assert.IsFalse(Config(handler.Requests[2]).TryGetProperty("responseFormat", out _));
        Assert.AreEqual(GeminiThinkingLevel.High, service.ThinkingLevel);
    }

    public sealed class StructuredAnswer
    {
        public string Answer { get; set; } = string.Empty;
    }

    private static GoogleAIService Create(string model, CaptureHandler handler) =>
        new("offline-google-key", model, new HttpClient(handler))
        {
            Temperature = 0.25f,
            TopP = 0.75f,
            ThinkingLevel = GeminiThinkingLevel.High
        };

    private static async Task<string> InvokeAsync(GoogleAIService service, string path)
    {
        if (path == "completion") return await service.GetCompletionAsync("test");
        if (path == "run")
        {
            await using var run = await service.StartRunAsync("test");
            return (await run.Result).Text;
        }
        var text = new StringBuilder();
        await foreach (var chunk in service.StreamAsync("test", StreamOptions.WithFunctions))
        {
            Assert.AreNotEqual(StreamingContentType.Error, chunk.Type, chunk.Content);
            if (chunk.Type == StreamingContentType.Text) text.Append(chunk.Content);
        }
        return text.ToString();
    }

    private static void AssertContract(JsonElement config, string? thinking)
    {
        foreach (var field in new[] { "temperature", "topP", "topK", "candidateCount" })
            Assert.IsFalse(config.TryGetProperty(field, out _), $"Unexpected generation field {field}.");
        if (thinking == null)
        {
            if (config.TryGetProperty("thinkingConfig", out var automatic))
                Assert.IsFalse(automatic.TryGetProperty("thinkingLevel", out _));
            return;
        }
        var thinkingConfig = config.GetProperty("thinkingConfig");
        Assert.AreEqual(thinking, thinkingConfig.GetProperty("thinkingLevel").GetString());
        Assert.IsFalse(thinkingConfig.TryGetProperty("thinkingBudget", out _));
    }

    private static JsonElement Body(CapturedRequest request) => JsonSerializer.Deserialize<JsonElement>(request.Body);
    private static JsonElement Config(CapturedRequest request) => Body(request).GetProperty("generationConfig");
    private static (string Body, HttpStatusCode Status) Response(string body, HttpStatusCode status = HttpStatusCode.OK) => (body, status);
    private static string TextAnswer(string text) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { role = "model", parts = new[] { new { text } } }, finishReason = "STOP" } }
    });

    private const string FunctionAnswer = """
        {"candidates":[{"content":{"role":"model","parts":[
          {"functionCall":{"id":"native-call-id","name":"lookup","args":{}},"thoughtSignature":"native-signature"}
        ]},"finishReason":"STOP"}]}
        """;

    private sealed record CapturedRequest(Uri Uri, string Body, string? ApiKey);

    private sealed class CaptureHandler(params (string Body, HttpStatusCode Status)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(string Body, HttpStatusCode Status)> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null));
            var response = _responses.Count == 0 ? Response(TextAnswer("answer")) : _responses.Dequeue();
            var stream = request.RequestUri!.AbsolutePath.EndsWith(":streamGenerateContent") && response.Status == HttpStatusCode.OK;
            var body = stream ? "data: " + response.Body.Replace("\r", "").Replace("\n", "") + "\n\ndata: [DONE]\n\n" : response.Body;
            return new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(body, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
