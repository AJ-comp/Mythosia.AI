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
    public class SonarService : AIService
    {
        public override AIProvider Provider => AIProvider.Perplexity;

        public SonarService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.perplexity.ai/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.PerplexitySonar)
            {
                MaxTokens = 4096,
                Temperature = 0.7f
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
                messagesList.Add(ConvertMessageForSonar(message));
            }

            // Build the request with Perplexity-specific parameters
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["messages"] = messagesList,
                ["temperature"] = ActivateChat.Temperature,
                ["top_p"] = ActivateChat.TopP,
                ["max_tokens"] = ActivateChat.MaxTokens,
                ["stream"] = ActivateChat.Stream,
                ["frequency_penalty"] = Math.Max(1.0f, ActivateChat.FrequencyPenalty),  // Perplexity recommends > 1.0
                ["presence_penalty"] = ActivateChat.PresencePenalty
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
            return ActivateChat.Model.Contains("sonar") ||
                   ActivateChat.Model.Contains("online");
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
                throw new AIServiceException("Failed to parse Perplexity response", ex);
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
            // Perplexity uses similar tokenization to GPT models
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
            ActivateChat.ChangeModel(AIModel.PerplexitySonarPro);
            return this;
        }

        /// <summary>
        /// Switches to Sonar Reasoning model for complex reasoning tasks
        /// </summary>
        public SonarService UseSonarReasoning()
        {
            ActivateChat.ChangeModel(AIModel.PerplexitySonarReasoning);
            return this;
        }

        /// <summary>
        /// Gets a completion with web search and citations
        /// </summary>
        public async Task<SonarSearchResponse> GetCompletionWithSearchAsync(
            string prompt,
            string[]? domainFilter = null,
            string recencyFilter = "month")
        {
            // Create a temporary chat block with search parameters
            var originalModel = ActivateChat.Model;
            var messagesList = new List<object>();

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

            messagesList.Add(new { role = "user", content = prompt });

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = originalModel,
                ["messages"] = messagesList,
                ["temperature"] = ActivateChat.Temperature,
                ["max_tokens"] = ActivateChat.MaxTokens,
                ["return_citations"] = true,
                ["search_recency_filter"] = recencyFilter
            };

            if (domainFilter != null && domainFilter.Length > 0)
            {
                requestBody["search_domain_filter"] = domainFilter;
            }

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            var response = await HttpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return ParseSearchResponse(responseContent);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Search request failed: {response.ReasonPhrase}", error);
            }
        }

        private SonarSearchResponse ParseSearchResponse(string responseContent)
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var result = new SonarSearchResponse();

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var content))
                    {
                        result.Content = content.GetString() ?? string.Empty;
                    }

                    if (message.TryGetProperty("citations", out var citations))
                    {
                        result.Citations = ParseCitations(citations);
                    }
                }
            }

            return result;
        }

        private List<Citation> ParseCitations(JsonElement citationsElement)
        {
            var citations = new List<Citation>();

            if (citationsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var citation in citationsElement.EnumerateArray())
                {
                    var cit = new Citation();

                    if (citation.TryGetProperty("url", out var url))
                        cit.Url = url.GetString() ?? string.Empty;

                    if (citation.TryGetProperty("title", out var title))
                        cit.Title = title.GetString() ?? string.Empty;

                    if (citation.TryGetProperty("snippet", out var snippet))
                        cit.Snippet = snippet.GetString() ?? string.Empty;

                    citations.Add(cit);
                }
            }

            return citations;
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

        #region Helper Classes

        public class SonarSearchResponse
        {
            public string Content { get; set; } = string.Empty;
            public List<Citation> Citations { get; set; } = new List<Citation>();
        }

        public class Citation
        {
            public string Url { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Snippet { get; set; } = string.Empty;
        }

        #endregion
    }
}