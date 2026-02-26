using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.xAI
{
    public partial class GrokService
    {
        #region Function Calling

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var requestBody = BuildRequestWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            request.Headers.Add("Accept", "application/json");

            return request;
        }

        private object BuildRequestWithFunctions()
        {
            var messagesList = new List<object>();

            // Add system message if present
            var effectiveSystemMsg = GetEffectiveSystemMessage();
            if (!string.IsNullOrEmpty(effectiveSystemMsg))
            {
                messagesList.Add(new { role = "system", content = effectiveSystemMsg });
            }

            // Convert messages
            foreach (var message in GetLatestMessages())
            {
                if (message.Role == ActorRole.Function)
                {
                    // Tool result message (role: "tool" with tool_call_id)
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
                    messagesList.Add(ConvertMessageForGrok(message));
                }
            }

            // Build tools array (OpenAI-compatible tools format)
            var toolsArray = Functions.Select(f =>
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
                ["model"] = Model,
                ["messages"] = messagesList,
                ["tools"] = toolsArray,
                ["stream"] = Stream,
                ["tool_choice"] = FunctionCallMode == FunctionCallMode.None ? "none" : "auto"
            };

            // Reasoning models may reject temperature parameter
            if (!IsReasoningModel())
            {
                requestBody["temperature"] = Temperature;
            }

            return requestBody;
        }

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return (string.Empty, null!);

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message))
                return (string.Empty, null!);

            string? content = null;
            FunctionCall? functionCall = null;

            // Extract content
            if (message.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind != JsonValueKind.Null)
            {
                content = contentElement.GetString();
            }

            // Extract tool_calls (OpenAI-compatible format)
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

            return (content ?? string.Empty, functionCall!);
        }

        #endregion
    }
}
