using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class DeepSeekResponsesContractTests
{
    // Fixtures follow the stateless input items and semantic SSE documented at:
    // https://api-docs.deepseek.com/api/create-response/
    // https://api-docs.deepseek.com/guides/responses_api/
    [TestMethod]
    [DataRow(ExecutionMode.Completion)]
    [DataRow(ExecutionMode.Callback)]
    [DataRow(ExecutionMode.Stream)]
    [DataRow(ExecutionMode.Run)]
    public async Task OptIn_RoutesEveryPublicExecutionPathAndReplaysConversation(ExecutionMode mode)
    {
        using var fixture = new Fixture();
        Assert.IsFalse(new DeepSeekService("offline-test-key", fixture.Client).UseResponsesApi);
        fixture.Service.SystemMessage = "Be concise.";
        Assert.AreEqual("answer", await Execute(fixture.Service, "first question", mode));
        Assert.AreEqual("answer", await Execute(fixture.Service, "second question", mode));
        foreach (var captured in fixture.Handler.Requests)
        {
            Assert.AreEqual("/responses", captured.Uri.AbsolutePath);
            var request = captured.Body;
            Assert.AreEqual(AIModels.DeepSeek.Flash, request["model"]?.GetValue<string>());
            Assert.AreEqual("none", request["reasoning"]?["effort"]?.GetValue<string>());
            Assert.AreEqual(8000, request["max_output_tokens"]?.GetValue<int>());
            Assert.IsFalse(request.ContainsKey("messages"));
            Assert.IsFalse(request.ContainsKey("max_tokens"));
            Assert.IsFalse(request.ContainsKey("thinking"));
            Assert.IsFalse(request.ContainsKey("reasoning_effort"));
            Assert.IsFalse(request.ContainsKey("previous_response_id"));
            Assert.IsFalse(request.ContainsKey("conversation"));
            Assert.IsFalse(request.ContainsKey("stream_options"));
        }
        var second = fixture.Handler.Requests[1].Body;
        var messages = Input(second).Where(item => item["role"] != null).ToArray();
        CollectionAssert.AreEqual(new[] { "first question", "second question" },
            messages.Where(item => item["role"]!.GetValue<string>() == "user").Select(ContentText).ToArray());
        Assert.AreEqual("answer", ContentText(messages.Single(item => item["role"]!.GetValue<string>() == "assistant")));
        Assert.IsTrue(second["instructions"]?.GetValue<string>() == "Be concise." ||
            messages.Any(item => item["role"]?.GetValue<string>() == "system" && ContentText(item) == "Be concise."));
    }

    [TestMethod]
    [DataRow(DeepSeekReasoning.Low, "low")]
    [DataRow(DeepSeekReasoning.High, "high")]
    [DataRow(DeepSeekReasoning.Max, "max")]
    public async Task NativeReasoning_UsesResponsesEffortAndOutputBudget(DeepSeekReasoning effort, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.WithDeepSeekReasoning(effort);
        fixture.Service.MaxTokens = uint.MaxValue;
        fixture.Service.TopP = 0.1f;
        await fixture.Service.GetCompletionAsync("question");
        var request = fixture.Handler.Requests.Single().Body;
        Assert.AreEqual(wire, request["reasoning"]?["effort"]?.GetValue<string>());
        Assert.AreEqual(393216, request["max_output_tokens"]?.GetValue<int>());
        Assert.AreEqual(0.95f, request["top_p"]?.GetValue<float>());
        Assert.IsFalse(request.ContainsKey("temperature"));
    }

    [TestMethod]
    [DataRow(ReasoningLevel.None, "none")]
    [DataRow(ReasoningLevel.Minimal, "low")]
    [DataRow(ReasoningLevel.Medium, "high")]
    [DataRow(ReasoningLevel.XHigh, "high")]
    [DataRow(ReasoningLevel.Max, "max")]
    public async Task CommonReasoningOverride_IsCapturedForOneRequest(ReasoningLevel level, string wire)
    {
        using var fixture = new Fixture();
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.High).WithReasoning(level);
        await fixture.Service.GetCompletionAsync("configured");
        await fixture.Service.GetCompletionAsync("ordinary");
        Assert.AreEqual(wire, fixture.Handler.Requests[0].Body["reasoning"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("high", fixture.Handler.Requests[1].Body["reasoning"]?["effort"]?.GetValue<string>());
        Assert.IsTrue(fixture.Service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.High, fixture.Service.ReasoningEffort);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ToolRounds_ReplayReasoningAndPairedOutputsAcrossLaterUserTurns(bool run)
    {
        using var fixture = new Fixture(
            Response("", "thought one", Call("first-call", "first", "{}")),
            Response("", "thought two", Call("second-call", "second", "{\"value\":\"first-result\"}")),
            Response("finished", "thought final"), Response("follow-up", "thought follow-up"));
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.Max);
        var invoked = new List<string>();
        fixture.Service.Functions.Add(new FunctionDefinition
        {
            Name = "first", AllowAsync = true,
            Handler = _ => { invoked.Add("first"); return Task.FromResult("first-result"); }
        });
        fixture.Service.Functions.Add(new FunctionDefinition
        {
            Name = "second", Handler = args =>
            {
                Assert.AreEqual("first-result", args["value"].ToString());
                invoked.Add("second");
                return Task.FromResult("second-result");
            }
        });
        var mode = run ? ExecutionMode.Run : ExecutionMode.Completion;
        Assert.AreEqual("finished", await Execute(fixture.Service, "use tools", mode));
        Assert.AreEqual("follow-up", await Execute(fixture.Service, "next question", mode));
        CollectionAssert.AreEqual(new[] { "first", "second" }, invoked);
        Assert.AreEqual(4, fixture.Handler.Requests.Count);
        var input = Input(fixture.Handler.Requests[3].Body).ToArray();
        CollectionAssert.AreEqual(new[] { "thought one", "thought two", "thought final" },
            input.Where(item => item["type"]?.GetValue<string>() == "reasoning").Select(ContentText).ToArray());
        var calls = input.Where(item => item["type"]?.GetValue<string>() == "function_call").ToArray();
        var outputs = input.Where(item => item["type"]?.GetValue<string>() == "function_call_output").ToArray();
        CollectionAssert.AreEqual(new[] { "first-call", "second-call" }, calls.Select(item => item["call_id"]!.GetValue<string>()).ToArray());
        CollectionAssert.AreEqual(new[] { "first-call", "second-call" }, outputs.Select(item => item["call_id"]!.GetValue<string>()).ToArray());
        CollectionAssert.AreEqual(new[] { "first-result", "second-result" }, outputs.Select(item => item["output"]!.GetValue<string>()).ToArray());
        foreach (var captured in fixture.Handler.Requests)
        {
            Assert.AreEqual("/responses", captured.Uri.AbsolutePath);
            Assert.AreEqual("max", captured.Body["reasoning"]?["effort"]?.GetValue<string>());
            Assert.IsFalse(captured.Body.ContainsKey("previous_response_id"));
            Assert.IsFalse(captured.Body.ContainsKey("background"));
            foreach (var tool in captured.Body["tools"]!.AsArray().OfType<JsonObject>())
            {
                Assert.AreEqual("function", tool["type"]?.GetValue<string>());
                Assert.IsNotNull(tool["name"]);
                Assert.IsNotNull(tool["parameters"]);
                Assert.IsFalse(tool.ContainsKey("function"));
                Assert.IsFalse(tool.ContainsKey("allow_async"));
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ThinkingWithNamedTool_UsesFlatToolChoiceThenAutomaticContinuation(bool run)
    {
        using var fixture = new Fixture(Response("", "plan", Call("forced", "tool", "{}")), Response("done", "finished"));
        fixture.Service.WithDeepSeekReasoning();
        fixture.Service.ForceFunctionName = "tool";
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ => Task.FromResult("value") });
        Assert.AreEqual("done", await Execute(fixture.Service, "question", run ? ExecutionMode.Run : ExecutionMode.Completion));
        var choice = fixture.Handler.Requests[0].Body["tool_choice"]!.AsObject();
        Assert.AreEqual("function", choice["type"]?.GetValue<string>());
        Assert.AreEqual("tool", choice["name"]?.GetValue<string>());
        Assert.IsFalse(choice.ContainsKey("function"));
        Assert.AreEqual("auto", fixture.Handler.Requests[1].Body["tool_choice"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ToolContinuation_KeepsCapturedTransportAndReasoningAfterServiceMutation(bool run)
    {
        using var fixture = new Fixture(Response("", "plan", Call("call", "tool", "{}")), Response("done", "finished"));
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.Max);
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ =>
        {
            fixture.Service.UseResponsesApi = false;
            fixture.Service.ReasoningEffort = DeepSeekReasoning.Low;
            return Task.FromResult("value");
        } });
        Assert.AreEqual("done", await Execute(fixture.Service, "use tool", run ? ExecutionMode.Run : ExecutionMode.Completion));
        Assert.AreEqual(2, fixture.Handler.Requests.Count);
        foreach (var request in fixture.Handler.Requests)
        {
            Assert.AreEqual("/responses", request.Uri.AbsolutePath);
            Assert.AreEqual("max", request.Body["reasoning"]?["effort"]?.GetValue<string>());
        }
        await fixture.Service.GetCompletionAsync("later request");
        Assert.AreEqual("/chat/completions", fixture.Handler.Requests[2].Uri.AbsolutePath);
        Assert.AreEqual("low", fixture.Handler.Requests[2].Body["reasoning_effort"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Run_CapturesTransportBeforeAsynchronousStartup()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.BeforeRunSession = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var startup = fixture.Service.StartRunAsync("question");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Service.UseResponsesApi = false;
        }
        finally { release.TrySetResult(); }
        await using var run = await startup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("answer", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual("/responses", fixture.Handler.Requests.Single().Uri.AbsolutePath);
        Assert.IsFalse(fixture.Service.UseResponsesApi);
    }

    [TestMethod]
    public async Task TypedRepair_PreservesCapturedResponsesSchemaAndReasoning()
    {
        using var fixture = new Fixture(Response("not-json", "first"), Response("{\"Value\":42}", "repair"));
        fixture.Service.WithDeepSeekReasoning(DeepSeekReasoning.Max);
        fixture.Handler.Before = index =>
        {
            if (index != 0) return;
            fixture.Service.UseResponsesApi = false;
            fixture.Service.ReasoningEffort = DeepSeekReasoning.Low;
        };
        var value = await fixture.Service.GetCompletionAsync<TypedValue>("Return JSON");
        Assert.AreEqual(42, value.Value);
        Assert.AreEqual(2, fixture.Handler.Requests.Count);
        foreach (var captured in fixture.Handler.Requests)
        {
            Assert.AreEqual("/responses", captured.Uri.AbsolutePath);
            var request = captured.Body;
            Assert.AreEqual("max", request["reasoning"]?["effort"]?.GetValue<string>());
            var format = request["text"]?["format"]?.AsObject();
            Assert.IsNotNull(format);
            Assert.AreEqual("json_schema", format["type"]?.GetValue<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(format["name"]?.GetValue<string>()));
            Assert.AreEqual("object", format["schema"]?["type"]?.GetValue<string>());
            Assert.IsNotNull(format["schema"]?["properties"]?["Value"]);
            Assert.IsFalse(request.ContainsKey("response_format"));
        }
        Assert.IsFalse(fixture.Service.UseResponsesApi);
        Assert.AreEqual(DeepSeekReasoning.Low, fixture.Service.ReasoningEffort);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ImageInputs_UseFlatInputImageWithOrderedTextAndBytes(bool run)
    {
        using var fixture = new Fixture();
        var bytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var message = new Message(ActorRole.User, new List<MessageContent>
        {
            new TextContent("Compare"), new ImageContent(bytes, "image/png"),
            new TextContent("and"), new ImageContent("https://example.com/image.jpg") { IsHighDetail = true }
        });
        if (run)
        {
            await using var execution = await fixture.Service.StartRunAsync(message);
            Assert.AreEqual("answer", (await execution.Result).Text);
        }
        else Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync(message));
        var content = Input(fixture.Handler.Requests.Single().Body).Single(item => item["role"]?.GetValue<string>() == "user")["content"]!.AsArray();
        CollectionAssert.AreEqual(new[] { "input_text", "input_image", "input_text", "input_image" }, content.Select(item => item!["type"]!.GetValue<string>()).ToArray());
        Assert.AreEqual("data:image/png;base64," + Convert.ToBase64String(bytes), content[1]!["image_url"]?.GetValue<string>());
        Assert.AreEqual("low", content[1]!["detail"]?.GetValue<string>());
        Assert.AreEqual("https://example.com/image.jpg", content[3]!["image_url"]?.GetValue<string>());
        Assert.AreEqual("high", content[3]!["detail"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProImageInput_IsRejectedBeforeTransportAndHistory(bool run)
    {
        using var fixture = new Fixture();
        fixture.Service.ChangeModel("deepseek-v4-pro");
        var message = new Message(ActorRole.User, new List<MessageContent> { new ImageContent("https://example.com/image.png") });
        await Assert.ThrowsAsync<MultimodalNotSupportedException>(async () =>
        {
            if (run) { await using var execution = await fixture.Service.StartRunAsync(message); await execution.Result; }
            else await fixture.Service.GetCompletionAsync(message);
        });
        Assert.AreEqual(0, fixture.Handler.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task Stream_UsesSemanticTerminalWithoutDoneAndDoesNotDuplicateSnapshots()
    {
        using var fixture = new Fixture(Response("answer", "reasoning"));
        fixture.Service.WithDeepSeekReasoning();
        var chunks = await Collect(fixture.Service);
        Assert.AreEqual("answer", string.Concat(chunks.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        Assert.AreEqual("reasoning", string.Concat(chunks.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content)));
        Assert.AreEqual(1, chunks.Count(item => item.Type == StreamingContentType.Completion));
        Assert.IsFalse(chunks.Any(item => item.Type == StreamingContentType.Error));
        var usage = chunks.Select(item => item.Usage).Last(item => item != null)!;
        Assert.AreEqual(100, usage.InputTokens);
        Assert.AreEqual(25, usage.OutputTokens);
        Assert.AreEqual(125, usage.TotalTokens);
        Assert.AreEqual(60, usage.CachedInputTokens);
        Assert.AreEqual(20, usage.ReasoningTokens);
        Assert.AreEqual("reasoning", fixture.Service.ActivateChat.Messages.Last().Metadata!["deepseek_reasoning_content"]);
    }

    [TestMethod]
    [DataRow("failed")]
    [DataRow("incomplete")]
    [DataRow("in_progress")]
    [DataRow("missing-status")]
    [DataRow("malformed-json")]
    public async Task NonSuccessfulNonStreamingResponse_ExecutesNoCollectedTools(string fault)
    {
        var response = Response("partial", "plan", Call("call", "tool", "{}"));
        if (fault == "missing-status") response.Remove("status");
        else response["status"] = fault;
        using var fixture = new Fixture(response);
        var invocations = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ => { invocations++; return Task.FromResult("value"); } });
        if (fault == "malformed-json") fixture.Handler.Payload = (_, _, _) => "{\"status\":\"completed\"";
        await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.GetCompletionAsync("question"));
        Assert.AreEqual(0, invocations);
        Assert.AreEqual(1, fixture.Handler.Requests.Count);
        AssertNoAssistantTurn(fixture.Service);
    }

    [TestMethod]
    [DataRow(false, "bad-json")]
    [DataRow(true, "bad-json")]
    [DataRow(false, "array-arguments")]
    [DataRow(true, "array-arguments")]
    [DataRow(false, "missing-call-id")]
    [DataRow(true, "missing-call-id")]
    [DataRow(false, "duplicate-call-id")]
    [DataRow(true, "duplicate-call-id")]
    public async Task MalformedLaterTool_PreventsEveryHandlerInBatch(bool run, string fault)
    {
        var second = Call("second-call", "tool", "{}");
        if (fault == "bad-json") second["arguments"] = "{bad";
        if (fault == "array-arguments") second["arguments"] = "[]";
        if (fault == "missing-call-id") second.Remove("call_id");
        if (fault == "duplicate-call-id") second["call_id"] = "first-call";
        using var fixture = new Fixture(Response("", "plan", Call("first-call", "tool", "{}"), second));
        var invocations = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ => { invocations++; return Task.FromResult("value"); } });
        await Assert.ThrowsAsync<AIServiceException>(() => Execute(fixture.Service, "question", run ? ExecutionMode.Run : ExecutionMode.Completion));
        Assert.AreEqual(0, invocations);
        Assert.AreEqual(1, fixture.Handler.Requests.Count);
        AssertNoAssistantTurn(fixture.Service);
    }

    [TestMethod]
    [DataRow("failed")]
    [DataRow("incomplete")]
    [DataRow("error")]
    [DataRow("malformed-json")]
    [DataRow("missing-terminal")]
    [DataRow("legacy-done")]
    [DataRow("missing-response")]
    [DataRow("contradictory-status")]
    public async Task UnsafeStreamTermination_ExecutesNoCollectedToolsOrCommitsPartialAnswer(string fault)
    {
        var response = Response("partial", "plan", Call("call", "tool", "{}"));
        using var fixture = new Fixture(response);
        var invocations = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ => { invocations++; return Task.FromResult("value"); } });
        fixture.Handler.Payload = (_, _, item) =>
        {
            var prefix = ToSse(item, includeTerminal: false);
            if (fault == "missing-terminal") return prefix;
            if (fault == "legacy-done") return prefix + "data: [DONE]\n\n";
            if (fault == "malformed-json") return prefix + "event: response.completed\ndata: {\"type\":\"response.completed\"\n\n";
            if (fault == "missing-response") return prefix + Event("response.completed", new JsonObject());
            if (fault == "error") return prefix + Event("error", new JsonObject { ["code"] = "server_error", ["message"] = "provider failed" });
            item["status"] = fault == "contradictory-status" ? "in_progress" : fault;
            var terminal = fault == "contradictory-status" ? "response.completed" : "response." + fault;
            return prefix + Event(terminal, new JsonObject { ["response"] = item });
        };
        var chunks = await Collect(fixture.Service);
        Assert.AreEqual(1, chunks.Count(item => item.Type == StreamingContentType.Error));
        Assert.AreEqual(0, chunks.Count(item => item.Type == StreamingContentType.Completion));
        Assert.AreEqual(0, invocations);
        Assert.AreEqual(1, fixture.Handler.Requests.Count);
        AssertNoAssistantTurn(fixture.Service);
    }

    [TestMethod]
    public async Task StreamCompletedSnapshot_CannotReplaceAlreadyEmittedText()
    {
        using var fixture = new Fixture(Response("original", "plan", Call("call", "tool", "{}")));
        var invocations = 0;
        fixture.Service.Functions.Add(new FunctionDefinition { Name = "tool", Handler = _ => { invocations++; return Task.FromResult("value"); } });
        fixture.Handler.Payload = (_, _, response) => ToSse(response, includeTerminal: false) +
            Event("response.completed", new JsonObject { ["response"] = Response("changed", "plan", Call("call", "tool", "{}")) });
        var chunks = await Collect(fixture.Service);
        Assert.AreEqual(1, chunks.Count(item => item.Type == StreamingContentType.Error));
        Assert.IsFalse(chunks.Any(item => item.Type == StreamingContentType.Completion));
        Assert.AreEqual(0, invocations);
        AssertNoAssistantTurn(fixture.Service);
    }

    public enum ExecutionMode { Completion, Callback, Stream, Run }
    public sealed class TypedValue { public int Value { get; set; } }

    private static async Task<string> Execute(DeepSeekService service, string prompt, ExecutionMode mode)
    {
        if (mode == ExecutionMode.Completion) return await service.GetCompletionAsync(prompt);
        if (mode == ExecutionMode.Run)
        {
            await using var run = await service.StartRunAsync(prompt, options: StreamOptions.FullOptions);
            return (await run.Result).Text;
        }
        var text = new StringBuilder();
        if (mode == ExecutionMode.Callback)
            await service.StreamCompletionAsync(new Message(ActorRole.User, prompt), piece => { text.Append(piece); return Task.CompletedTask; });
        else
            await foreach (var piece in service.StreamAsync(prompt, StreamOptions.FullOptions))
            {
                Assert.AreNotEqual(StreamingContentType.Error, piece.Type, piece.Content);
                if (piece.Type == StreamingContentType.Text) text.Append(piece.Content);
            }
        return text.ToString();
    }

    private static async Task<List<StreamingContent>> Collect(DeepSeekService service)
    {
        var result = new List<StreamingContent>();
        await foreach (var item in service.StreamAsync("question", StreamOptions.FullOptions)) result.Add(item);
        return result;
    }

    private static void AssertNoAssistantTurn(DeepSeekService service)
        => Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant || message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));

    private static IEnumerable<JsonObject> Input(JsonObject request) => request["input"]!.AsArray().OfType<JsonObject>();
    private static string ContentText(JsonObject item) => item["content"] is JsonArray parts
        ? string.Concat(parts.OfType<JsonObject>().Select(part => part["text"]?.GetValue<string>()))
        : item["content"]?.GetValue<string>() ?? "";

    private static JsonObject Call(string callId, string name, string arguments) => new()
    {
        ["id"] = "fc_" + callId, ["type"] = "function_call", ["status"] = "completed",
        ["call_id"] = callId, ["name"] = name, ["arguments"] = arguments
    };

    private static JsonObject Response(string text = "answer", string reasoning = "reasoning", params JsonObject[] calls)
    {
        var output = new JsonArray();
        if (reasoning.Length > 0) output.Add(new JsonObject
        {
            ["id"] = "rs_1", ["type"] = "reasoning", ["status"] = "completed", ["summary"] = new JsonArray(),
            ["content"] = new JsonArray(new JsonObject { ["type"] = "reasoning_text", ["text"] = reasoning })
        });
        if (text.Length > 0) output.Add(new JsonObject
        {
            ["id"] = "msg_1", ["type"] = "message", ["status"] = "completed", ["role"] = "assistant",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JsonArray() })
        });
        foreach (var call in calls) output.Add(call.DeepClone());
        return new JsonObject
        {
            ["id"] = "resp_1", ["object"] = "response", ["created_at"] = 1753000000,
            ["status"] = "completed", ["model"] = "deepseek-flash", ["output"] = output,
            ["store"] = false, ["previous_response_id"] = null, ["parallel_tool_calls"] = true,
            ["error"] = null, ["incomplete_details"] = null,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = 100, ["output_tokens"] = 25, ["total_tokens"] = 125,
                ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 60 },
                ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 20 }
            }
        };
    }

    private static string Event(string type, JsonObject payload, int sequence = 999)
    {
        payload["type"] = type;
        payload["sequence_number"] = sequence;
        return "event: " + type + "\ndata: " + payload.ToJsonString() + "\n\n";
    }

    private static string ToSse(JsonObject response, bool includeTerminal = true)
    {
        var output = new StringBuilder();
        var sequence = 0;
        output.Append(Event("response.created", new JsonObject
        {
            ["response"] = new JsonObject { ["id"] = "resp_1", ["object"] = "response", ["status"] = "in_progress", ["model"] = "deepseek-flash", ["output"] = new JsonArray() }
        }, sequence++));
        var items = response["output"]!.AsArray().OfType<JsonObject>().ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var added = item.DeepClone().AsObject();
            added["status"] = "in_progress";
            if (added.ContainsKey("arguments")) added["arguments"] = "";
            if (added.ContainsKey("content")) added["content"] = new JsonArray();
            output.Append(Event("response.output_item.added", new JsonObject { ["output_index"] = index, ["item"] = added }, sequence++));
            var function = item["type"]?.GetValue<string>() == "function_call";
            var prefix = function ? "response.function_call_arguments" : item["type"]?.GetValue<string>() == "reasoning" ? "response.reasoning_text" : "response.output_text";
            var text = function ? item["arguments"]?.GetValue<string>() ?? "" : ContentText(item);
            foreach (var piece in new[] { text[..(text.Length / 2)], text[(text.Length / 2)..] })
                output.Append(Event(prefix + ".delta", new JsonObject
                {
                    ["item_id"] = item["id"]!.DeepClone(), ["output_index"] = index, ["content_index"] = 0, ["delta"] = piece
                }, sequence++));
            var done = new JsonObject { ["item_id"] = item["id"]!.DeepClone(), ["output_index"] = index, ["content_index"] = 0 };
            done[function ? "arguments" : "text"] = text;
            output.Append(Event(prefix + ".done", done, sequence++));
            output.Append(Event("response.output_item.done", new JsonObject { ["output_index"] = index, ["item"] = item.DeepClone() }, sequence++));
        }
        if (includeTerminal)
            output.Append(Event("response." + response["status"]!.GetValue<string>(), new JsonObject { ["response"] = response.DeepClone() }, sequence));
        return output.ToString();
    }

    private sealed class Fixture : IDisposable
    {
        public HttpClient Client { get; }
        public Handler Handler { get; }
        public Probe Service { get; }
        public Fixture(params JsonObject[] replies)
        {
            Handler = new Handler(replies);
            Client = new HttpClient(Handler);
            Service = new Probe(Client) { UseResponsesApi = true };
        }
        public void Dispose() => Client.Dispose();
    }

    private sealed class Probe(HttpClient client) : DeepSeekService("offline-test-key", client)
    {
        public Func<CancellationToken, Task>? BeforeRunSession { get; set; }
        protected override async Task<RunSession> CreateRunSessionAsync(Message message, StreamOptions executionOptions,
            AIRequestContext? context, CancellationToken cancellationToken)
        {
            if (BeforeRunSession != null) await BeforeRunSession(cancellationToken);
            return await base.CreateRunSessionAsync(message, executionOptions, context, cancellationToken);
        }
    }

    private sealed record CapturedRequest(Uri Uri, JsonObject Body);
    private sealed class Handler(JsonObject[] replies) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();
        public Action<int>? Before { get; set; }
        public Func<int, bool, JsonObject, string>? Payload { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.AreEqual("api.deepseek.com", request.RequestUri!.Host);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            var index = Requests.Count;
            Requests.Add(new CapturedRequest(request.RequestUri, body));
            Before?.Invoke(index);
            var streaming = body["stream"]?.GetValue<bool>() == true;
            var response = replies.Length > index ? replies[index].DeepClone().AsObject() : Response();
            string payload;
            if (Payload != null) payload = Payload(index, streaming, response);
            else if (request.RequestUri.AbsolutePath == "/chat/completions")
                payload = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\",\"reasoning_content\":\"reasoning\"},\"finish_reason\":\"stop\"}]}";
            else payload = streaming ? ToSse(response) : response.ToJsonString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }
    }
}
