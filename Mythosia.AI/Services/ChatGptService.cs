using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TiktokenSharp;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Builders;

namespace Mythosia.AI.Services
{
    public class ChatGptService : AIService
    {
        public override AIProvider Provider => AIProvider.OpenAI;

        public ChatGptService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.openai.com/v1/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.Gpt4oMini)
            {
                MaxTokens = 16000
            };
            AddNewChat(chatBlock);
        }

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };

            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private object BuildRequestBody()
        {
            var messagesList = new List<object>();

            // Add system message if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

            // Add conversation messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForOpenAI(message));
            }

            return new
            {
                model = ActivateChat.Model,
                messages = messagesList,
                top_p = ActivateChat.TopP,
                temperature = ActivateChat.Temperature,
                frequency_penalty = ActivateChat.FrequencyPenalty,
                presence_penalty = ActivateChat.PresencePenalty,
                max_tokens = ActivateChat.MaxTokens,
                stream = ActivateChat.Stream
            };
        }

        private object ConvertMessageForOpenAI(Message message)
        {
            if (!message.HasMultimodalContent)
            {
                return new { role = message.Role.ToDescription(), content = message.Content };
            }

            // Handle multimodal content
            var contentList = new List<object>();
            foreach (var content in message.Contents)
            {
                contentList.Add(content.ToRequestFormat(Provider));
            }

            return new
            {
                role = message.Role.ToDescription(),
                content = contentList
            };
        }

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
                return responseObj.GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse OpenAI response", ex);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
                if (jsonElement.GetProperty("choices")[0]
                    .GetProperty("delta")
                    .TryGetProperty("content", out JsonElement contentElement))
                {
                    return contentElement.GetString() ?? string.Empty;
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            var currentModel = ActivateChat.Model;

            // Check if current model supports vision
            bool supportsVision = currentModel.Contains("gpt-4o") ||
                                 currentModel.Contains("gpt-4-turbo") ||
                                 currentModel.Contains("vision");

            if (!supportsVision)
            {
                // Switch to a vision-capable model
                // Use gpt-4o instead of deprecated gpt-4-vision-preview
                if (currentModel.Contains("mini"))
                {
                    // If using mini model, switch to full gpt-4o
                    ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                }
                else
                {
                    // For other models, switch to gpt-4o
                    ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                }

                Console.WriteLine($"[GetCompletionWithImageAsync] Switched from {currentModel} to {ActivateChat.Model} for vision support");
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var encoding = TikToken.EncodingForModel("gpt-4o");

            var allMessagesBuilder = new StringBuilder();

            // Add system message
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                allMessagesBuilder.Append(ActivateChat.SystemMessage).Append('\n');
            }

            // Add all messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                if (message.HasMultimodalContent)
                {
                    foreach (var content in message.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            allMessagesBuilder.Append(textContent.Text).Append('\n');
                        }
                        else if (content is ImageContent)
                        {
                            // Images consume fixed tokens based on detail level
                            allMessagesBuilder.Append("[IMAGE]").Append('\n');
                        }
                    }
                }
                else
                {
                    allMessagesBuilder.Append(message.Role).Append('\n')
                                      .Append(message.Content).Append('\n');
                }
            }

            var textTokens = (uint)encoding.Encode(allMessagesBuilder.ToString()).Count;

            // Add image tokens
            var imageTokens = ActivateChat.Messages
                .SelectMany(m => m.Contents)
                .OfType<ImageContent>()
                .Sum(img => img.EstimateTokens());

            return await Task.FromResult(textTokens + (uint)imageTokens);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var encoding = TikToken.EncodingForModel("gpt-4o");
            return await Task.FromResult((uint)encoding.Encode(prompt).Count);
        }

        #endregion

        #region Image Generation

        public override async Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = 1,
                size = size,
                response_format = "b64_json"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var imageData = responseJson.GetProperty("data")[0].GetProperty("b64_json").GetString();

                if (string.IsNullOrEmpty(imageData))
                {
                    throw new AIServiceException("Image generation returned empty data");
                }

                return Convert.FromBase64String(imageData);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Image generation failed: {response.ReasonPhrase}", error);
            }
        }

        public override async Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            var requestBody = new
            {
                model = "dall-e-3",
                prompt = prompt,
                n = 1,
                size = size,
                response_format = "url"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "images/generations")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var imageUrl = responseJson.GetProperty("data")[0].GetProperty("url").GetString();

                return imageUrl ?? string.Empty;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Image generation failed: {response.ReasonPhrase}", error);
            }
        }

        #endregion

        #region Audio Features

        /// <summary>
        /// Generates speech audio from text using OpenAI's TTS model
        /// </summary>
        /// <param name="inputText">The text to convert to speech</param>
        /// <param name="voice">The voice to use (alloy, echo, fable, onyx, nova, shimmer)</param>
        /// <param name="model">The model to use (tts-1 or tts-1-hd)</param>
        /// <returns>Audio data as byte array</returns>
        public async Task<byte[]> GetSpeechAsync(string inputText, string voice = "alloy", string model = "tts-1")
        {
            var requestBody = new
            {
                model = model,
                voice = voice,
                input = inputText
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Speech generation failed: {response.ReasonPhrase}", error);
            }
        }

        /// <summary>
        /// Transcribes audio to text using OpenAI's Whisper model
        /// </summary>
        /// <param name="audioData">Audio data as byte array</param>
        /// <param name="fileName">File name with extension (e.g., "audio.mp3")</param>
        /// <param name="language">The language of the audio (optional)</param>
        /// <returns>Transcribed text</returns>
        public async Task<string> TranscribeAudioAsync(byte[] audioData, string fileName, string? language = null)
        {
            using var form = new MultipartFormDataContent();

            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
            form.Add(audioContent, "file", fileName);
            form.Add(new StringContent("whisper-1"), "model");

            if (!string.IsNullOrEmpty(language))
            {
                form.Add(new StringContent(language), "language");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
            {
                Content = form
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                return responseJson.GetProperty("text").GetString() ?? string.Empty;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Audio transcription failed: {response.ReasonPhrase}", error);
            }
        }

        #endregion

        #region OpenAI-Specific Features

        /// <summary>
        /// Fine-tunes the response with specific OpenAI parameters
        /// </summary>
        public ChatGptService WithOpenAIParameters(float? presencePenalty = null, float? frequencyPenalty = null, int? bestOf = null)
        {
            if (presencePenalty.HasValue)
            {
                // Presence penalty can be added to ChatBlock if needed
            }
            if (frequencyPenalty.HasValue)
            {
                ActivateChat.FrequencyPenalty = frequencyPenalty.Value;
            }
            return this;
        }

        /// <summary>
        /// Sets up for function calling (OpenAI specific feature)
        /// </summary>
        public ChatGptService WithFunctions(params object[] functions)
        {
            // This would require extending the ChatBlock to support functions
            // For now, this is a placeholder for future implementation
            throw new NotImplementedException("Function calling support will be added in a future version");
        }

        #endregion
    }
}