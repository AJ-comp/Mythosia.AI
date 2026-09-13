using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
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

namespace Mythosia.AI.Services.xAI
{
    public partial class XAIService : OpenAICompatibleService, IImageGenerationService
    {
        public override string Provider => nameof(AIProvider.xAI);

        /// <summary>
        /// Reasoning effort for configurable Grok models. Auto leaves the provider default intact.
        /// Grok 4.3 accepts None, Low, Medium, and High; Grok 4.5 accepts Low, Medium, and High.
        /// Grok 4.6 also accepts XHigh and defaults to High when Auto is selected.
        /// </summary>
        public GrokReasoning ReasoningEffort { get; set; } = GrokReasoning.Auto;

        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[nameof(ReasoningEffort)] = ReasoningEffort;
        }

        protected override uint GetModelMaxOutputTokens()
        {
            // Grok 4.6 shares its 500,000-token context between input and output.
            // The server validates the remaining budget for each actual request.
            if (GetModelFamily() == GrokModelFamily.Grok4_6) return 500000;
            var model = RequestModel?.ToLower() ?? "";
            if (model.Contains("grok-4")) return 131072;
            if (model.Contains("grok-3")) return 131072;
            return 131072;
        }

        public XAIService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.x.ai/v1/", httpClient)
        {
            Model = AIModels.xAI.Grok4_5;
            MaxTokens = 8000;
        }

        /// <summary>
        /// Creates a XAIService with a specific model.
        /// </summary>
        public XAIService(string apiKey, string model, HttpClient httpClient)
            : this(apiKey, httpClient)
        {
            ChangeModel(model);
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            RequestCancellationToken.ThrowIfCancellationRequested();
            using var settingsScope = BeginRequestSettingsScope();
            using var featureScope = BeginRequestFeaturesScope(message);
            var policy = GetExecutionPolicy();

            using var cts = CreateRequestTimeoutCts(policy);

            ChatBlock? originalChat = null;
            if (RequestStatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                SetExecutionSetting(nameof(Stream), false);
                cts.Token.ThrowIfCancellationRequested();
                ActivateChat.Messages.Add(message);

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var result = await ProcessSingleRoundAsync(round, policy, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    if (result.IsComplete)
                        return result.Content;
                }

                cts.Token.ThrowIfCancellationRequested();
                throw new AIServiceException($"Maximum rounds ({policy.MaxRounds}) exceeded");
            }
            catch (TaskCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested &&
                exception.InnerException is TimeoutException)
            {
                throw new AIServiceException("The HTTP request timed out.", exception);
            }
            catch (OperationCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested && cts.IsCancellationRequested)
            {
                throw new AIServiceException($"Request timeout after {policy.TimeoutSeconds} seconds", exception);
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        private async Task<RoundResult> ProcessSingleRoundAsync(
            int round,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            if (policy.EnableLogging)
                Console.WriteLine($"[Grok Round {round + 1}/{policy.MaxRounds}]");

            cancellationToken.ThrowIfCancellationRequested();
            bool useFunctions = ShouldUseFunctions;
            using var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseContent = await ReadCompletionResponseBodyAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = responseContent;

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new RateLimitExceededException(
                        "xAI rate limit exceeded. Please try again later.",
                        TimeSpan.FromSeconds(60));
                }

                throw AIHttpErrorFactory.FromHttp(
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    errorContent,
                    "xAI API request failed",
                    includeErrorBodyInMessage: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (useFunctions)
                return await ProcessFunctionResponseAsync(responseContent, policy, cancellationToken);

            return ProcessRegularResponse(responseContent);
        }

        private async Task<RoundResult> ProcessFunctionResponseAsync(
            string responseContent,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            var (content, functionCalls) = ExtractFunctionCalls(responseContent);

            if (functionCalls.Calls.Count > 0)
            {
                var results = await ProcessFunctionCallsAsync(
                    functionCalls,
                    policy,
                    cancellationToken);
                AddFunctionCallBatchToHistory(
                    content,
                    functionCalls,
                    new Dictionary<string, object> { ["model"] = RequestModel });
                AddFunctionResultBatchToHistory(
                    results,
                    new Dictionary<string, object> { ["model"] = RequestModel });
                return RoundResult.Continue();
            }

            var finishReason = _protocol.ExtractFinishReason(responseContent);
            if (!string.IsNullOrEmpty(finishReason))
            {
                if (!string.Equals(finishReason, "stop", StringComparison.Ordinal))
                {
                    throw new AIServiceException(
                        $"xAI ended the response with finish_reason={finishReason}; the partial response was not saved.");
                }

                if (!string.IsNullOrEmpty(content))
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
                return RoundResult.Complete(content ?? string.Empty);
            }

            if (string.IsNullOrEmpty(content))
                return RoundResult.Continue();

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
            return RoundResult.Complete(content);
        }

        private RoundResult ProcessRegularResponse(string responseContent)
        {
            var result = ExtractResponseContent(responseContent);
            var finishReason = _protocol.ExtractFinishReason(responseContent);
            if (!string.IsNullOrEmpty(finishReason) &&
                !string.Equals(finishReason, "stop", StringComparison.Ordinal))
            {
                throw new AIServiceException(
                    $"xAI ended the response with finish_reason={finishReason}; the partial response was not saved.");
            }

            if (string.Equals(finishReason, "stop", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(result))
            {
                return RoundResult.Complete(string.Empty);
            }

            if (string.IsNullOrEmpty(result))
                return RoundResult.Continue();

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));
            return RoundResult.Complete(result);
        }

        #endregion

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var encoding = TikToken.EncodingForModel("gpt-4");

            var allMessagesBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(RequestSystemMessage))
            {
                allMessagesBuilder.Append(RequestSystemMessage).Append('\n');
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

        #region xAI-Specific Features

        /// <summary>
        /// xAI Grok supports vision (image inputs)
        /// </summary>
        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCancellationToken.ThrowIfCancellationRequested();
            return await base.GetCompletionWithImageAsync(prompt, imagePath, cancellationToken);
        }

