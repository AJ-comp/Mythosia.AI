using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService : AIService
    {
        public override AIProvider Provider => AIProvider.DeepSeek;

        protected override uint GetModelMaxOutputTokens()
        {
            return 8192;
        }

        public DeepSeekService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.deepseek.com/", httpClient)
        {
            Model = AIModel.DeepSeekChat.ToDescription();
            MaxTokens = 8000;
            AddNewChat(new ChatBlock());
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            // DeepSeek doesn't support function calling yet
            if (StatelessMode)
            {
                return await ProcessStatelessRequestAsync(message);
            }

            Stream = false;
            ActivateChat.Messages.Add(message);

            var request = CreateMessageRequest();

            try
            {
                var response = await HttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();

                    // Handle DeepSeek-specific errors
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        throw new RateLimitExceededException(
                            "DeepSeek rate limit exceeded. Please try again later.",
                            TimeSpan.FromSeconds(60));
                    }

                    throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var result = ExtractResponseContent(responseContent);

                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
                return result;
            }
            catch (HttpRequestException ex)
            {
                // Handle DeepSeek-specific errors
                if (ex.Message.Contains("rate limit"))
                {
                    throw new RateLimitExceededException(
                        "DeepSeek rate limit exceeded. Please try again later.",
                        TimeSpan.FromSeconds(60));
                }
                throw;
            }
        }

        private async Task<string> ProcessStatelessRequestAsync(Message message)
        {
            var tempChat = new ChatBlock
            {
                SystemMessage = SystemMessage
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;

            try
            {
                var request = CreateMessageRequest();
                var response = await HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return ExtractResponseContent(responseContent);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        #endregion

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            // DeepSeek uses similar tokenization to GPT models
            var encoding = TikToken.EncodingForModel("gpt-4");

            var allMessagesBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                allMessagesBuilder.Append(SystemMessage).Append('\n');
            }

            foreach (var message in GetLatestMessages())
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
            ChangeModel(AIModel.DeepSeekReasoner);
            return this;
        }

        /// <summary>
        /// Sets DeepSeek-specific parameters for code generation
        /// </summary>
        public DeepSeekService WithCodeGenerationMode(string language = "python")
        {
            var systemPrompt = $"You are an expert {language} programmer. Generate clean, efficient, and well-documented code.";
            SystemMessage = systemPrompt;
            Temperature = 0.1f; // Lower temperature for code generation
            return this;
        }

        /// <summary>
        /// Optimizes settings for mathematical reasoning
        /// </summary>
        public DeepSeekService WithMathMode()
        {
            SystemMessage = "You are a mathematics expert. Solve problems step by step, showing all work clearly.";
            Temperature = 0.2f;
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
    }
}