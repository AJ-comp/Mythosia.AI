using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Protocols
{
    /// <summary>
    /// Implements the OpenAI-compatible /chat/completions wire format.
    /// Used by Grok, DeepSeek, Sonar, and other compatible providers.
    /// </summary>
    public class ChatCompletionsProtocol : CompletionProtocol
    {
        public static readonly ChatCompletionsProtocol Instance = new ChatCompletionsProtocol();

        #region Response Parsing

        public override string ExtractResponse(string responseJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse chat/completions response", ex);
            }
        }

        public override string ParseStreamChunk(string chunkJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(chunkJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region Request Creation

        public override HttpRequestMessage CreateRequest(string apiKey, object requestBody)
        {
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            request.Headers.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("Authorization", $"Bearer {apiKey}");

            return request;
        }

        public override HttpRequestMessage CreateFunctionRequest(string apiKey, object requestBody)
        {
            // Same endpoint and auth for function-calling requests
            return CreateRequest(apiKey, requestBody);
        }

        #endregion

        #region Request Body Building

        public override object BuildRequestBody(
            ProtocolRequestParams p,
            Func<Message, object>? messageConverter = null)
        {
            var converter = messageConverter ?? ConvertMessage;
            var messagesList = new List<object>();

            // System message
            if (!string.IsNullOrEmpty(p.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = p.SystemMessage });
            }

            // Conversation messages
            foreach (var message in p.Messages)
            {
                messagesList.Add(converter(message));
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = p.Model,
                ["messages"] = messagesList,
                ["stream"] = p.Stream
            };

            // Standard parameters (can be excluded per provider)
            var exclude = p.ExcludeParameters;
            if (exclude == null || !exclude.Contains("temperature"))
                requestBody["temperature"] = p.Temperature;
            if (exclude == null || !exclude.Contains("top_p"))
                requestBody["top_p"] = p.TopP;
            if (exclude == null || !exclude.Contains("max_tokens"))
                requestBody["max_tokens"] = (int)p.MaxTokens;
            if (exclude == null || !exclude.Contains("frequency_penalty"))
                requestBody["frequency_penalty"] = p.FrequencyPenalty;
            if (exclude == null || !exclude.Contains("presence_penalty"))
                requestBody["presence_penalty"] = p.PresencePenalty;

            // Structured output
            if (p.StructuredOutputSchemaJson != null)
            {
                requestBody["response_format"] = new Dictionary<string, object> { ["type"] = "json_object" };
            }

            // Provider-specific extra parameters
            if (p.ExtraParameters != null)
            {
                foreach (var kvp in p.ExtraParameters)
                {
                    requestBody[kvp.Key] = kvp.Value;
                }
            }

            return requestBody;
        }

        public override object BuildFunctionRequestBody(
            ProtocolRequestParams p,
            IReadOnlyList<FunctionDefinition> functions,
            FunctionCallMode mode,
            Func<Message, object>? messageConverter = null)
        {
            var converter = messageConverter ?? ConvertMessage;
            var messagesList = new List<object>();

            // System message
            if (!string.IsNullOrEmpty(p.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = p.SystemMessage });
            }

            // Convert messages with function-aware handling
            foreach (var message in p.Messages)
            {
                if (message.Role == ActorRole.Function)
                {
                    // Tool result message
                    var toolCallId = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString() ?? "";

                    messagesList.Add(new
                    {
                        role = "tool",
                        tool_call_id = toolCallId,
                        content = message.Content ?? ""
                    });
                }
                else if (message.Role == ActorRole.Assistant &&
                         message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    // Assistant message with tool_calls
                    var functionId = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
                    var functionName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
                    var argumentsStr = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";

                    messagesList.Add(new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = functionId,
                                type = "function",
                                function = new
                                {
                                    name = functionName,
                                    arguments = argumentsStr
                                }
                            }
                        }
                    });
                }
                else
                {
                    messagesList.Add(converter(message));
                }
            }

            // Build tools array
            var toolsArray = functions.Select(f =>
            {
                var properties = new Dictionary<string, object>();
                var requiredList = new List<string>();

                if (f.Parameters?.Properties != null)
                {
                    foreach (var prop in f.Parameters.Properties)
                    {
                        var propObj = new Dictionary<string, object>();

                        propObj["type"] = !string.IsNullOrEmpty(prop.Value.Type) ? prop.Value.Type : "string";

                        if (!string.IsNullOrEmpty(prop.Value.Description))
                            propObj["description"] = prop.Value.Description;

                        if (prop.Value.Enum != null && prop.Value.Enum.Count > 0)
                            propObj["enum"] = prop.Value.Enum;

                        if (prop.Value.Default != null)
                            propObj["default"] = prop.Value.Default;

                        properties[prop.Key] = propObj;
                        requiredList.Add(prop.Key);
                    }
                }

                return new
                {
                    type = "function",
                    function = new
                    {
                        name = f.Name,
                        description = f.Description,
                        parameters = new
                        {
                            type = "object",
                            properties = properties,
                            required = requiredList
                        }
                    }
                };
            }).ToList();

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = p.Model,
                ["messages"] = messagesList,
                ["tools"] = toolsArray,
                ["stream"] = p.Stream,
                ["tool_choice"] = mode == FunctionCallMode.None ? "none" : "auto"
            };

            // Standard parameters (can be excluded per provider)
            var exclude = p.ExcludeParameters;
            if (exclude == null || !exclude.Contains("temperature"))
                requestBody["temperature"] = p.Temperature;

            // Provider-specific extra parameters
            if (p.ExtraParameters != null)
            {
                foreach (var kvp in p.ExtraParameters)
                {
                    requestBody[kvp.Key] = kvp.Value;
                }
            }

            return requestBody;
        }

        #endregion

        #region Function Call Extraction

        public override (string content, FunctionCall? functionCall) ExtractFunctionCall(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return (string.Empty, null);

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message))
                return (string.Empty, null);

            string? content = null;
            FunctionCall? functionCall = null;

            // Extract content
            if (message.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind != JsonValueKind.Null)
            {
                content = contentElement.GetString();
            }

            // Extract tool_calls (standard format)
            if (message.TryGetProperty("tool_calls", out var toolCallsElement) &&
                toolCallsElement.ValueKind == JsonValueKind.Array &&
                toolCallsElement.GetArrayLength() > 0)
            {
                var firstToolCall = toolCallsElement[0];

                if (firstToolCall.TryGetProperty("function", out var functionElement))
                {
                    functionCall = new FunctionCall
                    {
                        Name = functionElement.GetProperty("name").GetString(),
                        Arguments = new Dictionary<string, object>(),
                        Source = IdSource.OpenAI
                    };

                    // Get tool call ID
                    if (firstToolCall.TryGetProperty("id", out var idElement))
                    {
                        functionCall.Id = idElement.GetString() ?? $"call_{Guid.NewGuid().ToString().Substring(0, 20)}";
                    }
                    else
                    {
                        functionCall.Id = $"call_{Guid.NewGuid().ToString().Substring(0, 20)}";
                    }

                    // Parse arguments JSON string
                    if (functionElement.TryGetProperty("arguments", out var argsElement))
                    {
                        var argsString = argsElement.GetString();
                        if (!string.IsNullOrEmpty(argsString))
                        {
                            try
                            {
                                functionCall.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsString)
                                    ?? new Dictionary<string, object>();
                            }
                            catch
                            {
                                // Keep empty arguments on parse failure
                            }
                        }
                    }
                }
            }

            return (content ?? string.Empty, functionCall);
        }

        #endregion

        #region Message Conversion

        /// <summary>
        /// Converts a message to the standard chat/completions format.
        /// Handles both text-only and multimodal (OpenAI-compatible image_url format).
        /// </summary>
        public override object ConvertMessage(Message message)
        {
            var role = message.Role.ToDescription();

            if (!message.HasMultimodalContent)
            {
                return new { role, content = message.Content };
            }

            var contentParts = new List<object>();

            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                {
                    contentParts.Add(new { type = "text", text = text.Text });
                }
                else if (content is ImageContent image)
                {
                    if (!string.IsNullOrEmpty(image.Url))
                    {
                        contentParts.Add(new
                        {
                            type = "image_url",
                            image_url = new { url = image.Url }
                        });
                    }
                    else if (image.Data != null)
                    {
                        var base64 = Convert.ToBase64String(image.Data);
                        var mimeType = image.MimeType ?? "image/png";
                        contentParts.Add(new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mimeType};base64,{base64}" }
                        });
                    }
                }
            }

            return new { role, content = contentParts };
        }

        #endregion
    }
}
