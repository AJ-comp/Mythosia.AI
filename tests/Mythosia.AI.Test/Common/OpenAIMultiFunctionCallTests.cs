using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("FunctionCalling")]
public class OpenAIMultiFunctionCallTests
{
    [TestMethod]
    public async Task ResponsesApi_TwoCallsExecuteSequentiallyAndReplayOriginalOutputOnce()
    {
        const string toolRound = """
        {
          "id": "resp_tools",
          "status": "completed",
          "output": [
            { "id": "reasoning-1", "type": "reasoning", "status": "completed", "summary": [], "encrypted_content": "state" },
            { "id": "item-a", "type": "function_call", "status": "completed", "call_id": "call-a", "name": "first", "arguments": "{\"value\":1}" },
            { "id": "item-b", "type": "function_call", "status": "completed", "call_id": "call-b", "name": "second", "arguments": "{\"value\":2}" }
          ]
        }
        """;
        const string finalRound = """
        {
          "id": "resp_final",
          "status": "completed",
          "output": [
            { "type": "message", "role": "assistant", "content": [{ "type": "output_text", "text": "done" }] }
          ]
        }
        """;

        var handler = new QueueHttpMessageHandler(Response.Json(toolRound), Response.Json(finalRound));
        var service = CreateService(handler);
        var executionOrder = new List<string>();
        var concurrency = new ConcurrencyProbe();
        service.Functions.Add(CreateFunction("first", "result-a", executionOrder, concurrency));
        service.Functions.Add(CreateFunction("second", "result-b", executionOrder, concurrency));

        var result = await service.GetCompletionAsync("run both");

        Assert.AreEqual("done", result);
        CollectionAssert.AreEqual(new[] { "first", "second" }, executionOrder);
        Assert.AreEqual(1, concurrency.Maximum);
        Assert.AreEqual(2, handler.RequestBodies.Count);

        using var secondRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        var input = secondRequest.RootElement.GetProperty("input");
        Assert.AreEqual(6, input.GetArrayLength());
        Assert.AreEqual("reasoning", input[1].GetProperty("type").GetString());
        Assert.AreEqual("call-a", input[2].GetProperty("call_id").GetString());
        Assert.AreEqual("call-b", input[3].GetProperty("call_id").GetString());
        Assert.AreEqual("function_call_output", input[4].GetProperty("type").GetString());
        Assert.AreEqual("call-a", input[4].GetProperty("call_id").GetString());
        Assert.AreEqual("function_call_output", input[5].GetProperty("type").GetString());
        Assert.AreEqual("call-b", input[5].GetProperty("call_id").GetString());

        var assistantBatch = service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        var resultBatch = service.ActivateChat.Messages.Single(message => message.FunctionCallResultBatch != null);
        Assert.AreEqual(2, assistantBatch.FunctionCallBatch!.Calls.Count);
        Assert.AreEqual(2, resultBatch.FunctionCallResultBatch!.Results.Count);
    }

