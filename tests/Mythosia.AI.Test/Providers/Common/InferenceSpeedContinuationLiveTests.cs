using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Live")]
[TestCategory("InferenceSpeed")]
[TestCategory("InferenceSpeedContinuation")]
[DoNotParallelize]
public class InferenceSpeedContinuationLiveTests
{
    // Anthropic and Google intentionally exercise Standard. Their account/capacity restrictions
    // prevented verified Fast execution in the initial live checks; these cases do not certify Fast.
    [TestMethod]
    [DataRow("Anthropic", InferenceSpeed.Standard, false)]
    [DataRow("Anthropic", InferenceSpeed.Standard, true)]
    [DataRow("Google", InferenceSpeed.Standard, false)]
    [DataRow("Google", InferenceSpeed.Standard, true)]
    [DataRow("OpenAI", InferenceSpeed.Fast, false)]
    [DataRow("OpenAI", InferenceSpeed.Fast, true)]
    [DataRow("xAI", InferenceSpeed.Fast, false)]
    [DataRow("xAI", InferenceSpeed.Fast, true)]
    public Task ToolContinuation_PreservesModeThroughFinalAnswer(string provider, InferenceSpeed speed, bool useRun)
        => VerifyToolContinuationAsync(provider, provider switch
        {
            "Anthropic" => AIModels.Anthropic.ClaudeOpus5_5,
            "Google" => AIModels.Google.Gemini3_8Flash,
            "OpenAI" => AIModels.OpenAI.Gpt6Astra,
            "xAI" => AIModels.xAI.Grok4_6,
            _ => throw new ArgumentException(provider)
        }, speed, useRun);

    // GPT-5.6 uses Responses HTTP in this adapter, including SSE for Run. These two cases
    // complement Astra's Responses HTTP/WebSocket coverage; they do not exercise Chat Completions.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Gpt56_ResponsesHttpFast_ToolContinuation(bool useRun)
        => VerifyToolContinuationAsync("OpenAI", AIModels.OpenAI.Gpt5_6, InferenceSpeed.Fast, useRun);

    // Standard-only Claude models reject the speed field. Use their ordinary request schema;
    // absent actual-mode telemetry must remain unknown even though Standard was requested.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Sonnet5_StandardOnModelWithoutFast_ToolContinuation(bool useRun)
        => VerifyToolContinuationAsync("Anthropic", AIModels.Anthropic.ClaudeSonnet5, InferenceSpeed.Standard, useRun);

