using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
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
                if (message.Role == ActorRole.Function)
                {
                    messagesList.Add(new
                    {
                        role = "user",
                        content = $"Function result: {message.Content}"
                    });
                }
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

                if (ActivateChat.FunctionCallMode == FunctionCallMode.Force && !string.IsNullOrEmpty(ActivateChat.ForceFunctionName))
                {
                    requestBody["tool_choice"] = new { type = "tool", name = ActivateChat.ForceFunctionName };
                }
                else if (ActivateChat.FunctionCallMode == FunctionCallMode.None)
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
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string content = string.Empty;
                FunctionCall functionCall = null;

                // Extract content
                if (root.TryGetProperty("content", out var contentArray) && contentArray.ValueKind == JsonValueKind.Array)
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
                                functionCall = new FunctionCall
                                {
                                    Name = item.GetProperty("name").GetString(),
                                    Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        item.GetProperty("input").GetRawText())
                                };
                            }
                        }
                    }
                }

                return (content, functionCall);
            }
            catch
            {
                return (string.Empty, null);
            }
        }

        #endregion
    }
}