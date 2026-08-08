using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("FunctionCalling")]
public class ProviderParallelFunctionExecutionTests
{
    [TestMethod]
    public async Task OpenAI_NonStreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Json(OpenAIToolRound),
            Response.Json(OpenAIFinalRound));
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);

        await AssertParallelExecutionAsync(service);
    }

    [TestMethod]
    public async Task Google_NonStreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Json(GoogleToolRound),
            Response.Json(GoogleFinalRound));
        var service = new GoogleAIService("offline-test-key", new HttpClient(handler));

        await AssertParallelExecutionAsync(service);
    }

    [TestMethod]
    public async Task Anthropic_NonStreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Json(AnthropicToolRound),
            Response.Json(AnthropicFinalRound));
        var service = new AnthropicService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.Anthropic.ClaudeOpus5);

        await AssertParallelExecutionAsync(service);
    }

    [TestMethod]
    public async Task XAI_NonStreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Json(ChatCompletionsToolRound),
            Response.Json(ChatCompletionsFinalRound));
        var service = new XAIService("offline-test-key", new HttpClient(handler));

        await AssertParallelExecutionAsync(service);
    }

    [TestMethod]
    public async Task OpenAI_StreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Sse(OpenAIStreamingToolRound),
            Response.Sse(OpenAIStreamingFinalRound));
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);

        await AssertParallelStreamingExecutionAsync(service);
    }

    [TestMethod]
    public async Task Google_StreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Sse(ToSse(GoogleToolRound)),
            Response.Sse(ToSse(GoogleFinalRound)));
        var service = new GoogleAIService("offline-test-key", new HttpClient(handler));

        await AssertParallelStreamingExecutionAsync(service);
    }

    [TestMethod]
    public async Task Anthropic_StreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Sse(AnthropicStreamingToolRound),
            Response.Sse(AnthropicStreamingFinalRound));
        var service = new AnthropicService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.Anthropic.ClaudeOpus5);

        await AssertParallelStreamingExecutionAsync(service);
    }

    [TestMethod]
    public async Task XAI_StreamingUsesParallelExecutionPolicy()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Sse(ChatCompletionsStreamingToolRound),
            Response.Sse(ChatCompletionsStreamingFinalRound));
        var service = new XAIService("offline-test-key", new HttpClient(handler));

        await AssertParallelStreamingExecutionAsync(service);
    }

    private static async Task AssertParallelExecutionAsync(AIService service)
    {
        var probe = new ParallelHandlerProbe();
        service.Functions.Add(probe.CreateFunction("first_tool", "first-result"));
        service.Functions.Add(probe.CreateFunction("second_tool", "second-result"));
        service.DefaultPolicy = new FunctionCallingPolicy
        {
            ExecutionMode = FunctionExecutionMode.Parallel,
            MaxConcurrency = 2,
            MaxRounds = 3,
            TimeoutSeconds = 30
        };

        var completion = service.GetCompletionAsync("Run both tools.");
        try
        {
            await probe.BothStarted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, probe.Started);
            Assert.AreEqual(2, probe.MaximumConcurrency);
            Assert.IsFalse(completion.IsCompleted);
        }
        finally
        {
            probe.Release();
        }

        Assert.AreEqual("done", await completion);
        var resultBatch = service.ActivateChat.Messages
            .Single(message => message.FunctionCallResultBatch != null)
            .FunctionCallResultBatch!;
        CollectionAssert.AreEqual(
            new[] { "first_tool", "second_tool" },
            resultBatch.Results.Select(result => result.Call.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first-result", "second-result" },
            resultBatch.Results.Select(result => result.Content).ToArray());
    }

    private static async Task AssertParallelStreamingExecutionAsync(AIService service)
    {
        var probe = new ParallelHandlerProbe();
        service.Functions.Add(probe.CreateFunction("first_tool", "first-result"));
        service.Functions.Add(probe.CreateFunction("second_tool", "second-result"));
        service.DefaultPolicy = new FunctionCallingPolicy
        {
            ExecutionMode = FunctionExecutionMode.Parallel,
            MaxConcurrency = 2,
            MaxRounds = 3,
            TimeoutSeconds = 30
        };

        var streaming = DrainStreamAsync(service);
        try
        {
            await probe.BothStarted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, probe.Started);
            Assert.AreEqual(2, probe.MaximumConcurrency);
            Assert.IsFalse(streaming.IsCompleted);
        }
        finally
        {
            probe.Release();
        }

        var events = await streaming;
        var resultEvents = events
            .Where(content => content.Type == StreamingContentType.FunctionResult)
            .ToArray();
        Assert.AreEqual(2, resultEvents.Length);
        CollectionAssert.AreEqual(
            new[] { "first_tool", "second_tool" },
            resultEvents.Select(content => content.FunctionResult!.Call.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first-result", "second-result" },
            resultEvents.Select(content => content.FunctionResult!.Content).ToArray());
    }

    private static async Task<List<StreamingContent>> DrainStreamAsync(AIService service)
    {
        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync(
            "Run both tools.",
            StreamOptions.WithFunctions))
        {
            events.Add(content);
        }

        return events;
    }

    private static string ToSse(string json)
        => $"data: {json.Replace("\r", string.Empty).Replace("\n", string.Empty)}\n\ndata: [DONE]\n\n";

    private sealed class ParallelHandlerProbe
    {
        private readonly TaskCompletionSource<bool> _bothStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maximumConcurrency;
        private int _started;

        public Task BothStarted => _bothStarted.Task;
        public int Started => Volatile.Read(ref _started);
        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public FunctionDefinition CreateFunction(string name, string result)
        {
            return new FunctionDefinition
            {
                Name = name,
                Description = $"Executes {name}.",
                Handler = async _ =>
                {
                    var started = Interlocked.Increment(ref _started);
                    var active = Interlocked.Increment(ref _active);
                    UpdateMaximum(ref _maximumConcurrency, active);
                    if (started == 2)
                        _bothStarted.TrySetResult(true);
                    try
                    {
                        await _release.Task;
                        return result;
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _active);
                    }
                }
            };
        }

        public void Release() => _release.TrySetResult(true);

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

    private const string OpenAIToolRound = """
        {
          "id": "resp_tools",
          "status": "completed",
          "output": [
            { "id": "item-a", "type": "function_call", "status": "completed", "call_id": "call-a", "name": "first_tool", "arguments": "{}" },
            { "id": "item-b", "type": "function_call", "status": "completed", "call_id": "call-b", "name": "second_tool", "arguments": "{}" }
          ]
        }
        """;

    private const string OpenAIFinalRound = """
        {
          "id": "resp_final",
          "status": "completed",
          "output": [
            { "type": "message", "role": "assistant", "content": [{ "type": "output_text", "text": "done" }] }
          ]
        }
        """;

    private const string OpenAIStreamingToolRound = """
        data: {"type":"response.output_item.done","output_index":0,"item":{"id":"item-a","type":"function_call","status":"completed","call_id":"call-a","name":"first_tool","arguments":"{}"}}

        data: {"type":"response.output_item.done","output_index":1,"item":{"id":"item-b","type":"function_call","status":"completed","call_id":"call-b","name":"second_tool","arguments":"{}"}}

        data: {"type":"response.completed","response":{"id":"resp-tools","status":"completed","output":[{"id":"item-a","type":"function_call","status":"completed","call_id":"call-a","name":"first_tool","arguments":"{}"},{"id":"item-b","type":"function_call","status":"completed","call_id":"call-b","name":"second_tool","arguments":"{}"}]}}

        data: [DONE]

        """;

    private const string OpenAIStreamingFinalRound = """
        data: {"type":"response.output_text.delta","delta":"done"}

        data: {"type":"response.completed","response":{"id":"resp-final","status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"done"}]}]}}

        data: [DONE]

        """;

    private const string GoogleToolRound = """
        {
          "candidates": [{
            "content": {
              "role": "model",
              "parts": [
                { "functionCall": { "id": "google-call-a", "name": "first_tool", "args": {} } },
                { "functionCall": { "id": "google-call-b", "name": "second_tool", "args": {} } }
              ]
            },
            "finishReason": "STOP"
          }]
        }
        """;

    private const string GoogleFinalRound = """
        {"candidates":[{"content":{"role":"model","parts":[{"text":"done"}]},"finishReason":"STOP"}]}
        """;

    private const string AnthropicToolRound = """
        {
          "content": [
            { "type": "tool_use", "id": "toolu-a", "name": "first_tool", "input": {} },
            { "type": "tool_use", "id": "toolu-b", "name": "second_tool", "input": {} }
          ],
          "stop_reason": "tool_use"
        }
        """;

    private const string AnthropicFinalRound = """
        {"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}
        """;

    private const string AnthropicStreamingToolRound = """
        event: message_start
        data: {"type":"message_start","message":{"model":"claude-opus-5","usage":{"input_tokens":2}}}
        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu-a","name":"first_tool","input":{}}}
        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{}"}}
        event: content_block_stop
        data: {"type":"content_block_stop","index":0}
        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu-b","name":"second_tool","input":{}}}
        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{}"}}
        event: content_block_stop
        data: {"type":"content_block_stop","index":1}
        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":2}}
        event: message_stop
        data: {"type":"message_stop"}

        """;

    private const string AnthropicStreamingFinalRound = """
        event: message_start
        data: {"type":"message_start","message":{"model":"claude-opus-5","usage":{"input_tokens":2}}}
        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}
        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"done"}}
        event: content_block_stop
        data: {"type":"content_block_stop","index":0}
        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":1}}
        event: message_stop
        data: {"type":"message_stop"}

        """;

    private const string ChatCompletionsToolRound = """
        {
          "choices": [{
            "finish_reason": "tool_calls",
            "message": {
              "content": null,
              "tool_calls": [
                { "id": "call-a", "type": "function", "function": { "name": "first_tool", "arguments": "{}" } },
                { "id": "call-b", "type": "function", "function": { "name": "second_tool", "arguments": "{}" } }
              ]
            }
          }]
        }
        """;

    private const string ChatCompletionsFinalRound = """
        {"choices":[{"finish_reason":"stop","message":{"content":"done"}}]}
        """;

    private const string ChatCompletionsStreamingToolRound = """
        data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call-a","function":{"name":"first_tool","arguments":"{}"}},{"index":1,"id":"call-b","function":{"name":"second_tool","arguments":"{}"}}]}}]}

        data: [DONE]

        """;

    private const string ChatCompletionsStreamingFinalRound = """
        data: {"choices":[{"delta":{"content":"done"}}]}

        data: [DONE]

        """;
}
