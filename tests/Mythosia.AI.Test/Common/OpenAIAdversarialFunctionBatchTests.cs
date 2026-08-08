using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIAdversarialFunctionBatchTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResponsesNonStreaming_MissingOrEmptyCallId_DoesNotExecute(bool includeEmptyCallId)
    {
        var callId = includeEmptyCallId ? ",\"call_id\":\"\"" : string.Empty;
        var response =
            "{\"status\":\"completed\",\"output\":[" +
            "{\"id\":\"fc_unsafe\",\"type\":\"function_call\"" + callId +
            ",\"name\":\"dangerous_tool\",\"arguments\":\"{}\"}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationCount = 0;
        var service = CreateResponsesService(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "call_id");
        Assert.AreEqual(0, invocationCount);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task LegacyNonStreaming_ToolCallsWithWrongJsonKind_DoesNotExecute()
    {
        const string response =
            "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{" +
            "\"content\":null,\"tool_calls\":{\"id\":\"call_unsafe\"}}}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationCount = 0;
        var service = CreateLegacyService(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "tool_calls");
        Assert.AreEqual(0, invocationCount);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task ResponsesStreaming_SecondCallWithoutCallId_IsAtomic()
    {
        const string stream = """
            data: {"type":"response.output_item.done","output_index":0,"item":{"id":"fc_one","type":"function_call","status":"completed","call_id":"call_one","name":"first","arguments":"{}"}}

            data: {"type":"response.completed","response":{"id":"resp_unsafe","status":"completed","output":[{"id":"fc_one","type":"function_call","status":"completed","call_id":"call_one","name":"first","arguments":"{}"},{"id":"fc_two","type":"function_call","status":"completed","name":"second","arguments":"{}"}]}}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocations = new List<string>();
        var service = CreateResponsesService(handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var events = await CollectAsync(service);

        Assert.AreEqual(0, invocations.Count);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        var error = events.Single(item => item.Type == StreamingContentType.Error);
        StringAssert.Contains(error.Content ?? string.Empty, "call_id");
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task LegacyStreaming_TruncatedAfterCompleteArguments_IsAtomic()
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_unsafe","function":{"name":"dangerous_tool","arguments":"{}"}}]}}]}

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocationCount = 0;
        var service = CreateLegacyService(handler, () => invocationCount++);

        var events = await CollectAsync(service);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        StringAssert.Contains(
            events.Single(item => item.Type == StreamingContentType.Error).Content ?? string.Empty,
            "[DONE]");
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task LegacyStreaming_UnsafeFinishReason_IsAtomic()
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_unsafe","function":{"name":"dangerous_tool","arguments":"{}"}}]}}]}

            data: {"choices":[{"delta":{},"finish_reason":"length"}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocationCount = 0;
        var service = CreateLegacyService(handler, () => invocationCount++);

        var events = await CollectAsync(service);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(
            "length",
            events.Single(item => item.Type == StreamingContentType.Error).Metadata?["finish_reason"]);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task LegacyStreaming_RepeatedCumulativeArgumentSnapshots_RunOnce()
    {
        const string toolRound = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_stable","function":{"name":"dangerous_tool","arguments":"{\"value\":"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"value\":1}"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"value\":1}"}}]}}]}

            data: [DONE]

            """;
        const string finalRound = """
            data: {"choices":[{"delta":{"content":"done"}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(toolRound), Response.Sse(finalRound));
        var invocationCount = 0;
        var service = CreateLegacyService(handler);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Handler = arguments =>
            {
                invocationCount++;
                Assert.AreEqual("1", arguments["value"].ToString());
                return Task.FromResult("ok");
            }
        });

        var events = await CollectAsync(service);

        Assert.AreEqual(1, invocationCount);
        Assert.AreEqual("done", string.Concat(events
            .Where(item => item.Type == StreamingContentType.Text)
            .Select(item => item.Content)));
        var call = events.Single(item => item.Type == StreamingContentType.FunctionCall);
        var result = events.Single(item => item.Type == StreamingContentType.FunctionResult);
        Assert.AreEqual("call_stable", call.FunctionCall?.Id);
        Assert.AreEqual(call.FunctionCallBatchId, result.FunctionCallBatchId);
    }

    [TestMethod]
    public async Task LegacyStreaming_DuplicateIdsAtDistinctIndexes_DoNotCollapseOrExecute()
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_duplicate","function":{"name":"first","arguments":"{}"}},{"index":1,"id":"call_duplicate","function":{"name":"second","arguments":"{}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocations = new List<string>();
        var service = CreateLegacyService(handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            async () => await CollectAsync(service));

        StringAssert.Contains(exception.Message, "duplicate function-call ID");
        Assert.AreEqual(0, invocations.Count);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("length")]
    [DataRow("content_filter")]
    public async Task LegacyNonStreaming_UnsafeFinishReasonWithToolPayload_IsRejected(
        string finishReason)
    {
        var response =
            $"{{\"choices\":[{{\"finish_reason\":\"{finishReason}\",\"message\":{{" +
            "\"content\":null,\"tool_calls\":[{\"id\":\"call_unsafe\",\"type\":\"function\"," +
            "\"function\":{\"name\":\"dangerous_tool\",\"arguments\":\"{}\"}}]}}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationCount = 0;
        var service = CreateLegacyService(handler, () => invocationCount++);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "finish_reason");
        Assert.AreEqual(0, invocationCount);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task LegacyNonStreaming_CompletedEmptyResponseDoesNotRetry()
    {
        const string response =
            "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"\"}}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var service = CreateLegacyService(handler);
        service.Functions.Add(CreateFunction("unused_tool", new List<string>()));

        var result = await service.GetCompletionAsync("Return empty.");

        Assert.AreEqual(string.Empty, result);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task LegacyStreaming_SameIndexCannotChangeIdentity()
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_a","function":{"name":"first","arguments":"{"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_b","function":{"name":"second","arguments":"}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocations = new List<string>();
        var service = CreateLegacyService(handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            async () => await CollectAsync(service));

        StringAssert.Contains(exception.Message, "changed function-call ID");
        Assert.AreEqual(0, invocations.Count);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    public async Task ResponsesStreaming_LateCallIdEmitsOneStablyCorrelatedEvent()
    {
        const string toolRound = """
            data: {"type":"response.function_call_arguments.delta","output_index":0,"name":"dangerous_tool","delta":"{}"}

            data: {"type":"response.output_item.done","output_index":0,"item":{"id":"fc_late","type":"function_call","status":"completed","call_id":"call_late","name":"dangerous_tool","arguments":"{}"}}

            data: {"type":"response.completed","response":{"id":"resp_late","status":"completed","output":[{"id":"fc_late","type":"function_call","status":"completed","call_id":"call_late","name":"dangerous_tool","arguments":"{}"}]}}

            data: [DONE]

            """;
        const string finalRound = """
            data: {"type":"response.output_text.delta","delta":"done"}

            data: {"type":"response.completed","response":{"id":"resp_final","status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"done"}]}]}}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(toolRound),
            Response.Sse(finalRound));
        var invocationCount = 0;
        var service = CreateResponsesService(handler, () => invocationCount++);

        var events = await CollectAsync(service);

        Assert.AreEqual(1, invocationCount);
        var call = events.Single(item => item.Type == StreamingContentType.FunctionCall);
        var result = events.Single(item => item.Type == StreamingContentType.FunctionResult);
        Assert.AreEqual("call_late", call.FunctionCall?.Id);
        Assert.AreEqual("call_late", result.FunctionResult?.Call.Id);
        Assert.AreEqual(call.FunctionCallBatchId, result.FunctionCallBatchId);
        Assert.AreEqual(call.FunctionCall?.Index, result.FunctionResult?.Call.Index);
    }

    private static OpenAIService CreateResponsesService(
        QueueHttpMessageHandler handler,
        Action? onInvoke = null)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        if (onInvoke != null)
        {
            service.Functions.Add(new FunctionDefinition
            {
                Name = "dangerous_tool",
                Handler = _ =>
                {
                    onInvoke();
                    return Task.FromResult("unexpected");
                }
            });
        }

        return service;
    }

    private static OpenAIService CreateLegacyService(
        QueueHttpMessageHandler handler,
        Action? onInvoke = null)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt4oMini);
        if (onInvoke != null)
        {
            service.Functions.Add(new FunctionDefinition
            {
                Name = "dangerous_tool",
                Handler = _ =>
                {
                    onInvoke();
                    return Task.FromResult("unexpected");
                }
            });
        }

        return service;
    }

    private static FunctionDefinition CreateFunction(string name, List<string> invocations)
    {
        return new FunctionDefinition
        {
            Name = name,
            Handler = _ =>
            {
                invocations.Add(name);
                return Task.FromResult(name);
            }
        };
    }

    private static async Task<List<StreamingContent>> CollectAsync(OpenAIService service)
    {
        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Run tools.", StreamOptions.WithFunctions))
            events.Add(content);
        return events;
    }

    private static void AssertOnlyUserHistory(OpenAIService service)
    {
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
    }
}
