using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("FunctionCalling")]
public class OpenAIResponsesFunctionContinuationTests
{
    private const string ToolCallResponse = """
        {
          "id": "resp_tool_round",
          "object": "response",
          "status": "completed",
          "model": "gpt-5.6-sol",
          "output": [
            {
              "id": "rs_reasoning_round",
              "type": "reasoning",
              "status": "completed",
              "summary": [],
              "encrypted_content": "encrypted-reasoning-state"
            },
            {
              "id": "fc_weather_round",
              "type": "function_call",
              "status": "completed",
              "arguments": "{\"city\":\"Seoul\"}",
              "call_id": "call_weather_round",
              "name": "get_weather"
            }
          ],
          "usage": {
            "input_tokens": 10,
            "output_tokens": 5,
            "total_tokens": 15
          }
        }
        """;

    private const string FinalResponse = """
        {
          "id": "resp_final_round",
          "object": "response",
          "status": "completed",
          "model": "gpt-5.6-sol",
          "output": [
            {
              "id": "msg_final_round",
              "type": "message",
              "status": "completed",
              "role": "assistant",
              "content": [
                {
                  "type": "output_text",
                  "text": "Seoul is sunny.",
                  "annotations": []
                }
              ]
            }
          ],
          "output_text": "Seoul is sunny.",
          "usage": {
            "input_tokens": 20,
            "output_tokens": 4,
            "total_tokens": 24
          }
        }
        """;

    private const string SecondToolCallResponse = """
        {
          "id": "resp_second_tool_round",
          "object": "response",
          "status": "completed",
          "model": "gpt-5.6-sol",
          "output": [
            {
              "id": "rs_reasoning_second_round",
              "type": "reasoning",
              "status": "completed",
              "summary": [],
              "encrypted_content": "encrypted-second-reasoning-state"
            },
            {
              "id": "fc_time_round",
              "type": "function_call",
              "status": "completed",
              "arguments": "{\"city\":\"Seoul\"}",
              "call_id": "call_time_round",
              "name": "get_time"
            }
          ],
          "usage": {
            "input_tokens": 15,
            "output_tokens": 6,
            "total_tokens": 21
          }
        }
        """;

    private const string StreamingToolCallResponseWithCompletedOutput = """
        data: {"type":"response.created","response":{"id":"resp_stream_tool","model":"gpt-5.6-sol"}}

        data: {"type":"response.output_item.done","output_index":0,"item":{"id":"rs_reasoning_fallback","type":"reasoning","status":"completed","summary":[],"encrypted_content":"fallback-reasoning-state"}}

        data: {"type":"response.output_item.done","output_index":1,"item":{"id":"fc_weather_fallback","type":"function_call","status":"completed","arguments":"{\"city\":\"Seoul\"}","call_id":"call_weather_stream","name":"get_weather"}}

        data: {"type":"response.completed","response":{"id":"resp_stream_tool","model":"gpt-5.6-sol","status":"completed","output":[{"id":"rs_reasoning_stream","type":"reasoning","status":"completed","summary":[],"encrypted_content":"encrypted-stream-state"},{"id":"fc_weather_stream","type":"function_call","status":"completed","arguments":"{\"city\":\"Seoul\"}","call_id":"call_weather_stream","name":"get_weather"}],"usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}
        """;

    private const string StreamingToolCallResponseWithDoneItemsOnly = """
        data: {"type":"response.created","response":{"id":"resp_stream_tool","model":"gpt-5.6-sol"}}

        data: {"type":"response.output_item.done","output_index":0,"item":{"id":"rs_reasoning_fallback","type":"reasoning","status":"completed","summary":[],"encrypted_content":"fallback-reasoning-state"}}

        data: {"type":"response.output_item.done","output_index":1,"item":{"id":"fc_weather_fallback","type":"function_call","status":"completed","arguments":"{\"city\":\"Seoul\"}","call_id":"call_weather_stream","name":"get_weather"}}

        data: {"type":"response.completed","response":{"id":"resp_stream_tool","model":"gpt-5.6-sol","status":"completed","usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}
        """;

