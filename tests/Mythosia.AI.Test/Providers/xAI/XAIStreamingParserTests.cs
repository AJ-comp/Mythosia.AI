using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.xAI;
using Mythosia.AI.Tests.Common;
using System.Text.Json;

namespace Mythosia.AI.Tests.xAI;

[TestClass]
[TestCategory("Unit")]
public class XAIStreamingParserTests
{
    [TestMethod]
    public void ToolCallsDelta_ParsesEveryCallInProviderOrder()
    {
        var calls = new XAIStreamingProbe().ParseFunctionCalls(
            """
            {
              "choices": [
                {
                  "delta": {
                    "tool_calls": [
                      {
                        "index": 1,
                        "id": "call_second",
                        "function": {
                          "name": "get_forecast"
                        }
                      },
                      {
                        "index": 0,
                        "id": "call_first",
                        "function": {
                          "name": "get_time",
                          "arguments": "{\"zone\":\"Asia/Seoul\"}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """);

        Assert.AreEqual(2, calls.Count);

        Assert.AreEqual(1, calls[0].Index);
        Assert.AreEqual("call_second", calls[0].Id);
        Assert.AreEqual("get_forecast", calls[0].Name);
        Assert.AreEqual(true, calls[0].Arguments["_missing"]);

        Assert.AreEqual(0, calls[1].Index);
        Assert.AreEqual("call_first", calls[1].Id);
        Assert.AreEqual("get_time", calls[1].Name);
        Assert.AreEqual("{\"zone\":\"Asia/Seoul\"}", calls[1].Arguments["_partial"]);
    }

    [TestMethod]
    public void LegacyFunctionCallDelta_UsesSingleCallFallback()
    {
        var calls = new XAIStreamingProbe().ParseFunctionCalls(
            """
            {
              "choices": [
                {
                  "delta": {
                    "function_call": {
                      "name": "legacy_lookup",
                      "arguments": "{\"value\":42}"
                    }
                  }
                }
              ]
            }
            """);

        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual(0, calls[0].Index);
        Assert.AreEqual("legacy_lookup", calls[0].Name);
        Assert.AreEqual("{\"value\":42}", calls[0].Arguments["_partial"]);
    }

    [TestMethod]
    public void ToolCallsDelta_UsesProviderPositionWhenIndexIsMissing()
    {
        var calls = new XAIStreamingProbe().ParseFunctionCalls(
            """
            {
              "choices": [
                {
                  "delta": {
                    "tool_calls": [
                      { "id": "call_zero", "function": { "name": "first" } },
                      { "id": "call_one", "function": { "name": "second" } }
                    ]
                  }
                }
              ]
            }
            """);

        CollectionAssert.AreEqual(new[] { 0, 1 }, calls.Select(call => call.Index).ToArray());
    }

