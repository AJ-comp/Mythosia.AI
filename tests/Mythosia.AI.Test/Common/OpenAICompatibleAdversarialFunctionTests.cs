using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.xAI;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAICompatibleAdversarialFunctionTests
{
    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_MissingIndexesAcrossChunksKeepDistinctCalls(string provider)
    {
        const string toolRound = """
            data: {"choices":[{"delta":{"tool_calls":[{"id":"call_first","function":{"name":"first","arguments":"{}"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"id":"call_second","function":{"name":"second","arguments":"{}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(toolRound),
            Response.Sse(FinalTextStream));
        var invocations = new List<string>();
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var events = await CollectAsync(service);

        CollectionAssert.AreEqual(new[] { "first", "second" }, invocations);
        var results = events.Where(item => item.Type == StreamingContentType.FunctionResult).ToArray();
        CollectionAssert.AreEqual(
            new[] { "call_first", "call_second" },
            results.Select(item => item.FunctionResult?.Call.Id).ToArray());
        Assert.AreEqual(2, handler.RequestBodies.Count);
        using var continuation = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.AreEqual(2, continuation.RootElement.GetProperty("messages")
            .EnumerateArray().Single(message => message.TryGetProperty("tool_calls", out _))
            .GetProperty("tool_calls").GetArrayLength());
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_OutOfOrderChunksExecuteByExplicitProviderIndex(string provider)
    {
        const string toolRound = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":1,"id":"call_second","function":{"name":"second","arguments":"{}"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_first","function":{"name":"first","arguments":"{}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(toolRound),
            Response.Sse(FinalTextStream));
        var invocations = new List<string>();
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var events = await CollectAsync(service);

        CollectionAssert.AreEqual(new[] { "first", "second" }, invocations);
        var callEvents = events.Where(item => item.Type == StreamingContentType.FunctionCall).ToArray();
        var resultEvents = events.Where(item => item.Type == StreamingContentType.FunctionResult).ToArray();
        Assert.IsTrue(callEvents.Concat(resultEvents).All(item =>
            item.FunctionCallBatchId == callEvents[0].FunctionCallBatchId));
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            resultEvents.Select(item => item.FunctionResult!.Call.Index).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "call_first", "call_second" },
            callEvents.Select(item => item.FunctionCall!.Id).ToArray());
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_DuplicateIdsAtDistinctIndexesAreAtomic(string provider)
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_duplicate","function":{"name":"first","arguments":"{}"}},{"index":1,"id":"call_duplicate","function":{"name":"second","arguments":"{}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocations = new List<string>();
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            async () => await CollectAsync(service));

        StringAssert.Contains(exception.Message, "duplicate function-call ID");
        Assert.AreEqual(0, invocations.Count);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_MalformedSecondArgumentsAreAtomic(string provider)
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_first","function":{"name":"first","arguments":"{}"}},{"index":1,"id":"call_second","function":{"name":"second","arguments":"{bad"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocations = new List<string>();
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var events = await CollectAsync(service);

        Assert.AreEqual(0, invocations.Count);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(
            "malformed_function_arguments",
            events.Single(item => item.Type == StreamingContentType.Error).Metadata?["status"]);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_TruncatedTransportIsAtomic(string provider)
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_unsafe","function":{"name":"dangerous_tool","arguments":"{}"}}]}}]}

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocationCount = 0;
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("dangerous_tool", () => invocationCount++));

        var events = await CollectAsync(service);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        StringAssert.Contains(
            events.Single(item => item.Type == StreamingContentType.Error).Content ?? string.Empty,
            "[DONE]");
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_ProviderErrorAfterFirstCallIsAtomic(string provider)
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_unsafe","function":{"name":"dangerous_tool","arguments":"{}"}}]}}]}

            data: {"error":{"message":"provider failed","type":"server_error"}}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocationCount = 0;
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("dangerous_tool", () => invocationCount++));

        var events = await CollectAsync(service);

        Assert.AreEqual(0, invocationCount);
        var error = events.Single(item => item.Type == StreamingContentType.Error);
        StringAssert.Contains(error.Content ?? string.Empty, "provider failed");
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_RepeatedCumulativeSnapshotsRunOnce(string provider)
    {
        const string toolRound = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_stable","function":{"name":"dangerous_tool","arguments":"{\"value\":"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"value\":1}"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"value\":1}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(toolRound),
            Response.Sse(FinalTextStream));
        var invocationCount = 0;
        var service = CreateService(provider, handler);
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
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.FunctionResult));
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task NonStreaming_WrongToolCallsKindIsAtomic(string provider)
    {
        const string response =
            "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{" +
            "\"content\":null,\"tool_calls\":{\"id\":\"call_unsafe\"}}}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationCount = 0;
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("dangerous_tool", () => invocationCount++));

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "tool_calls");
        Assert.AreEqual(0, invocationCount);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task NonStreaming_ToolPayloadAndFinishReasonMustAgree(string provider)
    {
        const string response =
            "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{" +
            "\"content\":null,\"tool_calls\":[{\"id\":\"call_unsafe\",\"type\":\"function\"," +
            "\"function\":{\"name\":\"dangerous_tool\",\"arguments\":\"{}\"}}]}}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationCount = 0;
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("dangerous_tool", () => invocationCount++));

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "finish_reason");
        Assert.AreEqual(0, invocationCount);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task NonStreaming_ToolFinishWithoutCallsIsAtomic(string provider)
    {
        const string response =
            "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{" +
            "\"content\":null,\"tool_calls\":[]}}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationCount = 0;
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("dangerous_tool", () => invocationCount++));

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.Message, "no usable tool calls");
        Assert.AreEqual(0, invocationCount);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task NonStreaming_CompletedEmptyResponseDoesNotRetry(string provider)
    {
        const string response =
            "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"\"}}]}";
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("unused_tool", () => { }));

        var result = await service.GetCompletionAsync("Return empty.");

        Assert.AreEqual(string.Empty, result);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_SameIndexCannotChangeIdentity(string provider)
    {
        const string stream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_a","function":{"name":"first","arguments":"{"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_b","function":{"name":"second","arguments":"}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var invocations = new List<string>();
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("first", invocations));
        service.Functions.Add(CreateFunction("second", invocations));

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            async () => await CollectAsync(service));

        StringAssert.Contains(exception.Message, "changed function-call ID");
        Assert.AreEqual(0, invocations.Count);
        AssertOnlyUserHistory(service);
    }

    [TestMethod]
    [DataRow("xai")]
    [DataRow("qwen")]
    public async Task Streaming_LateCallIdEmitsOneStablyCorrelatedEvent(string provider)
    {
        const string toolRound = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"dangerous_tool","arguments":"{}"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_late","function":{}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(toolRound),
            Response.Sse(FinalTextStream));
        var invocationCount = 0;
        var service = CreateService(provider, handler);
        service.Functions.Add(CreateFunction("dangerous_tool", () => invocationCount++));

        var events = await CollectAsync(service);

        Assert.AreEqual(1, invocationCount);
        var call = events.Single(item => item.Type == StreamingContentType.FunctionCall);
        var result = events.Single(item => item.Type == StreamingContentType.FunctionResult);
        Assert.AreEqual("call_late", call.FunctionCall?.Id);
        Assert.AreEqual("call_late", result.FunctionResult?.Call.Id);
        Assert.AreEqual(call.FunctionCallBatchId, result.FunctionCallBatchId);
        Assert.AreEqual(call.FunctionCall?.Index, result.FunctionResult?.Call.Index);
    }

    private const string FinalTextStream = """
        data: {"choices":[{"delta":{"content":"done"}}]}

        data: [DONE]

        """;

    private static AIService CreateService(string provider, QueueHttpMessageHandler handler)
    {
        return provider == "xai"
            ? new XAIService("offline-test-key", new HttpClient(handler))
            : new QwenService("offline-test-key", new HttpClient(handler));
    }

    private static FunctionDefinition CreateFunction(string name, List<string> invocations)
    {
        return CreateFunction(name, () => invocations.Add(name));
    }

    private static FunctionDefinition CreateFunction(string name, Action onInvoke)
    {
        return new FunctionDefinition
        {
            Name = name,
            Handler = _ =>
            {
                onInvoke();
                return Task.FromResult(name);
            }
        };
    }

    private static async Task<List<StreamingContent>> CollectAsync(AIService service)
    {
        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Run tools.", StreamOptions.WithFunctions))
            events.Add(content);
        return events;
    }

    private static void AssertOnlyUserHistory(AIService service)
    {
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
    }
}
