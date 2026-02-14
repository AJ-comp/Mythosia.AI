using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Utilities;
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

            foreach (var message in GetLatestMessages())
            {
                // Handle Function results
                if (message.Role == ActorRole.Function)
                {
                    var functionId = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
                    var functionSource = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionSource);

                    if (string.IsNullOrEmpty(functionId) || functionSource == null)
                    {
                        throw new InvalidOperationException(
                            $"Function result message missing ID or source. Function: {message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)}"
                        );
                    }

                    var source = (IdSource)functionSource;
                    var claudeId = FunctionIdConverter.ToClaudeId(functionId, source);

                    messagesList.Add(new
                    {
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "tool_result",
                                tool_use_id = claudeId,
                                content = message.Content ?? ""
                            }
                        }
                    });
                }
                // Handle Assistant messages with function calls
                else if (message.Role == ActorRole.Assistant &&
                         message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    // Check if we have the original content preserved
                    if (message.Metadata.ContainsKey(MessageMetadataKeys.OriginalContent))
                    {
                        var originalContent = message.Metadata[MessageMetadataKeys.OriginalContent].ToString();
                        messagesList.Add(new
                        {
                            role = "assistant",
                            content = JsonSerializer.Deserialize<JsonElement>(originalContent)
                        });
                    }
                    else
                    {
                        // Reconstruct from metadata
                        var functionId = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
                        var functionSource = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionSource);
                        var functionName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
                        var argumentsStr = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";

                        if (string.IsNullOrEmpty(functionId) || functionSource == null)
                        {
                            throw new InvalidOperationException("Assistant function call message missing ID or source");
                        }

                        var source = (IdSource)functionSource;
                        var claudeId = FunctionIdConverter.ToClaudeId(functionId, source);

                        var contentList = new List<object>();

                        if (!string.IsNullOrEmpty(message.Content))
                        {
                            contentList.Add(new { type = "text", text = message.Content });
                        }

                        contentList.Add(new
                        {
                            type = "tool_use",
                            id = claudeId,
                            name = functionName,
                            input = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsStr) ?? new Dictionary<string, object>()
                        });

                        messagesList.Add(new
                        {
                            role = "assistant",
                            content = contentList
                        });
                    }
                }
                // Handle regular messages
                else
                {
                    messagesList.Add(ConvertMessageForClaude(message));
                }
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["max_tokens"] = GetEffectiveMaxTokens(),
                ["stream"] = Stream
            };

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                requestBody["system"] = SystemMessage;
            }

            // Add tools (Claude's function calling)
            if (ShouldUseFunctions)
            {
                requestBody["tools"] = Functions.Select(f => new
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
                if (FunctionCallMode == FunctionCallMode.None)
                {
                    requestBody["tool_choice"] = new { type = "none" };
                }
                else
                {
                    requestBody["tool_choice"] = new { type = "auto" };
                }
            }

            return requestBody;
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
                    // Preserve original content for later reconstruction
                    var originalContent = contentArray.GetRawText();

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
                                toolUseId = item.GetProperty("id").GetString();

                                functionCall = new FunctionCall
                                {
                                    Id = toolUseId,
                                    Source = IdSource.Claude,
                                    Name = item.GetProperty("name").GetString(),
                                    Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        item.GetProperty("input").GetRawText()) ?? new Dictionary<string, object>()
                                };
                            }
                        }
                    }

                    // If we have a tool_use, save the complete assistant response
                    if (functionCall != null && toolUseId != null)
                    {
                        // Claude API workaround: prevent empty content
                        var messageContent = string.IsNullOrWhiteSpace(content) ? "." : content;

                        // Create assistant message with standardized metadata
                        var assistantMessage = new Message(ActorRole.Assistant, messageContent)
                        {
                            Metadata = new Dictionary<string, object>
                            {
                                [MessageMetadataKeys.MessageType] = "function_call",
                                [MessageMetadataKeys.FunctionId] = functionCall.Id,
                                [MessageMetadataKeys.FunctionSource] = functionCall.Source,
                                [MessageMetadataKeys.FunctionName] = functionCall.Name,
                                [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(functionCall.Arguments),
                                [MessageMetadataKeys.OriginalContent] = originalContent
                            }
                        };

                        ActivateChat.Messages.Add(assistantMessage);
                        wasToolUseSaved = true;
                    }
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