using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Samples.ChatUi;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ChatUiAnthropicCurrentModelsTests
{
    [TestMethod]
    public void Catalogue_ContainsClaude5AndExcludesRetiredOpus4_1()
    {
        var catalogue = JsonSerializer.SerializeToElement(ChatUiModelHelpers.BuildModelCatalogue());
        var models = catalogue
            .EnumerateArray()
            .Single(group => group.GetProperty("provider").GetString() == "Anthropic")
            .GetProperty("models")
            .EnumerateArray()
            .ToDictionary(
                model => model.GetProperty("name").GetString()!,
                model => model);

        Assert.AreEqual(AIModels.Anthropic.ClaudeOpus5,
            models[nameof(AIModels.Anthropic.ClaudeOpus5)].GetProperty("description").GetString());
        Assert.AreEqual(AIModels.Anthropic.ClaudeSonnet5,
            models[nameof(AIModels.Anthropic.ClaudeSonnet5)].GetProperty("description").GetString());
        Assert.AreEqual($"{AIModels.Anthropic.ClaudeMythos5} (Project Glasswing limited access)",
            models[nameof(AIModels.Anthropic.ClaudeMythos5)].GetProperty("description").GetString());
        Assert.AreEqual(128000u,
            models[nameof(AIModels.Anthropic.ClaudeOpus5)].GetProperty("maxOutputTokens").GetUInt32());
        Assert.AreEqual(128000u,
            models[nameof(AIModels.Anthropic.ClaudeSonnet5)].GetProperty("maxOutputTokens").GetUInt32());
        Assert.AreEqual(128000u,
            models[nameof(AIModels.Anthropic.ClaudeMythos5)].GetProperty("maxOutputTokens").GetUInt32());
        Assert.IsFalse(models.ContainsKey("ClaudeOpus4_1_250805"));
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeSonnet5)]
    public void Claude5Models_ExposeAdaptiveThinkingControls(string model)
    {
        var reasoning = JsonSerializer.SerializeToElement(ChatUiModelHelpers.GetReasoningLevels(model));

        Assert.AreEqual("claude_adaptive", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(
            new[] { "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeFable5)]
    [DataRow(AIModels.Anthropic.ClaudeMythos5)]
    public void AlwaysOnModels_ExposeEffortControls(string model)
    {
        var reasoning = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetReasoningLevels(model));

        Assert.AreEqual("claude_always", reasoning.GetProperty("type").GetString());
        CollectionAssert.AreEqual(
            new[] { "Low", "Medium", "High", "XHigh", "Max" },
            reasoning.GetProperty("levels").EnumerateArray().Select(level => level.GetString()).ToArray());
    }

    [TestMethod]
    public void Claude5_SamplingControlsMatchSerializedRequestCapabilities()
    {
        var claude5 = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetSamplingControls(AIModels.Anthropic.ClaudeOpus5));
        var sonnet46 = JsonSerializer.SerializeToElement(
            ChatUiModelHelpers.GetSamplingControls(AIModels.Anthropic.ClaudeSonnet4_6));

        Assert.IsFalse(claude5.GetProperty("temperature").GetBoolean());
        Assert.IsFalse(claude5.GetProperty("topP").GetBoolean());
        Assert.IsTrue(sonnet46.GetProperty("temperature").GetBoolean());
        Assert.IsFalse(sonnet46.GetProperty("topP").GetBoolean());
    }

    [TestMethod]
    public void ChatUi_MapsAdaptiveEffortAndAlwaysOnDisableToHonestSettings()
    {
        var opus = new AnthropicService("offline-test-key", new HttpClient());
        opus.ChangeModel(AIModels.Anthropic.ClaudeOpus5);
        ChatUiSettingsHelpers.ApplyReasoningSettings(
            opus,
            CreateSettingsRequest(true, "Medium", "claude_adaptive"));

        Assert.AreEqual(ClaudeReasoningEffort.Medium, opus.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingDisplay.Summarized, opus.AdaptiveThinkingDisplay);

        var fable = new AnthropicService("offline-test-key", new HttpClient());
        fable.ChangeModel(AIModels.Anthropic.ClaudeFable5);
        ChatUiSettingsHelpers.ApplyReasoningSettings(
            fable,
            CreateSettingsRequest(false, null, "claude_always"));

        Assert.AreEqual(ClaudeReasoningEffort.Low, fable.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingDisplay.Omitted, fable.AdaptiveThinkingDisplay);

        var mythos = new AnthropicService("offline-test-key", new HttpClient());
        mythos.ChangeModel(AIModels.Anthropic.ClaudeMythos5);
        ChatUiSettingsHelpers.ApplyReasoningSettings(
            mythos,
            CreateSettingsRequest(false, null, "claude_always"));

        Assert.AreEqual(ClaudeReasoningEffort.Low, mythos.AdaptiveThinkingEffort);
        Assert.AreEqual(ClaudeThinkingDisplay.Omitted, mythos.AdaptiveThinkingDisplay);
    }

    private static SettingsRequest CreateSettingsRequest(
        bool reasoningEnabled,
        string? reasoningLevel,
        string reasoningType)
        => new(
            Temperature: null,
            TopP: null,
            MaxTokens: null,
            FrequencyPenalty: null,
            PresencePenalty: null,
            StatelessMode: null,
            SystemMessage: null,
            ReasoningEnabled: reasoningEnabled,
            ReasoningLevel: reasoningLevel,
            ReasoningType: reasoningType);
}

[TestClass]
[TestCategory("Unit")]
public class AnthropicToolContinuationTests
{
    private const string FinalResponse =
        "{\"content\":[{\"type\":\"text\",\"text\":\"done\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":2,\"output_tokens\":1}}";

    [TestMethod]
    public async Task NonStreamingToolRound_ReplaysCompleteAssistantContent()
    {
        const string toolResponse =
            "{\"content\":[" +
            "{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"sig-exact\"}," +
            "{\"type\":\"redacted_thinking\",\"data\":\"opaque-redacted-data\"}," +
            "{\"type\":\"text\",\"text\":\"Checking.\"}," +
            "{\"type\":\"tool_use\",\"id\":\"toolu_weather\",\"name\":\"get_weather\",\"input\":{\"city\":\"Seoul\"}}" +
            "],\"stop_reason\":\"tool_use\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";

        var handler = new QueueHttpMessageHandler(
            Response.Json(toolResponse),
            Response.Json(FinalResponse));
        var service = CreateService(handler);
        service.Functions.Add(CreateFunction("get_weather", "sunny"));

        var result = await service.GetCompletionAsync("Check Seoul weather.");

        Assert.AreEqual("done", result);
        Assert.AreEqual(2, handler.RequestBodies.Count);

        using var document = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = document.RootElement.GetProperty("messages");
        Assert.AreEqual(3, messages.GetArrayLength());

        var assistantContent = messages[1].GetProperty("content");
        Assert.AreEqual(4, assistantContent.GetArrayLength());
        Assert.AreEqual("thinking", assistantContent[0].GetProperty("type").GetString());
        Assert.AreEqual("", assistantContent[0].GetProperty("thinking").GetString());
        Assert.AreEqual("sig-exact", assistantContent[0].GetProperty("signature").GetString());
        Assert.AreEqual("redacted_thinking", assistantContent[1].GetProperty("type").GetString());
        Assert.AreEqual("opaque-redacted-data", assistantContent[1].GetProperty("data").GetString());
        Assert.AreEqual("Checking.", assistantContent[2].GetProperty("text").GetString());
        Assert.AreEqual("toolu_weather", assistantContent[3].GetProperty("id").GetString());

        var resultBlocks = messages[2].GetProperty("content");
        Assert.AreEqual(1, resultBlocks.GetArrayLength());
        Assert.AreEqual("tool_result", resultBlocks[0].GetProperty("type").GetString());
        Assert.AreEqual("toolu_weather", resultBlocks[0].GetProperty("tool_use_id").GetString());
        Assert.AreEqual("sunny", resultBlocks[0].GetProperty("content").GetString());

        var callMessage = service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        var resultMessage = service.ActivateChat.Messages.Single(message => message.FunctionCallResultBatch != null);
        Assert.AreEqual(1, callMessage.FunctionCallBatch!.Calls.Count);
        Assert.AreEqual(1, resultMessage.FunctionCallResultBatch!.Results.Count);
        Assert.AreEqual(
            callMessage.FunctionCallBatch.Id,
            resultMessage.FunctionCallResultBatch.FunctionCallBatchId);
        Assert.IsTrue(callMessage.FunctionCallBatch.Metadata?.ContainsKey(
            MessageMetadataKeys.OriginalContent) == true);
        Assert.IsFalse(callMessage.Metadata?.ContainsKey(MessageMetadataKeys.OriginalContent) == true,
            "Signed original content should be retained once on the batch, not duplicated on message metadata.");
    }

    [TestMethod]
    public async Task ForceFunctionName_ForcesOnlyInitialAdaptiveThinkingRound()
    {
        const string toolResponse =
            "{\"content\":[" +
            "{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"sig-force\"}," +
            "{\"type\":\"tool_use\",\"id\":\"toolu_weather\",\"name\":\"get_weather\",\"input\":{}}" +
            "],\"stop_reason\":\"tool_use\"}";

        var handler = new QueueHttpMessageHandler(
            Response.Json(toolResponse),
            Response.Json(FinalResponse));
        var service = CreateService(handler);
        service.Functions.Add(CreateFunction("get_weather", "sunny"));
        service.ForceFunctionName = "get_weather";

        await service.GetCompletionAsync("Check the weather, then answer.");

        Assert.AreEqual(2, handler.RequestBodies.Count);
        using var initialDocument = JsonDocument.Parse(handler.RequestBodies[0]);
        using var continuationDocument = JsonDocument.Parse(handler.RequestBodies[1]);

        var initialChoice = initialDocument.RootElement.GetProperty("tool_choice");
        Assert.AreEqual("tool", initialChoice.GetProperty("type").GetString());
        Assert.AreEqual("get_weather", initialChoice.GetProperty("name").GetString());
        Assert.AreEqual(
            "auto",
            continuationDocument.RootElement.GetProperty("tool_choice").GetProperty("type").GetString(),
            "The tool-result continuation must let Claude finish instead of forcing the same tool again.");
    }

    [TestMethod]
    public async Task ForceFunctionName_WithManualExtendedThinking_FallsBackToAuto()
    {
        var handler = new QueueHttpMessageHandler(Response.Json(FinalResponse));
        var service = CreateService(handler);
        service.ChangeModel(AIModels.Anthropic.ClaudeSonnet4_5_250929);
        service.ThinkingBudget = 1024;
        service.MaxTokens = 4096;
        service.Functions.Add(CreateFunction("get_weather", "sunny"));
        service.ForceFunctionName = "get_weather";

        await service.GetCompletionAsync("Check the weather.");

        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        Assert.AreEqual(
            "auto",
            document.RootElement.GetProperty("tool_choice").GetProperty("type").GetString(),
            "Anthropic rejects forced tool choice while manual extended thinking is enabled.");
    }

    [TestMethod]
    public async Task MultipleToolRound_ExecutesSequentiallyAndUsesOneAssistantAndResultTurn()
    {
        const string toolResponse =
            "{\"content\":[" +
            "{\"type\":\"tool_use\",\"id\":\"toolu_one\",\"name\":\"first_tool\",\"input\":{}}," +
            "{\"type\":\"tool_use\",\"id\":\"toolu_two\",\"name\":\"second_tool\",\"input\":{}}" +
            "],\"stop_reason\":\"tool_use\"}";

        var handler = new QueueHttpMessageHandler(
            Response.Json(toolResponse),
            Response.Json(FinalResponse));
        var service = CreateService(handler);
        var executionOrder = new List<string>();
        var activeHandlers = 0;
        var maxActiveHandlers = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Offline first tool",
            Handler = async _ =>
            {
                executionOrder.Add("first:start");
                activeHandlers++;
                maxActiveHandlers = Math.Max(maxActiveHandlers, activeHandlers);
                await Task.Delay(10);
                activeHandlers--;
                executionOrder.Add("first:end");
                return "one";
            }
        });
        service.Functions.Add(new FunctionDefinition
        {
            Name = "second_tool",
            Description = "Offline second tool",
            Handler = async _ =>
            {
                executionOrder.Add("second:start");
                activeHandlers++;
                maxActiveHandlers = Math.Max(maxActiveHandlers, activeHandlers);
                await Task.Delay(10);
                activeHandlers--;
                executionOrder.Add("second:end");
                return "two";
            }
        });

        await service.GetCompletionAsync("Use both tools.");

        CollectionAssert.AreEqual(
            new[] { "first:start", "first:end", "second:start", "second:end" },
            executionOrder);
        Assert.AreEqual(1, maxActiveHandlers);

        using var document = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = document.RootElement.GetProperty("messages");
        Assert.AreEqual(3, messages.GetArrayLength(),
            "Expected user / one assistant tool turn / one user result turn");
        Assert.AreEqual(2, messages[1].GetProperty("content").GetArrayLength());
        Assert.AreEqual(2, messages[2].GetProperty("content").GetArrayLength());
        Assert.AreEqual("toolu_one", messages[2].GetProperty("content")[0].GetProperty("tool_use_id").GetString());
        Assert.AreEqual("toolu_two", messages[2].GetProperty("content")[1].GetProperty("tool_use_id").GetString());

        var callMessage = service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        var resultMessage = service.ActivateChat.Messages.Single(message => message.FunctionCallResultBatch != null);
        CollectionAssert.AreEqual(
            new[] { "toolu_one", "toolu_two" },
            callMessage.FunctionCallBatch!.Calls.Select(call => call.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "one", "two" },
            resultMessage.FunctionCallResultBatch!.Results.Select(result => result.Content).ToArray());
        Assert.AreEqual(1, service.ActivateChat.Messages.Count(message => message.FunctionCallBatch != null));
        Assert.AreEqual(1, service.ActivateChat.Messages.Count(message => message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task NonStreamingMalformedSecondTool_DoesNotExecuteValidatedFirstTool()
    {
        const string toolResponse =
            "{\"content\":[" +
            "{\"type\":\"tool_use\",\"id\":\"toolu_valid\",\"name\":\"first_tool\",\"input\":{}}," +
            "{\"type\":\"tool_use\",\"id\":\"toolu_invalid\",\"name\":\"second_tool\",\"input\":[]}" +
            "],\"stop_reason\":\"tool_use\"}";
        var handler = new QueueHttpMessageHandler(Response.Json(toolResponse));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run when another call in the batch is invalid",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Use both tools."));

        StringAssert.Contains(exception.Message, "invalid arguments");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task NonStreamingToolBlockWithoutToolUseStopReason_DoesNotExecuteTool()
    {
        const string invalidToolResponse =
            "{\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_blocked\",\"name\":\"dangerous_tool\",\"input\":{}}]," +
            "\"stop_reason\":\"max_tokens\"}";
        var handler = new QueueHttpMessageHandler(Response.Json(invalidToolResponse));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Must not run without stop_reason=tool_use",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Use the tool."));

        StringAssert.Contains(exception.Message, "partial response was not saved");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task FunctionEnabledCompletion_RefreshesLastThinkingContent()
    {
        const string thinkingResponse =
            "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"fresh reasoning\",\"signature\":\"sig\"}," +
            "{\"type\":\"text\",\"text\":\"first\"}],\"stop_reason\":\"end_turn\"}";
        const string plainResponse =
            "{\"content\":[{\"type\":\"text\",\"text\":\"second\"}],\"stop_reason\":\"end_turn\"}";
        var handler = new QueueHttpMessageHandler(
            Response.Json(thinkingResponse),
            Response.Json(plainResponse));
        var service = CreateService(handler);
        service.Functions.Add(CreateFunction("unused_tool", "unused"));

        Assert.AreEqual("first", await service.GetCompletionAsync("First request."));
        Assert.AreEqual("fresh reasoning", service.LastThinkingContent);

        Assert.AreEqual("second", await service.GetCompletionAsync("Second request."));
        Assert.IsNull(service.LastThinkingContent);
    }

    [TestMethod]
    public async Task StreamingMultipleToolRound_ReplaysSignatureWithoutReasoningEvents()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Sse(BuildStreamingToolResponse()),
            Response.Sse(BuildStreamingFinalResponse()));
        var service = CreateService(handler);
        service.Functions.Add(CreateFunction("first_tool", "one"));
        service.Functions.Add(CreateFunction("second_tool", "two"));

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Use both tools.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(2, events.Count(item => item.Type == StreamingContentType.FunctionCall));
        Assert.AreEqual(2, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.Reasoning),
            "WithFunctions defaults IncludeReasoning to false");
        Assert.AreEqual(2, handler.RequestBodies.Count);

        using var document = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = document.RootElement.GetProperty("messages");
        Assert.AreEqual(3, messages.GetArrayLength());

        var assistantContent = messages[1].GetProperty("content");
        Assert.AreEqual("thinking", assistantContent[0].GetProperty("type").GetString());
        Assert.AreEqual("", assistantContent[0].GetProperty("thinking").GetString());
        Assert.AreEqual("stream-signature-exact", assistantContent[0].GetProperty("signature").GetString());
        Assert.AreEqual(2, assistantContent.EnumerateArray().Count(item =>
            item.GetProperty("type").GetString() == "tool_use"));
        Assert.AreEqual(2, messages[2].GetProperty("content").GetArrayLength());

        var callEvents = events.Where(item => item.Type == StreamingContentType.FunctionCall).ToList();
        var resultEvents = events.Where(item => item.Type == StreamingContentType.FunctionResult).ToList();
        Assert.IsTrue(callEvents.All(item => item.FunctionCall != null));
        Assert.IsTrue(resultEvents.All(item => item.FunctionResult != null));
        CollectionAssert.AreEqual(
            new[] { "toolu_one", "toolu_two" },
            callEvents.Select(item => item.FunctionCall!.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            resultEvents.Select(item => item.FunctionResult!.Call.Index).ToArray());
        Assert.IsTrue(callEvents.Concat(resultEvents).All(item =>
            item.FunctionCallBatchId == callEvents[0].FunctionCallBatchId));
        Assert.AreEqual(1, service.ActivateChat.Messages.Count(message => message.FunctionCallBatch != null));
        Assert.AreEqual(1, service.ActivateChat.Messages.Count(message => message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task TruncatedStreamingToolRound_DoesNotExecuteTool()
    {
        var truncated = BuildStreamingToolResponse().Replace(
            "event: message_stop\ndata: {\"type\":\"message_stop\"}\n",
            string.Empty,
            StringComparison.Ordinal);
        var handler = new QueueHttpMessageHandler(Response.Sse(truncated));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run after a truncated stream",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });
        service.Functions.Add(CreateFunction("second_tool", "unexpected"));

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Use both tools.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        var error = events.Single(item => item.Type == StreamingContentType.Error);
        StringAssert.Contains(error.Content ?? string.Empty, "before message_stop");
    }

    [TestMethod]
    public async Task StreamingApiErrorAfterToolBlock_DoesNotExecuteTool()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_blocked\",\"name\":\"dangerous_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "event: error\n" +
            "data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Must not run after an SSE error",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Use the tool.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        var error = events.Single(item => item.Type == StreamingContentType.Error);
        Assert.AreEqual("Overloaded", error.Content);
        Assert.AreEqual("overloaded_error", error.Metadata?["error_type"]);
    }

    [TestMethod]
    public async Task StreamingInvalidToolJson_DoesNotExecuteTool()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_invalid\",\"name\":\"dangerous_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"city\\\":\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":1}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Must not run with malformed arguments",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Use the tool.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        var error = events.Single(item => item.Type == StreamingContentType.Error);
        Assert.AreEqual("invalid_tool_arguments", error.Metadata?["error_type"]);
        StringAssert.Contains(error.Content ?? string.Empty, "invalid JSON arguments");
    }

    [TestMethod]
    public async Task StreamingToolWithoutId_EmitsTypedErrorAndDoesNotExecuteTool()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"name\":\"dangerous_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":1}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Must not run without a provider call ID",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Use the tool.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        var error = events.Single(item => item.Type == StreamingContentType.Error);
        Assert.AreEqual("invalid_tool_arguments", error.Metadata?["error_type"]);
        StringAssert.Contains(error.Content ?? string.Empty, "without an ID");
    }

    [TestMethod]
    public async Task StreamingMalformedSecondTool_DoesNotExecuteValidatedFirstTool()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_valid\",\"name\":\"first_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_invalid\",\"name\":\"second_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{bad\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":1}\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":1}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run when a later call is malformed",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Use both tools.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Error));
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task NonStreamingDuplicateToolIds_DoesNotExecuteOrCommitBatch()
    {
        const string toolResponse =
            "{\"content\":[" +
            "{\"type\":\"tool_use\",\"id\":\"toolu_duplicate\",\"name\":\"first_tool\",\"input\":{}}," +
            "{\"type\":\"tool_use\",\"id\":\"toolu_duplicate\",\"name\":\"second_tool\",\"input\":{}}" +
            "],\"stop_reason\":\"tool_use\"}";
        var handler = new QueueHttpMessageHandler(Response.Json(toolResponse));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run for a duplicate provider ID",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Use both tools."));

        StringAssert.Contains(exception.Message, "duplicate function-call ID");
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestBodies.Count);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task NonStreamingMissingLaterToolName_DoesNotExecuteValidatedFirstTool()
    {
        const string toolResponse =
            "{\"content\":[" +
            "{\"type\":\"tool_use\",\"id\":\"toolu_valid\",\"name\":\"first_tool\",\"input\":{}}," +
            "{\"type\":\"tool_use\",\"id\":\"toolu_invalid\",\"input\":{}}" +
            "],\"stop_reason\":\"tool_use\"}";
        var handler = new QueueHttpMessageHandler(Response.Json(toolResponse));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run when another call has no name",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Use both tools."));

        StringAssert.Contains(exception.Message, "without a name");
        Assert.AreEqual(0, invocationCount);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task StreamingDuplicateToolIds_EmitsErrorWithoutExecutingOrCommittingBatch()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_duplicate\",\"name\":\"first_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_duplicate\",\"name\":\"second_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":1}\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":1}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run for duplicate IDs",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync("Use both tools.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        var error = events.Single(content => content.Type == StreamingContentType.Error);
        Assert.AreEqual("invalid_tool_batch", error.Metadata?["error_type"]);
        Assert.IsFalse(events.Any(content => content.Type == StreamingContentType.FunctionResult));
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task StreamingMalformedDataAfterValidTool_DoesNotExecuteOrCommitPartialBatch()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_valid\",\"name\":\"first_tool\",\"input\":{}}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run after a malformed SSE data record",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync("Use both tools.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        var error = events.Single(content => content.Type == StreamingContentType.Error);
        Assert.AreEqual("malformed_stream", error.Metadata?["error_type"]);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    [TestMethod]
    public async Task StreamingOutOfOrderToolBlocks_ExecuteAndReplayInContentIndexOrder()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":2,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_second\",\"name\":\"second_tool\",\"input\":{},\"future_field\":\"keep-second\"}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_first\",\"name\":\"first_tool\",\"input\":{\"value\":1},\"future_field\":\"keep-first\"}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":2,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"value\\\":2}\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":2}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":1}\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":1}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(
            Response.Sse(stream),
            Response.Sse(BuildStreamingFinalResponse()));
        var service = CreateService(handler);
        var executionOrder = new List<string>();
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "First by provider content index",
            Handler = arguments =>
            {
                executionOrder.Add("first_tool:" + arguments["value"]);
                return Task.FromResult("one");
            }
        });
        service.Functions.Add(new FunctionDefinition
        {
            Name = "second_tool",
            Description = "Second by provider content index",
            Handler = arguments =>
            {
                executionOrder.Add("second_tool:" + arguments["value"]);
                return Task.FromResult("two");
            }
        });
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync("Use both tools.", StreamOptions.WithFunctions))
            events.Add(content);

        CollectionAssert.AreEqual(
            new[] { "first_tool:1", "second_tool:2" },
            executionOrder);
        var batch = service.ActivateChat.Messages.Single(
            message => message.FunctionCallBatch != null).FunctionCallBatch!;
        CollectionAssert.AreEqual(
            new[] { "toolu_first", "toolu_second" },
            batch.Calls.Select(call => call.Id).ToArray());

        using var continuation = JsonDocument.Parse(handler.RequestBodies[1]);
        var assistant = continuation.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.AreEqual("first_tool", assistant[0].GetProperty("name").GetString());
        Assert.AreEqual("keep-first", assistant[0].GetProperty("future_field").GetString());
        Assert.AreEqual("second_tool", assistant[1].GetProperty("name").GetString());
        Assert.AreEqual("keep-second", assistant[1].GetProperty("future_field").GetString());
    }

    [TestMethod]
    public async Task StreamingMessageStopWithOpenToolBlock_DoesNotExecuteOrCommitTool()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\"}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_open\",\"name\":\"first_tool\",\"input\":{}}}\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "first_tool",
            Description = "Must not run before content_block_stop",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });
        var events = new List<StreamingContent>();

        await foreach (var content in service.StreamAsync("Use the tool.", StreamOptions.WithFunctions))
            events.Add(content);

        Assert.AreEqual(0, invocationCount);
        var error = events.Single(content => content.Type == StreamingContentType.Error);
        Assert.AreEqual("incomplete_tool_use", error.Metadata?["error_type"]);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));
    }

    private static AnthropicService CreateService(QueueHttpMessageHandler handler)
    {
        var service = new AnthropicService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.Anthropic.ClaudeOpus5);
        service.ThinkingBudget = 1024;
        return service;
    }

    private static FunctionDefinition CreateFunction(string name, string result)
    {
        return new FunctionDefinition
        {
            Name = name,
            Description = $"Offline {name}",
            Handler = _ => Task.FromResult(result)
        };
    }

    private static string BuildStreamingToolResponse()
    {
        return string.Join("\n", new[]
        {
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":10}}}",
            "event: content_block_start",
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\"}}",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"signature_delta\",\"signature\":\"stream-signature-exact\"}}",
            "event: content_block_stop",
            "data: {\"type\":\"content_block_stop\",\"index\":0}",
            "event: content_block_start",
            "data: {\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_one\",\"name\":\"first_tool\",\"input\":{}}}",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}",
            "event: content_block_stop",
            "data: {\"type\":\"content_block_stop\",\"index\":1}",
            "event: content_block_start",
            "data: {\"type\":\"content_block_start\",\"index\":2,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_two\",\"name\":\"second_tool\",\"input\":{}}}",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":2,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}",
            "event: content_block_stop",
            "data: {\"type\":\"content_block_stop\",\"index\":2}",
            "event: message_delta",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":12}}",
            "event: message_stop",
            "data: {\"type\":\"message_stop\"}",
            ""
        });
    }

    private static string BuildStreamingFinalResponse()
    {
        return string.Join("\n", new[]
        {
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":20}}}",
            "event: content_block_start",
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
            "event: content_block_delta",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"done\"}}",
            "event: content_block_stop",
            "data: {\"type\":\"content_block_stop\",\"index\":0}",
            "event: message_delta",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":1}}",
            "event: message_stop",
            "data: {\"type\":\"message_stop\"}",
            ""
        });
    }
}