    private const string StreamingToolCallResponseWithImplicitDoneItemIndexes = """
        data: {"type":"response.created","response":{"id":"resp_stream_tool","model":"gpt-5.6-sol"}}

        data: {"type":"response.output_item.done","item":{"id":"rs_reasoning_fallback","type":"reasoning","status":"completed","summary":[],"encrypted_content":"fallback-reasoning-state"}}

        data: {"type":"response.output_item.done","item":{"id":"fc_weather_fallback","type":"function_call","status":"completed","arguments":"{\"city\":\"Seoul\"}","call_id":"call_weather_stream","name":"get_weather"}}

        data: {"type":"response.completed","response":{"id":"resp_stream_tool","model":"gpt-5.6-sol","status":"completed","usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}
        """;

    private const string StreamingReasoningSnapshotsResponse = """
        data: {"type":"response.created","response":{"id":"resp_reasoning","model":"gpt-5.6-sol"}}

        data: {"type":"response.reasoning_summary_text.delta","item_id":"rs_reasoning","output_index":0,"summary_index":0,"delta":"first "}

        data: {"type":"response.reasoning_summary_text.delta","item_id":"rs_reasoning","output_index":0,"summary_index":0,"delta":"second"}

        data: {"type":"response.reasoning_summary_text.done","item_id":"rs_reasoning","output_index":0,"summary_index":0,"text":"first second"}

        data: {"type":"response.output_item.done","output_index":0,"item":{"id":"rs_reasoning","type":"reasoning","status":"completed","summary":[{"type":"summary_text","text":"first second"}]}}

        data: {"type":"response.output_text.delta","delta":"answer"}

        data: {"type":"response.completed","response":{"id":"resp_reasoning","model":"gpt-5.6-sol","status":"completed","output":[{"id":"rs_reasoning","type":"reasoning","status":"completed","summary":[{"type":"summary_text","text":"first second"}]},{"id":"msg_reasoning","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"answer","annotations":[]}]}],"usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}
        """;

    private const string StreamingReasoningDeltaOnlyResponse = """
        data: {"type":"response.created","response":{"id":"resp_reasoning_delta","model":"gpt-5.6-sol"}}

        data: {"type":"response.reasoning_summary_text.delta","item_id":"rs_reused","output_index":0,"summary_index":0,"delta":"fresh summary"}

        data: {"type":"response.output_text.delta","delta":"first answer"}

        data: {"type":"response.completed","response":{"id":"resp_reasoning_delta","model":"gpt-5.6-sol","status":"completed","usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}
        """;

    private const string StreamingReasoningDoneOnlyResponse = """
        data: {"type":"response.created","response":{"id":"resp_reasoning_done","model":"gpt-5.6-sol"}}

        data: {"type":"response.reasoning_summary_text.done","item_id":"rs_reused","output_index":0,"summary_index":0,"text":"fresh summary"}

        data: {"type":"response.output_text.delta","delta":"second answer"}

        data: {"type":"response.completed","response":{"id":"resp_reasoning_done","model":"gpt-5.6-sol","status":"completed","usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}}
        """;

    private const string StreamingFinalResponse = """
        data: {"type":"response.created","response":{"id":"resp_stream_final","model":"gpt-5.6-sol"}}

        data: {"type":"response.output_text.delta","delta":"Seoul is sunny."}

        data: {"type":"response.completed","response":{"id":"resp_stream_final","model":"gpt-5.6-sol","status":"completed","output":[{"id":"msg_stream_final","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"Seoul is sunny.","annotations":[]}]}],"usage":{"input_tokens":20,"output_tokens":4,"total_tokens":24}}}
        """;

