using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("InferenceSpeed")]
public class AnthropicGoogleSpeedTests
{
    [TestMethod]
    [DataRow(false, InferenceSpeed.ProviderDefault, null)]
    [DataRow(false, InferenceSpeed.Standard, "standard")]
    [DataRow(false, InferenceSpeed.Fast, "fast")]
    [DataRow(true, InferenceSpeed.ProviderDefault, null)]
    [DataRow(true, InferenceSpeed.Standard, "standard")]
    [DataRow(true, InferenceSpeed.Fast, "priority")]
    public async Task RequestedSpeed_UsesProviderWireContract_AndDoesNotLeak(bool google, InferenceSpeed speed, string? wire)
    {
        using var fixture = new Fixture(google);
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = speed });
        await fixture.Service.GetCompletionAsync("configured");
        var first = fixture.Requests.Single();
        Assert.AreEqual(wire, first.Body[fixture.SpeedField]?.GetValue<string>());
        Assert.IsNull(first.Body["generationConfig"]?["serviceTier"]);
        Assert.AreEqual(!google && speed != InferenceSpeed.ProviderDefault, first.Beta.Contains("fast-mode-2026-02-01"));
        Assert.AreEqual(speed, fixture.Service.LastProcessing.Single().RequestedSpeed);

        await fixture.Service.GetCompletionAsync("provider default");
        Assert.IsFalse(fixture.Requests[1].Body.ContainsKey(fixture.SpeedField));
        Assert.IsFalse(fixture.Requests[1].Beta.Contains("fast-mode-2026-02-01"));
        Assert.AreEqual(InferenceSpeed.ProviderDefault, fixture.Service.LastProcessing.Single().RequestedSpeed);
        Assert.AreEqual(1, fixture.Service.LastProcessing.Single().RequestIndex);
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus4_8)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    public async Task ClaudeFast_SupportsOnlyCurrentDocumentedModels(string model)
    {
        using var fixture = new Fixture(false, model);
        Assert.AreEqual(CapabilitySupport.Supported, fixture.Service.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast));
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await fixture.Service.GetCompletionAsync("fast");
        Assert.AreEqual("fast", fixture.Requests.Single().Body["speed"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task ClaudeSonnetStandard_UsesNormalRequestWithoutUnsupportedSpeedParameter(int execution)
    {
        using var fixture = new Fixture(false, AIModels.Anthropic.ClaudeSonnet5) { ModeForResponse = _ => null };
        Assert.AreEqual(CapabilitySupport.Supported, fixture.Service.GetCapabilities().GetSpeedSupport(InferenceSpeed.Standard));
        Assert.AreEqual(CapabilitySupport.Unsupported, fixture.Service.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast));
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Standard });

        var processing = (await Execute(fixture.Service, execution)).Single();
        var request = fixture.Requests.Single();
        Assert.IsFalse(request.Body.ContainsKey("speed"));
        Assert.IsFalse(request.Beta.Contains("fast-mode-2026-02-01"));
        Assert.AreEqual(InferenceSpeed.Standard, processing.RequestedSpeed);
        Assert.IsNull(processing.AppliedSpeed);
        Assert.IsNull(processing.RawAppliedMode);
        Assert.IsFalse(processing.IsDowngraded);
    }

    [TestMethod]
    [DataRow(AIModels.Google.Gemini2_5Pro)]
    [DataRow(AIModels.Google.Gemini2_5Flash)]
    [DataRow(AIModels.Google.Gemini2_5FlashLite)]
    [DataRow(AIModels.Google.Gemini3FlashPreview)]
    [DataRow(AIModels.Google.Gemini3_1ProPreview)]
    [DataRow(AIModels.Google.Gemini3_1FlashLite)]
    [DataRow(AIModels.Google.Gemini3_5Flash)]
    [DataRow(AIModels.Google.Gemini3_5FlashLite)]
    [DataRow(AIModels.Google.Gemini3_6Flash)]
    [DataRow(AIModels.Google.Gemini3_7Flash)]
    [DataRow(AIModels.Google.Gemini3_8Flash)]
    public async Task GeminiFast_SupportsDocumentedChatModelsOnGenerateContent(string model)
    {
        using var fixture = new Fixture(true, model);
        Assert.AreEqual(CapabilitySupport.Supported, fixture.Service.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast));
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await fixture.Service.GetCompletionAsync("fast");
        Assert.AreEqual("priority", fixture.Requests.Single().Body["serviceTier"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false, "claude-opus-4-6")]
    [DataRow(false, "claude-opus-4-7")]
    [DataRow(false, "claude-sonnet-4-6")]
    [DataRow(false, "claude-opus-5-5-20260901")]
    [DataRow(false, "claude-opus-5-future")]
    [DataRow(true, "gemini-future-fast")]
    [DataRow(true, "gemini-3.8-flash-future")]
    public async Task UnsupportedFast_RejectsBeforeHistoryOrTransport(bool google, string model)
    {
        using var fixture = new Fixture(google, model);
        Assert.AreEqual(CapabilitySupport.Unsupported, fixture.Service.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast));
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("reject"));
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NonNativeEndpoint_RejectsFast(bool google)
    {
        using var fixture = new Fixture(google);
        fixture.Client.BaseAddress = new Uri("https://gateway.example.test/");
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync("reject"));
        Assert.AreEqual(0, fixture.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    public async Task ToolContinuations_RetainSnapshot_AndRecordEachTransport(bool google, int execution)
    {
        using var fixture = new Fixture(google) { ToolFirst = true };
        var executed = 0;
        fixture.Service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Handler = _ => { executed++; return Task.FromResult("tool-result"); }
        });
        var options = new AIRequestFeatures { Speed = InferenceSpeed.Fast };
        fixture.Service.ConfigureRequestFeatures(options);
        options.Speed = InferenceSpeed.Standard;
        fixture.BeforeResponse = index =>
        {
            if (index == 0)
                fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Standard });
        };
        fixture.ModeForResponse = index => index == 0 ? fixture.FastWire : "standard";
        var processing = await Execute(fixture.Service, execution);
        Assert.AreEqual(1, executed);
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.IsTrue(fixture.Requests.All(request => request.Body[fixture.SpeedField]?.GetValue<string>() == fixture.FastWire));
        Assert.AreEqual(2, processing.Count);
        Assert.AreEqual(1, processing[0].RequestIndex);
        Assert.AreEqual(2, processing[1].RequestIndex);
        Assert.AreEqual(InferenceSpeed.Fast, processing[0].AppliedSpeed);
        Assert.AreEqual(InferenceSpeed.Standard, processing[1].AppliedSpeed);
        Assert.AreEqual("response-0", processing[0].ResponseId);
        Assert.AreEqual("response-1", processing[1].ResponseId);
        Assert.IsFalse(processing[0].IsDowngraded);
        Assert.IsTrue(processing[1].IsDowngraded);

        await fixture.Service.GetCompletionAsync("next request");
        Assert.AreEqual("standard", fixture.Requests[2].Body[fixture.SpeedField]?.GetValue<string>());
        Assert.AreEqual(2, processing.Count, "Earlier observations are immutable snapshots.");
    }

    [TestMethod]
    [DataRow(false, null)]
    [DataRow(false, "future_mode")]
    [DataRow(true, null)]
    [DataRow(true, "future_tier")]
    public async Task UnreportedOrUnknownMode_DoesNotClaimFastWasApplied(bool google, string? mode)
    {
        using var fixture = new Fixture(google) { ModeForResponse = _ => mode };
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await fixture.Service.GetCompletionAsync("observe");
        var processing = fixture.Service.LastProcessing.Single();
        Assert.IsNull(processing.AppliedSpeed);
        Assert.AreEqual(mode, processing.RawAppliedMode);
        Assert.IsFalse(processing.IsDowngraded);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task GeminiHeader_IsRetainedWhenLaterUsageOmitsTier(int execution)
    {
        using var fixture = new Fixture(true) { HeaderMode = "standard", ModeForResponse = _ => null };
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        var processing = (await Execute(fixture.Service, execution)).Single();
        Assert.AreEqual(InferenceSpeed.Standard, processing.AppliedSpeed);
        Assert.AreEqual("standard", processing.RawAppliedMode);
        Assert.IsTrue(processing.IsDowngraded);
    }

    [TestMethod]
    public async Task GeminiReportedUsageTier_OverridesEarlierHeader()
    {
        using var fixture = new Fixture(true) { HeaderMode = "priority", ModeForResponse = _ => "standard" };
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        var processing = (await Execute(fixture.Service, 1)).Single();
        Assert.AreEqual("standard", processing.RawAppliedMode);
        Assert.AreEqual(InferenceSpeed.Standard, processing.AppliedSpeed);
    }

    [TestMethod]
    [DataRow("fast", InferenceSpeed.Fast)]
    [DataRow("standard", InferenceSpeed.Standard)]
    public async Task ClaudeFinalDeltaUsage_IsObservedEvenWhenStartOmitsSpeed(string mode, InferenceSpeed applied)
    {
        using var fixture = new Fixture(false) { ClaudeModeAtFinalDelta = true, ModeForResponse = _ => mode };
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        var processing = (await Execute(fixture.Service, 1)).Single();
        Assert.AreEqual(applied, processing.AppliedSpeed);
        Assert.AreEqual(mode, processing.RawAppliedMode);
        Assert.AreEqual("response-0", processing.ResponseId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CapacityError_DoesNotRetryAtAnotherSpeed(bool google)
    {
        using var fixture = new Fixture(google) { Status = HttpStatusCode.TooManyRequests };
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetCompletionAsync("capacity"));
        Assert.AreEqual(1, fixture.Requests.Count);
        Assert.AreEqual(fixture.FastWire, fixture.Requests.Single().Body[fixture.SpeedField]?.GetValue<string>());
        Assert.IsNull(fixture.Service.LastProcessing.Single().AppliedSpeed);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task TokenCounting_OmitsSpeedAndFastBeta_AndPreservesPendingOption(bool google, bool promptOnly)
    {
        using var fixture = new Fixture(google);
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        if (promptOnly) Assert.AreEqual(7u, await fixture.Service.GetInputTokenCountAsync("count"));
        else Assert.AreEqual(7u, await fixture.Service.GetInputTokenCountAsync());
        var countRequest = fixture.Requests.Single();
        Assert.IsFalse(countRequest.Body.ToJsonString().Contains("\"speed\""));
        Assert.IsFalse(countRequest.Body.ToJsonString().Contains("serviceTier"));
        Assert.IsFalse(countRequest.Beta.Contains("fast-mode-2026-02-01"));
        Assert.AreEqual(0, fixture.Service.LastProcessing.Count);
        await fixture.Service.GetCompletionAsync("generation");
        Assert.AreEqual(fixture.FastWire, fixture.Requests[1].Body[fixture.SpeedField]?.GetValue<string>());
        Assert.AreEqual(1, fixture.Service.LastProcessing.Single().RequestIndex);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TokenCounting_AfterGeneration_PreservesCompletedProcessing(bool google)
    {
        using var fixture = new Fixture(google);
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await fixture.Service.GetCompletionAsync("generate first");
        var generated = fixture.Service.LastProcessing.Single();

        Assert.AreEqual(7u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual(7u, await fixture.Service.GetInputTokenCountAsync("count another prompt"));
        var afterCounting = fixture.Service.LastProcessing.Single();
        Assert.AreEqual(generated.RequestIndex, afterCounting.RequestIndex);
        Assert.AreEqual(generated.RequestedSpeed, afterCounting.RequestedSpeed);
        Assert.AreEqual(generated.AppliedSpeed, afterCounting.AppliedSpeed);
        Assert.AreEqual(generated.RawAppliedMode, afterCounting.RawAppliedMode);
        Assert.AreEqual(generated.ResponseId, afterCounting.ResponseId);
        Assert.AreEqual(3, fixture.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StreamingTransportFailure_RetainsRequestedSpeedWithoutInventingAppliedSpeed(bool google)
    {
        using var fixture = new Fixture(google)
        {
            BeforeResponseAsync = (_, _) => Task.FromException(new HttpRequestException("connection failed"))
        };
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => ConsumeStream(fixture.Service));
        AssertUnreportedFastAttempt(fixture);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StreamingCancellationBeforeResponse_RetainsRequestedSpeedWithoutInventingAppliedSpeed(bool google)
    {
        using var cancellation = new CancellationTokenSource();
        var enteredTransport = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(google)
        {
            BeforeResponseAsync = (_, token) =>
            {
                enteredTransport.TrySetResult(true);
                return Task.Delay(Timeout.Infinite, token);
            }
        };
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        var consuming = ConsumeStream(fixture.Service, cancellation.Token);
        await enteredTransport.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => consuming);
        AssertUnreportedFastAttempt(fixture);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StructuredOutputRepair_RetainsSpeedAndSequentialAttemptObservations(bool google)
    {
        using var fixture = new Fixture(google)
        {
            TextForResponse = index => index == 0 ? "not-json" : "{\"Value\":7}"
        };
        fixture.Service.StructuredOutputMaxRetries = 1;
        fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Fast });
        fixture.BeforeResponse = index =>
        {
            if (index == 0)
                fixture.Service.ConfigureRequestFeatures(new AIRequestFeatures { Speed = InferenceSpeed.Standard });
        };

        var answer = await fixture.Service.GetCompletionAsync<StructuredAnswer>("return a JSON object");
        Assert.AreEqual(7, answer.Value);
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.IsTrue(fixture.Requests.All(request => request.Body[fixture.SpeedField]?.GetValue<string>() == fixture.FastWire));
        var processing = fixture.Service.LastProcessing;
        CollectionAssert.AreEqual(new[] { 1, 2 }, processing.Select(attempt => attempt.RequestIndex).ToArray());
        CollectionAssert.AreEqual(new[] { "response-0", "response-1" }, processing.Select(attempt => attempt.ResponseId).ToArray());
        Assert.IsTrue(processing.All(attempt => attempt.RequestedSpeed == InferenceSpeed.Fast && attempt.AppliedSpeed == InferenceSpeed.Fast));
    }

    private static async Task ConsumeStream(AIService service, CancellationToken cancellationToken = default)
    {
        await foreach (var _ in service.StreamAsync("stream", StreamOptions.FullOptions, cancellationToken)) { }
    }

    private static void AssertUnreportedFastAttempt(Fixture fixture)
    {
        Assert.AreEqual(1, fixture.Requests.Count, "A failed transport must not trigger a speed fallback.");
        Assert.AreEqual(fixture.FastWire, fixture.Requests.Single().Body[fixture.SpeedField]?.GetValue<string>());
        var attempt = fixture.Service.LastProcessing.Single();
        Assert.AreEqual(1, attempt.RequestIndex);
        Assert.AreEqual(InferenceSpeed.Fast, attempt.RequestedSpeed);
        Assert.IsNull(attempt.AppliedSpeed);
        Assert.IsNull(attempt.RawAppliedMode);
        Assert.IsNull(attempt.ResponseId);
        Assert.IsFalse(attempt.IsDowngraded);
    }

    public sealed class StructuredAnswer
    {
        public int Value { get; set; }
    }

    private static async Task<IReadOnlyList<AIProcessingInfo>> Execute(AIService service, int execution)
    {
        if (execution == 0) await service.GetCompletionAsync("perform lookup if needed");
        else if (execution == 1)
        {
            await foreach (var chunk in service.StreamAsync("perform lookup if needed", StreamOptions.FullOptions))
                Assert.AreNotEqual(StreamingContentType.Error, chunk.Type, chunk.Content);
        }
        else
        {
            await using var run = await service.StartRunAsync("perform lookup if needed");
            return (await run.Result).Processing;
        }
        return service.LastProcessing;
    }

    private sealed record CapturedRequest(JsonObject Body, string Beta);

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly bool _google;
        public HttpClient Client { get; }
        public AIService Service { get; }
        public List<CapturedRequest> Requests { get; } = new();
        public bool ToolFirst { get; init; }
        public bool ClaudeModeAtFinalDelta { get; init; }
        public string? HeaderMode { get; init; }
        public Func<int, string?> ModeForResponse { get; set; }
        public Func<int, string> TextForResponse { get; init; } = _ => "answer";
        public Action<int>? BeforeResponse { get; set; }
        public Func<int, CancellationToken, Task>? BeforeResponseAsync { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string SpeedField => _google ? "serviceTier" : "speed";
        public string FastWire => _google ? "priority" : "fast";

        public Fixture(bool google, string? model = null)
        {
            _google = google;
            Client = new HttpClient(this, disposeHandler: false);
            Service = google
                ? new GoogleAIService("offline-test-key", model ?? AIModels.Google.Gemini3_8Flash, Client)
                : new AnthropicService("offline-test-key", model ?? AIModels.Anthropic.ClaudeOpus4_8, Client);
            Service.DefaultPolicy.TimeoutSeconds = 10;
            ModeForResponse = _ => FastWire;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var index = Requests.Count;
            Requests.Add(new CapturedRequest(body, request.Headers.TryGetValues("anthropic-beta", out var betas) ? string.Join(",", betas) : ""));
            BeforeResponse?.Invoke(index);
            if (BeforeResponseAsync != null) await BeforeResponseAsync(index, cancellationToken);
            var count = request.RequestUri!.AbsolutePath.Contains("count", StringComparison.OrdinalIgnoreCase);
            var stream = body["stream"]?.GetValue<bool>() == true || request.RequestUri!.AbsolutePath.Contains("streamGenerateContent");
            var reply = count ? new JsonObject { [_google ? "totalTokens" : "input_tokens"] = 7 }
                : Status != HttpStatusCode.OK ? new JsonObject { ["error"] = new JsonObject { ["type"] = "rate_limit_error", ["message"] = "capacity exhausted" } }
                : _google ? GeminiReply(index, ModeForResponse(index), ToolFirst && index == 0, TextForResponse(index))
                : ClaudeReply(index, ModeForResponse(index), ToolFirst && index == 0, TextForResponse(index));
            var result = new HttpResponseMessage(Status)
            {
                Content = new StringContent(stream && !count && Status == HttpStatusCode.OK
                    ? _google ? GeminiStream(reply) : ClaudeStream(reply, ClaudeModeAtFinalDelta)
                    : reply.ToJsonString(), Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
            if (HeaderMode != null) result.Headers.Add("x-gemini-service-tier", HeaderMode);
            return result;
        }

        protected override void Dispose(bool disposing) { if (disposing) Client.Dispose(); base.Dispose(disposing); }
    }

    private static JsonObject ClaudeReply(int index, string? mode, bool tool, string text)
    {
        var usage = new JsonObject { ["input_tokens"] = 3, ["output_tokens"] = 2 };
        if (mode != null) usage["speed"] = mode;
        var blocks = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
        if (tool) blocks.Add(new JsonObject { ["type"] = "tool_use", ["id"] = "lookup-0", ["name"] = "lookup", ["input"] = new JsonObject() });
        return new JsonObject { ["id"] = "response-" + index, ["content"] = blocks, ["stop_reason"] = tool ? "tool_use" : "end_turn", ["usage"] = usage };
    }

    private static JsonObject GeminiReply(int index, string? mode, bool tool, string text)
    {
        var usage = new JsonObject { ["promptTokenCount"] = 3, ["candidatesTokenCount"] = 2, ["totalTokenCount"] = 5 };
        if (mode != null) usage["serviceTier"] = mode;
        var parts = new JsonArray(new JsonObject { ["text"] = text });
        if (tool) parts.Add(new JsonObject
        {
            ["functionCall"] = new JsonObject { ["id"] = "lookup-0", ["name"] = "lookup", ["args"] = new JsonObject() },
            ["thoughtSignature"] = "opaque-test-signature"
        });
        return new JsonObject
        {
            ["responseId"] = "response-" + index, ["usageMetadata"] = usage,
            ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["role"] = "model", ["parts"] = parts }, ["finishReason"] = "STOP" })
        };
    }

    private static string GeminiStream(JsonObject reply)
    {
        // A trailing usage-only frame without a service tier must not erase earlier observations.
        return "data: " + reply.ToJsonString() + "\n\ndata: {\"usageMetadata\":{\"totalTokenCount\":5}}\n\n";
    }

    private static string ClaudeStream(JsonObject reply, bool modeAtFinalDelta)
    {
        var start = (JsonObject)reply.DeepClone();
        start["content"] = new JsonArray();
        if (modeAtFinalDelta) start["usage"]!.AsObject().Remove("speed");
        var frames = new List<JsonObject> { new() { ["type"] = "message_start", ["message"] = start } };
        var index = 0;
        foreach (var original in reply["content"]!.AsArray().OfType<JsonObject>())
        {
            var block = (JsonObject)original.DeepClone();
            var tool = block["type"]!.GetValue<string>() == "tool_use";
            if (tool) block["input"] = new JsonObject(); else block["text"] = "";
            frames.Add(new JsonObject { ["type"] = "content_block_start", ["index"] = index, ["content_block"] = block });
            frames.Add(new JsonObject
            {
                ["type"] = "content_block_delta", ["index"] = index,
                ["delta"] = new JsonObject { ["type"] = tool ? "input_json_delta" : "text_delta", [tool ? "partial_json" : "text"] = tool ? "{}" : original["text"]!.GetValue<string>() }
            });
            frames.Add(new JsonObject { ["type"] = "content_block_stop", ["index"] = index++ });
        }
        var finalUsage = new JsonObject { ["output_tokens"] = 2 };
        if (modeAtFinalDelta) finalUsage["speed"] = reply["usage"]?["speed"]?.DeepClone();
        frames.Add(new JsonObject
        {
            ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = reply["stop_reason"]!.DeepClone() },
            ["usage"] = finalUsage
        });
        frames.Add(new JsonObject { ["type"] = "message_stop" });
        return string.Concat(frames.Select(frame => "data: " + frame.ToJsonString() + "\n\n"));
    }
}