    [TestMethod]
    public async Task StreamingInterleavedCalls_ExecuteSequentiallyAndSerializeOneBatch()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Sse(InterleavedToolStream),
            Response.Sse(FinalTextStream));
        var service = new XAIService("offline-test-key", new HttpClient(handler));
        var executionOrder = new List<string>();
        service.Functions.Add(CreateFunction("first", executionOrder));
        service.Functions.Add(CreateFunction("second", executionOrder));
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync("run both", StreamOptions.WithFunctions))
            events.Add(content);

        CollectionAssert.AreEqual(new[] { "first", "second" }, executionOrder);
        var callEvents = events.Where(item => item.Type == StreamingContentType.FunctionCall).ToArray();
        var resultEvents = events.Where(item => item.Type == StreamingContentType.FunctionResult).ToArray();
        CollectionAssert.AreEqual(
            new[] { "first", "second" },
            callEvents.Select(item => item.FunctionCall!.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            resultEvents.Select(item => item.FunctionResult!.Call.Index).ToArray());
        Assert.IsTrue(callEvents.Concat(resultEvents).All(item =>
            item.FunctionCallBatchId == callEvents[0].FunctionCallBatchId));
        Assert.AreEqual(2, handler.RequestBodies.Count);

        using var continuation = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = continuation.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var assistant = messages.Single(message => message.TryGetProperty("tool_calls", out _));
        Assert.AreEqual(2, assistant.GetProperty("tool_calls").GetArrayLength());
        CollectionAssert.AreEqual(
            new[] { "call-a", "call-b" },
            messages
                .Where(message => message.GetProperty("role").GetString() == "tool")
                .Select(message => message.GetProperty("tool_call_id").GetString())
                .ToArray());
    }

    [TestMethod]
    public async Task StreamingEventMutationCannotRewriteExecutionOrContinuationHistory()
    {
        const string functionStream = """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-original","function":{"name":"lookup","arguments":"{\"value\":\"original\"}"}}]}}]}

            data: [DONE]

            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(functionStream),
            Response.Sse(FinalTextStream));
        var service = new XAIService("offline-test-key", new HttpClient(handler));
        var observedArgument = string.Empty;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Handler = arguments =>
            {
                observedArgument = arguments["value"].ToString();
                return Task.FromResult("original-result");
            }
        });

        await foreach (var content in service.StreamAsync(
            "run lookup",
            StreamOptions.WithFunctions))
        {
            if (content.FunctionCall != null)
            {
                content.FunctionCall.Id = "attacker-call";
                content.FunctionCall.Name = "attacker-function";
                content.FunctionCall.Arguments.Clear();
            }

            if (content.FunctionResult != null)
            {
                content.FunctionResult.Call.Id = "attacker-result-call";
                content.FunctionResult.Call.Name = "attacker-result-function";
                content.FunctionResult.Content = "attacker-result";
            }
        }

        Assert.AreEqual("original", observedArgument);
        Assert.AreEqual(2, handler.RequestBodies.Count);
        using var continuation = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = continuation.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var assistant = messages.Single(message => message.TryGetProperty("tool_calls", out _));
        var serializedCall = assistant.GetProperty("tool_calls")[0];
        Assert.AreEqual("call-original", serializedCall.GetProperty("id").GetString());
        Assert.AreEqual(
            "lookup",
            serializedCall.GetProperty("function").GetProperty("name").GetString());
        var toolResult = messages.Single(message =>
            message.GetProperty("role").GetString() == "tool");
        Assert.AreEqual("call-original", toolResult.GetProperty("tool_call_id").GetString());
        Assert.AreEqual("original-result", toolResult.GetProperty("content").GetString());
    }

    private static FunctionDefinition CreateFunction(string name, List<string> executionOrder)
    {
        return new FunctionDefinition
        {
            Name = name,
            Handler = arguments =>
            {
                executionOrder.Add(name);
                Assert.AreEqual(name == "first" ? "1" : "2", arguments["value"].ToString());
                return Task.FromResult($"{name}-result");
            }
        };
    }

    private const string InterleavedToolStream = """
        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-a","function":{"name":"first","arguments":"{\"value\":"}}]}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":1,"id":"call-b","function":{"name":"second","arguments":"{\"value\":"}}]}}]}

        data: {"choices":[{"delta":{"tool_calls":[{"index":1,"function":{"arguments":"2}"}},{"index":0,"function":{"arguments":"1}"}}]}}]}

        data: [DONE]

        """;

    private const string FinalTextStream = """
        data: {"choices":[{"delta":{"content":"done"}}]}

        data: [DONE]

        """;

    private sealed class XAIStreamingProbe : XAIService
    {
        public XAIStreamingProbe()
            : base("test-api-key", new HttpClient())
        {
        }

        public IReadOnlyList<FunctionCall> ParseFunctionCalls(string json)
        {
            return ParseStreamChunk(json, StreamOptions.WithFunctions).FunctionCalls;
        }
    }
}
