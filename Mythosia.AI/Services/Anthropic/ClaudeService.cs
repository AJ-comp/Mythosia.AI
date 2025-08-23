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

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService : AIService
    {
        public override AIProvider Provider => AIProvider.Anthropic;

        public ClaudeService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.anthropic.com/v1/", httpClient)
        {
            var chatBlock = new ChatBlock(AIModel.Claude3_5Haiku241022)
            {
                MaxTokens = 8192,
                Temperature = 0.7f
            };
            AddNewChat(chatBlock);
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            // Check if we should use functions
            bool useFunctions = ActivateChat.Functions?.Count > 0
                               && ActivateChat.EnableFunctions
                               && !FunctionsDisabled;

            if (StatelessMode)
            {
                return await ProcessStatelessRequestAsync(message, useFunctions);
            }

            ActivateChat.Stream = false;
            ActivateChat.Messages.Add(message);

            // Create appropriate request based on function availability
            var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            // Handle function calls if present
            if (useFunctions)
            {
                var (content, functionCall) = ExtractFunctionCall(responseContent);

                if (functionCall != null)
                {
                    // Execute function
                    var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);

                    // Add to conversation
                    ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
                    {
                        Metadata = new Dictionary<string, object>
                        {
                            ["function_name"] = functionCall.Name,
                            ["arguments"] = functionCall.Arguments
                        }
                    });

                    // Get AI's final response based on function result
                    return await GetCompletionAsync("Based on the function result, please provide a helpful response.");
                }

                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
                return content;
            }
            else
            {
                // Regular response without functions
                var result = ExtractResponseContent(responseContent);
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
                return result;
            }
        }

        private async Task<string> ProcessStatelessRequestAsync(Message message, bool useFunctions)
        {
            var tempChat = new ChatBlock(ActivateChat.Model)
            {
                SystemMessage = ActivateChat.SystemMessage,
                Temperature = ActivateChat.Temperature,
                TopP = ActivateChat.TopP,
                MaxTokens = ActivateChat.MaxTokens,
                Functions = useFunctions ? ActivateChat.Functions : new List<FunctionDefinition>(),
                EnableFunctions = useFunctions
            };
            tempChat.Messages.Add(message);

            var backup = ActivateChat;
            ActivateChat = tempChat;

            try
            {
                var request = useFunctions
                    ? CreateFunctionMessageRequest()
                    : CreateMessageRequest();

                var response = await HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (useFunctions)
                    {
                        var (content, functionCall) = ExtractFunctionCall(responseContent);
                        if (functionCall != null)
                        {
                            var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);
                            return $"Function result: {result}";
                        }
                        return content;
                    }

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

        #region Request Creation

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

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            // Ensure we're using a vision-capable Claude model
            if (!ActivateChat.Model.Contains("claude-3") &&
                !ActivateChat.Model.Contains("claude-4") &&
                !ActivateChat.Model.Contains("opus-4"))
            {
                ActivateChat.ChangeModel(AIModel.Claude3_5Sonnet241022);
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        #endregion

        #region Claude-Specific Features

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
        /// Sets temperature for different use cases
        /// </summary>
        public ClaudeService WithTemperaturePreset(TemperaturePreset preset)
        {
            ActivateChat.Temperature = preset switch
            {
                TemperaturePreset.Deterministic => 0.0f,
                TemperaturePreset.Analytical => 0.1f,
                TemperaturePreset.Factual => 0.3f,
                TemperaturePreset.Balanced => 0.7f,
                TemperaturePreset.Creative => 1.0f,
                TemperaturePreset.VeryCreative => 1.5f,
                _ => 0.7f
            };
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
    }

    /// <summary>
    /// Temperature presets
    /// </summary>
    public enum TemperaturePreset
    {
        Deterministic,
        Analytical,
        Factual,
        Balanced,
        Creative,
        VeryCreative
    }
}