[TestClass]
[TestCategory("Unit")]
public class AnthropicRefusalTests
{
    private const string RefusalResponse =
        "{\"content\":[],\"stop_reason\":\"refusal\",\"stop_details\":{\"category\":\"general_harms\",\"explanation\":\"blocked\"}}";

    [TestMethod]
    public async Task NonStreamingRefusal_ThrowsInsteadOfReturningEmptySuccess()
    {
        var handler = new QueueHttpMessageHandler(Response.Json(RefusalResponse));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("refused request"));

        StringAssert.Contains(exception.Message, "stop_reason=refusal");
        StringAssert.Contains(exception.ErrorDetails ?? string.Empty, "general_harms");
        Assert.AreEqual(1, handler.RequestBodies.Count);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    public async Task FunctionResponseMarkedAsRefusal_DoesNotExecuteTool()
    {
        const string refusalWithTool =
            "{\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_blocked\",\"name\":\"dangerous_tool\",\"input\":{}}]," +
            "\"stop_reason\":\"refusal\",\"stop_details\":null}";
        var handler = new QueueHttpMessageHandler(Response.Json(refusalWithTool));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Must not run after refusal",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("refused tool request"));

        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task StreamingRefusal_YieldsErrorAndDoesNotExecuteCollectedTool()
    {
        var handler = new QueueHttpMessageHandler(Response.Sse(BuildStreamingRefusalResponse()));
        var service = CreateService(handler);
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "dangerous_tool",
            Description = "Must not run after refusal",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("unexpected");
            }
        });

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("refused stream", StreamOptions.WithFunctions))
            events.Add(content);

        var error = events.Single(item => item.Type == StreamingContentType.Error);
        Assert.AreEqual("refusal", error.Metadata?["stop_reason"]);
        Assert.AreEqual("general_harms", error.Metadata?["category"]);
        Assert.AreEqual("blocked", error.Metadata?["explanation"]);
        Assert.AreEqual(3, error.Usage?.OutputTokens);
        Assert.AreEqual(0, invocationCount);
        Assert.AreEqual(0, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.Role == ActorRole.Assistant || message.Role == ActorRole.Function));
    }

    [TestMethod]
    public async Task LegacyStreamingCallback_ThrowsOnRefusal()
    {
        var handler = new QueueHttpMessageHandler(Response.Sse(BuildStreamingRefusalResponse(includeTool: false)));
        var service = CreateService(handler);

        await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.StreamCompletionAsync(
                new Message(ActorRole.User, "refused stream"),
                _ => Task.CompletedTask));
    }

    [TestMethod]
    public async Task SimpleStringStreaming_ThrowsInsteadOfYieldingRefusalAsText()
    {
        var handler = new QueueHttpMessageHandler(Response.Sse(BuildStreamingRefusalResponse(includeTool: false)));
        var service = CreateService(handler);

        static async Task ConsumeAsync(AnthropicService target)
        {
            await foreach (var _ in target.StreamAsync("refused stream"))
            {
            }
        }

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => ConsumeAsync(service));
        StringAssert.Contains(exception.Message, "stop_reason=refusal");
    }

    private static AnthropicService CreateService(QueueHttpMessageHandler handler)
    {
        var service = new AnthropicService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.Anthropic.ClaudeOpus5);
        return service;
    }

    private static string BuildStreamingRefusalResponse(bool includeTool = true)
    {
        var lines = new List<string>
        {
            "event: message_start",
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":7}}}"
        };

        if (includeTool)
        {
            lines.AddRange(new[]
            {
                "event: content_block_start",
                "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_blocked\",\"name\":\"dangerous_tool\",\"input\":{}}}",
                "event: content_block_delta",
                "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}",
                "event: content_block_stop",
                "data: {\"type\":\"content_block_stop\",\"index\":0}"
            });
        }

        lines.AddRange(new[]
        {
            "event: message_delta",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"refusal\",\"stop_details\":{\"category\":\"general_harms\",\"explanation\":\"blocked\"}},\"usage\":{\"output_tokens\":3}}",
            "event: message_stop",
            "data: {\"type\":\"message_stop\"}",
            ""
        });
        return string.Join("\n", lines);
    }
}

