using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService
    {
        #region Function Calling Support

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var requestBody = BuildRequestBodyWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = content
            };

            request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Add("anthropic-beta", "tools-2024-04-04");

            return request;
        }

        private object BuildRequestBodyWithFunctions()
        {
            var messagesList = new List<object>();

            foreach (var message in ActivateChat.GetLatestMessages())
            {
                // Handle Function results
                if (message.Role == ActorRole.Function)
                {
                    // Use unified ID
                    var callId = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionCallId)?.ToString();

                    // Find Claude's tool_use_id for this call
                    var toolUseId = FindToolUseIdForCallId(callId);

                    if (string.IsNullOrEmpty(toolUseId))
                    {
                        // Skip this message if no matching tool_use_id found
                        Console.WriteLine($"[Warning] No tool_use_id found for function result with call_id: {callId}");
                        continue;
                    }

                    messagesList.Add(new
                    {
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "tool_result",
                                tool_use_id = toolUseId,
                                content = message.Content
                            }
                        }
                    });
                }
                // Handle Assistant messages with function calls
                else if (message.Role == ActorRole.Assistant &&
                         message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    var callId = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionCallId)?.ToString();
                    var functionName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
                    var argumentsStr = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";

                    // Get or generate Claude's tool_use_id
                    var toolUseId = message.Metadata.GetValueOrDefault(MessageMetadataKeys.ClaudeToolUseId)?.ToString();
                    if (string.IsNullOrEmpty(toolUseId))
                    {
                        toolUseId = $"toolu_{callId}";
                    }

                    var contentList = new List<object>();

                    // Add text content if exists
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        contentList.Add(new { type = "text", text = message.Content });
                    }

                    // Add tool_use - always parse from string
                    contentList.Add(new
                    {
                        type = "tool_use",
                        id = toolUseId,
                        name = functionName,
                        input = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsStr) ?? new Dictionary<string, object>()
                    });

                    messagesList.Add(new
                    {
                        role = "assistant",
                        content = contentList
                    });
                }
                // Handle regular messages
                else
                {
                    messagesList.Add(ConvertMessageForClaude(message));
                }
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["messages"] = messagesList,
                ["temperature"] = ActivateChat.Temperature,
                ["max_tokens"] = ActivateChat.MaxTokens,
                ["stream"] = ActivateChat.Stream
            };

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                requestBody["system"] = ActivateChat.SystemMessage;
            }

            // Add tools (Claude's function calling)
            if (ActivateChat.ShouldUseFunctions)
            {
                requestBody["tools"] = ActivateChat.Functions.Select(f => new
                {
                    name = f.Name,
                    description = f.Description,
                    input_schema = new
                    {
                        type = "object",
                        properties = f.Parameters.Properties,
                        required = f.Parameters.Required
                    }
                }).ToList();

                // Tool choice configuration
                if (ActivateChat.FunctionCallMode == FunctionCallMode.None)
                {
                    requestBody["tool_choice"] = new { type = "none" };
                }
                else
                {
                    requestBody["tool_choice"] = new { type = "auto" };  // default
                }
            }

            return requestBody;
        }

        private string FindToolUseIdForCallId(string callId)
        {
            if (string.IsNullOrEmpty(callId)) return null;

            // Search messages in reverse order (most recent first)
            for (int i = ActivateChat.Messages.Count - 1; i >= 0; i--)
            {
                var msg = ActivateChat.Messages[i];
                if (msg.Role == ActorRole.Assistant &&
                    msg.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionCallId)?.ToString() == callId)
                {
                    // Return Claude's tool_use_id
                    var toolUseId = msg.Metadata.GetValueOrDefault(MessageMetadataKeys.ClaudeToolUseId)?.ToString();
                    if (!string.IsNullOrEmpty(toolUseId))
                        return toolUseId;

                    // Fallback: generate from callId
                    return $"toolu_{callId}";
                }
            }

            return null;
        }

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
        {
            var result = ExtractFunctionCallWithMetadata(response);
            return (result.content, result.functionCall);
        }

        /// <summary>
        /// Enhanced extraction that also saves assistant message with tool_use
        /// </summary>
        private (string content, FunctionCall functionCall, bool wasToolUseSaved) ExtractFunctionCallWithMetadata(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string content = string.Empty;
                FunctionCall functionCall = null;
                string toolUseId = null;
                bool wasToolUseSaved = false;

                // Extract content and tool_use from response
                if (root.TryGetProperty("content", out var contentArray) &&
                    contentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in contentArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeElement))
                        {
                            var type = typeElement.GetString();

                            if (type == "text" && item.TryGetProperty("text", out var textElement))
                            {
                                content += textElement.GetString();
                            }
                            else if (type == "tool_use" && functionCall == null)
                            {
                                // Extract tool_use_id for proper message chaining
                                toolUseId = item.GetProperty("id").GetString();

                                functionCall = new FunctionCall
                                {
                                    Name = item.GetProperty("name").GetString(),
                                    ProviderSpecificId = toolUseId,
                                    Provider = AIProvider.Anthropic,
                                    Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        item.GetProperty("input").GetRawText()) ?? new Dictionary<string, object>()
                                };
                            }
                        }
                    }
                }

                // If we have a tool_use, save the complete assistant response
                if (functionCall != null && toolUseId != null)
                {
                    // Claude API 버그 우회: 빈 content 방지
                    var messageContent = string.IsNullOrWhiteSpace(content) ? "." : content;

                    // Create assistant message with standardized metadata
                    var assistantMessage = new Message(ActorRole.Assistant, messageContent)
                    {
                        Metadata = new Dictionary<string, object>
                        {
                            [MessageMetadataKeys.MessageType] = "function_call",
                            [MessageMetadataKeys.FunctionCallId] = functionCall.Id,
                            [MessageMetadataKeys.FunctionName] = functionCall.Name,
                            [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(functionCall.Arguments),
                            [MessageMetadataKeys.ClaudeToolUseId] = toolUseId
                        }
                    };

                    ActivateChat.Messages.Add(assistantMessage);
                    wasToolUseSaved = true;
                }

                return (content, functionCall, wasToolUseSaved);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting function call: {ex.Message}");
                return (string.Empty, null, false);
            }
        }

        #endregion
    }
}