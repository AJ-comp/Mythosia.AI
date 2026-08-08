using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Live")]
[TestCategory("FunctionCalling")]
[DoNotParallelize]
public class ProviderParallelFunctionLiveTests
{
    private const string OpenAISecret = "momedit-openai-secret";
    private const string AnthropicSecret = "momedit-antropic-secret";
    private const string GoogleSecret = "gemini-secret";
    private const string XAISecret = "xai-secret";

    private const string AlphaTool = "live_parallel_alpha";
    private const string BetaTool = "live_parallel_beta";
    private const string AlphaResult = "ALPHA_RESULT";
    private const string BetaResult = "BETA_RESULT";

    private const string SystemInstruction =
        "This is a deterministic function-calling integration test. When requested, issue " +
        "both named function calls together in one assistant response. Never split them across " +
        "assistant turns. Call each exactly once. After both results arrive, answer briefly.";

    private const string UserPrompt =
        "Call live_parallel_alpha exactly once and live_parallel_beta exactly once. " +
        "The calls take no arguments, are independent, and MUST be emitted together now in the " +
        "same assistant turn as one parallel tool-call batch. Do not wait for one result before " +
        "emitting the other. After both tool results arrive, reply PARALLEL_TOOLS_OK.";

    [TestMethod]
    public async Task OpenAI_Gpt56_NonStreaming_PreservesParallelBatch()
    {
        var service = await CreateOpenAIAsync();
        await AssertNonStreamingParallelBatchAsync("OpenAI GPT-5.6", service);
    }

    [TestMethod]
    public async Task OpenAI_Gpt56_Streaming_PreservesParallelBatch()
    {
        var service = await CreateOpenAIAsync();
        await AssertStreamingParallelBatchAsync("OpenAI GPT-5.6", service);
    }

    [TestMethod]
    public async Task Anthropic_ClaudeSonnet5_NonStreaming_PreservesParallelBatch()
    {
        var service = await CreateAnthropicAsync();
        await AssertNonStreamingParallelBatchAsync("Anthropic Claude Sonnet 5", service);
    }

    [TestMethod]
    public async Task Anthropic_ClaudeSonnet5_Streaming_PreservesParallelBatch()
    {
        var service = await CreateAnthropicAsync();
        await AssertStreamingParallelBatchAsync("Anthropic Claude Sonnet 5", service);
    }

    [TestMethod]
    public async Task Google_Gemini36Flash_NonStreaming_PreservesParallelBatch()
    {
        var service = await CreateGoogleAsync();
        await AssertNonStreamingParallelBatchAsync("Google Gemini 3.6 Flash", service);
    }

    [TestMethod]
    public async Task Google_Gemini36Flash_Streaming_PreservesParallelBatch()
    {
        var service = await CreateGoogleAsync();
        await AssertStreamingParallelBatchAsync("Google Gemini 3.6 Flash", service);
    }

    [TestMethod]
    public async Task XAI_Grok45_NonStreaming_PreservesParallelBatch()
    {
        var service = await CreateXAIAsync();
        await AssertNonStreamingParallelBatchAsync("xAI Grok 4.5", service);
    }

    [TestMethod]
    public async Task XAI_Grok45_Streaming_PreservesParallelBatch()
    {
        var service = await CreateXAIAsync();
        await AssertStreamingParallelBatchAsync("xAI Grok 4.5", service);
    }

    private static async Task AssertNonStreamingParallelBatchAsync(
        string provider,
        AIService service)
    {
        var probe = ConfigureParallelFunctions(service);
        var completion = await service.GetCompletionAsync(UserPrompt);

        var (callBatch, resultBatch) = AssertHistoryBatch(provider, service, completion);
        AssertProbe(provider, probe, callBatch, resultBatch, completion);
    }

    private static async Task AssertStreamingParallelBatchAsync(
        string provider,
        AIService service)
    {
        var probe = ConfigureParallelFunctions(service);
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync(UserPrompt, StreamOptions.WithFunctions))
            events.Add(content);

        var providerErrors = events
            .Where(content => content.Type == StreamingContentType.Error)
            .Select(content => content.Content ?? "<empty error>")
            .ToArray();
        Assert.AreEqual(
            0,
            providerErrors.Length,
            $"{provider}: provider stream errors: {string.Join(" | ", providerErrors)}");

        var finalText = string.Concat(events
            .Where(content => content.Type == StreamingContentType.Text)
            .Select(content => content.Content));
        var (callBatch, resultBatch) = AssertHistoryBatch(provider, service, finalText);
        AssertProbe(provider, probe, callBatch, resultBatch, finalText);

