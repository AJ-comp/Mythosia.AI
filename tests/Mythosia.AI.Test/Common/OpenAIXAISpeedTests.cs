using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIXAISpeedTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Speed_IsScopedToOneRequest_AndDefaultOmitsTheParameter(bool xai)
    {
        var handler = new CaptureHandler(Answer(xai, "priority"), Answer(xai, "default"), Answer(xai, null));
        var service = CreateService(xai, handler);
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        Assert.AreEqual("answer", await service.GetCompletionAsync("fast"));
        Assert.AreEqual(xai ? "priority" : "fast", Tier(handler.Requests[0]));
        Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing.Single().AppliedSpeed);
        Assert.AreEqual("priority", service.LastProcessing.Single().RawAppliedMode);

        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Standard });
        await service.GetCompletionAsync("standard");
        Assert.AreEqual("default", Tier(handler.Requests[1]));
        Assert.AreEqual(InferenceSpeed.Standard, service.LastProcessing.Single().RequestedSpeed);

        await service.GetCompletionAsync("provider default");
        Assert.IsNull(Tier(handler.Requests[2]));
        var processing = service.LastProcessing.Single();
        Assert.AreEqual(InferenceSpeed.ProviderDefault, processing.RequestedSpeed);
        Assert.IsNull(processing.AppliedSpeed);
        Assert.IsNull(processing.RawAppliedMode);
        Assert.AreEqual(1, processing.RequestIndex);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReportedDowngrade_IsPreservedWithoutAnAutomaticRetry(bool xai)
    {
        var handler = new CaptureHandler(Answer(xai, "default"));
        var service = CreateService(xai, handler);
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await service.GetCompletionAsync("fast");
        Assert.AreEqual(1, handler.Requests.Count);
        var processing = service.LastProcessing.Single();
        Assert.AreEqual(InferenceSpeed.Fast, processing.RequestedSpeed);
        Assert.AreEqual(InferenceSpeed.Standard, processing.AppliedSpeed);
        Assert.IsTrue(processing.IsDowngraded);
        Assert.AreEqual("resp_answer", processing.ResponseId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedFast_FailsBeforeNetworkAndConversationMutation(bool xai)
    {
        var handler = new CaptureHandler();
        var service = CreateService(xai, handler, "custom-unverified-model");
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync("unsupported"));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.AreEqual(0, service.LastProcessing.Count);
    }

    [TestMethod]
    [DataRow(false, "https://example.com/v1/")]
    [DataRow(true, "https://example.com/v1/")]
    [DataRow(false, "https://api.openai.com:8443/v1/")]
    [DataRow(false, "https://api.openai.com/custom/")]
    [DataRow(false, "https://api.openai.com/v1")]
    [DataRow(false, "http://api.openai.com/v1/")]
    [DataRow(true, "https://api.x.ai:8443/v1/")]
    [DataRow(true, "https://api.x.ai/custom/")]
    [DataRow(true, "https://us.api.x.ai:8443/v1/")]
    [DataRow(true, "https://us.api.x.ai/custom/")]
    public async Task CustomEndpoint_DoesNotInheritFastSupport(bool xai, string endpoint)
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        AIService service = xai ? new XAIService("test", "grok-4.6", client)
            : new OpenAIService("test", "gpt-5.6-sol", client);
        client.BaseAddress = new Uri(endpoint);
        Assert.AreEqual(CapabilitySupport.Unknown, service.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast));
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync("unsupported"));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectedPreparedRequest_ClearsPreviousProcessingBeforeValidation(bool xai)
    {
        var handler = new CaptureHandler(Answer(xai, "priority"));
        var service = CreateService(xai, handler);
        await service.CreateRequest("successful").WithSpeed(InferenceSpeed.Fast).GetCompletionAsync();
        var previous = service.LastProcessing;
        Assert.AreEqual("resp_answer", previous.Single().ResponseId);
        var historyCount = service.ActivateChat.Messages.Count;
        service.ChangeModel("custom-unverified-model");

        await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateRequest("unsupported")
            .WithSpeed(InferenceSpeed.Fast).GetCompletionAsync());

        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(historyCount, service.ActivateChat.Messages.Count);
        Assert.AreEqual(0, service.LastProcessing.Count);
        Assert.AreEqual("resp_answer", previous.Single().ResponseId);
    }

    [TestMethod]
    public async Task OpenAILegacyChat_StandardIsSerializedAndReportedTierRemainsAuthoritative()
    {
        var handler = new CaptureHandler(Answer(true, "priority"));
        var service = CreateService(false, handler, "gpt-4o");
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Standard });
        await service.GetCompletionAsync("chat");
        Assert.AreEqual("/v1/chat/completions", handler.Paths.Single());
        Assert.AreEqual("default", Tier(handler.Requests.Single()));
        Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing.Single().AppliedSpeed);
    }

    [TestMethod]
    [DataRow(false, "fast")]
    [DataRow(false, "priority")]
    [DataRow(true, "priority")]
    public async Task Stream_RecordsTierWithoutMetadata_AndMissingFinalTierDoesNotEraseIt(bool xai, string tier)
    {
        var handler = new CaptureHandler(StreamAnswer(xai, tier)) { Streaming = true };
        var service = CreateService(xai, handler);
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        var text = new StringBuilder();
        await foreach (var part in service.StreamAsync("stream", StreamOptions.TextOnlyOptions))
            if (part.Type == StreamingContentType.Text) text.Append(part.Content);
        Assert.AreEqual("answer", text.ToString());
        Assert.AreEqual(1, service.LastProcessing.Count);
        Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing[0].AppliedSpeed);
        Assert.AreEqual(tier, service.LastProcessing[0].RawAppliedMode);
        Assert.AreEqual("resp_stream", service.LastProcessing[0].ResponseId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RunResult_RetainsProcessingWithoutStreamMetadata(bool xai)
    {
        var handler = new CaptureHandler(StreamAnswer(xai, "default")) { Streaming = true };
        var service = CreateService(xai, handler);
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await using var run = await service.StartRunAsync("run", options: StreamOptions.TextOnlyOptions);
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual(1, result.Processing.Count);
        Assert.AreEqual("default", result.Processing[0].RawAppliedMode);
        Assert.IsTrue(result.Processing[0].IsDowngraded);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ToolContinuation_SendsSameSpeedAndRetainsBothActualTiers(bool xai)
    {
        var handler = new CaptureHandler(ToolAnswer(xai), Answer(xai, "default"));
        var service = CreateService(xai, handler);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "read_value", Description = "Returns a value", Handler = _ => Task.FromResult("42")
        });
        service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        Assert.AreEqual("answer", await service.GetCompletionAsync("use the tool"));
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(request => Tier(request) == (xai ? "priority" : "fast")));
        Assert.AreEqual(2, service.LastProcessing.Count);
        Assert.AreEqual(1, service.LastProcessing[0].RequestIndex);
        Assert.AreEqual(2, service.LastProcessing[1].RequestIndex);
        Assert.AreEqual(InferenceSpeed.Fast, service.LastProcessing[0].AppliedSpeed);
        Assert.AreEqual(InferenceSpeed.Standard, service.LastProcessing[1].AppliedSpeed);
        Assert.AreEqual("resp_tool", service.LastProcessing[0].ResponseId);
        Assert.AreEqual("resp_answer", service.LastProcessing[1].ResponseId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnknownReportedTier_IsRetainedWithoutInventingAnAppliedSpeed(bool xai)
    {
        var handler = new CaptureHandler(Answer(xai, "future_tier"));
        var service = CreateService(xai, handler);
        await service.GetCompletionAsync("default");
        Assert.AreEqual("future_tier", service.LastProcessing.Single().RawAppliedMode);
        Assert.IsNull(service.LastProcessing.Single().AppliedSpeed);
        Assert.IsFalse(service.LastProcessing.Single().IsDowngraded);
    }

    private static AIService CreateService(bool xai, CaptureHandler handler, string? model = null)
        => xai ? new XAIService("test", model ?? "grok-4.6", new HttpClient(handler))
            : new OpenAIService("test", model ?? "gpt-5.6-sol", new HttpClient(handler));

    private static string? Tier(JsonElement request)
        => request.TryGetProperty("service_tier", out var tier) ? tier.GetString() : null;

    private static string Answer(bool chat, string? tier)
    {
        var response = new Dictionary<string, object> { ["id"] = "resp_answer" };
        if (tier != null) response["service_tier"] = tier;
        if (chat)
            response["choices"] = new[] { new { index = 0, message = new { role = "assistant", content = "answer" }, finish_reason = "stop" } };
        else
        {
            response["status"] = "completed";
            response["output_text"] = "answer";
            response["output"] = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "answer" } } } };
        }
        return JsonSerializer.Serialize(response);
    }

    private static string StreamAnswer(bool chat, string tier)
    {
        if (chat)
            return $"data: {{\"id\":\"resp_stream\",\"service_tier\":\"{tier}\",\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"answer\"}},\"finish_reason\":null}}]}}\n\n" +
                "data: {\"id\":\"resp_stream\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        return $"data: {{\"type\":\"response.created\",\"response\":{{\"id\":\"resp_stream\",\"service_tier\":\"{tier}\"}}}}\n\n" +
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"answer\"}\n\n" +
            "data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_stream\",\"status\":\"completed\",\"output\":[]}}\n\n";
    }

    private static string ToolAnswer(bool chat) => chat
        ? """{"id":"resp_tool","service_tier":"priority","choices":[{"index":0,"message":{"role":"assistant","content":"","tool_calls":[{"id":"call_read","type":"function","function":{"name":"read_value","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}"""
        : """{"id":"resp_tool","status":"completed","service_tier":"priority","output":[{"id":"fc_read","call_id":"call_read","type":"function_call","status":"completed","name":"read_value","arguments":"{}"}]}""";

    private sealed class CaptureHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public bool Streaming { get; init; }
        public List<JsonElement> Requests { get; } = new();
        public List<string> Paths { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(document.RootElement.Clone());
            Paths.Add(request.RequestUri!.AbsolutePath);
            Assert.IsTrue(_responses.Count > 0, "Unexpected request or automatic fallback.");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, Streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
