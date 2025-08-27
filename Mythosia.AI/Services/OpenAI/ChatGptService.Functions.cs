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
        #region Function Calling Support

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

        private void BuildNewApiRequest(Dictionary<string, object> requestBody)
        {
            var inputList = new List<object>();

            // Convert messages to new format
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                if (message.Role == ActorRole.Function)
                {
                    inputList.Add(new
                    {
                        type = "function_call_output",
                        call_id = message.Metadata?["call_id"]?.ToString() ?? "",
                        output = message.Content
                    });
                }
                else
                {
                    inputList.Add(new
                    {
                        role = message.Role.ToDescription(),
                        content = message.Content
                    });
                }
            }

            // Convert functions to tools format
            var tools = ActivateChat.Functions.Select(f => new
            {
                type = "function",
                name = f.Name,
                description = f.Description,
                parameters = new
                {
                    type = "object",
                    properties = f.Parameters?.Properties ?? new Dictionary<string, ParameterProperty>(),
                    required = f.Parameters?.Required ?? new List<string>(),
                    additionalProperties = false
                },
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

            // Build functions array
            var functionsArray = ActivateChat.Functions.Select(f => new
            {
                name = f.Name,
                description = f.Description,
                parameters = new
                {
                    type = "object",
                    properties = f.Parameters?.Properties ?? new Dictionary<string, ParameterProperty>(),
                    required = f.Parameters?.Required ?? new List<string>()
                }
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

                if (type == "message" && item.TryGetProperty("content", out var messageContent))
                {
                    content += messageContent.GetString();
                }
                else if (type == "function_call")
                {
                    functionCall = new FunctionCall
                    {
                        Name = item.GetProperty("name").GetString(),
                        Arguments = new Dictionary<string, object>()
                    };

                    // Store call_id for response
                    if (item.TryGetProperty("call_id", out var callId))
                    {
                        functionCall.Arguments["__call_id"] = callId.GetString();
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
                                foreach (var kvp in args)
                                {
                                    if (kvp.Key != "__call_id")
                                    {
                                        functionCall.Arguments[kvp.Key] = kvp.Value;
                                    }
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

        #endregion
    }
}