    [TestMethod]
    public async Task ResponsesApi_InvalidSecondCallExecutesNeitherHandler()
    {
        const string malformedToolRound = """
        {
          "id": "resp_tools",
          "status": "completed",
          "output": [
            { "type": "function_call", "call_id": "call-a", "name": "first", "arguments": "{}" },
            { "type": "function_call", "call_id": "call-b", "name": "second", "arguments": "[" }
          ]
        }
        """;

        var handler = new QueueHttpMessageHandler(Response.Json(malformedToolRound));
        var service = CreateService(handler);
        var executions = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first",
            Handler = _ =>
            {
                executions++;
                return Task.FromResult("first");
            }
        });
        service.Functions.Add(new FunctionDefinition
        {
            Name = "second",
            Handler = _ =>
            {
                executions++;
                return Task.FromResult("second");
            }
        });

        await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.GetCompletionAsync("run both"));
        Assert.AreEqual(0, executions);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task ResponsesApi_DuplicateCallIdExecutesNeitherHandlerAndCommitsNoToolHistory()
    {
        const string duplicateToolRound = """
        {
          "id": "resp_tools",
          "status": "completed",
          "output": [
            { "type": "function_call", "call_id": "duplicate", "name": "first", "arguments": "{}" },
            { "type": "function_call", "call_id": "duplicate", "name": "second", "arguments": "{}" }
          ]
        }
        """;
        var handler = new QueueHttpMessageHandler(Response.Json(duplicateToolRound));
        var service = CreateService(handler);
        var executions = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first",
            Handler = _ =>
            {
                executions++;
                return Task.FromResult("first");
            }
        });
        service.Functions.Add(new FunctionDefinition
        {
            Name = "second",
            Handler = _ =>
            {
                executions++;
                return Task.FromResult("second");
            }
        });

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("run both"));

        StringAssert.Contains(exception.Message, "duplicate function-call ID");
        Assert.AreEqual(0, executions);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task ResponsesApiStreaming_TwoCallsExecuteSequentiallyAndKeepStableIndexes()
    {
        const string toolRound = """
        data: {"type":"response.output_item.done","output_index":1,"item":{"id":"item-a","type":"function_call","status":"completed","call_id":"call-a","name":"first","arguments":"{\"value\":1}"}}

        data: {"type":"response.output_item.done","output_index":3,"item":{"id":"item-b","type":"function_call","status":"completed","call_id":"call-b","name":"second","arguments":"{\"value\":2}"}}

        data: {"type":"response.completed","response":{"id":"resp-tools","status":"completed","output":[{"id":"item-a","type":"function_call","status":"completed","call_id":"call-a","name":"first","arguments":"{\"value\":1}"},{"id":"item-b","type":"function_call","status":"completed","call_id":"call-b","name":"second","arguments":"{\"value\":2}"}]}}

        data: [DONE]

        """;
        const string finalRound = """
        data: {"type":"response.output_text.delta","delta":"done"}

        data: {"type":"response.completed","response":{"id":"resp-final","status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"done"}]}]}}

        data: [DONE]

        """;

        var handler = new QueueHttpMessageHandler(Response.Sse(toolRound), Response.Sse(finalRound));
        var service = CreateService(handler);
        var executionOrder = new List<string>();
        var concurrency = new ConcurrencyProbe();
        service.Functions.Add(CreateFunction("first", "result-a", executionOrder, concurrency));
        service.Functions.Add(CreateFunction("second", "result-b", executionOrder, concurrency));
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync("run both", StreamOptions.WithFunctions))
            events.Add(content);

        CollectionAssert.AreEqual(new[] { "first", "second" }, executionOrder);
        Assert.AreEqual(1, concurrency.Maximum);
        var calls = events.Where(item => item.Type == StreamingContentType.FunctionCall).ToArray();
        var results = events.Where(item => item.Type == StreamingContentType.FunctionResult).ToArray();
        CollectionAssert.AreEqual(new[] { 1, 3 }, calls.Select(item => item.FunctionCall!.Index).ToArray());
        CollectionAssert.AreEqual(
            calls.Select(item => item.FunctionCall!.Index).ToArray(),
            results.Select(item => item.FunctionResult!.Call.Index).ToArray());
        Assert.IsTrue(calls.Concat(results).All(item =>
            item.FunctionCallBatchId == calls[0].FunctionCallBatchId));
        Assert.AreEqual(2, handler.RequestBodies.Count);

        using var continuation = JsonDocument.Parse(handler.RequestBodies[1]);
        var input = continuation.RootElement.GetProperty("input");
        Assert.AreEqual(5, input.GetArrayLength());
        Assert.AreEqual("call-a", input[1].GetProperty("call_id").GetString());
        Assert.AreEqual("call-b", input[2].GetProperty("call_id").GetString());
        Assert.AreEqual("call-a", input[3].GetProperty("call_id").GetString());
        Assert.AreEqual("call-b", input[4].GetProperty("call_id").GetString());
    }

    private static OpenAIService CreateService(QueueHttpMessageHandler handler)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        return service;
    }

    private static FunctionDefinition CreateFunction(
        string name,
        string result,
        List<string> executionOrder,
        ConcurrencyProbe concurrency)
    {
        return new FunctionDefinition
        {
            Name = name,
            Handler = async _ =>
            {
                executionOrder.Add(name);
                concurrency.Enter();
                await Task.Delay(10);
                concurrency.Exit();
                return result;
            }
        };
    }

    private sealed class ConcurrencyProbe
    {
        private int _active;

        public int Maximum { get; private set; }

        public void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            Maximum = Math.Max(Maximum, active);
        }

        public void Exit() => Interlocked.Decrement(ref _active);
    }
}
