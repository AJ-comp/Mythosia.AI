using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Exceptions;
using TiktokenSharp;

namespace Mythosia.AI.Services
{
    public class GeminiService : AIService
    {
        public override AIProvider Provider => AIProvider.Google;

        public GeminiService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://generativelanguage.googleapis.com/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.Gemini2_5Pro)
            {
                Temperature = 1.0f,
                TopP = 0.8f,
                MaxTokens = 2048
            };
            AddNewChat(chatBlock);
        }

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var modelName = ActivateChat.Model;
            var endpoint = ActivateChat.Stream
                ? $"v1beta/models/{modelName}:streamGenerateContent?alt=sse&key={ApiKey}"
                : $"v1beta/models/{modelName}:generateContent?key={ApiKey}";

            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            return new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
        }

        private object BuildRequestBody()
        {
            var contentsList = new List<object>();

            // Add system message as first user message if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                contentsList.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = ActivateChat.SystemMessage } }
                });

                // Add a model response to balance the conversation
                contentsList.Add(new
                {
                    role = "model",
                    parts = new[] { new { text = "Understood. I'll follow these instructions." } }
                });
            }

            // Add conversation messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                contentsList.Add(ConvertMessageForGemini(message));
            }

            var generationConfig = new
            {
                temperature = ActivateChat.Temperature,
                topP = ActivateChat.TopP,
                topK = 40,
                maxOutputTokens = (int)ActivateChat.MaxTokens,
                candidateCount = 1
            };

            return new
            {
                contents = contentsList,
                generationConfig = generationConfig,
                safetySettings = GetSafetySettings()
            };
        }

        private object ConvertMessageForGemini(Message message)
        {
            // Gemini uses "model" instead of "assistant"
            var role = message.Role == ActorRole.Assistant ? "model" : "user";

            if (!message.HasMultimodalContent)
            {
                return new
                {
                    role = role,
                    parts = new[] { new { text = message.Content } }
                };
            }

            // Handle multimodal content
            var parts = new List<object>();
            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent)
                {
                    parts.Add(new { text = textContent.Text });
                }
                else if (content is ImageContent imageContent)
                {
                    parts.Add(ConvertImageForGemini(imageContent));
                }
            }

            return new
            {
                role = role,
                parts = parts
            };
        }

        private object ConvertImageForGemini(ImageContent imageContent)
        {
            if (imageContent.Data != null)
            {
                return new
                {
                    inline_data = new
                    {
                        mime_type = imageContent.MimeType ?? "image/jpeg",
                        data = Convert.ToBase64String(imageContent.Data)
                    }
                };
            }
            else if (!string.IsNullOrEmpty(imageContent.Url))
            {
                // Gemini doesn't support URLs directly, need to download
                throw new NotSupportedException("Gemini API requires base64 encoded images. Please download the image and provide as byte array.");
            }

            throw new ArgumentException("Image content must have either Data or Url");
        }

        private object[] GetSafetySettings()
        {
            return new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" }
            };
        }

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var partsArr) &&
                        partsArr.ValueKind == JsonValueKind.Array)
                    {
                        var textParts = new StringBuilder();
                        foreach (var part in partsArr.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElem))
                            {
                                textParts.Append(textElem.GetString());
                            }
                        }
                        return textParts.ToString();
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse Gemini response", ex);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var partsArr) &&
                        partsArr.ValueKind == JsonValueKind.Array)
                    {
                        var textParts = new StringBuilder();
                        foreach (var part in partsArr.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElem))
                            {
                                textParts.Append(textElem.GetString());
                            }
                        }
                        return textParts.ToString();
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
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                }
            };

            return await GetTokenCountFromAPI(requestBody);
        }

        private object BuildTokenCountRequestBody()
        {
            var contentsList = new List<object>();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                contentsList.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = ActivateChat.SystemMessage } }
                });
            }

            foreach (var message in ActivateChat.GetLatestMessages())
            {
                contentsList.Add(ConvertMessageForGemini(message));
            }

            return new { contents = contentsList };
        }

        private async Task<uint> GetTokenCountFromAPI(object requestBody)
        {
            var modelName = ActivateChat.Model;
            var endpoint = $"v1beta/models/{modelName}:countTokens?key={ApiKey}";

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Gemini token count request failed: {response.ReasonPhrase}", errorMsg);
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);

            if (doc.RootElement.TryGetProperty("totalTokens", out var totalTokensElem))
            {
                return (uint)totalTokensElem.GetInt32();
            }

            return 0;
        }

        #endregion

        #region Gemini-Specific Features

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            // Ensure we're using a vision-capable model
            if (!ActivateChat.Model.Contains("vision") && !ActivateChat.Model.Contains("1.5"))
            {
                ActivateChat.ChangeModel(AIModel.GeminiProVision);
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        /// <summary>
        /// Gemini doesn't support direct image generation
        /// </summary>
        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Gemini", "Image Generation");
        }

        /// <summary>
        /// Gemini doesn't support direct image generation
        /// </summary>
        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Gemini", "Image Generation");
        }

        /// <summary>
        /// Downloads an image from URL for Gemini processing
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

        /// <summary>
        /// Sets Gemini-specific generation parameters
        /// </summary>
        public GeminiService WithGeminiParameters(int? topK = null, int? candidateCount = null)
        {
            // These would require extending ChatBlock for Gemini-specific parameters
            return this;
        }

        /// <summary>
        /// Configures safety settings for Gemini
        /// </summary>
        public GeminiService WithSafetyThreshold(string threshold = "BLOCK_MEDIUM_AND_ABOVE")
        {
            // This would allow customizing safety settings
            return this;
        }

        #endregion
    }
}