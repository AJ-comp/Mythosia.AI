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

            AddClaudeHeaders(request, "tools-2024-04-04");

            return request;
        }

        private object BuildRequestBodyWithFunctions()
        {
            var messagesList = new List<object>();

            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForFunctionCalling(message));
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["max_tokens"] = GetEffectiveMaxTokens(),
                ["stream"] = Stream
            };

            ApplySystemMessage(requestBody);
            ApplyThinkingConfig(requestBody);
            ApplyToolsConfig(requestBody);

            return requestBody;
        }

        private object ConvertMessageForFunctionCalling(Message message)
        {
            if (message.Role == ActorRole.Function)
                return ConvertFunctionResultMessage(message);

            if (message.Role == ActorRole.Assistant &&
                message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                return ConvertAssistantFunctionCallMessage(message);

            return ConvertMessageForClaude(message);
        }

        private object ConvertFunctionResultMessage(Message message)
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

            return new
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
            };
        }

        private object ConvertAssistantFunctionCallMessage(Message message)
        {
            // Check if we have the original content preserved
            if (message.Metadata.ContainsKey(MessageMetadataKeys.OriginalContent))
            {
                var originalContent = message.Metadata[MessageMetadataKeys.OriginalContent].ToString();
                return new
                {
                    role = "assistant",
                    content = JsonSerializer.Deserialize<JsonElement>(originalContent)
                };
            }

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

            return new
            {
                role = "assistant",
                content = contentList
            };
        }

        private void ApplyToolsConfig(Dictionary<string, object> requestBody)
        {
            if (!ShouldUseFunctions) return;

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
            requestBody["tool_choice"] = FunctionCallMode == FunctionCallMode.None
                ? new { type = "none" }
                : (object)new { type = "auto" };
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

                        var assistantMessage = CreateFunctionCallMessage(functionCall, messageContent);
                        assistantMessage.Metadata[MessageMetadataKeys.OriginalContent] = originalContent;

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