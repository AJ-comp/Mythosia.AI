using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Exceptions;
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

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService : AIService
    {
        public override string Provider => nameof(AIProvider.DeepSeek);

        /// <summary>
        /// Enables server-side thinking before the final answer. Disabled by default.
        /// </summary>
        public bool ThinkingEnabled { get; set; }

        /// <summary>Persistent thinking effort. Auto leaves the provider's High default intact.</summary>
        public DeepSeekReasoning ReasoningEffort { get; set; } = DeepSeekReasoning.Auto;

        private const string ReasoningMetadataKey = "deepseek_reasoning_content";
        private bool DisableReasoningForProfile => RequestSetting("DeepSeek.DisableReasoningForProfile", false);
        private string? _deepSeekRequestMessageId;

        protected override string? GetRequestMessageOverrideTargetId()
            => base.GetRequestMessageOverrideTargetId() ?? _deepSeekRequestMessageId;

        private sealed class DeepSeekRequestOptions
        {
            public bool ThinkingEnabled { get; set; }
            public DeepSeekReasoning ReasoningEffort { get; set; }
        }

        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[nameof(ThinkingEnabled)] = ThinkingEnabled;
            settings[nameof(ReasoningEffort)] = ReasoningEffort;
        }

        protected override object? CaptureProviderRequestOptions(Message message)
        {
            ValidateDeepSeekMessage(message);
            return new DeepSeekRequestOptions
            {
                ThinkingEnabled = RequestSetting(nameof(ThinkingEnabled), ThinkingEnabled),
                ReasoningEffort = RequestSetting(nameof(ReasoningEffort), ReasoningEffort)
            };
        }

        protected override object? CloneProviderRequestOptions(object? options)
            => options is DeepSeekRequestOptions captured
                ? new DeepSeekRequestOptions
                {
                    ThinkingEnabled = captured.ThinkingEnabled,
                    ReasoningEffort = captured.ReasoningEffort
                }
                : null;

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ApplyDeepSeekProfileSettings(profile);
            return () => { };
        }

        protected override void ApplyCapabilityRequestProfile(AIRequestProfile profile)
            => ApplyDeepSeekProfileSettings(profile);

        // Shared native flags only; execution-specific output reservations remain outside this helper.
        private void ApplyDeepSeekProfileSettings(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return;

            SetExecutionSetting(nameof(ThinkingEnabled), false);
            SetExecutionSetting("DeepSeek.DisableReasoningForProfile", true);
        }

        protected override uint GetModelMaxOutputTokens()
        {
            return 393216;
        }

        public DeepSeekService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.deepseek.com/", httpClient)
        {
            Model = AIModels.DeepSeek.Flash;
            MaxTokens = 8000;
        }

        /// <summary>
        /// Creates a DeepSeekService with a specific model.
        /// </summary>
        public DeepSeekService(string apiKey, string model, HttpClient httpClient)
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
            ValidateDeepSeekToolSelection();
            var policy = GetExecutionPolicy();
            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var cts = CreateRequestTimeoutCts(policy);
            SetExecutionSetting(nameof(Stream), false);
            var previousAnchor = _deepSeekRequestMessageId;
            _deepSeekRequestMessageId = message.Id;
            ChatBlock? originalChat = null;
            if (RequestStatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = originalChat.SystemMessage };
            }
            try
            {
                cts.Token.ThrowIfCancellationRequested();
                ActivateChat.Messages.Add(message);
                for (var round = 0; round < policy.MaxRounds; round++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var useFunctions = ShouldUseFunctions;
                    using var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
                    using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    var json = await ReadCompletionResponseBodyAsync(response, cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            throw new RateLimitExceededException("DeepSeek rate limit exceeded. Please try again later.", TimeSpan.FromSeconds(60));
                        throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, json);
                    }
                    var parsed = ParseDeepSeekResponse(json);
                    if (parsed.Usage != null) LastKnownInputTokens = parsed.Usage.InputTokens;
                    if (parsed.Calls.Calls.Count > 0)
                    {
                        if (!useFunctions || RequestFunctionCallMode == FunctionCallMode.None)
                            throw new AIServiceException("DeepSeek returned tool calls when function execution was disabled.");
                        ValidateDeepSeekFunctionBatch(parsed.Calls);
                        await ProcessFunctionBatchForRoundAsync(parsed.Text, parsed.Calls,
                            CreateReasoningMetadata(parsed.Reasoning), policy, cts.Token).ConfigureAwait(false);
                        continue;
                    }
                    cts.Token.ThrowIfCancellationRequested();
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, parsed.Text)
                    {
                        Metadata = CreateReasoningMetadata(parsed.Reasoning)
                    });
                    return parsed.Text;
                }
                cts.Token.ThrowIfCancellationRequested();
                throw new AIServiceException($"Maximum function-calling rounds ({policy.MaxRounds}) exceeded.");
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
            finally
            {
                _deepSeekRequestMessageId = previousAnchor;
                if (originalChat != null) ActivateChat = originalChat;
            }
        }

        #endregion

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            // DeepSeek uses similar tokenization to GPT models
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

        #region DeepSeek-Specific Features

        /// <summary>
        /// Reads an image and sends it with the prompt to a vision-capable DeepSeek model.
        /// </summary>
        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCancellationToken.ThrowIfCancellationRequested();
            return await base.GetCompletionWithImageAsync(prompt, imagePath, cancellationToken);
        }

        /// <summary>
        /// Uses the current Flash model with thinking enabled for complex reasoning tasks.
        /// </summary>
        public DeepSeekService UseReasonerModel()
        {
            ChangeModel(AIModels.DeepSeek.Flash);
            ThinkingEnabled = true;
            ReasoningEffort = DeepSeekReasoning.High;
            return this;
        }

        /// <summary>Enables thinking and sets its persistent effort. Auto uses the provider default.</summary>
        public DeepSeekService WithDeepSeekReasoning(DeepSeekReasoning effort = DeepSeekReasoning.High)
        {
            if (!Enum.IsDefined(typeof(DeepSeekReasoning), effort))
                throw new ArgumentOutOfRangeException(nameof(effort));
            ThinkingEnabled = true;
            ReasoningEffort = effort;
            return this;
        }

        /// <summary>
        /// Sets DeepSeek-specific parameters for code generation
        /// </summary>
        public DeepSeekService WithCodeGenerationMode(string language = "python")
        {
            var systemPrompt = $"You are an expert {language} programmer. Generate clean, efficient, and well-documented code.";
            SystemMessage = systemPrompt;
            Temperature = 0.1f; // Lower temperature for code generation
            return this;
        }

        /// <summary>
        /// Optimizes settings for mathematical reasoning
        /// </summary>
        public DeepSeekService WithMathMode()
        {
            SystemMessage = "You are a mathematics expert. Solve problems step by step, showing all work clearly.";
            Temperature = 0.2f;
            return this;
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