        /// <summary>
        /// Switches to Grok 4.5 for compatibility. To use Grok 4.6, select AIModels.xAI.Grok4_6.
        /// </summary>
        public XAIService UseGrok4Model()
        {
            ChangeModel(AIModels.xAI.Grok4_5);
            return this;
        }

        /// <summary>
        /// Switches to Grok 4.3 for fast general-purpose workloads.
        /// </summary>
        public XAIService UseGrok4FastModel()
        {
            ChangeModel(AIModels.xAI.Grok4_3);
            return this;
        }

        /// <summary>
        /// Sets the model-specific Grok reasoning effort.
        /// </summary>
        public XAIService WithGrokParameters(GrokReasoning reasoningEffort = GrokReasoning.High)
        {
            ReasoningEffort = reasoningEffort;
            return this;
        }

        /// <summary>Sets the model-specific Grok reasoning effort and returns this service.</summary>
        public XAIService WithGrokReasoning(GrokReasoning reasoningEffort = GrokReasoning.High)
            => WithGrokParameters(reasoningEffort);

        /// <summary>
        /// Sets Grok-specific parameters for code generation
        /// </summary>
        public XAIService WithCodeGenerationMode(string language = "python")
        {
            var systemPrompt = $"You are an expert {language} programmer. Generate clean, efficient, and well-documented code.";
            SystemMessage = systemPrompt;
            Temperature = 0.1f;
            return this;
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ApplyXAIProfileSettings(profile);
            return () => { };
        }

        protected override void ApplyCapabilityRequestProfile(AIRequestProfile profile)
            => ApplyXAIProfileSettings(profile);

        // Shared native flags only; execution-specific output reservations remain outside this helper.
        private void ApplyXAIProfileSettings(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return;

            SetExecutionSetting(nameof(ReasoningEffort), GetMinimumReasoningEffortForModel());
        }

        /// <summary>
        /// Gets completion with Chain of Thought prompting
        /// </summary>
        public async Task<string> GetCompletionWithCoTAsync(string prompt, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cotPrompt = $"{prompt}\n\nPlease think step by step and show your reasoning process.";
            return await GetCompletionAsync(cotPrompt, cancellationToken: cancellationToken);
        }

        #endregion
    }
}
