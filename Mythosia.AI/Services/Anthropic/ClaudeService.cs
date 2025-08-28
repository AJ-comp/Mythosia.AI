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
using System.Threading;
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
            var chatBlock = new ChatBlock(AIModel.ClaudeSonnet4_250514)
            {
                MaxTokens = 8192,
                Temperature = 0.7f
            };
            AddNewChat(chatBlock);
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            // Get policy (current or default)
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            using var cts = policy.TimeoutSeconds.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds.Value))
                : new CancellationTokenSource();

            // Stateless 모드 처리 (ChatGpt 방식)
            ChatBlock originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = ActivateChat.CloneWithoutMessages();
            }

            try
            {
                bool useFunctions = ActivateChat.Functions?.Count > 0
                                   && ActivateChat.EnableFunctions
                                   && !FunctionsDisabled;

                ActivateChat.Stream = false;
                ActivateChat.Messages.Add(message);

                // Function calling loop - use policy.MaxRounds
                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    if (policy.EnableLogging)
                    {
                        Console.WriteLine($"[Claude Round {round + 1}/{policy.MaxRounds}]");
                    }

                    var request = useFunctions
                        ? CreateFunctionMessageRequest()
                        : CreateMessageRequest();

                    var response = await HttpClient.SendAsync(request, cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        throw new AIServiceException($"API request failed: {response.ReasonPhrase}", errorContent);
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (useFunctions)
                    {
                        var extractResult = ExtractFunctionCallWithSave(responseContent);

                        if (extractResult.functionCall != null)
                        {
                            if (policy.EnableLogging)
                            {
                                Console.WriteLine($"  Executing function: {extractResult.functionCall.Name}");
                            }

                            // Execute function and add result message
                            await ExecuteFunctionAndAddResultAsync(extractResult.functionCall);

                            // Continue the loop to get AI's response based on function result
                            continue;
                        }

                        // No more function calls, we have the final response
                        if (!string.IsNullOrEmpty(extractResult.content))
                        {
                            // Only add if not already added by ExtractFunctionCall
                            if (!extractResult.wasToolUseSaved)
                            {
                                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, extractResult.content));
                            }
                            return extractResult.content;
                        }
                    }
                    else
                    {
                        var result = ExtractResponseContent(responseContent);
                        ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
                        return result;
                    }
                }

                throw new AIServiceException($"Maximum rounds ({policy.MaxRounds}) exceeded");
            }
            catch (OperationCanceledException)
            {
                throw new AIServiceException($"Request timeout after {policy.TimeoutSeconds} seconds");
            }
            finally
            {
                if (originalChat != null)
                {
                    ActivateChat = originalChat;
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Helper method to extract function call with save state
        /// </summary>
        private (string content, FunctionCall functionCall, bool wasToolUseSaved) ExtractFunctionCallWithSave(string response)
        {
            // Use the enhanced extraction method from Functions.cs
            return ExtractFunctionCallWithMetadata(response);
        }

        /// <summary>
        /// Executes a function call and adds the result message to the conversation
        /// </summary>
        private async Task<string> ExecuteFunctionAndAddResultAsync(FunctionCall functionCall)
        {
            // Execute the function
            var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);

            // Add the result message
            AddFunctionResultMessage(result, functionCall);

            return result;
        }

        /// <summary>
        /// Adds a function result message to the conversation
        /// </summary>
        private void AddFunctionResultMessage(string result, FunctionCall functionCall)
        {
            var metadata = new Dictionary<string, object>
            {
                ["function_name"] = functionCall.Name,
                ["tool_use_id"] = functionCall.CallId ?? Guid.NewGuid().ToString()
            };

            // Include arguments only if they exist
            if (functionCall.Arguments != null && functionCall.Arguments.Count > 0)
            {
                metadata["arguments"] = functionCall.Arguments;
            }

            ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
            {
                Metadata = metadata
            });
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