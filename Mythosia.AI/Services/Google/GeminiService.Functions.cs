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
            var modelName = Model;
            var endpoint = Stream
                ? $"v1beta/models/{modelName}:streamGenerateContent?alt=sse&key={ApiKey}"
                : $"v1beta/models/{modelName}:generateContent?key={ApiKey}";

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

            var isGemini3 = IsGemini3Model();

            foreach (var message in GetLatestMessages())
            {
                if (message.Role == ActorRole.Assistant &&
                    message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    // Model's functionCall response
                    var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "";
                    var argsJson = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
                    var args = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson) ?? new Dictionary<string, object>();

                    var functionCallPart = new Dictionary<string, object>
                    {
                        ["functionCall"] = new Dictionary<string, object>
                        {
                            ["name"] = funcName,
                            ["args"] = args
                        }
                    };

                    // Gemini 3: circulate thought signature back on functionCall parts (strict validation)
                    if (message.Metadata.TryGetValue(MessageMetadataKeys.ThoughtSignature, out var sigObj) && sigObj != null)
                    {
                        functionCallPart["thoughtSignature"] = sigObj.ToString()!;
                    }

                    contentsList.Add(new Dictionary<string, object>
                    {
                        ["role"] = "model",
                        ["parts"] = new[] { functionCallPart }
                    });
                }
                else if (message.Role == ActorRole.Function)
                {
                    // Function result - Gemini 3 uses "user" role, Gemini 2.x uses "function" role
                    var functionResponseRole = isGemini3 ? "user" : "function";
                    contentsList.Add(new Dictionary<string, object>
                    {
                        ["role"] = functionResponseRole,
                        ["parts"] = new[] { new Dictionary<string, object>
                        {
                            ["functionResponse"] = new Dictionary<string, object>
                            {
                                ["name"] = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "function",
                                ["response"] = new Dictionary<string, object> { ["content"] = message.Content }
                            }
                        }}
                    });
                }
                else
                {
                    contentsList.Add(ConvertMessageForGemini(message));
                }
            }

            var generationConfig = new Dictionary<string, object>
            {
                ["temperature"] = Temperature,
                ["topP"] = TopP,
                ["topK"] = 40,
                ["maxOutputTokens"] = (int)GetEffectiveMaxTokens()
            };

            ApplyThinkingConfig(generationConfig);

            var requestBody = new Dictionary<string, object>
            {
                ["contents"] = contentsList,
                ["generationConfig"] = generationConfig
            };

            // Add function declarations
            if (ShouldUseFunctions)
            {
                requestBody["tools"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["functionDeclarations"] = Functions.Select(f => new Dictionary<string, object>
                        {
                            ["name"] = f.Name,
                            ["description"] = f.Description,
                            ["parameters"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = f.Parameters.Properties.ToDictionary(
                                    kvp => kvp.Key,
                                    kvp => (object)ConvertParameterProperty(kvp.Value)),
                                ["required"] = f.Parameters.Required
                            }
                        }).ToList()
                    }
                };

                // 
                if (FunctionCallMode == FunctionCallMode.None)
                {
                    requestBody["toolConfig"] = new Dictionary<string, object>
                    {
                        ["functionCallingConfig"] = new Dictionary<string, object> { ["mode"] = "NONE" }
                    };
                }
                // Auto mode 
                // Gemini 
            }

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                requestBody["systemInstruction"] = new
                {
                    parts = new[] { new { text = ActivateChat.SystemMessage } }
                };
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
                                    Id = Guid.NewGuid().ToString(),
                                    Source = IdSource.Gemini,
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

        private Dictionary<string, object> ConvertParameterProperty(ParameterProperty prop)
        {
            var result = new Dictionary<string, object>
            {
                ["type"] = prop.Type ?? "string"
            };

            if (!string.IsNullOrEmpty(prop.Description))
                result["description"] = prop.Description;

            if (prop.Enum != null && prop.Enum.Count > 0)
                result["enum"] = prop.Enum;

            if (prop.Items != null)
                result["items"] = ConvertParameterProperty(prop.Items);

            return result;
        }

        #endregion
    }
}