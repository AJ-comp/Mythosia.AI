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

namespace Mythosia.AI.Services.Perplexity
{
    public partial class SonarService : AIService
    {
        public override AIProvider Provider => AIProvider.Perplexity;

        protected override uint GetModelMaxOutputTokens()
        {
            return 8192;
        }

        public SonarService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.perplexity.ai/", httpClient)
        {
            Model = AIModel.PerplexitySonar.ToDescription();
            MaxTokens = 4096;
            Temperature = 0.7f;
            AddNewChat(new ChatBlock());
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            if (StatelessMode)
            {
                return await ProcessStatelessRequestAsync(message);
            }

            Stream = false;
            ActivateChat.Messages.Add(message);

            var request = CreateMessageRequest();
            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException(
                    $"API request failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? errorContent : response.ReasonPhrase)}",
                    errorContent);
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = ExtractResponseContent(responseContent);

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
            return result;
        }

        private async Task<string> ProcessStatelessRequestAsync(Message message)
        {
            var tempChat = new ChatBlock
            {
                SystemMessage = ActivateChat.SystemMessage
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
                    throw new AIServiceException(
                        $"API request failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? errorContent : response.ReasonPhrase)}",
                        errorContent);
                }
            }
            finally
            {
                ActivateChat = backup;
            }
        }

        #endregion

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
            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForSonar(message));
            }

            // Build the request with Perplexity-specific parameters
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["top_p"] = TopP,
                ["max_tokens"] = GetEffectiveMaxTokens(),
                ["stream"] = Stream,
                ["frequency_penalty"] = Math.Max(1.0f, FrequencyPenalty),  // Perplexity recommends > 1.0
                ["presence_penalty"] = PresencePenalty
            };

            // Add search-specific parameters if using search-enabled models
            if (IsSearchEnabledModel())
            {
                requestBody["search_domain_filter"] = new string[] { };  // Empty = search all domains
                requestBody["return_citations"] = true;
                requestBody["search_recency_filter"] = "month";  // Options: day, week, month, year
            }

            return requestBody;
        }

        private bool IsSearchEnabledModel()
        {
            // Perplexity's online models have search capabilities
            return Model.Contains("sonar") ||
                   Model.Contains("online");
        }

        private object ConvertMessageForSonar(Message message)
        {
            // Perplexity currently supports text-only through their API
            if (!message.HasMultimodalContent)
            {
                return new { role = message.Role.ToDescription(), content = message.Content };
            }

            // Convert multimodal to text with descriptions
            var textContent = new StringBuilder();
            var hasNonTextContent = false;

            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                {
                    textContent.Append(text.Text);
                }
                else if (content is ImageContent)
                {
                    hasNonTextContent = true;
                    textContent.Append(" [Image content - not supported by Perplexity API] ");
                }
                else
                {
                    hasNonTextContent = true;
                    textContent.Append($" [{content.Type} content - not supported] ");
                }
            }

            if (hasNonTextContent)
            {
                Console.WriteLine("Warning: Perplexity Sonar currently only supports text inputs. Non-text content will be ignored.");
            }

            return new { role = message.Role.ToDescription(), content = textContent.ToString().Trim() };
        }

        #endregion

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            // Perplexity uses similar tokenization to GPT models
            var encoding = TikToken.EncodingForModel("gpt-4");

            var allMessagesBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                allMessagesBuilder.Append(ActivateChat.SystemMessage).Append('\n');
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

        #region Perplexity-Specific Features

        /// <summary>
        /// Perplexity doesn't support multimodal inputs
        /// </summary>
        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            Console.WriteLine("Warning: Perplexity Sonar doesn't support image inputs. Processing text only.");
            return await GetCompletionAsync(prompt);
        }

        /// <summary>
        /// Perplexity doesn't support image generation
        /// </summary>
        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Perplexity Sonar", "Image Generation");
        }

        /// <summary>
        /// Perplexity doesn't support image generation
        /// </summary>
        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
        {
            throw new MultimodalNotSupportedException("Perplexity Sonar", "Image Generation");
        }

        /// <summary>
        /// Switches to Sonar Pro model for enhanced capabilities
        /// </summary>
        public SonarService UseSonarPro()
        {
            ChangeModel(AIModel.PerplexitySonarPro);
            return this;
        }

        /// <summary>
        /// Switches to Sonar Reasoning model for complex reasoning tasks
        /// </summary>
        public SonarService UseSonarReasoning()
        {
            ChangeModel(AIModel.PerplexitySonarReasoning);
            return this;
        }

        /// <summary>
        /// Configures search parameters for the service
        /// </summary>
        public SonarService WithSearchParameters(
            bool returnCitations = true,
            string recencyFilter = "month",
            params string[] domainFilter)
        {
            // This would require extending ChatBlock to support Perplexity-specific parameters
            // For now, these parameters need to be passed to GetCompletionWithSearchAsync
            return this;
        }

        #endregion
    }
}