using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("Google")]
public class GoogleTerminalSafetyTests
{
    [TestMethod]
    public async Task NonStreamingPromptBlock_DoesNotExecuteToolOrSaveAssistant()
    {
        const string response = """
            {"promptFeedback":{"blockReason":"SAFETY","safetyRatings":[{"category":"HARM_CATEGORY_DANGEROUS_CONTENT","blocked":true}]}}
            """;
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "blocked the prompt");
        StringAssert.Contains(exception.ErrorDetails ?? string.Empty, "SAFETY");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    [DataRow("MAX_TOKENS")]
    [DataRow("SAFETY")]
    [DataRow("MALFORMED_FUNCTION_CALL")]
    [DataRow("FUTURE_FINISH_REASON")]
    public async Task NonStreamingNonStopFinishReason_DoesNotExecuteToolOrRetry(string finishReason)
    {
        var handler = new QueueHttpMessageHandler(Response.Json(FunctionCallCandidate(finishReason)));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.ErrorDetails ?? string.Empty, finishReason);
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount, "A failed terminal response must not start another billed round.");
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task NonStreamingMalformedJson_FailsBeforeToolExtraction()
    {
        var handler = new QueueHttpMessageHandler(Response.Json("{\"candidates\":["));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "Failed to parse");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task NonStreamingMissingFinishReason_FailsAsIncomplete()
    {
        const string response = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"partial"}]}}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Return a response."));

        StringAssert.Contains(exception.Message, "without a terminal finish reason");
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task NonStreamingStopWithEmptyCandidate_CompletesWithoutRetry()
    {
        const string response = """
            {"candidates":[{"finishReason":"STOP"}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var service = CreateService(handler);

        var result = await service.GetCompletionAsync("Return no content.");

        Assert.AreEqual(string.Empty, result);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(2, service.ActivateChat.Messages.Count);
        Assert.AreEqual(ActorRole.Assistant, service.ActivateChat.Messages[1].Role);
        Assert.AreEqual(string.Empty, service.ActivateChat.Messages[1].Content);
    }

    [TestMethod]
    public async Task StreamingPromptBlock_EmitsErrorWithoutCompletionOrAssistantCommit()
    {
        const string blocked = """
            {"promptFeedback":{"blockReason":"SAFETY","safetyRatings":[]}}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(blocked)));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(service);

        var error = AssertSingleTerminalErrorWithoutCompletion(chunks);
        Assert.AreEqual("SAFETY", error.Metadata?["reason"]?.ToString());
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    [DataRow("MAX_TOKENS")]
    [DataRow("SAFETY")]
    [DataRow("MALFORMED_FUNCTION_CALL")]
    [DataRow("FUTURE_FINISH_REASON")]
    public async Task StreamingNonStopFinishReason_EmitsErrorWithoutCompletion(string finishReason)
    {
        var terminal = TextCandidate("partial", finishReason);
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(terminal)));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(service);

        var error = AssertSingleTerminalErrorWithoutCompletion(chunks);
        Assert.AreEqual(finishReason, error.Metadata?["reason"]?.ToString());
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Text),
            "Content from a failed terminal envelope must not be emitted as successful text.");
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingStopThenEofWithoutDone_CompletesAndSavesAssistant()
    {
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(TextCandidate("done", "STOP"))));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(service);

        Assert.AreEqual("done", TextFrom(chunks));
        Assert.AreEqual(1, chunks.Count(chunk => chunk.Type == StreamingContentType.Completion));
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Error));
        Assert.AreEqual(2, service.ActivateChat.Messages.Count);
        Assert.AreEqual("done", service.ActivateChat.Messages[1].Content);
    }

    [TestMethod]
    public async Task StreamingEofWithoutStop_FailsAndDoesNotSavePartialAssistant()
    {
        const string partial = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"partial"}]}}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(partial)));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(service);

        Assert.AreEqual("partial", TextFrom(chunks));
        var error = AssertSingleTerminalErrorWithoutCompletion(chunks);
        Assert.AreEqual("incomplete_stream", error.Metadata?["reason"]?.ToString());
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingDoneWithoutStop_FailsAndDoesNotSavePartialAssistant()
    {
        const string partial = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"partial"}]}}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(partial, "[DONE]")));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(service);

        Assert.AreEqual("partial", TextFrom(chunks));
        var error = AssertSingleTerminalErrorWithoutCompletion(chunks);
        Assert.AreEqual("incomplete_stream", error.Metadata?["reason"]?.ToString());
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingFunctionCollectedThenSafetyFailure_DoesNotExecuteOrCommitTool()
    {
        const string functionCall = """
            {"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"id":"call_dangerous","name":"dangerous_tool","args":{}}}]}}]}
            """;
        const string terminalFailure = """
            {"candidates":[{"finishReason":"SAFETY"}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(functionCall, terminalFailure)));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        Assert.IsTrue(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionCall),
            "The fixture must prove that a function call was collected before terminal failure.");
        var error = AssertSingleTerminalErrorWithoutCompletion(chunks);
        Assert.AreEqual("SAFETY", error.Metadata?["reason"]?.ToString());
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingFunctionCollectedThenMalformedJson_DoesNotExecuteOrCommitTool()
    {
        const string functionCall = """
            {"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"id":"call_dangerous","name":"dangerous_tool","args":{}}}]}}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(functionCall, "{\"candidates\":[")));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        Assert.IsTrue(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionCall));
        var error = AssertSingleTerminalErrorWithoutCompletion(chunks);
        Assert.AreEqual("malformed_stream", error.Metadata?["reason"]?.ToString());
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingStopBeforeUsage_DrainsUsageOnlyChunkWithoutDone()
    {
        var terminal = TextCandidate("done", "STOP");
        const string usage = """
            {"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":4,"totalTokenCount":14}}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(terminal, usage)));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(service);
        var completion = chunks.Single(chunk => chunk.Type == StreamingContentType.Completion);

        Assert.AreEqual("done", TextFrom(chunks));
        Assert.IsNotNull(completion.Usage);
        Assert.AreEqual(10, completion.Usage.InputTokens);
        Assert.AreEqual(4, completion.Usage.OutputTokens);
        Assert.AreEqual(14, completion.Usage.TotalTokens);
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Error));
    }

    [TestMethod]
    public async Task StreamingUsage_NormalizesToolUseAndReasoningTokens()
    {
        const string terminalWithUsage = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"answer"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":29,"toolUsePromptTokenCount":33,"candidatesTokenCount":69,"thoughtsTokenCount":35,"cachedContentTokenCount":7,"totalTokenCount":166}}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(terminalWithUsage)));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(service);
        var completion = chunks.Single(chunk => chunk.Type == StreamingContentType.Completion);

        Assert.IsNotNull(completion.Usage);
        Assert.AreEqual(62, completion.Usage.InputTokens);
        Assert.AreEqual(104, completion.Usage.OutputTokens);
        Assert.AreEqual(166, completion.Usage.TotalTokens);
        Assert.AreEqual(7, completion.Usage.CachedInputTokens);
        Assert.AreEqual(35, completion.Usage.ReasoningTokens);
        Assert.AreEqual(69, completion.Usage.VisibleOutputTokens);
        Assert.AreEqual(55, completion.Usage.NonCachedInputTokens);
    }

    [TestMethod]
    public async Task LegacyCallbackEarlyEof_ThrowsAndDoesNotSavePartialAssistant()
    {
        const string partial = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"partial"}]}}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(partial)));
        var service = CreateService(handler);
        var received = new StringBuilder();

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.StreamCompletionAsync(
                new Message(ActorRole.User, "Return a response."),
                chunk =>
                {
                    received.Append(chunk);
                    return Task.CompletedTask;
                }));

        StringAssert.Contains(exception.Message, "terminal finish reason");
        Assert.AreEqual("partial", received.ToString());
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task LegacyCallbackMalformedJson_ThrowsAndDoesNotSavePartialAssistant()
    {
        const string partial = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"partial"}]}}]}
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(partial, "{\"candidates\":[")));
        var service = CreateService(handler);
        var received = new StringBuilder();

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.StreamCompletionAsync(
                new Message(ActorRole.User, "Return a response."),
                chunk =>
                {
                    received.Append(chunk);
                    return Task.CompletedTask;
                }));

        StringAssert.Contains(exception.Message, "malformed streaming response");
        Assert.AreEqual("partial", received.ToString());
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StatelessFunctionCall_ContinuesToFinalGeminiResponseWithoutPollutingOriginalChat()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Json(FunctionCallCandidate("STOP")),
            Response.Json(TextCandidate("final answer", "STOP")));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);
        service.StatelessMode = true;
        service.ActivateChat.SystemMessage = "persistent system";
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "persistent history"));
        var originalChat = service.ActivateChat;

        var result = await service.GetCompletionAsync("Use the tool, then answer.");

        Assert.AreEqual("final answer", result,
            "Stateless function calling must return the model's follow-up answer, not a local function-result placeholder.");
        Assert.AreEqual(1, invocationCount);
        Assert.AreEqual(2, handler.RequestCount);
        Assert.AreEqual(2, handler.RequestBodies.Count);
        StringAssert.Contains(handler.RequestBodies[1], "\"functionResponse\"");
        StringAssert.Contains(handler.RequestBodies[1], "done");
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual("persistent system", service.ActivateChat.SystemMessage);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count,
            "The temporary stateless tool conversation must not leak into the original ChatBlock.");
        Assert.AreEqual("persistent history", service.ActivateChat.Messages[0].Content);
    }

    private static GoogleAIService CreateService(HttpMessageHandler handler)
        => new("offline-test-key", new HttpClient(handler));

    private static GoogleAIService CreateServiceWithTool(HttpMessageHandler handler, Action onInvoke)
    {
        var service = CreateService(handler);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "A side-effecting tool that must run only after successful termination.",
            Handler = _ =>
            {
                onInvoke();
                return Task.FromResult("done");
            }
        });
        return service;
    }

    private static async Task<List<StreamingContent>> CollectAdvancedStreamAsync(
        GoogleAIService service,
        StreamOptions? options = null)
    {
        var chunks = new List<StreamingContent>();
        await foreach (var chunk in service.StreamAsync(
            "Run the tool.",
            options ?? StreamOptions.Default.WithFunctionCalls(false)))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static StreamingContent AssertSingleTerminalErrorWithoutCompletion(
        IReadOnlyCollection<StreamingContent> chunks)
    {
        var error = chunks.Single(chunk => chunk.Type == StreamingContentType.Error);
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Completion),
            "A failed Gemini round must not be followed by a synthesized Completion chunk.");
        return error;
    }

    private static void AssertOnlyUserMessageWasSaved(GoogleAIService service)
    {
        Assert.AreEqual(1, service.ActivateChat.Messages.Count,
            "Partial assistant content and function records must not be committed on failure.");
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
    }

    private static string TextFrom(IEnumerable<StreamingContent> chunks)
        => string.Concat(chunks
            .Where(chunk => chunk.Type == StreamingContentType.Text)
            .Select(chunk => chunk.Content));

    private static string Sse(params string[] payloads)
        => string.Join("\n\n", payloads.Select(payload => $"data: {payload}")) + "\n\n";

    private static string TextCandidate(string text, string finishReason)
        => "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"" +
           text + "\"}]},\"finishReason\":\"" + finishReason + "\"}]}";

    private static string FunctionCallCandidate(string finishReason)
        => "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"functionCall\":{" +
           "\"id\":\"call_dangerous\",\"name\":\"dangerous_tool\",\"args\":{}}}]}," +
           "\"finishReason\":\"" + finishReason + "\"}]}";

    private readonly record struct Response(string Body, string MediaType)
    {
        public static Response Json(string body) => new(body, "application/json");

        public static Response Sse(string body) => new(body, "text/event-stream");
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Response> _responses;

        public QueueHttpMessageHandler(params Response[] responses)
        {
            _responses = new Queue<Response>(responses);
        }

        public int RequestCount { get; private set; }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued offline response remains.");

            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var response = _responses.Dequeue();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, response.MediaType)
            };
        }
    }
}
