using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        #region Function Calling Support

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            // Check if using new API (GPT-5, GPT-4.1) or legacy API
            bool useNewApi = IsNewApiModel(ActivateChat.Model);

            if (useNewApi)
            {
                return CreateNewApiFunctionRequest();
            }
            else
            {
                return CreateLegacyFunctionRequest();
            }
        }

        private bool IsNewApiModel(string model)
        {
            return model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                   model.StartsWith("gpt-4.1", StringComparison.OrdinalIgnoreCase);
        }

        private HttpRequestMessage CreateNewApiFunctionRequest()
        {
            var requestBody = BuildNewApiRequestWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // New API uses /responses endpoint
            var endpoint = ActivateChat.Stream ? "responses?stream=true" : "responses";
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private HttpRequestMessage CreateLegacyFunctionRequest()
        {
            var requestBody = BuildLegacyRequestWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private object BuildNewApiRequestWithFunctions()
        {
            var inputList = new List<object>();

            // Add system message as instructions
            string instructions = !string.IsNullOrEmpty(ActivateChat.SystemMessage)
                ? ActivateChat.SystemMessage
                : null;

            // Convert messages to new format
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                if (message.Role == ActorRole.Function)
                {
                    // Function results in new format
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
                strict = true  // Enable strict mode for reliability
            }).ToList();

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["input"] = inputList,
                ["tools"] = tools
            };

            if (!string.IsNullOrEmpty(instructions))
            {
                requestBody["instructions"] = instructions;
            }

            // Tool choice configuration
            if (ActivateChat.FunctionCallMode == FunctionCallMode.Force && !string.IsNullOrEmpty(ActivateChat.ForceFunctionName))
            {
                requestBody["tool_choice"] = new { type = "function", name = ActivateChat.ForceFunctionName };
            }
            else if (ActivateChat.FunctionCallMode == FunctionCallMode.None)
            {
                requestBody["tool_choice"] = "none";
            }
            else
            {
                requestBody["tool_choice"] = "auto";
            }

            if (ActivateChat.Stream)
            {
                requestBody["stream"] = true;
            }

            return requestBody;
        }

        private object BuildLegacyRequestWithFunctions()
        {
            var messagesList = new List<object>();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

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

            // For models that support the legacy functions parameter
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

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["messages"] = messagesList,
                ["temperature"] = ActivateChat.Temperature,
                ["max_tokens"] = ActivateChat.MaxTokens,
                ["stream"] = ActivateChat.Stream,
                ["functions"] = functionsArray
            };

            // Set function call mode for legacy API
            if (ActivateChat.FunctionCallMode == FunctionCallMode.Force && !string.IsNullOrEmpty(ActivateChat.ForceFunctionName))
            {
                requestBody["function_call"] = new { name = ActivateChat.ForceFunctionName };
            }
            else if (ActivateChat.FunctionCallMode == FunctionCallMode.None)
            {
                requestBody["function_call"] = "none";
            }
            else
            {
                requestBody["function_call"] = "auto";
            }

            return requestBody;
        }

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
        {
            // Check response format to determine API version
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // New API format (GPT-5, GPT-4.1)
            if (root.TryGetProperty("output", out var output))
            {
                return ExtractNewApiFunctionCall(output);
            }
            // Legacy API format
            else if (root.TryGetProperty("choices", out var choices))
            {
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
                        Name = item.GetProperty("name").GetString()
                    };

                    // Store call_id for response
                    if (item.TryGetProperty("call_id", out var callId))
                    {
                        functionCall.Arguments = functionCall.Arguments ?? new Dictionary<string, object>();
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
            {
                return (string.Empty, null);
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message))
            {
                return (string.Empty, null);
            }

            string content = null;
            FunctionCall functionCall = null;

            // Check for content
            if (message.TryGetProperty("content", out var contentElement))
            {
                content = contentElement.GetString();
            }

            // Check for function call
            if (message.TryGetProperty("function_call", out var functionCallElement))
            {
                functionCall = new FunctionCall
                {
                    Name = functionCallElement.GetProperty("name").GetString()
                };

                // Parse arguments - they come as a JSON string
                if (functionCallElement.TryGetProperty("arguments", out var argsElement))
                {
                    var argsString = argsElement.GetString();
                    if (!string.IsNullOrEmpty(argsString))
                    {
                        try
                        {
                            functionCall.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsString);
                        }
                        catch
                        {
                            functionCall.Arguments = new Dictionary<string, object>();
                        }
                    }
                }

                if (functionCall.Arguments == null)
                {
                    functionCall.Arguments = new Dictionary<string, object>();
                }
            }

            return (content ?? string.Empty, functionCall);
        }

        #endregion
    }
}