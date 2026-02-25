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

            // Add system message if present
            if (!string.IsNullOrEmpty(SystemMessage))
            {
                messagesList.Add(new { role = "system", content = SystemMessage });
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
