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
using System.IO;
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
        /// Auto uses the selected model's provider default. Gemini 3.8/3.6/3.5 Flash default
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

        #region RequestModel Detection Helpers

        /// <summary>
        /// Returns true if the current model is a Gemini 3 series model.
        /// </summary>
        private bool IsGemini3Model()
        {
            return RequestModel != null && RequestModel.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the current model requires thinking mode and cannot disable it.
        /// Gemini 2.5 Pro only works in thinking mode (minimum budget 128).
        /// </summary>
        private bool IsThinkingRequiredModel()
        {
            return RequestModel != null &&
                   RequestModel.Contains("-pro", StringComparison.OrdinalIgnoreCase) &&
                   !IsGemini3Model();
        }

        #endregion

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            RequestCancellationToken.ThrowIfCancellationRequested();
            using var requestScope = BeginRequestSettingsScope();
            using var featureScope = BeginRequestFeaturesScope(message);
            LastThinkingContent = null;
            var policy = GetExecutionPolicy();
            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var cts = CreateRequestTimeoutCts(policy);
            bool useFunctions = ShouldUseFunctions;
            SetExecutionSetting(nameof(Stream), false);

            try
            {
                if (RequestStatelessMode)
                    return await ProcessStatelessRequestAsync(message, useFunctions, policy, cts.Token);

                cts.Token.ThrowIfCancellationRequested();
                ActivateChat.Messages.Add(message);

                var request = useFunctions
                    ? CreateFunctionMessageRequest()
                    : CreateMessageRequest();

                var responseContent = await SendAndReadAsync(request, cts.Token);

                if (useFunctions)
                    return await ProcessFunctionCallLoopAsync(responseContent, policy, cts.Token);

                cts.Token.ThrowIfCancellationRequested();
                return AddAssistantResponseWithSignature(responseContent);
            }
            catch (TaskCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested &&
                exception.InnerException is TimeoutException)
            {
                throw new AIServiceException("The HTTP request timed out.", exception);
            }
            catch (OperationCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested && cts.IsCancellationRequested)
            {
                throw new AIServiceException($"Request timeout after {timeoutSeconds} seconds", exception);
            }
        }

        private async Task<string> ProcessFunctionCallLoopAsync(
            string responseContent,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            for (int round = 0; round < policy.MaxRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (content, thinking, functionCalls, thoughtSignature) = ExtractFunctionCallsWithSignature(responseContent);
                LastThinkingContent = thinking;

                if (functionCalls.Calls.Count == 0)
                {
                    AddAssistantMessage(content, thoughtSignature, responseContent);
                    return content;
                }

                var results = await ProcessFunctionCallsAsync(
                    functionCalls,
                    policy,
                    cancellationToken);
                AddFunctionCallBatchToHistory(content, functionCalls);
                AddFunctionResultBatchToHistory(results);
                cancellationToken.ThrowIfCancellationRequested();

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
            AddAssistantMessage(text, sig, responseContent);
            return text;
        }

        private void AddAssistantMessage(string content, string? thoughtSignature, string? response = null)
        {
            var msg = new Message(ActorRole.Assistant, content);
            if (thoughtSignature != null)
            {
                msg.Metadata = new Dictionary<string, object>
                {
                    [MessageMetadataKeys.ThoughtSignature] = thoughtSignature
                };
            }
            if (response != null)
                PreserveNativeGeminiParts(msg, response);
            ActivateChat.Messages.Add(msg);
        }

        private async Task<string> SendAndReadAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default,
            bool observeProcessing = true)
        {
            using var ownedRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            var processing = observeProcessing ? BeginProcessingObservation() : null;
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (processing != null) RecordGeminiProcessingHeaders(response, processing);
            var responseContent = await ReadCompletionResponseBodyAsync(response, cancellationToken);
            if (processing != null) RecordGeminiProcessing(responseContent, processing);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = responseContent;
                throw AIHttpErrorFactory.FromHttp(
                    (int)response.StatusCode, response.ReasonPhrase, errorContent, "Gemini API request failed");
            }

            cancellationToken.ThrowIfCancellationRequested();
            RecordGeminiCitations(responseContent);
            return responseContent;
        }

        private async Task<string> ProcessStatelessRequestAsync(
            Message message,
            bool useFunctions,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tempChat = new ChatBlock
            {
                SystemMessage = RequestSystemMessage
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
            var endpoint = RequestStream
                ? $"v1beta/models/{RequestModel}:streamGenerateContent?alt=sse"
                : $"v1beta/models/{RequestModel}:generateContent";

            var requestBody = BuildRequestBody(includeThoughts);
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            return CreateGoogleRequest(HttpMethod.Post, endpoint, content);
        }

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCancellationToken.ThrowIfCancellationRequested();
            return await base.GetCompletionWithImageAsync(prompt, imagePath, cancellationToken);
        }

        public override async Task<string> GetCompletionWithImageUrlAsync(string prompt, string imageUrl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = await CreateMessageWithImageUrl(prompt, imageUrl, cancellationToken);
            return await GetCompletionAsync(message, null, null, cancellationToken);
        }

        #endregion

        #region Gemini-Specific Features

        /// <summary>
        /// Downloads an image from URL for Gemini processing
        /// </summary>
        public async Task<Message> CreateMessageWithImageUrl(string prompt, string imageUrl, CancellationToken cancellationToken = default)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RequestCancellationToken);
            var token = cancellation.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                using var imageResponse = await HttpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, token);
                if (!imageResponse.IsSuccessStatusCode)
                    throw new AIServiceException($"Failed to download image from {imageUrl}");

                byte[] imageData;
                using (token.Register(imageResponse.Dispose))
                {
                    using var source = await imageResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var buffer = new MemoryStream();
                    await source.CopyToAsync(buffer, 81920, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    imageData = buffer.ToArray();
                }
                var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? DefaultImageMimeType;

                return new Message(ActorRole.User, new List<MessageContent>
                {
                    new TextContent(prompt),
                    new ImageContent(imageData, contentType)
                });
            }
            catch (Exception exception) when (token.IsCancellationRequested &&
                (exception is OperationCanceledException || exception is IOException ||
                 exception is ObjectDisposedException || exception is HttpRequestException))
            {
                throw new OperationCanceledException("The image download was canceled.", exception,
                    cancellationToken.IsCancellationRequested ? cancellationToken : RequestCancellationToken);
            }
        }

        /// <summary>
        /// Lowest thinking level the current model supports (used when reasoning is disabled).
        /// Gemini 3 Pro and Gemini 3.7/3.8 Flash require at least Low.
        /// </summary>
        private GeminiThinkingLevel LowestThinkingLevel()
        {
            if (HasLowThinkingFloor())
                return GeminiThinkingLevel.Low;
            return GeminiThinkingLevel.Minimal;
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ApplyGoogleProfileSettings(profile);
            return () => { };
        }

        protected override void ApplyCapabilityRequestProfile(AIRequestProfile profile)
            => ApplyGoogleProfileSettings(profile);

        // Shared native flags only; execution-specific output reservations remain outside this helper.
        private void ApplyGoogleProfileSettings(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return;

            // Gemini 2.5 Pro cannot disable thinking; other models use their supported floor.
            SetExecutionSetting(nameof(ThinkingBudget), IsThinkingRequiredModel() ? 128 : 0);
            SetExecutionSetting(nameof(ThinkingLevel), LowestThinkingLevel());
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
                    SetExecutionSetting(nameof(MaxTokens), Math.Min(
                        GetModelMaxOutputTokens(),
                        checked(profile.MaxTokens.Value + MinimumGemini25ProThinkingTokens)));
                }
                else if (IsGemini3Model())
                {
                    SetExecutionSetting(nameof(MaxTokens), Math.Min(
                        GetModelMaxOutputTokens(),
                        Math.Max(profile.MaxTokens.Value, MinimumGemini3InternalProfileTokens)));
                }
            }

            return restore;
        }

        #endregion

        #region Not Supported Features

        #endregion
    }
}
