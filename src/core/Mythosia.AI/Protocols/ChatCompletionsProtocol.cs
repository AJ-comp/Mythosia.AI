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

            // Request token usage in streaming responses
            if (p.Stream)
            {
                requestBody["stream_options"] = new Dictionary<string, object>
                {
                    ["include_usage"] = true
                };
            }

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
                if (message.FunctionCallBatch != null)
                {
                    messagesList.Add(new
                    {
                        role = "assistant",
                        content = string.IsNullOrEmpty(message.Content) ? null : message.Content,
                        tool_calls = message.FunctionCallBatch.Calls.Select(call => new
                        {
                            id = call.Id,
                            type = "function",
                            function = new
                            {
                                name = call.Name,
                                arguments = JsonSerializer.Serialize(call.Arguments)
                            }
                        }).ToList()
                    });
                }
                else if (message.FunctionCallResultBatch != null)
                {
                    foreach (var result in message.FunctionCallResultBatch.Results)
                    {
                        messagesList.Add(new
                        {
                            role = "tool",
                            tool_call_id = result.Call.Id,
                            content = result.Content
                        });
                    }
                }
                else if (message.Role == ActorRole.Function)
                {
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

            // Request token usage in streaming responses
            if (p.Stream)
            {
                requestBody["stream_options"] = new Dictionary<string, object>
                {
                    ["include_usage"] = true
                };
            }

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

        public override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices))
                return (string.Empty, new FunctionCallBatch());
            if (choices.ValueKind != JsonValueKind.Array)
                throw new AIServiceException("The chat/completions choices field must be an array.");
            if (choices.GetArrayLength() == 0)
                return (string.Empty, new FunctionCallBatch());

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message))
                return (string.Empty, new FunctionCallBatch());

            var finishReason = GetFinishReason(choice);

            string? content = null;
            var functionCalls = new List<FunctionCall>();

            // Extract content
            if (message.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind != JsonValueKind.Null)
            {
                content = contentElement.GetString();
            }

            // Extract tool_calls in provider order.
            var hasModernToolCalls = message.TryGetProperty(
                "tool_calls",
                out var toolCallsElement) &&
                toolCallsElement.ValueKind != JsonValueKind.Null;
            if (hasModernToolCalls)
            {
                if (toolCallsElement.ValueKind != JsonValueKind.Array)
                    throw new AIServiceException("The chat/completions tool_calls field must be an array.");

                var index = 0;
                foreach (var toolCall in toolCallsElement.EnumerateArray())
                {
                    if (toolCall.ValueKind != JsonValueKind.Object)
                        throw new AIServiceException($"Tool call at index {index} must be an object.");
                    if (!toolCall.TryGetProperty("function", out var functionElement) ||
                        functionElement.ValueKind != JsonValueKind.Object)
                        throw new AIServiceException($"Tool call at index {index} is missing its function payload.");

                    if (!toolCall.TryGetProperty("id", out var idElement) ||
                        idElement.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(idElement.GetString()))
                    {
                        throw new AIServiceException(
                            $"Tool call at index {index} is missing its provider call ID.");
                    }

                    var functionCall = new FunctionCall
                    {
                        Id = idElement.GetString()!,
                        Name = functionElement.TryGetProperty("name", out var nameElement)
                            ? nameElement.GetString() ?? string.Empty
                            : string.Empty,
                        Arguments = ParseFunctionArguments(functionElement, index),
                        Source = IdSource.OpenAI,
                        Index = index
                    };

                    functionCalls.Add(functionCall);
                    index++;
                }

                if (!string.Equals(finishReason, "tool_calls", StringComparison.Ordinal))
                {
                    throw new AIServiceException(
                        $"The response contained tool_calls but finish_reason was '{finishReason ?? "missing"}'.");
                }
            }
            else if (message.TryGetProperty("function_call", out var legacyFunctionCall))
            {
                if (legacyFunctionCall.ValueKind != JsonValueKind.Object)
                    throw new AIServiceException("The legacy function_call field must be an object.");
                functionCalls.Add(new FunctionCall
                {
                    Id = $"call_{Guid.NewGuid():N}",
                    Name = legacyFunctionCall.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty,
                    Arguments = ParseFunctionArguments(legacyFunctionCall, 0),
                    Source = IdSource.OpenAI,
                    Index = 0
                });

                if (!string.Equals(finishReason, "function_call", StringComparison.Ordinal))
                {
                    throw new AIServiceException(
                        $"The response contained function_call but finish_reason was '{finishReason ?? "missing"}'.");
                }
            }

            if (string.Equals(finishReason, "tool_calls", StringComparison.Ordinal) &&
                functionCalls.Count == 0)
            {
                throw new AIServiceException(
                    "The response ended with finish_reason=tool_calls but contained no usable tool calls.");
            }
            if (string.Equals(finishReason, "function_call", StringComparison.Ordinal) &&
                functionCalls.Count == 0)
            {
                throw new AIServiceException(
                    "The response ended with finish_reason=function_call but contained no usable function call.");
            }

            return (content ?? string.Empty, new FunctionCallBatch(functionCalls));
        }

        public string? ExtractFinishReason(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            return GetFinishReason(choices[0]);
        }

        private static string? GetFinishReason(JsonElement choice)
        {
            if (!choice.TryGetProperty("finish_reason", out var finishReason) ||
                finishReason.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (finishReason.ValueKind != JsonValueKind.String)
                throw new AIServiceException("The chat/completions finish_reason field must be a string or null.");

            return finishReason.GetString();
        }

        private static Dictionary<string, object> ParseFunctionArguments(
            JsonElement functionElement,
            int callIndex)
        {
            if (!functionElement.TryGetProperty("arguments", out var argumentsElement) ||
                argumentsElement.ValueKind != JsonValueKind.String)
            {
                throw new AIServiceException(
                    $"Tool call at index {callIndex} is missing string JSON arguments.");
            }

            var argumentsJson = argumentsElement.GetString();
            try
            {
                using var argumentsDocument = JsonDocument.Parse(argumentsJson ?? string.Empty);
                if (argumentsDocument.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Function arguments must be a JSON object.");

                return JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson!)
                    ?? new Dictionary<string, object>();
            }
            catch (JsonException exception)
            {
                throw new AIServiceException(
                    $"Tool call at index {callIndex} contains invalid JSON arguments.",
                    exception);
            }
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
