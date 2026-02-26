using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
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
            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForDeepSeek(message));
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["top_p"] = TopP,
                ["max_tokens"] = (int)GetEffectiveMaxTokens(),
                ["stream"] = Stream,
                ["frequency_penalty"] = FrequencyPenalty,
                ["presence_penalty"] = PresencePenalty
            };

            if (_structuredOutputSchemaJson != null)
            {
                requestBody["response_format"] = new Dictionary<string, object> { ["type"] = "json_object" };
            }

            return requestBody;
        }

        private object ConvertMessageForDeepSeek(Message message)
        {
            // DeepSeek currently doesn't support multimodal in their public API
            // But we'll prepare the structure for when they do
            if (!message.HasMultimodalContent)
            {
                return new { role = message.Role.ToDescription(), content = message.Content };
            }

            // For now, convert multimodal to text description
            var textContent = new StringBuilder();
            var hasImages = false;

            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                {
                    textContent.Append(text.Text);
                }
                else if (content is ImageContent)
                {
                    hasImages = true;
                    textContent.Append(" [Image] ");
                }
            }

            if (hasImages)
            {
                // Log warning or throw exception based on requirements
                System.Console.WriteLine("Warning: DeepSeek doesn't currently support image inputs. Images will be ignored.");
            }

            return new { role = message.Role.ToDescription(), content = textContent.ToString() };
        }

        #endregion
    }
}