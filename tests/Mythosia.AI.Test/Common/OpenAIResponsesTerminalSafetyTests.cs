using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("OpenAI")]
public class OpenAIResponsesTerminalSafetyTests
{
    private const string StreamingToolCallEvent = """
        data: {"type":"response.output_item.done","output_index":0,"item":{"id":"fc_dangerous","type":"function_call","status":"completed","arguments":"{}","call_id":"call_dangerous","name":"dangerous_tool"}}
        """;

    [TestMethod]
    [DataRow("failed")]
    [DataRow("incomplete")]
    public async Task NonStreamingFailureStatus_DoesNotExecuteToolOrRetry(string terminalStatus)
    {
        var response = terminalStatus == "failed"
            ? """
              {"id":"resp_failed","object":"response","status":"failed","error":{"code":"server_error","message":"generation failed"},"output":[{"type":"function_call","name":"dangerous_tool","call_id":"call_dangerous","arguments":"{}"}]}
              """
            : """
              {"id":"resp_incomplete","object":"response","status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[{"type":"function_call","name":"dangerous_tool","call_id":"call_dangerous","arguments":"{}"}]}
              """;
        var handler = new QueueHttpMessageHandler((response, "application/json"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "did not complete successfully");
        StringAssert.Contains(exception.ErrorDetails, terminalStatus);
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount, "A terminal failure must not trigger another billed round.");
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task NonStreamingCompletedRefusal_DoesNotExecuteTool()
    {
        const string response = """
            {
              "id":"resp_refusal",
              "object":"response",
              "status":"completed",
              "output":[
                {"type":"function_call","name":"dangerous_tool","call_id":"call_dangerous","arguments":"{}"},
                {"type":"message","status":"completed","role":"assistant","content":[{"type":"refusal","refusal":"I cannot help with that."}]}
              ]
            }
            """;
        var handler = new QueueHttpMessageHandler((response, "application/json"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "refused");
        StringAssert.Contains(exception.ErrorDetails, "refusal");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task NonStreamingCompletedEmptyResponse_IsTerminalWithoutRetry()
    {
        const string response = """
            {"id":"resp_empty","object":"response","status":"completed","output":[]}
            """;
        var handler = new QueueHttpMessageHandler((response, "application/json"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var result = await service.GetCompletionAsync("Return no output.");

        Assert.AreEqual(string.Empty, result);
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount, "A completed empty response must not loop through MaxRounds.");
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task NonStreamingMalformedResponse_FailsBeforeToolExtraction()
    {
        var handler = new QueueHttpMessageHandler(("{\"status\":\"completed\"", "application/json"));
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
    [DataRow("failed")]
    [DataRow("incomplete")]
    [DataRow("error")]
    public async Task StreamingTerminalFailure_DoesNotCommitOrExecuteCollectedTool(string terminalEvent)
    {
        var terminalJson = terminalEvent switch
        {
            "failed" => "{\"type\":\"response.failed\",\"response\":{\"id\":\"resp_failed\",\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":\"generation failed\"}}}",
            "incomplete" => "{\"type\":\"response.incomplete\",\"response\":{\"id\":\"resp_incomplete\",\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}",
            _ => "{\"type\":\"error\",\"code\":\"server_error\",\"message\":\"stream failed\"}"
        };
        var sse = $"{StreamingToolCallEvent}\n\ndata: {terminalJson}\n\n";
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        AssertSingleTerminalErrorWithoutCompletion(chunks, terminalEvent);
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingMalformedJson_DoesNotExecuteCollectedTool()
    {
        var sse = $"{StreamingToolCallEvent}\n\ndata: {{\"type\":\"response.completed\"\n\n";
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        AssertSingleTerminalErrorWithoutCompletion(chunks, "malformed");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingCompletedWithMalformedFunctionArguments_DoesNotExecuteTool()
    {
        const string sse = """
            data: {"type":"response.output_item.done","output_index":0,"item":{"id":"fc_dangerous","type":"function_call","status":"completed","arguments":"{bad","call_id":"call_dangerous","name":"dangerous_tool"}}

            data: {"type":"response.completed","response":{"id":"resp_bad_args","status":"completed","output":[{"id":"fc_dangerous","type":"function_call","status":"completed","arguments":"{bad","call_id":"call_dangerous","name":"dangerous_tool"}]}}

            """;
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        AssertSingleTerminalErrorWithoutCompletion(chunks, "malformed JSON arguments");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingArgumentDeltasAndDoneSnapshot_ExecuteWithOneValidJsonObject()
    {
        const string toolRound = """
            data: {"type":"response.output_item.added","output_index":0,"item":{"id":"fc_dangerous","type":"function_call","status":"in_progress","arguments":"","call_id":"call_dangerous","name":"dangerous_tool"}}

            data: {"type":"response.function_call_arguments.delta","item_id":"fc_dangerous","output_index":0,"delta":"{\"value\":"}

            data: {"type":"response.function_call_arguments.delta","item_id":"fc_dangerous","output_index":0,"delta":"1}"}

            data: {"type":"response.function_call_arguments.done","item_id":"fc_dangerous","output_index":0,"arguments":"{\"value\":1}"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"id":"fc_dangerous","type":"function_call","status":"completed","arguments":"{\"value\":1}","call_id":"call_dangerous","name":"dangerous_tool"}}

            data: {"type":"response.completed","response":{"id":"resp_tool","status":"completed","output":[{"id":"fc_dangerous","type":"function_call","status":"completed","arguments":"{\"value\":1}","call_id":"call_dangerous","name":"dangerous_tool"}]}}

            """;
        const string finalRound = """
            data: {"type":"response.output_text.delta","delta":"done"}

            data: {"type":"response.completed","response":{"id":"resp_final","status":"completed","output":[{"type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"done"}]}]}}

            """;
        var handler = new QueueHttpMessageHandler(
            (toolRound, "text/event-stream"),
            (finalRound, "text/event-stream"));
        Dictionary<string, object>? receivedArguments = null;
        var service = CreateService(handler);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Test tool.",
            Handler = arguments =>
            {
                receivedArguments = arguments;
                return Task.FromResult("tool result");
            }
        });

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        Assert.IsNotNull(receivedArguments);
        Assert.AreEqual("1", receivedArguments["value"].ToString());
        Assert.AreEqual(2, handler.RequestCount);
        Assert.AreEqual("done", string.Concat(chunks
            .Where(chunk => chunk.Type == StreamingContentType.Text)
            .Select(chunk => chunk.Content)));
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Error));
    }

    [TestMethod]
    public async Task StreamingEarlyEof_DoesNotExecuteCollectedToolOrSavePartialText()
    {
        var sse = $"data: {{\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}}\n\n{StreamingToolCallEvent}\n\n";
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        Assert.IsTrue(chunks.Any(chunk =>
            chunk.Type == StreamingContentType.Text && chunk.Content == "partial"));
        AssertSingleTerminalErrorWithoutCompletion(chunks, "response.completed");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingCompletedRefusal_DoesNotExecuteCollectedTool()
    {
        const string completedRefusal = """
            {"type":"response.completed","response":{"id":"resp_refusal","status":"completed","output":[{"type":"message","status":"completed","role":"assistant","content":[{"type":"refusal","refusal":"I cannot help with that."}]}]}}
            """;
        var sse = $"{StreamingToolCallEvent}\n\ndata: {completedRefusal}\n\n";
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var invocationCount = 0;
        var service = CreateServiceWithTool(handler, () => invocationCount++);

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        AssertSingleTerminalErrorWithoutCompletion(chunks, "refusal");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task StreamingSimpleTextApi_ThrowsOnEarlyEof()
    {
        const string sse = """
            data: {"type":"response.output_text.delta","delta":"partial"}

            """;
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(async () =>
        {
            await foreach (var _ in service.StreamAsync("hello"))
            {
            }
        });

        StringAssert.Contains(exception.Message, "response.completed");
        Assert.AreEqual(1, handler.RequestCount);
        AssertOnlyUserMessageWasSaved(service);
    }

    [TestMethod]
    public async Task ResponsesUsage_ParsesCacheWriteAndOutputReasoningDetails()
    {
        const string sse = """
            data: {"type":"response.completed","response":{"id":"resp_usage","status":"completed","model":"gpt-5.6-sol","output":[],"usage":{"input_tokens":100,"input_tokens_details":{"cached_tokens":11,"cache_write_tokens":7},"output_tokens":20,"output_tokens_details":{"reasoning_tokens":13},"total_tokens":120}}}

            """;
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var service = CreateService(handler);

        var chunks = await CollectAdvancedStreamAsync(
            service,
            StreamOptions.Default.WithFunctionCalls(false));
        var completion = chunks.Single(chunk => chunk.Type == StreamingContentType.Completion);

        Assert.IsNotNull(completion.Usage);
        Assert.AreEqual(100, completion.Usage.InputTokens);
        Assert.AreEqual(20, completion.Usage.OutputTokens);
        Assert.AreEqual(11, completion.Usage.CachedInputTokens);
        Assert.AreEqual(7, completion.Usage.CacheCreationTokens);
        Assert.AreEqual(13, completion.Usage.ReasoningTokens);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(",\"usage\":null")]
    public async Task LegacyUsage_MissingOrNullBeforeFinalChunk_StillParsesFinalUsage(
        string nonTerminalUsageProperty)
    {
        var sse =
            "data: {\"id\":\"chatcmpl_usage\",\"model\":\"gpt-4o-mini\"," +
            "\"choices\":[{\"delta\":{\"content\":\"ok\"}}]" +
            nonTerminalUsageProperty + "}\n\n" +
            "data: {\"id\":\"chatcmpl_usage\",\"model\":\"gpt-4o-mini\",\"choices\":[]," +
            "\"usage\":{\"prompt_tokens\":10,\"prompt_tokens_details\":{\"cached_tokens\":3}," +
            "\"completion_tokens\":5,\"completion_tokens_details\":{\"reasoning_tokens\":4}," +
            "\"total_tokens\":15}}\n\ndata: [DONE]\n\n";
        var handler = new QueueHttpMessageHandler((sse, "text/event-stream"));
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt4oMini);

        var chunks = await CollectAdvancedStreamAsync(
            service,
            StreamOptions.Default.WithFunctionCalls(false));
        var completion = chunks.Single(chunk => chunk.Type == StreamingContentType.Completion);

        Assert.IsNotNull(completion.Usage);
        Assert.AreEqual(10, completion.Usage.InputTokens);
        Assert.AreEqual(5, completion.Usage.OutputTokens);
        Assert.AreEqual(3, completion.Usage.CachedInputTokens);
        Assert.AreEqual(4, completion.Usage.ReasoningTokens);
    }

    [TestMethod]
    public async Task LegacyCompleteToolPayload_WithStopFinishReason_ExecutesAndKeepsDiagnostic()
    {
        const string toolRound = """
            data: {"id":"chatcmpl_tool","model":"gpt-4o","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_stop","type":"function","function":{"name":"dangerous_tool","arguments":"{\"value\":1}"}}]},"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl_tool","model":"gpt-4o","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":null}

            data: [DONE]

            """;
        const string finalRound = """
            data: {"id":"chatcmpl_final","model":"gpt-4o","choices":[{"index":0,"delta":{"content":"done"},"finish_reason":null}],"usage":null}

            data: {"id":"chatcmpl_final","model":"gpt-4o","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":null}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(
            (toolRound, "text/event-stream"),
            (finalRound, "text/event-stream"));
        var invocationCount = 0;
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt4o);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Test tool.",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("tool result");
            }
        });

        var chunks = await CollectAdvancedStreamAsync(service, StreamOptions.WithFunctions);

        Assert.AreEqual(1, invocationCount);
        Assert.AreEqual(2, handler.RequestCount);
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Error));
        Assert.IsTrue(chunks.Any(chunk =>
            chunk.Type == StreamingContentType.FunctionResult &&
            chunk.FunctionResult?.Call.Name == "dangerous_tool"));
        var callMessage = service.ActivateChat.Messages.Single(message =>
            message.FunctionCallBatch != null);
        Assert.AreEqual(
            "stop",
            callMessage.Metadata?["function_finish_reason_mismatch"]?.ToString());
    }

    [TestMethod]
    public async Task LegacyNonStreamingCompleteToolPayload_WithStopFinishReason_ExecutesAndKeepsDiagnostic()
    {
        const string toolRound = """
            {"id":"chatcmpl_tool","model":"gpt-4o","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_stop","type":"function","function":{"name":"dangerous_tool","arguments":"{\"value\":1}"}}]}}]}
            """;
        const string finalRound = """
            {"id":"chatcmpl_final","model":"gpt-4o","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}]}
            """;
        var handler = new QueueHttpMessageHandler(
            (toolRound, "application/json"),
            (finalRound, "application/json"));
        var invocationCount = 0;
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt4o);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Test tool.",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("tool result");
            }
        });

        var result = await service.GetCompletionAsync("Run the tool.");

        Assert.AreEqual("done", result);
        Assert.AreEqual(1, invocationCount);
        Assert.AreEqual(2, handler.RequestCount);
        var callMessage = service.ActivateChat.Messages.Single(message =>
            message.FunctionCallBatch != null);
        Assert.AreEqual(
            "stop",
            callMessage.Metadata?["function_finish_reason_mismatch"]?.ToString());
        Assert.AreEqual(
            "stop",
            callMessage.FunctionCallBatch?.Metadata?["function_finish_reason_mismatch"]?.ToString());
    }

    private static OpenAIService CreateService(HttpMessageHandler handler)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        return service;
    }

    private static OpenAIService CreateServiceWithTool(HttpMessageHandler handler, Action onInvoke)
    {
        var service = CreateService(handler);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "A side-effecting tool that must run only after a completed response.",
            Handler = _ =>
            {
                onInvoke();
                return Task.FromResult("done");
            }
        });
        return service;
    }

    private static async Task<List<StreamingContent>> CollectAdvancedStreamAsync(
        OpenAIService service,
        StreamOptions options)
    {
        var chunks = new List<StreamingContent>();
        await foreach (var chunk in service.StreamAsync("Run the tool.", options))
            chunks.Add(chunk);
        return chunks;
    }

    private static void AssertSingleTerminalErrorWithoutCompletion(
        IReadOnlyCollection<StreamingContent> chunks,
        string expectedMessage)
    {
        var error = chunks.Single(chunk => chunk.Type == StreamingContentType.Error);
        StringAssert.Contains(error.Content, expectedMessage);
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Completion),
            "A failed round must not be followed by a synthesized Completion chunk.");
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionResult),
            "A failed round must not execute a collected tool.");
    }

    private static void AssertOnlyUserMessageWasSaved(OpenAIService service)
    {
        Assert.AreEqual(1, service.ActivateChat.Messages.Count,
            "Partial assistant content and function records must not be committed on failure.");
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(string Body, string MediaType)> _responses;

        public QueueHttpMessageHandler(params (string Body, string MediaType)[] responses)
        {
            _responses = new Queue<(string Body, string MediaType)>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued response remains.");

            var response = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, response.MediaType)
            });
        }
    }
}
