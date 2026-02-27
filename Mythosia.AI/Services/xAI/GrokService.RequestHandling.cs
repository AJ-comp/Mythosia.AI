using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.xAI
{
    public partial class GrokService
    {
        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            request.Headers.Add("Accept", "application/json");

            return request;
        }

        private object BuildRequestBody()
        {
            var messagesList = new List<object>();

            // Add system message if present (with structured output instruction)
            var systemMsg = GetEffectiveSystemMessage();
            var structuredInstruction = GetStructuredOutputInstruction();
            if (structuredInstruction != null)
                systemMsg += structuredInstruction;

            if (!string.IsNullOrEmpty(systemMsg))
            {
                messagesList.Add(new { role = "system", content = systemMsg });
            }

            // Add conversation messages
            foreach (var message in GetLatestMessagesWithFunctionFallback())
            {
                messagesList.Add(ConvertMessageForGrok(message));
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["top_p"] = TopP,
                ["max_tokens"] = (int)GetEffectiveMaxTokens(),
                ["stream"] = Stream
            };

            // Reasoning models (grok-3-mini, grok-4*) do not support
            // frequency_penalty, presence_penalty, and stop parameters
            if (!IsReasoningModel())
            {
                requestBody["frequency_penalty"] = FrequencyPenalty;
                requestBody["presence_penalty"] = PresencePenalty;
            }

            if (_structuredOutputSchemaJson != null)
            {
                requestBody["response_format"] = new Dictionary<string, object> { ["type"] = "json_object" };
            }

            // Apply reasoning_effort — only grok-3-mini supports this parameter.
            // grok-3, grok-4, grok-4-fast-reasoning do NOT support it.
            if (SupportsReasoningEffort() && ReasoningEffort != GrokReasoning.Off)
            {
                requestBody["reasoning_effort"] = ReasoningEffort.ToString().ToLowerInvariant();
            }

            return requestBody;
        }

        /// <summary>
        /// Determines if the current model is a reasoning model.
        /// xAI reasoning models reject frequency_penalty, presence_penalty, stop parameters.
        /// </summary>
        private bool IsReasoningModel()
        {
            var model = Model?.ToLower() ?? "";
            return model.Contains("grok-3-mini") ||
                   model.Contains("grok-4");
        }

        /// <summary>
        /// Only grok-3-mini supports the reasoning_effort parameter.
        /// grok-3, grok-4, grok-4-fast-reasoning do NOT support it.
        /// </summary>
        private bool SupportsReasoningEffort()
        {
            var model = Model?.ToLower() ?? "";
            return model.Contains("grok-3-mini");
        }

        private object ConvertMessageForGrok(Message message)
        {
            if (!message.HasMultimodalContent)
            {
                return new { role = message.Role.ToDescription(), content = message.Content };
            }

            // Grok supports vision via OpenAI-compatible multimodal format
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
                        var base64 = System.Convert.ToBase64String(image.Data);
                        var mimeType = image.MimeType ?? "image/png";
                        contentParts.Add(new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mimeType};base64,{base64}" }
                        });
                    }
                }
            }

            return new { role = message.Role.ToDescription(), content = contentParts };
        }

        #endregion
    }
}