    private static async Task VerifyToolContinuationAsync(string provider, string model, InferenceSpeed speed, bool useRun)
    {
        var secret = provider switch
        {
            "Anthropic" => "momedit-antropic-secret",
            "OpenAI" => "momedit-openai-secret",
            "Google" => "gemini-secret",
            "xAI" => "xai-secret",
            _ => throw new ArgumentException(provider)
        };
        var key = await LiveTestSecrets.GetAsync(secret);
        using var handler = new RequestRecorder();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(4) };
        AIService service = provider switch
        {
            "Anthropic" => new AnthropicService(key, model, http),
            "OpenAI" => new OpenAIService(key, model, http)
            {
                Gpt6ReasoningEffort = Gpt6Reasoning.Low,
                Gpt6ReasoningSummary = null,
                Gpt6Verbosity = Verbosity.Low,
                Gpt5_6ReasoningEffort = Gpt5_6Reasoning.None,
                Gpt5_6ReasoningSummary = null
            },
            "Google" => new GoogleAIService(key, model, http),
            "xAI" => new XAIService(key, model, http),
            _ => throw new ArgumentException(provider)
        };
        service.MaxTokens = 2048;
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = 180, MaxRounds = 2 };
        service.SystemMessage = "Perform this synthetic integration check exactly. Keep reasoning and output minimal. " +
            "Never invent a tool result. After receiving the tool result, return its verification_nonce verbatim.";
        const string toolName = "read_speed_verification_nonce";
        string? expectedNonce = null;
        var invocations = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = toolName,
            Description = "Read the verification nonce for the final answer. Call exactly once with no arguments.",
            AllowAsync = false,
            Handler = _ =>
            {
                Interlocked.Increment(ref invocations);
                // The nonce does not exist until the real tool is invoked, so no prompt can leak it.
                expectedNonce = "SPEED_TOOL_" + Guid.NewGuid().ToString("N");
                return Task.FromResult(JsonSerializer.Serialize(new { verification_nonce = expectedNonce }));
            }
        });
        var request = service.CreateRequest(
                "Call read_speed_verification_nonce exactly once. Then reply with only the exact verification_nonce " +
                "returned by the tool. Do not guess the nonce, and do not call the tool again.")
            .WithSpeed(speed).WithStatelessMode();
        Assert.AreEqual(CapabilitySupport.Supported, request.GetCapabilities().GetSpeedSupport(speed));
        if (provider == "Anthropic" && model == AIModels.Anthropic.ClaudeSonnet5)
            Assert.AreEqual(CapabilitySupport.Unsupported, request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast));

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var nativeWebSocket = provider == "OpenAI" && model == AIModels.OpenAI.Gpt6Astra && useRun;
        var standardOnlyClaude = provider == "Anthropic" && model == AIModels.Anthropic.ClaudeSonnet5;
        string text;
        IReadOnlyList<AIProcessingInfo> processing;
        if (useRun)
        {
            var callbackText = new StringBuilder();
            await using var run = await request.StartRunAsync(onText: value => callbackText.Append(value),
                cancellationToken: timeout.Token);
            Assert.AreEqual(nativeWebSocket, run.CanSteer, "The expected Run transport must be exercised.");
            var result = await run.Result.WaitAsync(timeout.Token);
            text = result.Text;
            processing = result.Processing;
            Assert.AreEqual(text, callbackText.ToString(), "The streamed callback and final result must agree.");
        }
        else
        {
            text = await request.GetCompletionAsync(timeout.Token);
            processing = service.LastProcessing;
        }

        Assert.AreEqual(1, Volatile.Read(ref invocations), "Exactly one real tool invocation is required.");
        Assert.IsNotNull(expectedNonce);
        StringAssert.Contains(text, expectedNonce!);
        Assert.IsTrue(processing.Count >= 2, "Both the tool-call inference and final-answer inference must be observed.");
        CollectionAssert.AreEqual(Enumerable.Range(1, processing.Count).ToArray(),
            processing.Select(item => item.RequestIndex).ToArray());
        var transport = nativeWebSocket ? "responses-websocket" : provider == "OpenAI" ? "responses-http" : "http";
        foreach (var item in processing)
        {
            Console.WriteLine($"LIVE_SPEED_CONTINUATION provider={provider} model={model} path={(useRun ? "run" : "completion")} " +
                $"transport={transport} requested={speed} index={item.RequestIndex} " +
                $"actual={item.AppliedSpeed?.ToString() ?? "unknown"} raw={item.RawAppliedMode ?? "missing"}");
            Assert.AreEqual(speed, item.RequestedSpeed, "The captured mode must survive the tool continuation.");
            if (standardOnlyClaude && item.RawAppliedMode == null)
            {
                Assert.IsNull(item.AppliedSpeed, "Do not infer an applied mode from a Standard request.");
                continue;
            }
            Assert.IsNotNull(item.AppliedSpeed, "A missing actual mode cannot certify processing speed.");
            Assert.AreEqual(speed, item.AppliedSpeed.Value,
                "A reported downgrade is observable, but does not pass this requested-mode verification.");
            Assert.IsFalse(item.IsDowngraded);
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.RawAppliedMode));
        }

        if (nativeWebSocket)
        {
            Assert.AreEqual(0, handler.Requests.Count, "Astra Run must use its native WebSocket transport.");
            return;
        }
        Assert.IsTrue(handler.Requests.Count >= 2, "Both HTTP inference requests must reach the real provider.");
        var expectedPath = provider switch
        {
            "Anthropic" => "/v1/messages",
            "OpenAI" => "/v1/responses",
            "Google" => $"/v1beta/models/{model}:{(useRun ? "streamGenerateContent" : "generateContent")}",
            "xAI" => "/v1/chat/completions",
            _ => throw new ArgumentException(provider)
        };
        var expectedWireMode = provider switch
        {
            "Anthropic" => "standard", "Google" => "standard", "OpenAI" => "fast", "xAI" => "priority",
            _ => throw new ArgumentException(provider)
        };
        foreach (var sent in handler.Requests)
        {
            Assert.AreEqual(expectedPath, sent.Path);
            Assert.AreEqual(standardOnlyClaude ? null : expectedWireMode, sent.Mode,
                "Use the native speed field only when the model accepts it.");
            if (provider != "Google") Assert.AreEqual(useRun, sent.Streaming);
            if (provider == "Anthropic") Assert.AreEqual(!standardOnlyClaude, sent.SpeedBeta,
                "The speed beta accompanies an explicit speed field; ordinary Sonnet requests omit both.");
        }
    }

    // Record only non-sensitive request fields. The real response continues to the provider parser.
    private sealed class RequestRecorder() : DelegatingHandler(new HttpClientHandler())
    {
        public List<SentRequest> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            string? mode = null;
            foreach (var name in new[] { "speed", "service_tier", "serviceTier" })
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    mode = value.GetString();
            var streaming = root.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.True;
            var speedBeta = request.Headers.TryGetValues("anthropic-beta", out var betaValues) &&
                betaValues.Any(value => value.Split(',').Any(beta => beta.Trim() == "fast-mode-2026-02-01"));
            Requests.Add(new SentRequest(request.RequestUri!.AbsolutePath, mode, streaming, speedBeta));
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed record SentRequest(string Path, string? Mode, bool Streaming, bool SpeedBeta);
}
