using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialRunResultTests
{
    public static IEnumerable<object[]> ReportedTotalCases => new[]
    {
        "OpenAI Chat", "OpenAI Responses", "Google", "xAI", "DeepSeek",
        "Perplexity", "Qwen", "vLLM", "Ollama"
    }.SelectMany(provider => new[] { false, true }.Select(totalOnly => new object[] { provider, totalOnly }));

    [TestMethod]
    [DynamicData(nameof(ReportedTotalCases))]
    public async Task ReportedTotal_SurvivesProviderParsingRoundEventsAndResult(string provider, bool totalOnly)
    {
        using var client = new HttpClient(new SseHandler(ProviderStream(provider, totalOnly)));
        var service = CreateService(provider, client);
        await using var run = await service.StartRunAsync("question");
        var events = new List<StreamingContent>();
        await foreach (var item in run.StreamAsync()) events.Add(item);
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(13, result.Usage.TotalTokens, "An explicit provider total must not be recomputed from partial component counts.");
        Assert.AreEqual(totalOnly ? 0 : 7, result.Usage.InputTokens);
        Assert.AreEqual(totalOnly ? 0 : 2, result.Usage.OutputTokens);
        Assert.AreEqual(13, events.Single(item => item.Type == StreamingContentType.RoundUsage).Usage!.TotalTokens);
        Assert.AreEqual(13, events.Single(item => item.Type == StreamingContentType.Completion).Usage!.TotalTokens);
        Assert.AreEqual(1, result.RoundCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MultipleRounds_AccumulateEachReportedTotalOnce(bool customStream)
    {
        var service = new UsageProbeService(customStream);
        await using var run = await service.StartRunAsync("question");
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(36, result.Usage.TotalTokens);
        Assert.AreEqual(2, result.RoundCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SharedObservationSequence_RejectsAnotherEnumerator(bool finishFirstReader)
    {
        var service = new UsageProbeService(customStream: true);
        await using var run = await service.StartRunAsync("question");
        var output = run.StreamAsync();
        await using var first = output.GetAsyncEnumerator();
        Assert.IsTrue(await first.MoveNextAsync());
        if (finishFirstReader)
            while (await first.MoveNextAsync()) { }

        await using var second = output.GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.MoveNextAsync().AsTask());
        await first.DisposeAsync();
        Assert.AreEqual(2, (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).RoundCount);
    }

    private static AIService CreateService(string provider, HttpClient client)
    {
        AIService service = provider switch
        {
            "OpenAI Chat" => new OpenAIService("offline", AIModels.OpenAI.Gpt4o, client),
            "OpenAI Responses" => new OpenAIService("offline", AIModels.OpenAI.Gpt4_1, client),
            "Google" => new GoogleAIService("offline", AIModels.Google.Gemini2_5Flash, client),
            "xAI" => new XAIService("offline", client),
            "DeepSeek" => new DeepSeekService("offline", client),
            "Perplexity" => new PerplexityService("offline", client),
            "Qwen" => new QwenService("offline", client),
            "vLLM" => new QwenService("https://offline.invalid/", EndpointPlatform.Vllm, client),
            "Ollama" => new QwenService("https://offline.invalid/", EndpointPlatform.Ollama, client),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 3, EnableLogging = false };
        return service;
    }

    private static string ProviderStream(string provider, bool totalOnly)
    {
        if (provider is "OpenAI Responses" or "Perplexity")
        {
            var usage = totalOnly ? "\"total_tokens\":13" : "\"input_tokens\":7,\"output_tokens\":2,\"total_tokens\":13";
            return Sse("""{"type":"response.output_text.delta","delta":"answer"}""",
                """{"type":"response.completed","response":{"id":"resp-test","status":"completed","usage":{USAGE},"output":[{"id":"msg-test","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"answer"}]}]}}""".Replace("USAGE", usage));
        }
        if (provider == "Google")
        {
            var usage = totalOnly ? "\"totalTokenCount\":13" : "\"promptTokenCount\":7,\"candidatesTokenCount\":2,\"totalTokenCount\":13";
            return Sse("""{"candidates":[{"content":{"role":"model","parts":[{"text":"answer"}]},"finishReason":"STOP"}],"usageMetadata":{USAGE}}""".Replace("USAGE", usage));
        }
        var chatUsage = totalOnly ? "\"total_tokens\":13" : "\"prompt_tokens\":7,\"completion_tokens\":2,\"total_tokens\":13";
        return Sse("""{"choices":[{"index":0,"delta":{"content":"answer"},"finish_reason":"stop"}]}""",
            """{"choices":[],"usage":{USAGE}}""".Replace("USAGE", chatUsage), "[DONE]");
    }

    private static string Sse(params string[] events) => string.Concat(events.Select(item => $"data: {item}\n\n"));

    private sealed class SseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            });
    }

    private sealed class UsageProbeService(bool customStream) : AIService("offline", "https://offline.invalid/", new HttpClient())
    {
        private int _round;
        public override string Provider => "Probe";

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(StreamOptions options,
            bool useFunctions, FunctionCallingPolicy policy, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var round = ++_round;
            yield return new StreamingContent { Type = StreamingContentType.Status, Usage = new TokenUsage { TotalTokens = 999 } };
            if (round == 1) yield return new StreamingContent { Type = StreamingContentType.FunctionResult };
            yield return new StreamingContent { Type = StreamingContentType.Completion, Usage = new TokenUsage { TotalTokens = round == 1 ? 13 : 23 } };
        }

        protected override IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message, StreamOptions options,
            CancellationToken cancellationToken = default)
            => customStream ? StreamCustom(cancellationToken) : base.StreamCoreAsync(message, options, cancellationToken);

        private static async IAsyncEnumerable<StreamingContent> StreamCustom([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 1, Usage = new TokenUsage { TotalTokens = 999 } };
            yield return new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 1, Usage = new TokenUsage { TotalTokens = 13 } };
            yield return new StreamingContent { Type = StreamingContentType.RoundUsage, RoundIndex = 2, Usage = new TokenUsage { TotalTokens = 23 } };
            yield return new StreamingContent { Type = StreamingContentType.Completion, RoundIndex = 2 };
        }

        public override Task<string> GetCompletionAsync(Message message) => throw new NotSupportedException();
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No HTTP expected.");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new FunctionCallBatch());
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }
}
