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
                               ActivateChat.ShouldUseFunctions &&
                               !FunctionsDisabled;

            ChatBlock originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = ActivateChat.CloneWithoutMessages();
            }

            try
            {
                ActivateChat.Stream = true;
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
            var currentToolUse = new ToolUseData();
            bool functionEventSent = false;
            string currentModel = null;

            string line;
            while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
            {
                if (!line.StartsWith("data:") && !line.StartsWith("event:"))
                    continue;

                // Handle event type
                if (line.StartsWith("event:"))
                {
                    var eventType = line.Substring("event:".Length).Trim();
                    currentToolUse.CurrentEventType = eventType;
                    continue;
                }

                var jsonData = line.Substring("data:".Length).Trim();
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
                            ["model"] = currentModel ?? ActivateChat.Model
                        } : null
                    });
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
                                ["model"] = currentModel ?? ActivateChat.Model
                            }
                        });
                    }
                    break;
                }
            }

            // Process tool use (function)
            if (currentToolUse.IsComplete && !string.IsNullOrEmpty(currentToolUse.Name))
            {
                // Tool use ID is required
                if (string.IsNullOrEmpty(currentToolUse.Id))
                {
                    throw new InvalidOperationException($"Tool use without ID: {currentToolUse.Name}");
                }

                // Parse arguments
                Dictionary<string, object> arguments = new Dictionary<string, object>();
                if (currentToolUse.Arguments.Length > 0)
                {
                    arguments = TryParseArguments(currentToolUse.Arguments.ToString())
                        ?? new Dictionary<string, object>();
                }

                // Save assistant message (with tool_use)
                var assistantMsg = new Message(ActorRole.Assistant, textBuffer.ToString())
                {
                    Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.MessageType] = "function_call",
                        [MessageMetadataKeys.FunctionId] = currentToolUse.Id,
                        [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                        [MessageMetadataKeys.FunctionName] = currentToolUse.Name,
                        [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(arguments)
                    }
                };
                ActivateChat.Messages.Add(assistantMsg);

                // Execute function
                if (policy.EnableLogging)
                    Console.WriteLine($"  → Executing function: {currentToolUse.Name}");

                var result = await ProcessFunctionCallAsync(currentToolUse.Name, arguments);

                // Function result event
                streamQueue.Enqueue(new StreamingContent
                {
                    Type = StreamingContentType.FunctionResult,
                    Metadata = new Dictionary<string, object>
                    {
                        ["function_name"] = currentToolUse.Name,
                        ["status"] = "completed",
                        ["result"] = result
                    }
                });

                if (policy.EnableLogging)
                    Console.WriteLine($"  → Function result: {result}");

                // Save function result message
                ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
                {
                    Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.MessageType] = "function_result",
                        [MessageMetadataKeys.FunctionId] = currentToolUse.Id,
                        [MessageMetadataKeys.FunctionSource] = IdSource.Claude,
                        [MessageMetadataKeys.FunctionName] = currentToolUse.Name
                    }
                });

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
        }

        private class ClaudeStreamParseResult
        {
            public string TextContent { get; set; }
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
                            if (root.TryGetProperty("index", out var indexElem))
                            {
                                // Check if tool use is complete
                                if (!string.IsNullOrEmpty(toolUseData.Name))
                                {
                                    toolUseData.IsComplete = true;
                                }
                            }
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