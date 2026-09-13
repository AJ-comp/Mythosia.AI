using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.xAI;

[TestClass]
[TestCategory("Unit")]
public class XAIGrok46ContractTests
{
    [TestMethod]
    public void ModelAndEnum_AdditionsPreserveExistingDefaultsAndOrdinals()
    {
        Assert.AreEqual("grok-4.6", typeof(AIModels.xAI).GetField(nameof(AIModels.xAI.Grok4_6))!.GetRawConstantValue());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, new[]
        {
            (int)GrokReasoning.Auto, (int)GrokReasoning.None, (int)GrokReasoning.Low,
            (int)GrokReasoning.Medium, (int)GrokReasoning.High, (int)GrokReasoning.XHigh
        });
        var service = new XAIService("offline-key", new HttpClient(new CaptureHandler()));
        Assert.AreEqual(AIModels.xAI.Grok4_5, service.Model);
        Assert.AreEqual(GrokReasoning.Auto, service.ReasoningEffort);
        Assert.AreSame(service, service.WithGrokReasoning(GrokReasoning.High));
        Assert.AreEqual(GrokReasoning.High, service.ReasoningEffort);
        Assert.AreSame(service, service.WithGrokParameters(GrokReasoning.Medium));
        Assert.AreEqual(GrokReasoning.Medium, service.ReasoningEffort);
    }

    [TestMethod]
    [DataRow("completion")]
    [DataRow("stream")]
    [DataRow("run")]
    public async Task DirectAndCommonReasoning_SerializeEverySupportedLevelAcrossTextPaths(string path)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        service.MaxTokens = 500000;
        foreach (var effort in new[] { GrokReasoning.Auto, GrokReasoning.Low,
                     GrokReasoning.Medium, GrokReasoning.High, GrokReasoning.XHigh })
        {
            var expected = effort == GrokReasoning.Auto ? null : effort.ToString().ToLowerInvariant();
            service.ReasoningEffort = effort;
            Assert.AreEqual("answer", await InvokeAsync(service, path));
            AssertRequest(handler.Requests.Last(), expected, 500000);

            service.ReasoningEffort = GrokReasoning.Medium;
            service.WithReasoning(Enum.Parse<ReasoningLevel>(effort.ToString()));
            Assert.AreEqual("answer", await InvokeAsync(service, path));
            AssertRequest(handler.Requests.Last(), expected, 500000);
            Assert.AreEqual(GrokReasoning.Medium, service.ReasoningEffort);
        }
    }

    [TestMethod]
    [DataRow(GrokReasoning.None)]
    [DataRow((GrokReasoning)999)]
    public async Task UnsupportedDirectEffort_FailsBeforeHistoryAndHttp(GrokReasoning effort)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        service.ReasoningEffort = effort;
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.GetCompletionAsync("invalid"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(ReasoningLevel.None)]
    [DataRow(ReasoningLevel.Minimal)]
    [DataRow(ReasoningLevel.Max)]
    public async Task UnsupportedCommonEffort_FailsBeforeHistoryAndHttp(ReasoningLevel effort)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithReasoning(effort).GetCompletionAsync("invalid"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task UnsupportedCommonCapabilities_AreRejectedInsteadOfDiscarded()
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("invalid"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithWebSearch().GetCompletionAsync("invalid"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.WithFileSearch(new FileSearchStore("xAI", "store-test")).GetCompletionAsync("invalid"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(AIModels.xAI.Grok4_5)]
    [DataRow(AIModels.xAI.Grok4_5Latest)]
    [DataRow(AIModels.xAI.GrokBuildLatest)]
    [DataRow(AIModels.xAI.Grok4_3)]
    [DataRow(AIModels.xAI.Grok4_20Reasoning)]
    [DataRow(AIModels.xAI.Grok4_20NonReasoning)]
    [DataRow(AIModels.xAI.GrokBuild0_1)]
    [DataRow("grok-unknown")]
    public async Task OtherModels_RejectNewXHighAndKeepCommonFeaturePolicy(string model)
    {
        var handler = new CaptureHandler();
        var service = Create(handler, model);
        service.ReasoningEffort = GrokReasoning.XHigh;
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.GetCompletionAsync("invalid"));
        service.ReasoningEffort = GrokReasoning.Auto;
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.Low).GetCompletionAsync("invalid"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow("completion")]
    [DataRow("stream")]
    [DataRow("run")]
    public async Task FunctionRounds_KeepClonedCommonEffortAndNativeCallIds(string path)
    {
        var handler = new CaptureHandler(ToolReply);
        var service = Create(handler);
        var options = new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = ReasoningLevel.XHigh } };
        service.ConfigureRequestFeatures(options);
        options.Reasoning.Level = ReasoningLevel.None;
        var calls = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup", Description = "Gets a value.",
            Handler = _ => { calls++; return Task.FromResult("value"); }
        });

        Assert.AreEqual("answer", await InvokeAsync(service, path));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var request in handler.Requests) AssertRequest(request, "xhigh");
        var messages = Body(handler.Requests[1]).GetProperty("messages");
        var assistant = messages.EnumerateArray().Single(m => m.GetProperty("role").GetString() == "assistant");
        Assert.AreEqual("native-call", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
        var result = messages.EnumerateArray().Single(m => m.GetProperty("role").GetString() == "tool");
        Assert.AreEqual("native-call", result.GetProperty("tool_call_id").GetString());
        Assert.AreEqual(GrokReasoning.High, service.ReasoningEffort);

        await InvokeAsync(service, path);
        AssertRequest(handler.Requests[2], "high");
    }

    [TestMethod]
    [DataRow("completion")]
    [DataRow("run")]
    public async Task FailedRequest_DoesNotLeakCommonEffortToNextCall(string path)
    {
        var handler = new CaptureHandler(TextReply("offline failure", HttpStatusCode.BadRequest));
        var service = Create(handler);
        service.WithReasoning(ReasoningLevel.XHigh);
        await Assert.ThrowsExactlyAsync<AIServiceException>(async () => await InvokeAsync(service, path));
        AssertRequest(handler.Requests[0], "xhigh");
        Assert.AreEqual(GrokReasoning.High, service.ReasoningEffort);

        Assert.AreEqual("answer", await InvokeAsync(service, path));
        AssertRequest(handler.Requests[1], "high");
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task InternalProfiles_UseLowAndRestoreStateAndPendingOptions(bool summarize, bool fail)
    {
        var handler = fail ? new CaptureHandler(TextReply("offline failure", HttpStatusCode.BadRequest)) : new CaptureHandler();
        var service = Create(handler);
        service.WithGrokReasoning(GrokReasoning.XHigh).WithReasoning(ReasoningLevel.Medium);
        var profile = summarize ? RequestProfiles.Summarization : RequestProfiles.QueryRewrite;
        if (fail)
            await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.GetCompletionAsync("internal", profile));
        else
            Assert.AreEqual("answer", await service.GetCompletionAsync("internal", profile));

        AssertRequest(handler.Requests[0], "low", profile.MaxTokens!.Value, expectedTemperature: profile.Temperature!.Value);
        Assert.AreEqual(GrokReasoning.XHigh, service.ReasoningEffort);
        Assert.AreEqual(8000u, service.MaxTokens);
        Assert.AreEqual(0.25f, service.Temperature);
        Assert.IsFalse(service.StatelessMode);
        Assert.IsFalse(service.FunctionsDisabled);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);

        await service.GetCompletionAsync("ordinary");
        AssertRequest(handler.Requests[1], "medium");
        await service.GetCompletionAsync("next");
        AssertRequest(handler.Requests[2], "xhigh");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StructuredRepair_PreservesEffortAndCleansUpAfterSuccessOrFailure(bool fail)
    {
        var handler = new CaptureHandler(TextReply("invalid JSON"), TextReply(fail ? "still invalid" : "{\"Answer\":\"repaired\"}"));
        var service = Create(handler);
        service.StructuredOutputMaxRetries = 1;
        service.WithReasoning(ReasoningLevel.XHigh);
        if (fail)
            await Assert.ThrowsExactlyAsync<StructuredOutputException>(() => service.GetCompletionAsync<StructuredAnswer>("return JSON"));
        else
            Assert.AreEqual("repaired", (await service.GetCompletionAsync<StructuredAnswer>("return JSON")).Answer);

        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            AssertRequest(request, "xhigh");
            Assert.AreEqual("json_object", Body(request).GetProperty("response_format").GetProperty("type").GetString());
        }
        StringAssert.Contains(handler.Requests[1].Body, "STRUCTURED OUTPUT CORRECTION");
        await service.GetCompletionAsync("ordinary");
        AssertRequest(handler.Requests[2], "high");
        Assert.IsFalse(Body(handler.Requests[2]).TryGetProperty("response_format", out _));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task StreamingSummary_IsSeparatedFromFinalTextAndHonorsObservationOptions(bool includeReasoning)
    {
        var reply = new Reply(TextReply("answer").Json,
            """{"choices":[{"delta":{"reasoning_content":"summary","content":"answer"},"finish_reason":"stop"}]}""");
        var handler = new CaptureHandler(reply);
        var service = Create(handler);
        service.WithGrokReasoning(GrokReasoning.XHigh);
        var chunks = new List<StreamingContent>();
        await foreach (var chunk in service.StreamAsync("test", new StreamOptions { IncludeReasoning = includeReasoning }))
            chunks.Add(chunk);
        Assert.AreEqual("answer", string.Concat(chunks.Where(c => c.Type == StreamingContentType.Text).Select(c => c.Content)));
        Assert.AreEqual(includeReasoning ? "summary" : string.Empty,
            string.Concat(chunks.Where(c => c.Type == StreamingContentType.Reasoning).Select(c => c.Content)));
        AssertRequest(handler.Requests.Single(), "xhigh");
    }

    public sealed class StructuredAnswer
    {
        public string Answer { get; set; } = string.Empty;
    }

    private static XAIService Create(CaptureHandler handler, string model = AIModels.xAI.Grok4_6) =>
        new("offline-key", model, new HttpClient(handler))
        {
            ReasoningEffort = GrokReasoning.High,
            Temperature = 0.25f, TopP = 0.75f,
            FrequencyPenalty = 0.5f, PresencePenalty = 0.5f
        };

    private static async Task<string> InvokeAsync(XAIService service, string path)
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

    private static void AssertRequest(CapturedRequest request, string? effort, uint maxTokens = 8000, float expectedTemperature = 0.25f)
    {
        var body = Body(request);
        Assert.AreEqual("/v1/chat/completions", request.Uri.AbsolutePath);
        Assert.AreEqual(AIModels.xAI.Grok4_6, body.GetProperty("model").GetString());
        foreach (var name in new[] { "frequency_penalty", "presence_penalty", "stop" })
            Assert.IsFalse(body.TryGetProperty(name, out _), $"Unexpected {name}.");
        if (effort == null) Assert.IsFalse(body.TryGetProperty("reasoning_effort", out _));
        else Assert.AreEqual(effort, body.GetProperty("reasoning_effort").GetString());
        Assert.AreEqual(expectedTemperature, body.GetProperty("temperature").GetSingle());
        Assert.AreEqual(0.75f, body.GetProperty("top_p").GetSingle());
        Assert.AreEqual(maxTokens, body.GetProperty("max_tokens").GetUInt32());
    }

    private static JsonElement Body(CapturedRequest request) => JsonSerializer.Deserialize<JsonElement>(request.Body);
    private sealed record CapturedRequest(Uri Uri, string Body);
    private sealed record Reply(string Json, string Stream, HttpStatusCode Status = HttpStatusCode.OK);

    private static Reply TextReply(string text, HttpStatusCode status = HttpStatusCode.OK) => new(
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text }, finish_reason = "stop" } } }),
        JsonSerializer.Serialize(new { choices = new[] { new { delta = new { role = "assistant", content = text }, finish_reason = "stop" } } }), status);

    private static readonly Reply ToolReply = new(
        """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"native-call","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""",
        """{"choices":[{"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"native-call","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""");

    private sealed class CaptureHandler(params Reply[] responses) : HttpMessageHandler
    {
        private readonly Queue<Reply> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(captured);
            var reply = _responses.Count > 0 ? _responses.Dequeue() : TextReply("answer");
            var stream = Body(captured).GetProperty("stream").GetBoolean() && reply.Status == HttpStatusCode.OK;
            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(stream ? "data: " + reply.Stream + "\n\ndata: [DONE]\n\n" : reply.Json,
                    Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
