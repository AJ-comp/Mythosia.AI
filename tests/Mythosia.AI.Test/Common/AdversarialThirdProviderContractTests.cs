using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialThirdProviderContractTests
{
    private const string Stop = """{"model":"reported-model","choices":[{"index":0,"delta":{"content":"answer"},"finish_reason":"stop"}]}""";

    [TestMethod]
    [DataRow("OpenAI")]
    [DataRow("xAI")]
    [DataRow("Qwen")]
    [DataRow("DeepSeek")]
    public async Task ExplicitTerminalReasonRejectsLaterContentBeforeRunResultOrHistorySuccess(string provider)
    {
        using var client = new HttpClient(new SseHandler(Sse(Stop,
            """{"choices":[{"index":0,"delta":{"content":"after-stop"},"finish_reason":null}]}""", "[DONE]")));
        var service = Create(provider, client);
        await using var run = await service.StartRunAsync("question");

        await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow("OpenAI")]
    [DataRow("xAI")]
    [DataRow("Qwen")]
    [DataRow("DeepSeek")]
    public async Task ConflictingTerminalReasonsCannotBeSilentlyReplacedInRunMetadata(string provider)
    {
        using var client = new HttpClient(new SseHandler(Sse(Stop,
            """{"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}""", "[DONE]")));
        var service = Create(provider, client);
        await using var run = await service.StartRunAsync("question");

        await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow("OpenAI")]
    [DataRow("xAI")]
    [DataRow("Qwen")]
    public async Task FunctionPayloadArrivingAfterStopCannotExecute(string provider)
    {
        var handler = new SseHandler(Sse(Stop,
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"late-call","type":"function","function":{"name":"late_tool","arguments":"{}"}}]},"finish_reason":null}]}""",
            "[DONE]"), Sse(Stop, "[DONE]"));
        using var client = new HttpClient(handler);
        var service = Create(provider, client);
        var calls = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "late_tool", Handler = _ => { calls++; return Task.FromResult("result"); }
        });
        await using var run = await service.StartRunAsync("question");

        await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, calls);
        Assert.AreEqual(1, handler.Calls);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant || message.Role == ActorRole.Function));
    }

    [TestMethod]
    [DataRow("OpenAI")]
    [DataRow("xAI")]
    [DataRow("Qwen")]
    [DataRow("DeepSeek")]
    public async Task FinalDeltaInTerminalChunkAndFollowingUsageOnlyEventRemainValid(string provider)
    {
        using var client = new HttpClient(new SseHandler(Sse(Stop,
            """{"model":"reported-model","choices":[],"usage":{"prompt_tokens":3,"completion_tokens":2,"total_tokens":5}}""", "[DONE]")));
        var service = Create(provider, client);
        await using var run = await service.StartRunAsync("question");

        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual("reported-model", result.Model);
        Assert.AreEqual("stop", result.RawFinishReason);
        Assert.AreEqual(AIFinishReason.Stop, result.FinishReason);
        Assert.AreEqual(5, result.Usage!.TotalTokens);
        Assert.AreEqual(1, result.RoundCount);
    }

    private static AIService Create(string provider, HttpClient client)
    {
        AIService service = provider switch
        {
            "OpenAI" => new OpenAIService("offline", AIModels.OpenAI.Gpt4o, client),
            "xAI" => new XAIService("offline", client),
            "Qwen" => new QwenService("offline", client),
            "DeepSeek" => new DeepSeekService("offline", client),
            _ => throw new ArgumentException(provider)
        };
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 2 };
        return service;
    }

    private static string Sse(params string[] events) => string.Concat(events.Select(item => $"data: {item}\n\n"));

    private sealed class SseHandler(params string[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = responses[Math.Min(Calls++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            });
        }
    }
}
