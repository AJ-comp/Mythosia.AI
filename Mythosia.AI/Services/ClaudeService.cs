using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Exceptions;
using TiktokenSharp;

namespace Mythosia.AI.Services
{
    public class ClaudeService : AIService
    {
        public override AIProvider Provider => AIProvider.Anthropic;

        public ClaudeService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.anthropic.com/v1/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.Claude3_5Sonnet241022)
            {
                MaxTokens = 8192
            };
            AddNewChat(chatBlock);
        }

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = content
            };

            // Claude API headers
            request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            return request;
        }

        private object BuildRequestBody()
        {
            var messagesList = new List<object>();

            // Convert messages to Claude format
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForClaude(message));
            }

            var requestBody = new
            {
                model = ActivateChat.Model,
                system = ActivateChat.SystemMessage,
                messages = messagesList,
                top_p = ActivateChat.TopP,
                temperature = ActivateChat.Temperature,
                stream = ActivateChat.Stream,
                max_tokens = ActivateChat.MaxTokens
            };

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
            if (imageContent.Data != null)
            {
                return new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = imageContent.MimeType ?? "image/jpeg",
                        data = Convert.ToBase64String(imageContent.Data)
                    }
                };
            }
            else if (!string.IsNullOrEmpty(imageContent.Url))
            {
                // Claude doesn't support direct URLs, need to download and convert
                throw new NotSupportedException("Claude API requires base64 encoded images. Please download the image and provide as byte array.");
            }

            throw new ArgumentException("Image content must have either Data or Url");
        }

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var content = responseObj.GetProperty("content");

                if (content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
                {
                    return content[0].GetProperty("text").GetString() ?? string.Empty;
                }

                return string.Empty;
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

                if (jsonElement.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();

                    if (type == "content_block_delta" &&
                        jsonElement.TryGetProperty("delta", out var deltaElement) &&
                        deltaElement.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var requestBody = BuildTokenCountRequestBody();
            return await GetTokenCountFromAPI(requestBody);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var messagesList = new List<object>
            {
                new { role = ActorRole.User.ToDescription(), content = prompt }
            };

            var requestBody = new
            {
                model = ActivateChat.Model,
                messages = messagesList
            };

            return await GetTokenCountFromAPI(requestBody);
        }

        private object BuildTokenCountRequestBody()
        {
            var messagesList = new List<object>();

            foreach (var message in ActivateChat.GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForClaude(message));
            }

            return new
            {
                model = ActivateChat.Model,
                system = ActivateChat.SystemMessage,
                messages = messagesList
            };
        }

        private async Task<uint> GetTokenCountFromAPI(object requestBody)
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "messages/count_tokens")
            {
                Content = content
            };

            request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Add("anthropic-beta", "token-counting-2024-11-01");

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Token count request failed: {response.StatusCode}", errorJson);
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenCountResponse>(jsonString);

            return tokenResponse?.InputTokens ?? 0;
        }

        #endregion

        #region Claude-Specific Features

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            // Ensure we're using a vision-capable Claude 3 model
            if (!ActivateChat.Model.Contains("claude-3"))
            {
                ActivateChat.ChangeModel(AIModel.Claude3_5Sonnet241022);
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        /// <summary>
        /// Claude doesn't support image generation
        /// </summary>
        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Claude", "Image Generation");
        }

        /// <summary>
        /// Claude doesn't support image generation
        /// </summary>
        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Claude", "Image Generation");
        }

        #endregion

        #region Claude-Specific Methods

        /// <summary>
        /// Sets Claude-specific parameters
        /// </summary>
        public ClaudeService WithClaudeParameters(int? topK = null)
        {
            // Claude supports top_k parameter
            // This would require extending ChatBlock to support Claude-specific parameters
            return this;
        }

        /// <summary>
        /// Enables or disables Claude's constitutional AI features
        /// </summary>
        public ClaudeService WithConstitutionalAI(bool enabled = true)
        {
            // Placeholder for future constitutional AI features
            return this;
        }

        /// <summary>
        /// Downloads an image from URL and converts to base64 for Claude
        /// </summary>
        public async Task<Message> CreateMessageWithImageUrl(string prompt, string imageUrl)
        {
            using var imageResponse = await HttpClient.GetAsync(imageUrl);
            if (!imageResponse.IsSuccessStatusCode)
            {
                throw new AIServiceException($"Failed to download image from {imageUrl}");
            }

            var imageData = await imageResponse.Content.ReadAsByteArrayAsync();
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

            return new Message(ActorRole.User, new List<MessageContent>
            {
                new TextContent(prompt),
                new ImageContent(imageData, contentType)
            });
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