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

namespace Mythosia.AI.Services.Google
{
    public partial class GeminiService : AIService
    {
        public override AIProvider Provider => AIProvider.Google;

        /// <summary>
        /// Controls the thinking token budget for Gemini 2.5 models.
        /// -1: Dynamic (model decides automatically, default)
        /// 0: Disable thinking (Flash/Lite only, Pro minimum is 128)
        /// 128~32768: Specific token budget (Pro max: 32768, Flash/Lite max: 24576)
        /// </summary>
        public int ThinkingBudget { get; set; } = -1;

        public GeminiService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://generativelanguage.googleapis.com/", httpClient)
        {
            Model = AIModel.Gemini2_5Pro.ToDescription();
            Temperature = 1.0f;
            TopP = 0.8f;
            MaxTokens = 2048;
            AddNewChat(new ChatBlock());
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            // Check if we should use functions
            bool useFunctions = ShouldUseFunctions;

            if (StatelessMode)
            {
                return await ProcessStatelessRequestAsync(message, useFunctions);
            }

            Stream = false;
            ActivateChat.Messages.Add(message);

            var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new AIServiceException($"Gemini API request failed: {response.ReasonPhrase}", errorContent);
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
                var result = ExtractResponseContent(responseContent);
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
                return result;
            }
        }

        private async Task<string> ProcessStatelessRequestAsync(Message message, bool useFunctions)
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
            var modelName = Model;
            var endpoint = Stream
                ? $"v1beta/models/{modelName}:streamGenerateContent?alt=sse&key={ApiKey}"
                : $"v1beta/models/{modelName}:generateContent?key={ApiKey}";

            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            return new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
        }

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            // Gemini 2.0+ models all support vision natively
            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        #endregion

        #region Gemini-Specific Features

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

        #endregion

        #region Not Supported Features

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

        #endregion
    }
}