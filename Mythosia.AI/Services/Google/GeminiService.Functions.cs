using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GeminiService
    {
        #region Function Calling Support

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var modelName = ActivateChat.Model;
            var endpoint = $"v1beta/models/{modelName}:generateContent?key={ApiKey}";

            var requestBody = BuildRequestBodyWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            return new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
        }

        private object BuildRequestBodyWithFunctions()
        {
            var contentsList = new List<object>();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                contentsList.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = ActivateChat.SystemMessage } }
                });
                contentsList.Add(new
                {
                    role = "model",
                    parts = new[] { new { text = "Understood. I'll follow these instructions." } }
                });
            }

            foreach (var message in ActivateChat.GetLatestMessages())
            {
                if (message.Role == ActorRole.Function)
                {
                    contentsList.Add(new
                    {
                        role = "function",
                        parts = new[] { new
                {
                    functionResponse = new
                    {
                        name = message.Metadata?["function_name"]?.ToString() ?? "function",
                        response = new { content = message.Content }
                    }
                }}
                    });
                }
                else
                {
                    contentsList.Add(ConvertMessageForGemini(message));
                }
            }

            var requestBody = new Dictionary<string, object>
            {
                ["contents"] = contentsList,
                ["generationConfig"] = new
                {
                    temperature = ActivateChat.Temperature,
                    topP = ActivateChat.TopP,
                    topK = 40,
                    maxOutputTokens = (int)ActivateChat.MaxTokens
                }
            };

            // Add function declarations
            if (ActivateChat.ShouldUseFunctions)
            {
                requestBody["tools"] = new[]
                {
                    new
                    {
                        function_declarations = ActivateChat.Functions.Select(f => new
                        {
                            name = f.Name,
                            description = f.Description,
                            parameters = new
                            {
                                type = "object",
                                properties = f.Parameters.Properties,
                                required = f.Parameters.Required
                            }
                        }).ToList()
                    }
                };

                // ✅ 단순화된 tool_config 설정 (Force 제거)
                if (ActivateChat.FunctionCallMode == FunctionCallMode.None)
                {
                    requestBody["tool_config"] = new
                    {
                        function_calling_config = new { mode = "NONE" }
                    };
                }
                // Auto mode가 기본값이므로 별도 설정 불필요
                // Gemini는 tool_config를 명시하지 않으면 자동으로 AUTO mode
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

                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElement))
                            {
                                content += textElement.GetString();
                            }
                            else if (part.TryGetProperty("functionCall", out var functionCallElement))
                            {
                                functionCall = new FunctionCall
                                {
                                    Name = functionCallElement.GetProperty("name").GetString(),
                                    Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        functionCallElement.GetProperty("args").GetRawText())
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