using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService : AIService, IImageGenerationService
    {
        private const uint MinimumGemini3InternalProfileTokens = 1024;
        private const uint MinimumGemini25ProThinkingTokens = 128;

        public override string Provider => nameof(AIProvider.Google);

        protected override uint GetModelMaxOutputTokens()
        {
            return 65536;
        }

        /// <summary>
        /// Controls the thinking token budget for Gemini 2.5 models.
        /// Ignored when ThinkingLevel is set (Gemini 3 uses ThinkingLevel instead).
        /// -1: Dynamic (model decides automatically, default)
        /// 0: Disable thinking (Flash/Lite only, Pro minimum is 128)
        /// 128~32768: Specific token budget (Pro max: 32768, Flash/Lite max: 24576)
        /// </summary>
        public int ThinkingBudget { get; set; } = -1;

        /// <summary>
        /// Controls the thinking level for Gemini 3 models.
        /// Auto uses the selected model's provider default. Gemini 3.6/3.5 Flash default
        /// to Medium, Flash-Lite defaults to Minimal, while 3 Flash Preview and Pro Preview default to High.
        /// Note: Do not set both ThinkingLevel and ThinkingBudget.
        /// </summary>
        public GeminiThinkingLevel ThinkingLevel { get; set; } = GeminiThinkingLevel.Auto;

        /// <summary>
        /// The most recent non-streaming thought summary returned by Gemini, when requested.
        /// </summary>
        public string? LastThinkingContent { get; private set; }

        /// <summary>Gemini harassment-filter threshold.</summary>
        public GeminiSafetyThreshold HarassmentSafetyThreshold { get; set; } = GeminiSafetyThreshold.ProviderDefault;

        /// <summary>Gemini hate-speech-filter threshold.</summary>
        public GeminiSafetyThreshold HateSpeechSafetyThreshold { get; set; } = GeminiSafetyThreshold.ProviderDefault;

        /// <summary>Gemini sexually-explicit-content-filter threshold.</summary>
        public GeminiSafetyThreshold SexuallyExplicitSafetyThreshold { get; set; } = GeminiSafetyThreshold.ProviderDefault;

        /// <summary>Gemini dangerous-content-filter threshold.</summary>
        public GeminiSafetyThreshold DangerousContentSafetyThreshold { get; set; } = GeminiSafetyThreshold.ProviderDefault;

        public GoogleAIService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://generativelanguage.googleapis.com/", httpClient)
        {
            Model = AIModels.Google.Gemini3_6Flash;
            Temperature = 1.0f;
            TopP = 0.8f;
            MaxTokens = 8192;

            // FunctionCallingPolicy is the timeout authority for text, streaming, token-count,
            // and image requests. In particular, Gemini image generation uses the 200-second
            // Vision policy, which would otherwise be cut off by HttpClient's 100-second default.
            // AIService already requires an unused client so BaseAddress can be assigned above.
            HttpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        /// <summary>
        /// Creates a GoogleAIService with a specific model.
        /// </summary>
        public GoogleAIService(string apiKey, string model, HttpClient httpClient)
            : this(apiKey, httpClient)
        {
            ChangeModel(model);
        }

        #region Model Detection Helpers

        /// <summary>
        /// Returns true if the current model is a Gemini 3 series model.
        /// </summary>
        private bool IsGemini3Model()
        {
            return Model != null && Model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the current model requires thinking mode and cannot disable it.
        /// Gemini 2.5 Pro only works in thinking mode (minimum budget 128).
        /// </summary>
        private bool IsThinkingRequiredModel()
        {
            return Model != null &&
                   Model.Contains("-pro", StringComparison.OrdinalIgnoreCase) &&
                   !IsGemini3Model();
        }

        #endregion

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            LastThinkingContent = null;
            var policy = (CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            CurrentPolicy = null;
            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var cts = CreateRequestTimeoutCts(policy);
            bool useFunctions = ShouldUseFunctions;
            Stream = false;

            try
            {
                if (StatelessMode)
                    return await ProcessStatelessRequestAsync(message, useFunctions, policy, cts.Token);

                ActivateChat.Messages.Add(message);

                var request = useFunctions
                    ? CreateFunctionMessageRequest()
                    : CreateMessageRequest();

                var responseContent = await SendAndReadAsync(request, cts.Token);

                if (useFunctions)
                    return await ProcessFunctionCallLoopAsync(responseContent, policy, cts.Token);

                return AddAssistantResponseWithSignature(responseContent);
            }
            catch (OperationCanceledException)
            {
                throw new AIServiceException($"Request timeout after {timeoutSeconds} seconds");
            }
        }

        private async Task<string> ProcessFunctionCallLoopAsync(
            string responseContent,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            for (int round = 0; round < policy.MaxRounds; round++)
            {
                var (content, thinking, functionCalls, thoughtSignature) = ExtractFunctionCallsWithSignature(responseContent);
                LastThinkingContent = thinking;

                if (functionCalls.Calls.Count == 0)
                {
                    AddAssistantMessage(content, thoughtSignature);
                    return content;
                }

                var results = await ProcessFunctionCallsAsync(
                    functionCalls,
                    policy,
                    cancellationToken);
                AddFunctionCallBatchToHistory(content, functionCalls);
                AddFunctionResultBatchToHistory(results);

                if (round + 1 >= policy.MaxRounds)
                    break;

                var request = CreateFunctionMessageRequest();
                responseContent = await SendAndReadAsync(request, cancellationToken);
            }

            throw new AIServiceException($"Maximum rounds ({policy.MaxRounds}) exceeded");
        }

        private string AddAssistantResponseWithSignature(string responseContent)
        {
            var (text, thinking, sig) = ExtractResponseContentWithSignature(responseContent);
            LastThinkingContent = thinking;
            AddAssistantMessage(text, sig);
            return text;
        }

        private void AddAssistantMessage(string content, string? thoughtSignature)
        {
            var msg = new Message(ActorRole.Assistant, content);
            if (thoughtSignature != null)
            {
                msg.Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.ThoughtSignature] = thoughtSignature
                };
            }
            ActivateChat.Messages.Add(msg);
        }

        private async Task<string> SendAndReadAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw AIHttpErrorFactory.FromHttp(
                    (int)response.StatusCode, response.ReasonPhrase, errorContent, "Gemini API request failed");
            }

            return await response.Content.ReadAsStringAsync();
        }

        private async Task<string> ProcessStatelessRequestAsync(
            Message message,
            bool useFunctions,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
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

                var responseContent = await SendAndReadAsync(request, cancellationToken);

                if (!useFunctions)
                    return ExtractResponseContent(responseContent);

                return await ProcessFunctionCallLoopAsync(responseContent, policy, cancellationToken);
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
            return CreateMessageRequest(includeThoughts: false);
        }

        internal HttpRequestMessage CreateMessageRequest(bool includeThoughts)
        {
            var endpoint = Stream
                ? $"v1beta/models/{Model}:streamGenerateContent?alt=sse"
                : $"v1beta/models/{Model}:generateContent";

            var requestBody = BuildRequestBody(includeThoughts);
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            return CreateGoogleRequest(HttpMethod.Post, endpoint, content);
        }

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        public override async Task<string> GetCompletionWithImageUrlAsync(string prompt, string imageUrl)
        {
            var message = await CreateMessageWithImageUrl(prompt, imageUrl);
            return await GetCompletionAsync(message, null, null);
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
                throw new AIServiceException($"Failed to download image from {imageUrl}");

            var imageData = await imageResponse.Content.ReadAsByteArrayAsync();
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? DefaultImageMimeType;

            return new Message(ActorRole.User, new List<MessageContent>
            {
                new TextContent(prompt),
                new ImageContent(imageData, contentType)
            });
        }

        /// <summary>
        /// Lowest thinking level the current model supports (used when reasoning is disabled).
        /// Gemini 3 "pro" models do NOT support MINIMAL (their floor is Low); Flash/Lite and others do.
        /// </summary>
        private GeminiThinkingLevel LowestThinkingLevel()
        {
            var model = Model?.ToLowerInvariant() ?? string.Empty;
            if (model.Contains("gemini-3") && model.Contains("-pro"))
                return GeminiThinkingLevel.Low;
            return GeminiThinkingLevel.Minimal;
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return base.ApplyProviderSpecificRequestProfile(profile);

            var backupThinkingBudget = ThinkingBudget;
            var backupThinkingLevel = ThinkingLevel;

            // Gemini 2.5 Pro requires thinking mode (minimum budget 128).
            // Flash/Lite models can disable thinking with budget 0.
            ThinkingBudget = IsThinkingRequiredModel() ? 128 : 0;
            ThinkingLevel = LowestThinkingLevel();

            return () =>
            {
                ThinkingBudget = backupThinkingBudget;
                ThinkingLevel = backupThinkingLevel;
            };
        }

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            var restore = base.ApplyRequestProfile(profile);

            if (profile.DisableReasoning == true && profile.MaxTokens.HasValue)
            {
                // Gemini counts hidden thinking against maxOutputTokens. The common internal
                // profiles describe the text budget they need, so reserve room for the lowest
                // reasoning setting on models where thinking cannot be fully disabled.
                if (IsThinkingRequiredModel())
                {
                    MaxTokens = Math.Min(
                        GetModelMaxOutputTokens(),
                        checked(profile.MaxTokens.Value + MinimumGemini25ProThinkingTokens));
                }
                else if (IsGemini3Model())
                {
                    MaxTokens = Math.Min(
                        GetModelMaxOutputTokens(),
                        Math.Max(profile.MaxTokens.Value, MinimumGemini3InternalProfileTokens));
                }
            }

            return restore;
        }

        #endregion

        #region Not Supported Features

        #endregion
    }
}
