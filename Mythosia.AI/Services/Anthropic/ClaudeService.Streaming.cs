using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService
    {
        #region Streaming Implementation

        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            bool useFunctions = options.IncludeFunctionCalls &&
                               ShouldUseFunctions &&
                               !FunctionsDisabled;

            ChatBlock originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                Stream = true;
                ActivateChat.Messages.Add(message);

                var streamQueue = new Queue<StreamingContent>();

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    if (policy.EnableLogging)
                        Console.WriteLine($"[Claude Stream Round {round + 1}/{policy.MaxRounds}]");

                    var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
                    var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        streamQueue.Enqueue(new StreamingContent
                        {
                            Type = StreamingContentType.Error,
                            Metadata = new Dictionary<string, object>
                            {
                                ["error"] = error,
                                ["status_code"] = (int)response.StatusCode
                            }
                        });

                        while (streamQueue.Count > 0)
                            yield return streamQueue.Dequeue();
                        yield break;
                    }

                    // Process stream
                    var (continueLoop, functionExecuted) = await ProcessClaudeStreamResponse(
                        response, options, policy, streamQueue, cancellationToken);

                    // Yield queued items
                    while (streamQueue.Count > 0)
                    {
                        yield return streamQueue.Dequeue();
                    }

                    if (!continueLoop)
                        break;

                    if (!functionExecuted)
                        break;
                }
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        private async Task<(bool continueLoop, bool functionExecuted)> ProcessClaudeStreamResponse(
            HttpResponseMessage response,
            StreamOptions options,
            FunctionCallingPolicy policy,
            Queue<StreamingContent> streamQueue,
            CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var textBuffer = new StringBuilder();
            var thinkingBuffer = new StringBuilder();
            var collectedToolUses = new List<FunctionCall>();  // ★ 모든 tool_use 수집
            var currentToolUse = new ToolUseData();
            bool functionEventSent = false;
            string currentModel = null;

            string line;
            while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
            {
                if (!line.StartsWith(SseDataPrefix) && !line.StartsWith(SseEventPrefix))
                    continue;

                // Handle event type
                if (line.StartsWith(SseEventPrefix))
                {
                    var eventType = line.Substring(SseEventPrefix.Length).Trim();
                    currentToolUse.CurrentEventType = eventType;
                    continue;
                }

                var jsonData = line.Substring(SseDataPrefix.Length).Trim();
                if (string.IsNullOrEmpty(jsonData))
                    continue;

                var parseResult = TryParseClaudeStreamChunk(jsonData, currentToolUse, options, policy);
                if (parseResult == null) continue;

                // Extract model info
                if (currentModel == null && parseResult.Model != null)
                {
                    currentModel = parseResult.Model;
                }

                // Tool use (function call) started
                if (parseResult.ToolUseStarted && !functionEventSent && options.IncludeFunctionCalls)
                {
                    functionEventSent = true;
                    streamQueue.Enqueue(new StreamingContent
                    {
                        Type = StreamingContentType.FunctionCall,
                        Metadata = new Dictionary<string, object>
                        {
                            ["function_name"] = currentToolUse.Name ?? "unknown",
                            ["tool_use_id"] = currentToolUse.Id ?? "",
                            ["status"] = "started"
                        }
                    });

                    if (policy.EnableLogging)
                        Console.WriteLine($"  → Tool use detected: {currentToolUse.Name}");
                }

                // Thinking content (extended thinking / reasoning)
                if (!string.IsNullOrEmpty(parseResult.ThinkingContent) && options.IncludeReasoning)
                {
                    thinkingBuffer.Append(parseResult.ThinkingContent);
                    streamQueue.Enqueue(new StreamingContent
                    {
                        Type = StreamingContentType.Reasoning,
                        Content = parseResult.ThinkingContent,
                        Metadata = options.IncludeMetadata ? new Dictionary<string, object>
                        {
                            ["model"] = currentModel ?? Model
                        } : null
                    });
                }

                // Text content
                if (!string.IsNullOrEmpty(parseResult.TextContent))
                {
                    textBuffer.Append(parseResult.TextContent);
                    streamQueue.Enqueue(new StreamingContent
                    {
                        Type = StreamingContentType.Text,
                        Content = parseResult.TextContent,
                        Metadata = options.IncludeMetadata ? new Dictionary<string, object>
                        {
                            ["model"] = currentModel ?? Model
                        } : null
                    });
                }

                // ★ Tool use completed - collect it
                if (currentToolUse.IsComplete && !string.IsNullOrEmpty(currentToolUse.Name))
                {
                    collectedToolUses.Add(CollectCompletedToolUse(currentToolUse));
                    currentToolUse = new ToolUseData();
                }

                // Message complete
                if (parseResult.MessageComplete)
                {
                    if (options.IncludeMetadata)
                    {
                        streamQueue.Enqueue(new StreamingContent
                        {
                            Type = StreamingContentType.Completion,
                            Metadata = new Dictionary<string, object>
                            {
                                ["total_length"] = textBuffer.Length,
                                ["model"] = currentModel ?? Model
                            }
                        });
                    }
                    break;
                }
            }

            // ★★★ Process all collected tool uses with unified method ★★★
            if (collectedToolUses.Count > 0)
            {
                if (policy.EnableLogging)
                {
                    Console.WriteLine($"  → Processing {collectedToolUses.Count} tool use(s)");
                }

                // Use the unified method to process all tool uses
                await ProcessMultipleToolUses(collectedToolUses, textBuffer.ToString(), policy);

                // Send function result events
                foreach (var toolUse in collectedToolUses)
                {
                    streamQueue.Enqueue(new StreamingContent
                    {
                        Type = StreamingContentType.FunctionResult,
                        Metadata = new Dictionary<string, object>
                        {
                            ["function_name"] = toolUse.Name,
                            ["status"] = "completed"
                        }
                    });
                }

                return (true, true); // continue loop, function executed
            }
            else if (textBuffer.Length > 0)
            {
                // No function call, just regular response
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
            }

            return (false, false); // don't continue loop
        }

        // Legacy callback-based method (for compatibility)
        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            await foreach (var content in StreamAsync(message, StreamOptions.TextOnlyOptions))
            {
                if (content.Type == StreamingContentType.Text && content.Content != null)
                    await messageReceivedAsync(content.Content);
            }
        }

        #endregion

        #region Helper Classes and Methods

        private class ToolUseData
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
            public bool IsComplete { get; set; }
            public string CurrentEventType { get; set; }
            public string CurrentBlockType { get; set; }
        }

        private class ClaudeStreamParseResult
        {
            public string TextContent { get; set; }
            public string ThinkingContent { get; set; }
            public bool ToolUseStarted { get; set; }
            public bool MessageComplete { get; set; }
            public string Model { get; set; }
        }

        private ClaudeStreamParseResult TryParseClaudeStreamChunk(
            string jsonData,
            ToolUseData toolUseData,
            StreamOptions options,
            FunctionCallingPolicy policy)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;
                var result = new ClaudeStreamParseResult();

                // Extract model info
                if (root.TryGetProperty("model", out var modelElem))
                {
                    result.Model = modelElem.GetString();
                }

                // Type-based processing
                if (root.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();

                    switch (type)
                    {
                        case "message_start":
                            // Message start - can extract metadata
                            if (root.TryGetProperty("message", out var msgStart))
                            {
                                if (msgStart.TryGetProperty("model", out var msgModel))
                                {
                                    result.Model = msgModel.GetString();
                                }
                            }
                            break;

                        case "content_block_start":
                            // Content block start
                            if (root.TryGetProperty("content_block", out var blockElement))
                            {
                                if (blockElement.TryGetProperty("type", out var blockType))
                                {
                                    var blockTypeStr = blockType.GetString();
                                    toolUseData.CurrentBlockType = blockTypeStr;

                                    if (blockTypeStr == "tool_use")
                                    {
                                        // Tool use start - ID is required
                                        if (!blockElement.TryGetProperty("id", out var idElem))
                                        {
                                            throw new InvalidOperationException($"tool_use without id! JSON: {blockElement.GetRawText()}");
                                        }

                                        toolUseData.Id = idElem.GetString();

                                        if (string.IsNullOrEmpty(toolUseData.Id))
                                        {
                                            throw new InvalidOperationException("tool_use id is empty!");
                                        }

                                        if (blockElement.TryGetProperty("name", out var nameElem))
                                        {
                                            toolUseData.Name = nameElem.GetString();
                                        }

                                        toolUseData.Arguments.Clear();
                                        result.ToolUseStarted = true;
                                    }
                                    // thinking block start - no special action needed, deltas will follow
                                }
                            }
                            break;

                        case "content_block_delta":
                            // Content delta
                            if (root.TryGetProperty("delta", out var deltaElement))
                            {
                                if (deltaElement.TryGetProperty("type", out var deltaType))
                                {
                                    var deltaTypeStr = deltaType.GetString();

                                    if (deltaTypeStr == "text_delta")
                                    {
                                        if (deltaElement.TryGetProperty("text", out var textElem))
                                        {
                                            result.TextContent = textElem.GetString();
                                        }
                                    }
                                    else if (deltaTypeStr == "thinking_delta")
                                    {
                                        if (deltaElement.TryGetProperty("thinking", out var thinkingElem))
                                        {
                                            result.ThinkingContent = thinkingElem.GetString();
                                        }
                                    }
                                    else if (deltaTypeStr == "input_json_delta")
                                    {
                                        if (deltaElement.TryGetProperty("partial_json", out var jsonElem))
                                        {
                                            toolUseData.Arguments.Append(jsonElem.GetString());
                                        }
                                    }
                                }
                            }
                            break;

                        case "content_block_stop":
                            // Content block complete
                            if (toolUseData.CurrentBlockType == "tool_use" && !string.IsNullOrEmpty(toolUseData.Name))
                            {
                                toolUseData.IsComplete = true;
                            }
                            toolUseData.CurrentBlockType = null;
                            break;

                        case "message_delta":
                            // Message delta (usage info etc)
                            if (root.TryGetProperty("usage", out var usageElem) && options.IncludeTokenInfo)
                            {
                                // Token info processing (if needed)
                            }
                            break;

                        case "message_stop":
                            // Message complete
                            result.MessageComplete = true;
                            break;

                        case "error":
                            // Error handling
                            if (root.TryGetProperty("error", out var errorElem))
                            {
                                if (policy.EnableLogging)
                                {
                                    Console.WriteLine($"[Claude Stream Error] {errorElem.GetRawText()}");
                                }
                            }
                            break;
                    }
                }

                return result;
            }
            catch (JsonException ex)
            {
                if (policy.EnableLogging)
                    Console.WriteLine($"[Claude Parse Error] {ex.Message}");
                return null;
            }
        }

        private FunctionCall CollectCompletedToolUse(ToolUseData toolUseData)
        {
            if (string.IsNullOrEmpty(toolUseData.Id))
            {
                throw new InvalidOperationException($"Tool use without ID: {toolUseData.Name}");
            }

            var arguments = toolUseData.Arguments.Length > 0
                ? TryParseArguments(toolUseData.Arguments.ToString()) ?? new Dictionary<string, object>()
                : new Dictionary<string, object>();

            return new FunctionCall
            {
                Id = toolUseData.Id,
                Source = IdSource.Claude,
                Name = toolUseData.Name,
                Arguments = arguments
            };
        }

        private Dictionary<string, object> TryParseArguments(string argsJson)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}