        var callEvents = events
            .Where(content => content.Type == StreamingContentType.FunctionCall)
            .ToArray();
        var resultEvents = events
            .Where(content => content.Type == StreamingContentType.FunctionResult)
            .ToArray();
        Assert.AreEqual(
            2,
            callEvents.Length,
            $"{provider}: expected two streamed FunctionCall events, observed {callEvents.Length}. " +
            BuildEventDiagnostic(events));
        Assert.AreEqual(
            2,
            resultEvents.Length,
            $"{provider}: expected two streamed FunctionResult events, observed {resultEvents.Length}. " +
            BuildEventDiagnostic(events));

        CollectionAssert.AreEqual(
            callEvents.Select(content => content.FunctionCall!.Name).ToArray(),
            resultEvents.Select(content => content.FunctionResult!.Call.Name).ToArray(),
            $"{provider}: streamed result order did not match provider call order.");
        Assert.IsTrue(
            callEvents.Concat(resultEvents).All(content =>
                content.FunctionCallBatchId == callBatch.Id),
            $"{provider}: streamed calls/results did not preserve history batch ID {callBatch.Id}. " +
            BuildEventDiagnostic(events));
    }

    private static FiniteConcurrencyProbe ConfigureParallelFunctions(AIService service)
    {
        var probe = new FiniteConcurrencyProbe();
        service.Functions.Add(probe.CreateFunction(AlphaTool, AlphaResult, 700));
        service.Functions.Add(probe.CreateFunction(BetaTool, BetaResult, 150));
        service.FunctionCallMode = FunctionCallMode.Auto;
        service.DefaultPolicy = new FunctionCallingPolicy
        {
            ExecutionMode = FunctionExecutionMode.Parallel,
            MaxConcurrency = 2,
            MaxRounds = 4,
            TimeoutSeconds = 240
        };
        return probe;
    }

    private static (FunctionCallBatch Calls, FunctionCallResultBatch Results) AssertHistoryBatch(
        string provider,
        AIService service,
        string finalText)
    {
        var callBatches = service.ActivateChat.Messages
            .Where(message => message.FunctionCallBatch != null)
            .Select(message => message.FunctionCallBatch!)
            .ToArray();
        var resultBatches = service.ActivateChat.Messages
            .Where(message => message.FunctionCallResultBatch != null)
            .Select(message => message.FunctionCallResultBatch!)
            .ToArray();
        var diagnostic = BuildHistoryDiagnostic(callBatches, resultBatches, finalText);

        Assert.AreEqual(
            1,
            callBatches.Length,
            $"{provider}: the model did not emit exactly one assistant tool-call batch. {diagnostic}");
        Assert.AreEqual(
            1,
            resultBatches.Length,
            $"{provider}: expected exactly one tool-result batch. {diagnostic}");

        var callBatch = callBatches[0];
        var resultBatch = resultBatches[0];
        Assert.AreEqual(
            2,
            callBatch.Calls.Count,
            $"{provider}: the assistant batch did not contain both calls. {diagnostic}");
        Assert.AreEqual(
            2,
            resultBatch.Results.Count,
            $"{provider}: the result batch did not contain both results. {diagnostic}");
        Assert.AreEqual(
            callBatch.Id,
            resultBatch.FunctionCallBatchId,
            $"{provider}: call/result batch IDs differ. {diagnostic}");

        CollectionAssert.AreEquivalent(
            new[] { AlphaTool, BetaTool },
            callBatch.Calls.Select(call => call.Name).ToArray(),
            $"{provider}: expected each independent tool exactly once. {diagnostic}");
        CollectionAssert.AreEqual(
            callBatch.Calls.Select(call => call.Name).ToArray(),
            resultBatch.Results.Select(result => result.Call.Name).ToArray(),
            $"{provider}: result order did not match provider call order. {diagnostic}");

        foreach (var result in resultBatch.Results)
        {
            Assert.IsFalse(result.IsError, $"{provider}: handler failed for {result.Call.Name}: {result.Content}");
            Assert.AreEqual(
                ExpectedResult(result.Call.Name),
                result.Content,
                $"{provider}: unexpected result correlation for {result.Call.Name}.");
        }

        return (callBatch, resultBatch);
    }

    private static void AssertProbe(
        string provider,
        FiniteConcurrencyProbe probe,
        FunctionCallBatch callBatch,
        FunctionCallResultBatch resultBatch,
        string finalText)
    {
        var diagnostic = BuildHistoryDiagnostic(new[] { callBatch }, new[] { resultBatch }, finalText);
        Assert.AreEqual(2, probe.Started, $"{provider}: expected two handler starts. {diagnostic}");
        Assert.AreEqual(2, probe.Completed, $"{provider}: expected two completed handlers. {diagnostic}");
        Assert.IsTrue(
            probe.MaximumConcurrency >= 2,
            $"{provider}: ExecutionMode.Parallel did not overlap both handlers. {diagnostic}");
    }

    private static string ExpectedResult(string functionName) => functionName switch
    {
        AlphaTool => AlphaResult,
        BetaTool => BetaResult,
        _ => throw new AssertFailedException($"Unexpected live-test function name: {functionName}")
    };

    private static string BuildHistoryDiagnostic(
        IReadOnlyList<FunctionCallBatch> callBatches,
        IReadOnlyList<FunctionCallResultBatch> resultBatches,
        string finalText)
    {
        var calls = string.Join(
            "; ",
            callBatches.Select((batch, index) =>
                $"calls[{index}]=[{string.Join(",", batch.Calls.Select(call => call.Name))}]"));
        var results = string.Join(
            "; ",
            resultBatches.Select((batch, index) =>
                $"results[{index}]=[{string.Join(",", batch.Results.Select(result => result.Call.Name))}]"));
        return $"Observed {callBatches.Count} call batch(es), {resultBatches.Count} result batch(es); " +
               $"{calls}; {results}; final='{Truncate(finalText)}'.";
    }

    private static string BuildEventDiagnostic(IEnumerable<StreamingContent> events)
    {
        return "Events=" + string.Join(
            ",",
            events.Select(content =>
                content.Type switch
                {
                    StreamingContentType.FunctionCall =>
                        $"FunctionCall:{content.FunctionCall?.Name}:{content.FunctionCallBatchId}",
                    StreamingContentType.FunctionResult =>
                        $"FunctionResult:{content.FunctionResult?.Call.Name}:{content.FunctionCallBatchId}",
                    _ => content.Type.ToString()
                }));
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "<empty>";
        return value.Length <= 300 ? value : value[..300] + "...";
    }

    private static async Task<AIService> CreateOpenAIAsync()
    {
        var service = new OpenAIService(
            await LiveTestSecrets.GetAsync(OpenAISecret),
            CreateHttpClient());
        service.ChangeModel(AIModels.OpenAI.Gpt5_6);
        service.Gpt5_6ReasoningEffort = Gpt5_6Reasoning.None;
        service.Gpt5_6ReasoningSummary = null;
        return ConfigureService(service);
    }

    private static async Task<AIService> CreateAnthropicAsync()
    {
        var service = new AnthropicService(
            await LiveTestSecrets.GetAsync(AnthropicSecret),
            CreateHttpClient());
        service.ChangeModel(AIModels.Anthropic.ClaudeSonnet5);
        service.ThinkingBudget = -1;
        return ConfigureService(service);
    }

    private static async Task<AIService> CreateGoogleAsync()
    {
        var service = new GoogleAIService(
            await LiveTestSecrets.GetAsync(GoogleSecret),
            CreateHttpClient());
        service.ChangeModel(AIModels.Google.Gemini3_6Flash);
        service.ThinkingLevel = GeminiThinkingLevel.Minimal;
        return ConfigureService(service);
    }

    private static async Task<AIService> CreateXAIAsync()
    {
        var service = new XAIService(
            await LiveTestSecrets.GetAsync(XAISecret),
            CreateHttpClient());
        service.ChangeModel(AIModels.xAI.Grok4_5);
        service.ReasoningEffort = GrokReasoning.Low;
        return ConfigureService(service);
    }

    private static T ConfigureService<T>(T service)
        where T : AIService
    {
        service.MaxTokens = 1024;
        service.ActivateChat.SystemMessage = SystemInstruction;
        return service;
    }

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private sealed class FiniteConcurrencyProbe
    {
        private int _active;
        private int _completed;
        private int _maximumConcurrency;
        private int _started;

        public int Started => Volatile.Read(ref _started);
        public int Completed => Volatile.Read(ref _completed);
        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public FunctionDefinition CreateFunction(string name, string result, int delayMilliseconds)
        {
            return new FunctionDefinition
            {
                Name = name,
                Description =
                    $"Required independent live-test tool. Call {name} exactly once together with " +
                    $"{(name == AlphaTool ? BetaTool : AlphaTool)} in the same assistant turn. " +
                    "It accepts no arguments and returns a fixed marker.",
                Handler = async _ =>
                {
                    Interlocked.Increment(ref _started);
                    var active = Interlocked.Increment(ref _active);
                    UpdateMaximum(ref _maximumConcurrency, active);
                    try
                    {
                        // Finite by design: a provider that emits one call per turn fails the
                        // concurrency assertion instead of deadlocking while waiting for call two.
                        await Task.Delay(delayMilliseconds);
                        return result;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _active);
                        Interlocked.Increment(ref _completed);
                    }
                }
            };
        }

        private static void UpdateMaximum(ref int maximum, int candidate)
        {
            var observed = Volatile.Read(ref maximum);
            while (candidate > observed)
            {
                var previous = Interlocked.CompareExchange(ref maximum, candidate, observed);
                if (previous == observed)
                    return;
                observed = previous;
            }
        }
    }
}
