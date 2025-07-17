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

namespace Mythosia.AI.Services
{
    public class DeepSeekService : AIService
    {
        public override AIProvider Provider => AIProvider.DeepSeek;

        public DeepSeekService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.deepseek.com/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.DeepSeekChat)
            {
                MaxTokens = 8000
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
            request.Headers.Add("Accept", "application/json");

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
                messagesList.Add(ConvertMessageForDeepSeek(message));
            }

            var requestBody = new
            {
                model = ActivateChat.Model,
                messages = messagesList,
                temperature = ActivateChat.Temperature,
                top_p = ActivateChat.TopP,
                max_tokens = (int)ActivateChat.MaxTokens,
                stream = ActivateChat.Stream,
                frequency_penalty = ActivateChat.FrequencyPenalty,
                presence_penalty = ActivateChat.PresencePenalty,
                stop = (string?)null
            };

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
                Console.WriteLine("Warning: DeepSeek doesn't currently support image inputs. Images will be ignored.");
            }

            return new { role = message.Role.ToDescription(), content = textContent.ToString() };
        }

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse DeepSeek response", ex);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
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
            // DeepSeek uses similar tokenization to GPT models
            var encoding = TikToken.EncodingForModel("gpt-4");

            var allMessagesBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                allMessagesBuilder.Append(ActivateChat.SystemMessage).Append('\n');
            }

            foreach (var message in ActivateChat.GetLatestMessages())
            {
                allMessagesBuilder.Append(message.Role).Append('\n');
                allMessagesBuilder.Append(message.GetDisplayText()).Append('\n');
            }

            var tokens = encoding.Encode(allMessagesBuilder.ToString());
            return await Task.FromResult((uint)tokens.Count);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var encoding = TikToken.EncodingForModel("gpt-4");
            var tokens = encoding.Encode(prompt);
            return await Task.FromResult((uint)tokens.Count);
        }

        #endregion

        #region DeepSeek-Specific Features

        /// <summary>
        /// DeepSeek doesn't currently support image inputs
        /// </summary>
        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            Console.WriteLine("Warning: DeepSeek doesn't support image inputs. Processing text only.");
            return await GetCompletionAsync(prompt);
        }

        /// <summary>
        /// DeepSeek doesn't support image generation
        /// </summary>
        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("DeepSeek", "Image Generation");
        }

        /// <summary>
        /// DeepSeek doesn't support image generation
        /// </summary>
        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("DeepSeek", "Image Generation");
        }

        /// <summary>
        /// Switches to DeepSeek Reasoner model for complex reasoning tasks
        /// </summary>
        public DeepSeekService UseReasonerModel()
        {
            ActivateChat.ChangeModel(AIModel.DeepSeekReasoner);
            return this;
        }

        /// <summary>
        /// Sets DeepSeek-specific parameters for code generation
        /// </summary>
        public DeepSeekService WithCodeGenerationMode(string language = "python")
        {
            var systemPrompt = $"You are an expert {language} programmer. Generate clean, efficient, and well-documented code.";
            ActivateChat.SystemMessage = systemPrompt;
            ActivateChat.Temperature = 0.1f; // Lower temperature for code generation
            return this;
        }

        /// <summary>
        /// Optimizes settings for mathematical reasoning
        /// </summary>
        public DeepSeekService WithMathMode()
        {
            ActivateChat.SystemMessage = "You are a mathematics expert. Solve problems step by step, showing all work clearly.";
            ActivateChat.Temperature = 0.2f;
            return this;
        }

        /// <summary>
        /// Gets completion with Chain of Thought prompting
        /// </summary>
        public async Task<string> GetCompletionWithCoTAsync(string prompt)
        {
            var cotPrompt = $"{prompt}\n\nPlease think step by step and show your reasoning process.";
            return await GetCompletionAsync(cotPrompt);
        }

        #endregion

        #region Error Handling

        public override async Task<string> GetCompletionAsync(Message message)
        {
            try
            {
                return await base.GetCompletionAsync(message);
            }
            catch (HttpRequestException ex)
            {
                // Handle DeepSeek-specific errors
                if (ex.Message.Contains("rate limit"))
                {
                    throw new RateLimitExceededException("DeepSeek rate limit exceeded. Please try again later.", TimeSpan.FromSeconds(60));
                }
                throw;
            }
        }

        #endregion
    }
}