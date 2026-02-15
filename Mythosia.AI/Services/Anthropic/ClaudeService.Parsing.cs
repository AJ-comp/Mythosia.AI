using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService
    {
        #region Request Body Building

        private object BuildRequestBody()
        {
            var messagesList = new List<object>();

            // Convert messages to Claude format
            foreach (var message in GetLatestMessagesWithFunctionFallback())
            {
                messagesList.Add(ConvertMessageForClaude(message));
            }

            // Dictionary 사용으로 null/empty 체크 가능
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["stream"] = Stream,
                ["max_tokens"] = GetEffectiveMaxTokens()
            };

            ApplySystemMessage(requestBody);
            ApplyThinkingConfig(requestBody);

            return requestBody;
        }

        private object ConvertMessageForClaude(Message message)
        {
            if (!message.HasMultimodalContent)
            {
                return new { role = message.Role.ToDescription(), content = message.Content };
            }

            // Claude expects content as an array for multimodal
            var contentArray = new List<object>();
            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent)
                {
                    contentArray.Add(new { type = "text", text = textContent.Text });
                }
                else if (content is ImageContent imageContent)
                {
                    contentArray.Add(ConvertImageForClaude(imageContent));
                }
            }

            return new
            {
                role = message.Role.ToDescription(),
                content = contentArray
            };
        }

        private object ConvertImageForClaude(ImageContent imageContent)
        {
            if (!string.IsNullOrEmpty(imageContent.Url))
            {
                // Claude doesn't support direct URLs, need to download and convert
                throw new NotSupportedException("Claude API requires base64 encoded images. Please download the image and provide as byte array.");
            }

            if (imageContent.Data == null)
            {
                throw new ArgumentException("Image content must have either Data or Url");
            }

            return new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = imageContent.MimeType ?? DefaultImageMimeType,
                    data = Convert.ToBase64String(imageContent.Data)
                }
            };
        }

        #endregion

        #region Response Parsing

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var content = responseObj.GetProperty("content");

                if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0)
                    return string.Empty;

                var textParts = new StringBuilder();
                var thinkingParts = new StringBuilder();

                foreach (var block in content.EnumerateArray())
                {
                    if (!block.TryGetProperty("type", out var typeElem)) continue;
                    var blockType = typeElem.GetString();

                    if (blockType == "thinking" && block.TryGetProperty("thinking", out var thinkingElem))
                    {
                        thinkingParts.Append(thinkingElem.GetString());
                    }
                    else if (blockType == "text" && block.TryGetProperty("text", out var textElem))
                    {
                        textParts.Append(textElem.GetString());
                    }
                }

                LastThinkingContent = thinkingParts.Length > 0 ? thinkingParts.ToString() : null;

                return textParts.ToString();
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse Claude response", ex);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);

                if (jsonElement.TryGetProperty("type", out var typeElement) &&
                    typeElement.GetString() == "content_block_delta" &&
                    jsonElement.TryGetProperty("delta", out var deltaElement) &&
                    deltaElement.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region Helper Classes

        private class TokenCountResponse
        {
            [JsonPropertyName("input_tokens")]
            public uint InputTokens { get; set; }

            [JsonPropertyName("cache_creation_input_tokens")]
            public uint? CacheCreationInputTokens { get; set; }

            [JsonPropertyName("cache_read_input_tokens")]
            public uint? CacheReadInputTokens { get; set; }
        }

        #endregion
    }
}