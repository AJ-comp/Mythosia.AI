using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AIRequestBuilderPrimaryProviderTests
{
    private const string OpenAIAnswer = """{"id":"r1","status":"completed","output_text":"answer","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"answer"}]}]}""";
    private const string ChatAnswer = """{"choices":[{"message":{"role":"assistant","content":"answer"},"finish_reason":"stop"}]}""";
    private const string GoogleAnswer = """{"candidates":[{"content":{"role":"model","parts":[{"text":"answer"}]},"finishReason":"STOP"}]}""";
    private const string ClaudeAnswer = """{"id":"a1","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";

    [TestMethod]
    public async Task OpenAI_BranchesKeepCapturedSamplingAndModel_WithoutMutatingDefaults()
    {
        using var handler = new CaptureHandler(ChatAnswer);
        using var client = new HttpClient(handler);
        var service = new OpenAIService("test-key", "gpt-4o", client)
        { Temperature = 0.5f, TopP = 0.7f, MaxTokens = 600, SystemMessage = "captured system" };
        var basis = service.CreateRequest("explain");
        var summary = basis.WithTemperature(0.2f);
        var creative = basis.WithTemperature(0.8f);
        service.Temperature = 0.9f;
        service.TopP = 0.3f;
        service.MaxTokens = 2000;
        service.SystemMessage = "new default";
        service.ChangeModel("gpt-6-astra");
        handler.BeforeResponse = () =>
        {
            Assert.AreEqual(0.9f, service.Temperature);
            Assert.AreEqual("gpt-6-astra", service.Model);
            Assert.AreEqual("new default", service.SystemMessage);
        };

        Assert.AreEqual("answer", await summary.GetCompletionAsync());
        Assert.AreEqual("answer", await creative.GetCompletionAsync());
        Assert.AreEqual("answer", await basis.GetCompletionAsync());

        CollectionAssert.AreEqual(new[] { 0.2f, 0.8f, 0.5f },
            handler.Bodies.Select(body => body.GetProperty("temperature").GetSingle()).ToArray());
        foreach (var body in handler.Bodies)
        {
            Assert.AreEqual("gpt-4o", body.GetProperty("model").GetString());
            Assert.AreEqual(0.7f, body.GetProperty("top_p").GetSingle());
            Assert.AreEqual(600, body.GetProperty("max_tokens").GetInt32());
            Assert.AreEqual("captured system", body.GetProperty("messages")[0].GetProperty("content").GetString());
        }
        Assert.IsTrue(handler.Uris.All(uri => uri.AbsolutePath.EndsWith("/chat/completions")));
    }

    [TestMethod]
    public async Task OpenAI_CapturedNativeReasoning_AndFluentReasoningRemainIndependent()
    {
        using var handler = new CaptureHandler(OpenAIAnswer);
        using var client = new HttpClient(handler);
        var service = new OpenAIService("test-key", "gpt-6-astra", client)
        { Gpt6ReasoningEffort = Gpt6Reasoning.High, Gpt6Verbosity = Verbosity.High };
        var basis = service.CreateRequest("solve");
        var lower = basis.WithReasoning(ReasoningLevel.Low);
        service.Gpt6ReasoningEffort = Gpt6Reasoning.Max;
        service.Gpt6Verbosity = Verbosity.Low;
        service.ChangeModel("gpt-4o");

        Assert.AreEqual("answer", await lower.GetCompletionAsync());
        Assert.AreEqual("answer", await basis.GetCompletionAsync());

        Assert.AreEqual("low", handler.Bodies[0].GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.AreEqual("high", handler.Bodies[1].GetProperty("reasoning").GetProperty("effort").GetString());
        foreach (var body in handler.Bodies)
        {
            Assert.AreEqual("gpt-6-astra", body.GetProperty("model").GetString());
            Assert.AreEqual("high", body.GetProperty("text").GetProperty("verbosity").GetString());
        }
        Assert.AreEqual(Gpt6Reasoning.Max, service.Gpt6ReasoningEffort);
        Assert.AreEqual(Verbosity.Low, service.Gpt6Verbosity);
        Assert.AreEqual("gpt-4o", service.Model);
    }

    [TestMethod]
    public async Task Google_CapturesSamplingThinkingAndSafety_AlongWithEndpointModel()
    {
        using var handler = new CaptureHandler(GoogleAnswer);
        using var client = new HttpClient(handler);
        var service = new GoogleAIService("test-key", "gemini-2.5-flash", client)
        { Temperature = 0.5f, ThinkingBudget = 1024, HarassmentSafetyThreshold = GeminiSafetyThreshold.BlockLowAndAbove };
        var request = service.CreateRequest("explain").WithTemperature(0.2f);
        service.Temperature = 0.9f;
        service.ThinkingBudget = 0;
        service.HarassmentSafetyThreshold = GeminiSafetyThreshold.Off;
        service.ChangeModel("gemini-3.8-flash");

        Assert.AreEqual("answer", await request.GetCompletionAsync());

        var body = handler.Bodies.Single();
        var config = body.GetProperty("generationConfig");
        Assert.AreEqual(0.2f, config.GetProperty("temperature").GetSingle());
        Assert.AreEqual(1024, config.GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
        Assert.AreEqual("BLOCK_LOW_AND_ABOVE", body.GetProperty("safetySettings")[0].GetProperty("threshold").GetString());
        StringAssert.Contains(handler.Uris.Single().AbsolutePath, "gemini-2.5-flash:generateContent");
        Assert.AreEqual(0.9f, service.Temperature);
        Assert.AreEqual(0, service.ThinkingBudget);
        Assert.AreEqual(GeminiSafetyThreshold.Off, service.HarassmentSafetyThreshold);
        Assert.AreEqual("gemini-3.8-flash", service.Model);
    }

    [TestMethod]
    public async Task Google_FluentReasoningUsesCapturedModel_AndDoesNotChangeNativeDefaults()
    {
        using var handler = new CaptureHandler(GoogleAnswer);
        using var client = new HttpClient(handler);
        var service = new GoogleAIService("test-key", "gemini-3.6-flash", client)
        { ThinkingLevel = GeminiThinkingLevel.High };
        var basis = service.CreateRequest("solve");
        var lower = basis.WithReasoning(ReasoningLevel.Low);
        service.ThinkingLevel = GeminiThinkingLevel.Minimal;
        service.ChangeModel("gemini-2.5-flash");

        await lower.GetCompletionAsync();
        await basis.GetCompletionAsync();

        Assert.AreEqual("LOW", handler.Bodies[0].GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.AreEqual("HIGH", handler.Bodies[1].GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.AreEqual(GeminiThinkingLevel.Minimal, service.ThinkingLevel);
        Assert.IsTrue(handler.Uris.All(uri => uri.AbsolutePath.Contains("gemini-3.6-flash:")));
    }

    [TestMethod]
    public async Task Anthropic_CapturedDefaultsDoNotInheritLaterAdaptiveThinkingConfiguration()
    {
        using var handler = new CaptureHandler(ClaudeAnswer);
        using var client = new HttpClient(handler);
        var service = new AnthropicService("test-key", "claude-sonnet-4-6", client)
        { Temperature = 0.5f, ThinkingBudget = -1 };
        var request = service.CreateRequest("explain").WithTemperature(0.2f);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Omitted);
        service.Temperature = 0.9f;
        service.ChangeModel("claude-fable-5-1");

        Assert.AreEqual("answer", await request.GetCompletionAsync());

        var body = handler.Bodies.Single();
        Assert.AreEqual("claude-sonnet-4-6", body.GetProperty("model").GetString());
        Assert.AreEqual(0.2f, body.GetProperty("temperature").GetSingle());
        Assert.IsFalse(body.TryGetProperty("thinking", out _));
        Assert.AreEqual(ClaudeReasoningEffort.High, service.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingDisplay.Omitted, service.AdaptiveThinkingDisplay);
        Assert.AreEqual(1024, service.ThinkingBudget);
        Assert.AreEqual(0.9f, service.Temperature);
        Assert.AreEqual("claude-fable-5-1", service.Model);
    }

    [TestMethod]
    public async Task Anthropic_FluentReasoningKeepsCapturedDisplay_AndPreservesNativeDefaults()
    {
        using var handler = new CaptureHandler(ClaudeAnswer);
        using var client = new HttpClient(handler);
        var service = new AnthropicService("test-key", "claude-sonnet-4-6", client);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Summarized);
        var basis = service.CreateRequest("solve");
        var lower = basis.WithReasoning(ReasoningLevel.Low);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.Max, ClaudeThinkingDisplay.Omitted);

        await lower.GetCompletionAsync();
        await basis.GetCompletionAsync();

        Assert.AreEqual("low", handler.Bodies[0].GetProperty("output_config").GetProperty("effort").GetString());
        Assert.AreEqual("high", handler.Bodies[1].GetProperty("output_config").GetProperty("effort").GetString());
        foreach (var body in handler.Bodies)
            Assert.AreEqual("summarized", body.GetProperty("thinking").GetProperty("display").GetString());
        Assert.AreEqual(ClaudeReasoningEffort.Max, service.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingDisplay.Omitted, service.AdaptiveThinkingDisplay);
    }

    [TestMethod]
    public async Task OpenAI_ProfileNeverTemporarilyOverwritesPublicDefaults()
    {
        using var handler = new CaptureHandler(OpenAIAnswer);
        using var client = new HttpClient(handler);
        var service = new OpenAIService("test-key", "gpt-6-astra", client)
        { Gpt6ReasoningEffort = Gpt6Reasoning.Max, Gpt6ReasoningMode = Gpt6ReasoningMode.Pro, MaxTokens = 16000 };
        handler.BeforeResponse = () =>
        {
            Assert.AreEqual(Gpt6Reasoning.Max, service.Gpt6ReasoningEffort);
            Assert.AreEqual(Gpt6ReasoningMode.Pro, service.Gpt6ReasoningMode);
            Assert.AreEqual(16000u, service.MaxTokens);
            Assert.IsFalse(service.StatelessMode);
        };

        await service.GetCompletionAsync("short", RequestProfiles.Summarization);

        Assert.AreEqual("low", handler.Bodies[0].GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.IsFalse(handler.Bodies[0].GetProperty("reasoning").TryGetProperty("mode", out _));
        Assert.AreEqual(4096, handler.Bodies[0].GetProperty("max_output_tokens").GetInt32());
    }

    [TestMethod]
    public async Task Google_ProfileNeverTemporarilyOverwritesPublicDefaults()
    {
        using var handler = new CaptureHandler(GoogleAnswer);
        using var client = new HttpClient(handler);
        var service = new GoogleAIService("test-key", "gemini-3.6-flash", client)
        { ThinkingLevel = GeminiThinkingLevel.High, ThinkingBudget = 2048, MaxTokens = 16000, Temperature = 0.9f };
        handler.BeforeResponse = () =>
        {
            Assert.AreEqual(GeminiThinkingLevel.High, service.ThinkingLevel);
            Assert.AreEqual(2048, service.ThinkingBudget);
            Assert.AreEqual(16000u, service.MaxTokens);
            Assert.AreEqual(0.9f, service.Temperature);
            Assert.IsFalse(service.StatelessMode);
        };

        await service.GetCompletionAsync("short", RequestProfiles.Summarization);

        var config = handler.Bodies[0].GetProperty("generationConfig");
        Assert.AreEqual("MINIMAL", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.AreEqual(1024, config.GetProperty("maxOutputTokens").GetInt32());
    }

    [TestMethod]
    public async Task BuilderProfilesRetainProviderBudgetRules_AndHonorLaterTokenOverrides()
    {
        using var handler = new CaptureHandler(OpenAIAnswer);
        using var client = new HttpClient(handler);
        var service = new OpenAIService("test-key", "gpt-6-astra", client)
        { Gpt6ReasoningEffort = Gpt6Reasoning.High, MaxTokens = 16000 };
        var basis = service.CreateRequest("summarize");
        var summary = basis.WithProfile(RequestProfiles.Summarization);
        var largerSummary = summary.WithMaxTokens(6000);

        await summary.GetCompletionAsync();
        await largerSummary.GetCompletionAsync();

        Assert.AreEqual(4096, handler.Bodies[0].GetProperty("max_output_tokens").GetInt32());
        Assert.AreEqual(6000, handler.Bodies[1].GetProperty("max_output_tokens").GetInt32());
        Assert.AreEqual(16000u, service.MaxTokens);
        Assert.AreEqual(Gpt6Reasoning.High, service.Gpt6ReasoningEffort);
    }

    [TestMethod]
    public async Task Anthropic_ProfileNeverTemporarilyOverwritesPublicDefaults()
    {
        using var handler = new CaptureHandler(ClaudeAnswer);
        using var client = new HttpClient(handler);
        var service = new AnthropicService("test-key", "claude-sonnet-4-6", client) { MaxTokens = 16000 };
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Summarized);
        handler.BeforeResponse = () =>
        {
            Assert.AreEqual(ClaudeReasoningEffort.High, service.AdaptiveThinkingEffort);
            Assert.AreEqual(ClaudeThinkingDisplay.Summarized, service.AdaptiveThinkingDisplay);
            Assert.AreEqual(1024, service.ThinkingBudget);
            Assert.AreEqual(16000u, service.MaxTokens);
            Assert.IsFalse(service.StatelessMode);
        };

        await service.GetCompletionAsync("short", RequestProfiles.Summarization);

        Assert.IsFalse(handler.Bodies[0].TryGetProperty("thinking", out _));
        Assert.AreEqual(1024, handler.Bodies[0].GetProperty("max_tokens").GetInt32());
    }

    [TestMethod]
    public async Task OpenAI_ToolContinuationKeepsCapturedSettingsAndHandlers_WhenDefaultsChangeMidRequest()
    {
        const string toolResponse = """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""";
        using var handler = new CaptureHandler(toolResponse, ChatAnswer);
        using var client = new HttpClient(handler);
        var service = new OpenAIService("test-key", "gpt-4o", client) { Temperature = 0.2f };
        var calls = 0;
        service.Functions.Add(new FunctionDefinition
        { Name = "lookup", Description = "Look up a value", Handler = _ => { calls++; return Task.FromResult("found"); } });
        var request = service.CreateRequest("look up a value");
        handler.BeforeResponse = () =>
        {
            service.Temperature = 0.9f;
            service.ChangeModel("gpt-6-astra");
            service.Functions.Clear();
        };

        Assert.AreEqual("answer", await request.GetCompletionAsync());

        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, handler.Bodies.Count);
        foreach (var body in handler.Bodies)
        {
            Assert.AreEqual("gpt-4o", body.GetProperty("model").GetString());
            Assert.AreEqual(0.2f, body.GetProperty("temperature").GetSingle());
            Assert.AreEqual("lookup", body.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        }
        Assert.AreEqual(0.9f, service.Temperature);
        Assert.AreEqual("gpt-6-astra", service.Model);
        Assert.AreEqual(0, service.Functions.Count);
    }

    [TestMethod]
    public async Task Anthropic_ReusingBuilderCreatesFreshProviderObservationState()
    {
        const string transformed = """{"id":"a1","content":[{"type":"text","text":"answer"}],"stop_reason":"end_turn","input_transformations":[{"type":"thinking_dropped","path":"messages.1.content.0","reason":"prefix_binding_mismatch"}]}""";
        using var handler = new CaptureHandler(transformed, ClaudeAnswer);
        using var client = new HttpClient(handler);
        var service = new AnthropicService("test-key", "claude-fable-5-1", client);
        var request = service.CreateRequest("explain");

        await request.GetCompletionAsync();
        Assert.AreEqual(1, service.LastInputTransformations.Count);
        var firstObservation = service.LastInputTransformations.Single();
        await request.GetCompletionAsync();

        Assert.AreEqual(0, service.LastInputTransformations.Count);
        Assert.AreEqual("prefix_binding_mismatch", firstObservation.Reason);
    }

    [TestMethod]
    public async Task Anthropic_RunProfileKeepsOmittedDisplayAfterStartReturns_WithoutChangingUpdatesDefault()
    {
        const string sse = """
            data: {"type":"message_start","message":{"id":"profile-run","type":"message","role":"assistant","model":"claude-fable-5-1","content":[],"usage":{"input_tokens":1,"output_tokens":0}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":1}}

            data: {"type":"message_stop"}

            """;
        using var handler = new CaptureHandler(sse);
        using var client = new HttpClient(handler);
        var service = new DeferredAnthropicService(client);
        service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);
        handler.BeforeResponse = () => Assert.AreEqual(ClaudeThinkingDisplay.Updates, service.AdaptiveThinkingDisplay);
        var request = service.CreateRequest("answer briefly")
            .WithProfile(new AIRequestProfile { DisableReasoning = true });

        await using var run = await request.StartRunAsync();
        await service.StreamReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, handler.Bodies.Count, "Hold serialization until StartRunAsync has returned and its profile cleanup has run.");
        Assert.AreEqual(ClaudeThinkingDisplay.Updates, service.AdaptiveThinkingDisplay);
        service.ContinueStreaming.TrySetResult();

        Assert.AreEqual("answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        var body = handler.Bodies.Single();
        Assert.AreEqual("claude-fable-5-1", body.GetProperty("model").GetString());
        Assert.AreEqual("adaptive", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.AreEqual("omitted", body.GetProperty("thinking").GetProperty("display").GetString());
        Assert.AreEqual("low", body.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.AreEqual(ClaudeThinkingDisplay.Updates, service.AdaptiveThinkingDisplay);
        Assert.AreEqual(ClaudeReasoningEffort.High, service.AdaptiveThinkingEffort);
    }

    private sealed class DeferredAnthropicService(HttpClient client) : AnthropicService("test-key", "claude-fable-5-1", client)
    {
        public TaskCompletionSource StreamReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueStreaming { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options, bool useFunctions, FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamReached.TrySetResult();
            await ContinueStreaming.Task.WaitAsync(cancellationToken);
            await foreach (var content in base.StreamRoundAsync(options, useFunctions, policy, cancellationToken))
                yield return content;
        }
    }

    private sealed class CaptureHandler(params string[] responses) : HttpMessageHandler
    {
        public List<JsonElement> Bodies { get; } = new();
        public List<Uri> Uris { get; } = new();
        public Action? BeforeResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Bodies.Add(document.RootElement.Clone());
            Uris.Add(request.RequestUri!);
            BeforeResponse?.Invoke();
            var response = responses[Math.Min(Bodies.Count - 1, responses.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response, Encoding.UTF8, response.StartsWith("data: ") ? "text/event-stream" : "application/json") };
        }
    }
}
