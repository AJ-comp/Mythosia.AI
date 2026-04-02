using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Protocols;
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
    public partial class PerplexityService : AIService
    {
        public override string Provider => nameof(AIProvider.Perplexity);

        protected override uint GetModelMaxOutputTokens()
        {
            return 8192;
        }

        public PerplexityService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.perplexity.ai/", httpClient)
        {
            Model = AIModels.Perplexity.Sonar;
            MaxTokens = 4096;
            Temperature = 0.7f;
        }

        /// <summary>
        /// Creates a PerplexityService with a specific model.
        /// </summary>
        public PerplexityService(string apiKey, string model, HttpClient httpClient)
            : this(apiKey, httpClient)
        {
            ChangeModel(model);
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            Stream = false;

            if (StatelessMode)
            {
                return await ProcessStatelessRequestAsync(message);
            }

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

        private static readonly ChatCompletionsProtocol _protocol = ChatCompletionsProtocol.Instance;

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var systemMsg = GetEffectiveSystemMessageWithRequestContext();

            var p = new ProtocolRequestParams
            {
                Model = Model,
                Messages = GetLatestMessages(),
                SystemMessage = systemMsg,
                Temperature = Temperature,
                TopP = TopP,
                FrequencyPenalty = Math.Max(1.0f, FrequencyPenalty),  // Perplexity recommends > 1.0
                PresencePenalty = PresencePenalty,
                MaxTokens = GetEffectiveMaxTokens(),
                Stream = Stream,
                StructuredOutputSchemaJson = _structuredOutputSchemaJson
            };

            // Add search-specific parameters if using search-enabled models
            if (IsSearchEnabledModel())
            {
                p.ExtraParameters = new Dictionary<string, object>
                {
                    ["search_domain_filter"] = new string[] { },
                    ["return_citations"] = true,
                    ["search_recency_filter"] = "month"
                };
            }

            var body = _protocol.BuildRequestBody(p, ConvertMessageForSonar);
            return _protocol.CreateRequest(ApiKey, body);
        }

        private bool IsSearchEnabledModel()
        {
            // Perplexity's online models have search capabilities
            return Model.Contains("sonar") ||
                   Model.Contains("online");
        }

        private object ConvertMessageForSonar(Message message)
        {
            var role = message.Role.ToDescription();

            // Perplexity currently supports text-only through their API
            if (!message.HasMultimodalContent)
            {
                return new { role, content = message.Content };
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

            return new { role, content = textContent.ToString().Trim() };
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
        public PerplexityService UseSonarPro()
        {
            ChangeModel(AIModels.Perplexity.SonarPro);
            return this;
        }

        /// <summary>
        /// Switches to Sonar Reasoning model for complex reasoning tasks
        /// </summary>
        public PerplexityService UseSonarReasoning()
        {
            ChangeModel(AIModels.Perplexity.SonarReasoning);
            return this;
        }

        /// <summary>
        /// Configures search parameters for the service
        /// </summary>
        public PerplexityService WithSearchParameters(
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