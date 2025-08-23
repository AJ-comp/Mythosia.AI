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
            var requestBody = BuildRequestBodyWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private object BuildRequestBodyWithFunctions()
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

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["messages"] = messagesList,
                ["temperature"] = ActivateChat.Temperature,
                ["max_tokens"] = ActivateChat.MaxTokens,
                ["stream"] = ActivateChat.Stream
            };

            // Add function definitions
            if (ActivateChat.ShouldUseFunctions)
            {
                requestBody["functions"] = ActivateChat.Functions.Select(f => new
                {
                    name = f.Name,
                    description = f.Description,
                    parameters = f.Parameters
                }).ToList();

                // Set function call mode
                requestBody["function_call"] = ActivateChat.FunctionCallMode switch
                {
                    FunctionCallMode.None => "none",
                    FunctionCallMode.Force => new { name = ActivateChat.ForceFunctionName },
                    _ => "auto"
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

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
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
                        Name = functionCallElement.GetProperty("name").GetString(),
                        Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            functionCallElement.GetProperty("arguments").GetString())
                    };
                }

                return (content ?? string.Empty, functionCall);
            }
            catch (Exception)
            {
                // If parsing fails, return empty function call
                return (string.Empty, null);
            }
        }

        #endregion
    }
}