[TestClass]
[TestCategory("Unit")]
public class AnthropicStopReasonTests
{
    [TestMethod]
    public async Task FunctionEnabledEmptyEndTurn_ReturnsOnceWithoutRetrying()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Json("{\"content\":[],\"stop_reason\":\"end_turn\"}"));
        var service = CreateService(handler);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "unused_tool",
            Description = "Unused",
            Handler = _ => Task.FromResult("unused")
        });

        var result = await service.GetCompletionAsync("Return no content.");

        Assert.AreEqual(string.Empty, result);
        Assert.AreEqual(1, handler.RequestBodies.Count);
    }

    [TestMethod]
    public async Task NonStreamingMaxTokens_ThrowsAndDoesNotSavePartialResponse()
    {
        var handler = new QueueHttpMessageHandler(Response.Json(
            "{\"content\":[{\"type\":\"text\",\"text\":\"partial\"}],\"stop_reason\":\"max_tokens\"}"));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Long request."));

        StringAssert.Contains(exception.ErrorDetails ?? string.Empty, "max_tokens");
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    public async Task StreamingMaxTokens_YieldsTerminalErrorWithStopReason()
    {
        const string stream =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":2}}}\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"partial\"}}\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"},\"usage\":{\"output_tokens\":3}}\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";
        var handler = new QueueHttpMessageHandler(Response.Sse(stream));
        var service = CreateService(handler);

        var events = new List<StreamingContent>();
        await foreach (var content in service.StreamAsync("Long request.", StreamOptions.Default))
            events.Add(content);

        var error = events.Single(content => content.Type == StreamingContentType.Error);
        Assert.AreEqual("max_tokens", error.Metadata?["stop_reason"]);
        Assert.AreEqual(3, error.Usage?.OutputTokens);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    private static AnthropicService CreateService(QueueHttpMessageHandler handler)
    {
        var service = new AnthropicService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.Anthropic.ClaudeOpus5);
        return service;
    }
}

internal readonly record struct Response(string Body, string MediaType)
{
    public static Response Json(string body) => new Response(body, "application/json");
    public static Response Sse(string body) => new Response(body, "text/event-stream");
}

internal sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Response> _responses;

    public QueueHttpMessageHandler(params Response[] responses)
    {
        _responses = new Queue<Response>(responses);
    }

    public List<string> RequestBodies { get; } = new List<string>();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content == null
            ? string.Empty
            : await request.Content.ReadAsStringAsync());

        if (_responses.Count == 0)
            throw new InvalidOperationException("No queued Anthropic response remains.");

        var response = _responses.Dequeue();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, response.MediaType)
        };
    }
}
