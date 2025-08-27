using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var requestBody = BuildRequestWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Determine endpoint based on model
            string endpoint = IsNewApiModel(ActivateChat.Model)
                ? (ActivateChat.Stream ? "responses?stream=true" : "responses")
                : "chat/completions";

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private object BuildRequestWithFunctions()
        {
            var requestBody = new Dictionary<string, object>();

            if (IsNewApiModel(ActivateChat.Model))
            {
                // Build new API format (GPT-5, o3, GPT-4.1)
                BuildNewApiRequest(requestBody);
            }
            else
            {
                // Build legacy API format
                BuildLegacyRequest(requestBody);
            }

            // Apply model-specific parameter configurations
            ApplyModelSpecificParameters(requestBody);

            return requestBody;
        }

        /// <summary>
        /// Creates function schema that works for both old and new API
        /// </summary>
        private Dictionary<string, object> CreateFunctionParameterSchema(FunctionDefinition f, bool isNewApi = false)
        {
            var properties = new Dictionary<string, object>();
            var allPropertyNames = new List<string>();

            if (f.Parameters?.Properties != null && f.Parameters.Properties.Count > 0)
            {
                foreach (var prop in f.Parameters.Properties)
                {
                    var propObj = new Dictionary<string, object>();

                    // Type is required
                    propObj["type"] = !string.IsNullOrEmpty(prop.Value.Type)
                        ? prop.Value.Type
                        : "string";

                    // Description
                    if (!string.IsNullOrEmpty(prop.Value.Description))
                        propObj["description"] = prop.Value.Description;

                    // Enum values
                    if (prop.Value.Enum != null && prop.Value.Enum.Count > 0)
                        propObj["enum"] = prop.Value.Enum;

                    // Default value (indicates optional parameter)
                    if (prop.Value.Default != null)
                        propObj["default"] = prop.Value.Default;

                    properties[prop.Key] = propObj;
                    allPropertyNames.Add(prop.Key);  // Add all properties to required
                }
            }

            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = allPropertyNames  // All properties in required for compatibility
            };

            // New API requires additionalProperties: false
            if (isNewApi)
            {
                schema["additionalProperties"] = false;
            }

            return schema;
        }

        private void BuildNewApiRequest(Dictionary<string, object> requestBody)
        {
            var inputList = new List<object>();

            // Convert messages to new format
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                // Handle Assistant messages with function_call metadata
                if (message.Role == ActorRole.Assistant &&
                    message.Metadata?.GetValueOrDefault("type")?.ToString() == "function_call")
                {
                    var callId = message.Metadata.GetValueOrDefault("call_id")?.ToString();
                    var functionName = message.Metadata.GetValueOrDefault("function_name")?.ToString();
                    var arguments = message.Metadata.GetValueOrDefault("arguments")?.ToString();

                    if (!string.IsNullOrEmpty(callId) && !string.IsNullOrEmpty(functionName))
                    {
                        inputList.Add(new
                        {
                            type = "function_call",
                            call_id = callId,
                            name = functionName,
                            arguments = arguments ?? "{}"
                        });
                    }
                }
                // Handle Function results
                else if (message.Role == ActorRole.Function)
                {
                    var callId = message.Metadata?.GetValueOrDefault("call_id")?.ToString();

                    if (!string.IsNullOrEmpty(callId))
                    {
                        inputList.Add(new
                        {
                            type = "function_call_output",
                            call_id = callId,
                            output = message.Content
                        });
                    }
                    else
                    {
                        var functionName = message.Metadata?.GetValueOrDefault("function_name")?.ToString() ?? "unknown";
                        Console.WriteLine($"[Warning] Skipping function result without call_id: {functionName}");
                    }
                }
                // Handle regular messages
                else if (message.Role != ActorRole.Assistant ||
                         message.Metadata?.GetValueOrDefault("type")?.ToString() != "function_call")
                {
                    inputList.Add(new
                    {
                        role = message.Role.ToDescription(),
                        content = message.Content
                    });
                }
            }

            // Convert functions to tools format using unified schema
            var tools = ActivateChat.Functions.Select(f => new
            {
                type = "function",
                name = f.Name,
                description = f.Description,
                parameters = CreateFunctionParameterSchema(f, isNewApi: true),  // Pass true for new API
                strict = true
            }).ToList();

            requestBody["model"] = ActivateChat.Model;
            requestBody["input"] = inputList;
            requestBody["tools"] = tools;

            // Add instructions if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                requestBody["instructions"] = ActivateChat.SystemMessage;
            }

            // Tool choice configuration
            requestBody["tool_choice"] = ActivateChat.FunctionCallMode == FunctionCallMode.None
                ? "none"
                : "auto";

            if (ActivateChat.Stream)
            {
                requestBody["stream"] = true;
            }
        }

        private void BuildLegacyRequest(Dictionary<string, object> requestBody)
        {
            var messagesList = new List<object>();

            // Add system message if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

            // Convert messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                if (message.Role == ActorRole.Function)
                {
                    messagesList.Add(new
                    {
                        role = "function",
                        name = message.Metadata?["function_name"]?.ToString() ?? "function",
                        content = message.Content
                    });
                }
                else
                {
                    messagesList.Add(ConvertMessageForOpenAI(message));
                }
            }

            // Build functions array using unified schema
            var functionsArray = ActivateChat.Functions.Select(f => new
            {
                name = f.Name,
                description = f.Description,
                parameters = CreateFunctionParameterSchema(f, isNewApi: false)  // Pass false for legacy API
            }).ToList();

            requestBody["model"] = ActivateChat.Model;
            requestBody["messages"] = messagesList;
            requestBody["functions"] = functionsArray;
            requestBody["temperature"] = ActivateChat.Temperature;
            requestBody["stream"] = ActivateChat.Stream;

            // Function call mode
            requestBody["function_call"] = ActivateChat.FunctionCallMode == FunctionCallMode.None
                ? "none"
                : "auto";
        }

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Check API format and extract accordingly
            if (root.TryGetProperty("output", out var output))
            {
                // New API format (GPT-5, o3, GPT-4.1)
                return ExtractNewApiFunctionCall(output);
            }
            else if (root.TryGetProperty("choices", out var choices))
            {
                // Legacy API format
                return ExtractLegacyFunctionCall(choices);
            }

            return (string.Empty, null);
        }

        private (string content, FunctionCall functionCall) ExtractNewApiFunctionCall(JsonElement output)
        {
            string content = string.Empty;
            FunctionCall functionCall = null;

            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var typeElem))
                    continue;

                var type = typeElem.GetString();

                if (type == "message")
                {
                    // content가 배열인 경우 처리
                    if (item.TryGetProperty("content", out var messageContent))
                    {
                        if (messageContent.ValueKind == JsonValueKind.Array)
                        {
                            // content가 배열인 경우
                            foreach (var contentItem in messageContent.EnumerateArray())
                            {
                                if (contentItem.TryGetProperty("type", out var contentType) &&
                                    contentType.GetString() == "output_text" &&
                                    contentItem.TryGetProperty("text", out var textElem))
                                {
                                    content += textElem.GetString();
                                }
                            }
                        }
                        else if (messageContent.ValueKind == JsonValueKind.String)
                        {
                            // content가 문자열인 경우 (이전 형식 호환)
                            content += messageContent.GetString();
                        }
                    }
                }
                else if (type == "function_call")
                {
                    functionCall = new FunctionCall
                    {
                        Name = item.GetProperty("name").GetString(),
                        Arguments = new Dictionary<string, object>()
                    };

                    // Store call_id
                    if (item.TryGetProperty("call_id", out var callId))
                    {
                        functionCall.CallId = callId.GetString();
                    }

                    // Parse arguments
                    if (item.TryGetProperty("arguments", out var argsElem))
                    {
                        var argsString = argsElem.GetString();
                        if (!string.IsNullOrEmpty(argsString))
                        {
                            try
                            {
                                var args = JsonSerializer.Deserialize<Dictionary<string, object>>(argsString);
                                foreach (var kvp in args ?? new Dictionary<string, object>())
                                {
                                    functionCall.Arguments[kvp.Key] = kvp.Value;
                                }
                            }
                            catch
                            {
                                // Keep empty arguments on parse failure
                            }
                        }
                    }
                }
            }

            return (content, functionCall);
        }

        private (string content, FunctionCall functionCall) ExtractLegacyFunctionCall(JsonElement choices)
        {
            if (choices.GetArrayLength() == 0)
                return (string.Empty, null);

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message))
                return (string.Empty, null);

            string content = null;
            FunctionCall functionCall = null;

            // Extract content
            if (message.TryGetProperty("content", out var contentElement))
            {
                content = contentElement.GetString();
            }

            // Extract function call
            if (message.TryGetProperty("function_call", out var functionCallElement))
            {
                functionCall = new FunctionCall
                {
                    Name = functionCallElement.GetProperty("name").GetString(),
                    Arguments = new Dictionary<string, object>()
                };

                // Parse arguments JSON string
                if (functionCallElement.TryGetProperty("arguments", out var argsElement))
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

            return (content ?? string.Empty, functionCall);
        }
    }
}