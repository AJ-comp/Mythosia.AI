using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService
    {
        #region Streaming Implementation

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            // Get policy (current or default)
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            using var cts = policy.TimeoutSeconds.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds.Value))
                : new CancellationTokenSource();

            // Stateless 모드 처리
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

                // Function calling loop for streaming - use policy.MaxRounds
                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    if (policy.EnableLogging)
                    {
                        Console.WriteLine($"[Claude Stream Round {round + 1}/{policy.MaxRounds}]");
                    }

                    // Check if functions are enabled
                    bool useFunctions = ActivateChat.Functions?.Count > 0
                                       && ActivateChat.EnableFunctions
                                       && !FunctionsDisabled;

                    var request = useFunctions
                        ? CreateFunctionMessageRequest()
                        : CreateMessageRequest();

                    var response = await HttpClient.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
                    }

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream);

                    var allContent = new StringBuilder();
                    FunctionCall currentFunctionCall = null;
                    string currentToolUseId = null;
                    var partialJsonBuilder = new StringBuilder(); // For accumulating partial JSON
                    bool inToolUse = false;

                    while (!reader.EndOfStream && !cts.Token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        // Claude uses event-stream format
                        if (line.StartsWith("event:"))
                        {
                            var eventType = line.Substring("event:".Length).Trim();
                            // Handle specific events if needed
                            continue;
                        }

                        if (!line.StartsWith("data:"))
                            continue;

                        var jsonData = line.Substring("data:".Length).Trim();
                        if (string.IsNullOrEmpty(jsonData))
                            continue;

                        try
                        {
                            if (useFunctions)
                            {
                                // Parse for function calls in streaming
                                var (content, functionCall, toolUseId, partialJson) = StreamParseFunctionJson(jsonData);

                                if (!string.IsNullOrEmpty(toolUseId))
                                {
                                    currentToolUseId = toolUseId;
                                    currentFunctionCall = functionCall;
                                    inToolUse = true;
                                    partialJsonBuilder.Clear(); // Start fresh for new function
                                }

                                // Accumulate partial JSON
                                if (inToolUse && !string.IsNullOrEmpty(partialJson))
                                {
                                    partialJsonBuilder.Append(partialJson);

                                    // Try to parse if we might have complete JSON
                                    var accumulated = partialJsonBuilder.ToString();
                                    if (accumulated.StartsWith("{") && accumulated.EndsWith("}"))
                                    {
                                        try
                                        {
                                            var args = JsonSerializer.Deserialize<Dictionary<string, object>>(accumulated);
                                            if (args != null && currentFunctionCall != null)
                                            {
                                                currentFunctionCall.Arguments = args;
                                                // Successfully parsed complete arguments
                                                inToolUse = false; // Done with this tool use
                                            }
                                        }
                                        catch
                                        {
                                            // Not yet complete, continue accumulating
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(content))
                                {
                                    allContent.Append(content);
                                    await messageReceivedAsync(content);
                                }
                            }
                            else
                            {
                                var content = StreamParseJson(jsonData);
                                if (!string.IsNullOrEmpty(content))
                                {
                                    allContent.Append(content);
                                    await messageReceivedAsync(content);
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            if (policy.EnableLogging)
                            {
                                Console.WriteLine($"[Claude Stream] Parse error: {ex.Message}");
                            }
                            // Continue processing instead of throwing
                            continue;
                        }
                    }

                    // Check for cancellation
                    cts.Token.ThrowIfCancellationRequested();

                    // Handle function call if present
                    if (currentFunctionCall != null && useFunctions && currentFunctionCall.Arguments?.Count > 0)
                    {
                        if (policy.EnableLogging)
                        {
                            Console.WriteLine($"  Executing function: {currentFunctionCall.Name}");
                        }

                        // Save assistant message with tool use
                        var assistantMessage = new Message(ActorRole.Assistant, allContent.ToString())
                        {
                            Metadata = new Dictionary<string, object>
                            {
                                ["tool_use_id"] = currentToolUseId ?? Guid.NewGuid().ToString(),
                                ["function_name"] = currentFunctionCall.Name,
                                ["arguments"] = JsonSerializer.Serialize(currentFunctionCall.Arguments)
                            }
                        };
                        ActivateChat.Messages.Add(assistantMessage);

                        // Set CallId for consistency
                        currentFunctionCall.CallId = currentToolUseId;

                        // Execute function and add result message
                        await ExecuteFunctionAndAddResultAsync(currentFunctionCall);

                        // Continue to next round for getting final response
                        continue;
                    }
                    else
                    {
                        // No function call, save final message and return
                        if (allContent.Length > 0)
                        {
                            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, allContent.ToString()));
                        }
                        return;
                    }
                }

                throw new AIServiceException($"Maximum rounds ({policy.MaxRounds}) exceeded in streaming");
            }
            catch (OperationCanceledException)
            {
                throw new AIServiceException($"Stream timeout after {policy.TimeoutSeconds} seconds");
            }
            finally
            {
                if (originalChat != null)
                {
                    ActivateChat = originalChat;
                }
            }
        }

        private async Task ProcessStatelessStreamAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            // Stateless 모드는 단순히 원래 ChatBlock을 백업하고 복원하는 방식으로 처리
            // (메인 StreamCompletionAsync에서 처리됨)
            throw new NotImplementedException("This method is no longer used. Stateless mode is handled in main StreamCompletionAsync.");
        }

        /// <summary>
        /// Parse streaming JSON for function calls
        /// </summary>
        private (string content, FunctionCall functionCall, string toolUseId, string partialJson) StreamParseFunctionJson(string jsonData)
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
                string content = string.Empty;
                FunctionCall functionCall = null;
                string toolUseId = null;
                string partialJson = null;

                if (jsonElement.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();

                    // Handle different event types
                    switch (type)
                    {
                        case "content_block_start":
                            if (jsonElement.TryGetProperty("content_block", out var blockElement))
                            {
                                if (blockElement.TryGetProperty("type", out var blockType) &&
                                    blockType.GetString() == "tool_use")
                                {
                                    toolUseId = blockElement.GetProperty("id").GetString();
                                    var name = blockElement.GetProperty("name").GetString();

                                    functionCall = new FunctionCall
                                    {
                                        Name = name,
                                        CallId = toolUseId,
                                        Arguments = new Dictionary<string, object>()
                                    };
                                }
                            }
                            break;

                        case "content_block_delta":
                            if (jsonElement.TryGetProperty("delta", out var deltaElement))
                            {
                                if (deltaElement.TryGetProperty("type", out var deltaType))
                                {
                                    if (deltaType.GetString() == "text_delta" &&
                                        deltaElement.TryGetProperty("text", out var textElement))
                                    {
                                        content = textElement.GetString() ?? string.Empty;
                                    }
                                    else if (deltaType.GetString() == "input_json_delta" &&
                                            deltaElement.TryGetProperty("partial_json", out var jsonElement2))
                                    {
                                        // Return partial JSON to be accumulated
                                        partialJson = jsonElement2.GetString();
                                    }
                                }
                            }
                            break;

                        case "content_block_stop":
                            // Block completed
                            break;
                    }
                }

                return (content, functionCall, toolUseId, partialJson);
            }
            catch
            {
                return (string.Empty, null, null, null);
            }
        }

        // Override the new streaming method with options
        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Check if functions are available and should be used
            bool useFunctions = options.IncludeFunctionCalls &&
                               ActivateChat.ShouldUseFunctions &&
                               !FunctionsDisabled;

            // For now, use the callback-based streaming and convert
            // This can be optimized later to directly handle streaming with metadata
            var channel = Channel.CreateUnbounded<StreamingContent>();

            var streamingTask = Task.Run(async () =>
            {
                try
                {
                    var allContent = new StringBuilder();
                    FunctionCall currentFunctionCall = null;
                    string currentToolUseId = null;

                    await StreamCompletionAsync(message, async content =>
                    {
                        allContent.Append(content);
                        var streamingContent = new StreamingContent
                        {
                            Type = StreamingContentType.Text,
                            Content = content
                        };

                        if (options.IncludeMetadata)
                        {
                            streamingContent.Metadata = new Dictionary<string, object>
                            {
                                ["provider"] = "Claude",
                                ["timestamp"] = DateTime.UtcNow
                            };
                        }

                        await channel.Writer.WriteAsync(streamingContent, cancellationToken);
                    });

                    // Check if we accumulated a function call
                    if (currentFunctionCall != null && useFunctions)
                    {
                        var functionContent = new StreamingContent
                        {
                            Type = StreamingContentType.FunctionCall,
                            FunctionCallData = new FunctionCallData
                            {
                                Name = currentFunctionCall.Name,
                                IsComplete = true
                            }
                        };
                        functionContent.FunctionCallData.Arguments.Append(
                            JsonSerializer.Serialize(currentFunctionCall.Arguments));

                        await channel.Writer.WriteAsync(functionContent, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    await channel.Writer.WriteAsync(new StreamingContent
                    {
                        Type = StreamingContentType.Error,
                        Metadata = new Dictionary<string, object> { ["error"] = ex.Message }
                    }, cancellationToken);
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            }, cancellationToken);

            await foreach (var content in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return content;
            }

            await streamingTask;
        }

        #endregion
    }
}