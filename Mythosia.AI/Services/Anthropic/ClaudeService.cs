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
        private const string AnthropicApiVersion = "2023-06-01";
        private const string DefaultImageMimeType = "image/jpeg";
        private const string SseDataPrefix = "data:";
        private const string SseEventPrefix = "event:";

        public override AIProvider Provider => AIProvider.Anthropic;

        /// <summary>
        /// Controls the thinking token budget for Claude extended thinking.
        /// -1: Disabled (default) - no extended thinking
        /// 1024+: Specific token budget (must be less than MaxTokens)
        /// Supported models: Claude Sonnet 4+, Claude Opus 4+, Claude Haiku 4.5+
        /// Note: When thinking is enabled, temperature is automatically set to 1 (Claude requirement).
        /// </summary>
        public int ThinkingBudget { get; set; } = -1;

        /// <summary>
        /// Contains the thinking/reasoning content from the last non-streaming API call.
        /// Only populated when ThinkingBudget is enabled (>= 1024).
        /// </summary>
        public string? LastThinkingContent { get; private set; }

        protected override uint GetModelMaxOutputTokens()
        {
            var model = Model?.ToLower() ?? "";
            if (model.Contains("opus-4-6")) return 128000;
            if (model.Contains("sonnet-4-6")) return 65536;
            if (model.Contains("opus-4-5")) return 64000;
            if (model.Contains("sonnet-4-5")) return 65536;
            if (model.Contains("haiku-4-5")) return 65536;
            if (model.Contains("opus-4")) return 32768;
            if (model.Contains("sonnet-4")) return 16384;
            if (model.Contains("haiku-4")) return 8192;
            return 8192;  // safe default
        }

        public ClaudeService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.anthropic.com/v1/", httpClient)
        {
            Model = AIModel.ClaudeSonnet4_250514.ToDescription();
            MaxTokens = 8192;
            Temperature = 0.7f;
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
                        throw new AIServiceException(
                            $"API request failed ({(int)response.StatusCode}): {(string.IsNullOrEmpty(response.ReasonPhrase) ? errorContent : response.ReasonPhrase)}",
                            errorContent);
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

            for (int i = 0; i < toolUses.Count; i++)
            {
                var call = toolUses[i];
                var content = (i == 0) ? (textContent ?? ".") : ".";

                if (policy.EnableLogging)
                {
                    Console.WriteLine($"  Executing function: {call.Name}");
                }

                ActivateChat.Messages.Add(CreateFunctionCallMessage(call, content));
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
        /// Executes a function call and adds the result message to the conversation
        /// </summary>
        private async Task<string> ExecuteFunctionAndAddResultAsync(FunctionCall functionCall)
        {
            var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);

            ActivateChat.Messages.Add(CreateFunctionResultMessage(functionCall, result));

            return result;
        }

        private Message CreateFunctionCallMessage(FunctionCall call, string content)
        {
            return new Message(ActorRole.Assistant, content)
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_call",
                    [MessageMetadataKeys.FunctionId] = call.Id,
                    [MessageMetadataKeys.FunctionSource] = call.Source,
                    [MessageMetadataKeys.FunctionName] = call.Name,
                    [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(call.Arguments)
                }
            };
        }

        private Message CreateFunctionResultMessage(FunctionCall call, string result)
        {
            return new Message(ActorRole.Function, result)
            {
                Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.MessageType] = "function_result",
                    [MessageMetadataKeys.FunctionId] = call.Id,
                    [MessageMetadataKeys.FunctionSource] = call.Source,
                    [MessageMetadataKeys.FunctionName] = call.Name
                }
            };
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

            AddClaudeHeaders(request);

            return request;
        }

        /// <summary>
        /// Adds standard Claude API headers to the request
        /// </summary>
        private void AddClaudeHeaders(HttpRequestMessage request, params string[] betaHeaders)
        {
            request.Headers.Add("x-api-key", ApiKey);
            request.Headers.Add("anthropic-version", AnthropicApiVersion);

            foreach (var beta in betaHeaders)
            {
                request.Headers.Add("anthropic-beta", beta);
            }
        }

        /// <summary>
        /// Adds system message to request body if present.
        /// Appends structured output instruction when in structured output mode.
        /// </summary>
        private void ApplySystemMessage(Dictionary<string, object> requestBody)
        {
            var systemMsg = GetEffectiveSystemMessage();
            var structuredInstruction = GetStructuredOutputInstruction();
            if (structuredInstruction != null)
                systemMsg += structuredInstruction;

            if (!string.IsNullOrEmpty(systemMsg))
            {
                requestBody["system"] = systemMsg;
            }
        }

        /// <summary>
        /// Returns true if the current model supports extended thinking.
        /// Supported: Claude Sonnet 4+, Claude Opus 4+, Claude Haiku 4.5+
        /// </summary>
        public bool SupportsExtendedThinking => IsExtendedThinkingModel();

        private bool IsExtendedThinkingModel()
        {
            var model = Model?.ToLower() ?? "";
            if (model.Contains("sonnet-4")) return true;
            if (model.Contains("opus-4")) return true;
            if (model.Contains("haiku-4")) return true;
            return false;
        }

        /// <summary>
        /// Returns true if extended thinking is enabled and the model supports it.
        /// </summary>
        private bool IsThinkingEnabled => ThinkingBudget >= 1024 && IsExtendedThinkingModel();

        /// <summary>
        /// Applies thinking configuration to the request body if enabled.
        /// When thinking is enabled, temperature must be 1 (Claude requirement).
        /// Also auto-adjusts max_tokens to ensure budget_tokens &lt; max_tokens.
        /// </summary>
        private void ApplyThinkingConfig(Dictionary<string, object> requestBody)
        {
            if (!IsThinkingEnabled) return;

            // Claude requires budget_tokens < max_tokens
            var effectiveMaxTokens = GetEffectiveMaxTokens();
            if ((uint)ThinkingBudget >= effectiveMaxTokens)
            {
                var modelMax = GetModelMaxOutputTokens();
                var required = (uint)ThinkingBudget + 1024;
                requestBody["max_tokens"] = Math.Min(required, modelMax);
            }

            requestBody["temperature"] = 1.0f;
            requestBody["thinking"] = new Dictionary<string, object>
            {
                ["type"] = "enabled",
                ["budget_tokens"] = ThinkingBudget
            };
        }

        /// <summary>
        /// Sets Claude extended thinking parameters.
        /// Budget must be >= 1024 and less than MaxTokens.
        /// When thinking is enabled, temperature is automatically forced to 1.
        /// </summary>
        public ClaudeService WithThinkingParameters(int budgetTokens)
        {
            ThinkingBudget = budgetTokens;
            return this;
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
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? DefaultImageMimeType;

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