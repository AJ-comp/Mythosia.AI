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

        protected override uint GetModelMaxOutputTokens()
        {
            var model = Model?.ToLower() ?? "";
            if (model.Contains("opus-4")) return 32768;
            if (model.Contains("sonnet-4")) return 16384;
            if (model.Contains("3-7-sonnet") || model.Contains("3.7")) return 8192;
            if (model.Contains("3-5-haiku")) return 8192;
            if (model.Contains("3-opus")) return 4096;
            if (model.Contains("3-haiku")) return 4096;
            return 8192;  // safe default
        }

        public ClaudeService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.anthropic.com/v1/", httpClient)
        {
            Model = AIModel.ClaudeSonnet4_250514.ToDescription();
            MaxTokens = 8192;
            Temperature = 0.7f;
            AddNewChat(new ChatBlock());
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
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                bool useFunctions = ShouldUseFunctions;

                Stream = false;
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
                        // Extract all tool uses at once
                        var allToolUses = ExtractAllToolUses(responseContent);
                        var textContent = ExtractTextContent(responseContent);

                        if (allToolUses.Count > 0)
                        {
                            if (policy.EnableLogging)
                            {
                                Console.WriteLine($"  Executing {allToolUses.Count} function(s)");
                            }

                            // Process all tool uses with unified method
                            await ProcessMultipleToolUses(allToolUses, textContent, policy);

                            // Continue the loop to get AI's response based on function results
                            continue;
                        }

                        // No more function calls, we have the final response
                        if (!string.IsNullOrEmpty(textContent))
                        {
                            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textContent));
                            return textContent;
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
        /// Unified method to process multiple tool uses
        /// </summary>
        private async Task ProcessMultipleToolUses(
            List<FunctionCall> toolUses,
            string textContent,
            FunctionCallingPolicy policy)
        {
            if (toolUses.Count == 0) return;

            // Process first tool use with text content
            var firstCall = toolUses[0];

            if (policy.EnableLogging)
            {
                Console.WriteLine($"  Executing function: {firstCall.Name}");
            }

            var assistantMsg = new Message(ActorRole.Assistant, textContent ?? ".")
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_call",
                    [MessageMetadataKeys.FunctionId] = firstCall.Id,
                    [MessageMetadataKeys.FunctionSource] = firstCall.Source,
                    [MessageMetadataKeys.FunctionName] = firstCall.Name,
                    [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(firstCall.Arguments)
                }
            };
            ActivateChat.Messages.Add(assistantMsg);
            await ExecuteFunctionAndAddResultAsync(firstCall);

            // Process additional tool uses
            for (int i = 1; i < toolUses.Count; i++)
            {
                var call = toolUses[i];

                if (policy.EnableLogging)
                {
                    Console.WriteLine($"  Executing additional function: {call.Name}");
                }

                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, ".")
                {
                    Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.MessageType] = "function_call",
                        [MessageMetadataKeys.FunctionId] = call.Id,
                        [MessageMetadataKeys.FunctionSource] = call.Source,
                        [MessageMetadataKeys.FunctionName] = call.Name,
                        [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(call.Arguments)
                    }
                });

                await ExecuteFunctionAndAddResultAsync(call);
            }
        }

        /// <summary>
        /// Extract all tool uses from response
        /// </summary>
        private List<FunctionCall> ExtractAllToolUses(string response)
        {
            var toolUses = new List<FunctionCall>();

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("content", out var contentArray) &&
                    contentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in contentArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeElement) &&
                            typeElement.GetString() == "tool_use")
                        {
                            var functionCall = new FunctionCall
                            {
                                Id = item.GetProperty("id").GetString(),
                                Source = IdSource.Claude,
                                Name = item.GetProperty("name").GetString(),
                                Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                    item.GetProperty("input").GetRawText()) ?? new Dictionary<string, object>()
                            };
                            toolUses.Add(functionCall);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting tool uses: {ex.Message}");
            }

            return toolUses;
        }

        /// <summary>
        /// Extract text content from response
        /// </summary>
        private string ExtractTextContent(string response)
        {
            var content = string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("content", out var contentArray) &&
                    contentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in contentArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeElement) &&
                            typeElement.GetString() == "text" &&
                            item.TryGetProperty("text", out var textElement))
                        {
                            content += textElement.GetString();
                        }
                    }
                }
            }
            catch { }

            return content;
        }

        /// <summary>
        /// Helper method to extract function call with save state (for backward compatibility)
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

            // Add the result message with standardized metadata
            var metadata = new Dictionary<string, object>
            {
                [MessageMetadataKeys.MessageType] = "function_result",
                [MessageMetadataKeys.FunctionId] = functionCall.Id,
                [MessageMetadataKeys.FunctionSource] = functionCall.Source,
                [MessageMetadataKeys.FunctionName] = functionCall.Name
            };

            ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
            {
                Metadata = metadata
            });

            return result;
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
            if (!Model.Contains("claude-3") &&
                !Model.Contains("claude-4") &&
                !Model.Contains("opus-4"))
            {
                ChangeModel(AIModel.ClaudeSonnet4_250514);
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
            Temperature = preset switch
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