    [TestMethod]
    public async Task ReasoningToolCall_ReplaysExactResponseItemsBeforeFunctionOutput()
    {
        var handler = new QueueHttpMessageHandler(ToolCallResponse, FinalResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel("gpt-5.6-sol");
        service.ForceFunctionName = "get_weather";
        service.ActivateChat.SystemMessage = "Use the weather tool and answer briefly.";
        service.WithGpt5_6Parameters(
            reasoningEffort: Gpt5_6Reasoning.Max,
            verbosity: Verbosity.High,
            reasoningSummary: ReasoningSummary.Detailed,
            reasoningMode: Gpt5_6ReasoningMode.Pro);

        var invocationCount = 0;
        Dictionary<string, object>? receivedArguments = null;
        var function = new FunctionDefinition
        {
            Name = "get_weather",
            Description = "Gets the current weather for a city",
            Handler = arguments =>
            {
                invocationCount++;
                receivedArguments = arguments;
                return Task.FromResult("{\"condition\":\"sunny\",\"temperature\":22}");
            }
        };
        function.Parameters.Properties["city"] = new ParameterProperty
        {
            Type = "string",
            Description = "The city name"
        };
        function.Parameters.Required.Add("city");
        service.Functions.Add(function);

        var result = await service.GetCompletionAsync(
            "What is the weather in Seoul?",
            context: new AIRequestContext
            {
                SystemMessagePrefix = "Runtime prefix.",
                SystemMessageSuffix = "Runtime suffix."
            });

        Assert.AreEqual("Seoul is sunny.", result);
        Assert.AreEqual(1, invocationCount);
        Assert.IsNotNull(receivedArguments);
        Assert.AreEqual("Seoul", receivedArguments["city"].ToString());
        Assert.AreEqual(2, handler.Requests.Count, "The tool flow should take exactly two Responses API rounds.");

        using var firstDocument = JsonDocument.Parse(handler.Requests[0].Body);
        var firstRequest = firstDocument.RootElement;
        Assert.AreEqual("/v1/responses", handler.Requests[0].Uri.AbsolutePath);
        Assert.AreEqual("gpt-5.6-sol", firstRequest.GetProperty("model").GetString());
        Assert.AreEqual(
            "Runtime prefix.\n\nUse the weather tool and answer briefly.\n\nRuntime suffix.",
            firstRequest.GetProperty("instructions").GetString());
        Assert.IsTrue(firstRequest.GetProperty("parallel_tool_calls").GetBoolean());
        AssertAdvancedGpt5_6Parameters(firstRequest);
        Assert.IsFalse(firstRequest.TryGetProperty("reasoning_effort", out _));

        using var secondDocument = JsonDocument.Parse(handler.Requests[1].Body);
        var secondRequest = secondDocument.RootElement;
        Assert.AreEqual("/v1/responses", handler.Requests[1].Uri.AbsolutePath);
        Assert.AreEqual(
            "Runtime prefix.\n\nUse the weather tool and answer briefly.\n\nRuntime suffix.",
            secondRequest.GetProperty("instructions").GetString());
        Assert.IsTrue(secondRequest.GetProperty("parallel_tool_calls").GetBoolean());
        AssertAdvancedGpt5_6Parameters(secondRequest);

        var input = secondRequest.GetProperty("input");
        Assert.AreEqual(4, input.GetArrayLength(), "The replay must not add a synthesized duplicate function call.");
        Assert.AreEqual("user", input[0].GetProperty("role").GetString());

        var reasoningItem = input[1];
        Assert.AreEqual("reasoning", reasoningItem.GetProperty("type").GetString());
        Assert.AreEqual("rs_reasoning_round", reasoningItem.GetProperty("id").GetString());
        Assert.AreEqual(
            "encrypted-reasoning-state",
            reasoningItem.GetProperty("encrypted_content").GetString());

        var functionCallItem = input[2];
        Assert.AreEqual("function_call", functionCallItem.GetProperty("type").GetString());
        Assert.AreEqual("fc_weather_round", functionCallItem.GetProperty("id").GetString());
        Assert.AreEqual("call_weather_round", functionCallItem.GetProperty("call_id").GetString());
        Assert.AreEqual("get_weather", functionCallItem.GetProperty("name").GetString());
        Assert.AreEqual("{\"city\":\"Seoul\"}", functionCallItem.GetProperty("arguments").GetString());

        var functionOutputItem = input[3];
        Assert.AreEqual("function_call_output", functionOutputItem.GetProperty("type").GetString());
        Assert.AreEqual("call_weather_round", functionOutputItem.GetProperty("call_id").GetString());
        Assert.AreEqual(
            "{\"condition\":\"sunny\",\"temperature\":22}",
            functionOutputItem.GetProperty("output").GetString());
    }

    [TestMethod]
    public async Task StreamingReasoningToolCall_PrefersCompletedResponseOutputForContinuation()
    {
        await AssertStreamingContinuationAsync(
            StreamingToolCallResponseWithCompletedOutput,
            expectedReasoningId: "rs_reasoning_stream",
            expectedEncryptedContent: "encrypted-stream-state",
            expectedFunctionCallItemId: "fc_weather_stream");
    }

    [TestMethod]
    public async Task StreamingReasoningToolCall_FallsBackToOrderedDoneItemsForContinuation()
    {
        await AssertStreamingContinuationAsync(
            StreamingToolCallResponseWithDoneItemsOnly,
            expectedReasoningId: "rs_reasoning_fallback",
            expectedEncryptedContent: "fallback-reasoning-state",
            expectedFunctionCallItemId: "fc_weather_fallback");
    }

    [TestMethod]
    public async Task StreamingReasoningToolCall_AssignsArrivalOrderWhenDoneItemIndexesAreMissing()
    {
        await AssertStreamingContinuationAsync(
            StreamingToolCallResponseWithImplicitDoneItemIndexes,
            expectedReasoningId: "rs_reasoning_fallback",
            expectedEncryptedContent: "fallback-reasoning-state",
            expectedFunctionCallItemId: "fc_weather_fallback",
            expectedFunctionIndex: 0);
    }

    [TestMethod]
    public async Task StreamingReasoning_DeltaDoneAndOutputItemDone_YieldsSummaryExactlyOnce()
    {
        var handler = new QueueSseHttpMessageHandler(StreamingReasoningSnapshotsResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);

        var reasoningChunks = new List<string>();
        var text = new StringBuilder();
        var options = StreamOptions.Default
            .WithFunctionCalls(false)
            .WithReasoning();

        await foreach (var content in service.StreamAsync("reason", options))
        {
            if (content.Type == StreamingContentType.Reasoning && content.Content != null)
                reasoningChunks.Add(content.Content);
            if (content.Type == StreamingContentType.Text && content.Content != null)
                text.Append(content.Content);
        }

        CollectionAssert.AreEqual(new[] { "first ", "second" }, reasoningChunks);
        Assert.AreEqual("first second", string.Concat(reasoningChunks));
        Assert.AreEqual("answer", text.ToString());
    }

    [TestMethod]
    public async Task StreamingReasoning_NewCallClearsSnapshotDeduplicationState()
    {
        var handler = new QueueSseHttpMessageHandler(
            StreamingReasoningDeltaOnlyResponse,
            StreamingReasoningDoneOnlyResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        var options = StreamOptions.Default
            .WithFunctionCalls(false)
            .WithReasoning();

        var firstReasoning = await CollectReasoningAsync(service, "first", options);
        var secondReasoning = await CollectReasoningAsync(service, "second", options);

        Assert.AreEqual("fresh summary", firstReasoning);
        Assert.AreEqual("fresh summary", secondReasoning);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ImportedFunctionHistory_UsesSyntheticCallWhenExactResponseItemsAreUnavailable()
    {
        var handler = new QueueHttpMessageHandler(FinalResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "get_weather",
            Description = "Gets weather",
            Handler = _ => Task.FromResult("unused")
        });

        const string importedFunctionId = "claude_weather_call";
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Imported question"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, string.Empty)
        {
            Metadata = new Dictionary<string, object>
            {
                [MessageMetadataKeys.MessageType] = "function_call",
                [MessageMetadataKeys.FunctionId] = importedFunctionId,
                [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                [MessageMetadataKeys.FunctionName] = "get_weather",
                [MessageMetadataKeys.FunctionArguments] = "{\"city\":\"Busan\"}"
            }
        });
        service.ActivateChat.Messages.Add(new Message(ActorRole.Function, "{\"condition\":\"rain\"}")
        {
            Metadata = new Dictionary<string, object>
            {
                [MessageMetadataKeys.MessageType] = "function_result",
                [MessageMetadataKeys.FunctionId] = importedFunctionId,
                [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                [MessageMetadataKeys.FunctionName] = "get_weather"
            }
        });

        var result = await service.GetCompletionAsync("Continue the imported conversation.");

        Assert.AreEqual("Seoul is sunny.", result);
        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var input = document.RootElement.GetProperty("input");
        Assert.AreEqual(4, input.GetArrayLength());

        var functionCall = input[1];
        Assert.AreEqual("function_call", functionCall.GetProperty("type").GetString());
        Assert.AreEqual("get_weather", functionCall.GetProperty("name").GetString());
        Assert.AreEqual("{\"city\":\"Busan\"}", functionCall.GetProperty("arguments").GetString());
        var convertedCallId = functionCall.GetProperty("call_id").GetString();
        Assert.IsNotNull(convertedCallId);
        Assert.IsTrue(convertedCallId.StartsWith("call_", StringComparison.Ordinal));
        Assert.AreNotEqual(importedFunctionId, convertedCallId);

        var functionOutput = input[2];
        Assert.AreEqual("function_call_output", functionOutput.GetProperty("type").GetString());
        Assert.AreEqual(convertedCallId, functionOutput.GetProperty("call_id").GetString());
        Assert.AreEqual("{\"condition\":\"rain\"}", functionOutput.GetProperty("output").GetString());
    }

    [TestMethod]
    public async Task ConsecutiveToolRounds_ReplayEachRoundsExactItemsWithoutStateMixing()
    {
        var handler = new QueueHttpMessageHandler(
            ToolCallResponse,
            SecondToolCallResponse,
            FinalResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        service.Functions.Add(CreateStringFunction(
            "get_weather",
            "{\"condition\":\"sunny\"}"));
        service.Functions.Add(CreateStringFunction(
            "get_time",
            "{\"time\":\"12:00\"}"));

        var result = await service.GetCompletionAsync("Use both tools.");

        Assert.AreEqual("Seoul is sunny.", result);
        Assert.AreEqual(3, handler.Requests.Count);

        using var thirdDocument = JsonDocument.Parse(handler.Requests[2].Body);
        var input = thirdDocument.RootElement.GetProperty("input");
        Assert.AreEqual(7, input.GetArrayLength());
        Assert.AreEqual("rs_reasoning_round", input[1].GetProperty("id").GetString());
        Assert.AreEqual("fc_weather_round", input[2].GetProperty("id").GetString());
        Assert.AreEqual("call_weather_round", input[3].GetProperty("call_id").GetString());
        Assert.AreEqual("rs_reasoning_second_round", input[4].GetProperty("id").GetString());
        Assert.AreEqual("fc_time_round", input[5].GetProperty("id").GetString());
        Assert.AreEqual("call_time_round", input[6].GetProperty("call_id").GetString());
    }

    private static async Task AssertStreamingContinuationAsync(
        string toolCallResponse,
        string expectedReasoningId,
        string expectedEncryptedContent,
        string expectedFunctionCallItemId,
        int expectedFunctionIndex = 1)
    {
        var handler = new QueueSseHttpMessageHandler(toolCallResponse, StreamingFinalResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel("gpt-5.6-sol");
        service.ForceFunctionName = "get_weather";
        service.ActivateChat.SystemMessage = "Use the weather tool and answer briefly.";
        service.WithGpt5_6Parameters(
            reasoningEffort: Gpt5_6Reasoning.Max,
            verbosity: Verbosity.High,
            reasoningSummary: ReasoningSummary.Detailed,
            reasoningMode: Gpt5_6ReasoningMode.Pro);

        var invocationCount = 0;
        var function = new FunctionDefinition
        {
            Name = "get_weather",
            Description = "Gets the current weather for a city",
            Handler = arguments =>
            {
                invocationCount++;
                Assert.AreEqual("Seoul", arguments["city"].ToString());
                return Task.FromResult("{\"condition\":\"sunny\",\"temperature\":22}");
            }
        };
        function.Parameters.Properties["city"] = new ParameterProperty
        {
            Type = "string",
            Description = "The city name"
        };
        function.Parameters.Required.Add("city");
        service.Functions.Add(function);

        var streamedText = new StringBuilder();
        var functionCallIndexes = new List<int>();
        var functionResultIndexes = new List<int>();
        await foreach (var content in service.StreamAsync(
                           "What is the weather in Seoul?",
                           StreamOptions.WithFunctions))
        {
            if (content.Type == StreamingContentType.Text && content.Content != null)
                streamedText.Append(content.Content);
            if (content.Type == StreamingContentType.FunctionCall && content.FunctionCall != null)
                functionCallIndexes.Add(content.FunctionCall.Index);
            if (content.Type == StreamingContentType.FunctionResult && content.FunctionResult != null)
                functionResultIndexes.Add(content.FunctionResult.Call.Index);
        }

        Assert.AreEqual("Seoul is sunny.", streamedText.ToString());
        Assert.AreEqual(1, invocationCount);
        CollectionAssert.AreEqual(new[] { expectedFunctionIndex }, functionCallIndexes);
        CollectionAssert.AreEqual(functionCallIndexes, functionResultIndexes);
        Assert.AreEqual(2, handler.Requests.Count, "The streaming tool flow should take exactly two rounds.");

        using var firstDocument = JsonDocument.Parse(handler.Requests[0].Body);
        var firstRequest = firstDocument.RootElement;
        Assert.AreEqual("/v1/responses", handler.Requests[0].Uri.AbsolutePath);
        Assert.AreEqual(
            "Use the weather tool and answer briefly.",
            firstRequest.GetProperty("instructions").GetString());
        Assert.IsTrue(firstRequest.GetProperty("parallel_tool_calls").GetBoolean());
        AssertAdvancedGpt5_6Parameters(firstRequest);
        Assert.IsTrue(firstRequest.GetProperty("stream").GetBoolean());
        var firstToolChoice = firstRequest.GetProperty("tool_choice");
        Assert.AreEqual("function", firstToolChoice.GetProperty("type").GetString());
        Assert.AreEqual("get_weather", firstToolChoice.GetProperty("name").GetString());

        using var secondDocument = JsonDocument.Parse(handler.Requests[1].Body);
        var secondRequest = secondDocument.RootElement;
        Assert.AreEqual("/v1/responses", handler.Requests[1].Uri.AbsolutePath);
        Assert.AreEqual(
            "Use the weather tool and answer briefly.",
            secondRequest.GetProperty("instructions").GetString());
        Assert.IsTrue(secondRequest.GetProperty("parallel_tool_calls").GetBoolean());
        AssertAdvancedGpt5_6Parameters(secondRequest);
        Assert.IsTrue(secondRequest.GetProperty("stream").GetBoolean());
        Assert.AreEqual("auto", secondRequest.GetProperty("tool_choice").GetString());

        var input = secondRequest.GetProperty("input");
        Assert.AreEqual(4, input.GetArrayLength(), "The replay must not synthesize a duplicate function call.");
        Assert.AreEqual("user", input[0].GetProperty("role").GetString());

        var reasoningItem = input[1];
        Assert.AreEqual("reasoning", reasoningItem.GetProperty("type").GetString());
        Assert.AreEqual(expectedReasoningId, reasoningItem.GetProperty("id").GetString());
        Assert.AreEqual(expectedEncryptedContent, reasoningItem.GetProperty("encrypted_content").GetString());

        var functionCallItem = input[2];
        Assert.AreEqual("function_call", functionCallItem.GetProperty("type").GetString());
        Assert.AreEqual(expectedFunctionCallItemId, functionCallItem.GetProperty("id").GetString());
        Assert.AreEqual("call_weather_stream", functionCallItem.GetProperty("call_id").GetString());
        Assert.AreEqual("get_weather", functionCallItem.GetProperty("name").GetString());
        Assert.AreEqual("{\"city\":\"Seoul\"}", functionCallItem.GetProperty("arguments").GetString());

        var functionOutputItem = input[3];
        Assert.AreEqual("function_call_output", functionOutputItem.GetProperty("type").GetString());
        Assert.AreEqual("call_weather_stream", functionOutputItem.GetProperty("call_id").GetString());
        Assert.AreEqual(
            "{\"condition\":\"sunny\",\"temperature\":22}",
            functionOutputItem.GetProperty("output").GetString());
    }

    private static void AssertAdvancedGpt5_6Parameters(JsonElement request)
    {
        var reasoning = request.GetProperty("reasoning");
        Assert.AreEqual("max", reasoning.GetProperty("effort").GetString());
        Assert.AreEqual("current_turn", reasoning.GetProperty("context").GetString());
        Assert.AreEqual("detailed", reasoning.GetProperty("summary").GetString());
        Assert.AreEqual("pro", reasoning.GetProperty("mode").GetString());
        Assert.AreEqual("high", request.GetProperty("text").GetProperty("verbosity").GetString());
    }

    private static async Task<string> CollectReasoningAsync(
        OpenAIService service,
        string prompt,
        StreamOptions options)
    {
        var reasoning = new StringBuilder();
        await foreach (var content in service.StreamAsync(prompt, options))
        {
            if (content.Type == StreamingContentType.Reasoning && content.Content != null)
                reasoning.Append(content.Content);
        }

        return reasoning.ToString();
    }

    private static FunctionDefinition CreateStringFunction(string name, string result)
    {
        var function = new FunctionDefinition
        {
            Name = name,
            Description = name,
            Handler = _ => Task.FromResult(result)
        };
        function.Parameters.Properties["city"] = new ParameterProperty
        {
            Type = "string",
            Description = "The city name"
        };
        function.Parameters.Required.Add("city");
        return function;
    }

    private static CapturedRequest AssertSingleRequest(QueueHttpMessageHandler handler)
    {
        Assert.AreEqual(1, handler.Requests.Count);
        return handler.Requests[0];
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued response remains for the captured request.");
            }

            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class QueueSseHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueSseHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued SSE response remains for the captured request.");
            }

            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Body);
}
