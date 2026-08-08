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
    public partial class XAIService : OpenAICompatibleService
    {
        public override string Provider => nameof(AIProvider.xAI);

        /// <summary>
        /// Reasoning effort for configurable Grok models. Auto leaves the provider default intact.
        /// Grok 4.3 accepts None, Low, Medium, and High; Grok 4.5 accepts Low, Medium, and High.
        /// </summary>
        public GrokReasoning ReasoningEffort { get; set; } = GrokReasoning.Auto;

        protected override uint GetModelMaxOutputTokens()
        {
            var model = Model?.ToLower() ?? "";
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
            var policy = (CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            CurrentPolicy = null;

            using var cts = policy.TimeoutSeconds.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds.Value))
                : new CancellationTokenSource();

            ChatBlock? originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                Stream = false;
                ActivateChat.Messages.Add(message);

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    var result = await ProcessSingleRoundAsync(round, policy, cts.Token);
                    if (result.IsComplete)
                        return result.Content;
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

            bool useFunctions = ShouldUseFunctions;
            var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();

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

            var responseContent = await response.Content.ReadAsStringAsync();

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
                    new Dictionary<string, object> { ["model"] = Model });
                AddFunctionResultBatchToHistory(
                    results,
                    new Dictionary<string, object> { ["model"] = Model });
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

            if (!string.IsNullOrEmpty(SystemMessage))
            {
                allMessagesBuilder.Append(SystemMessage).Append('\n');
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
        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        /// <summary>
        /// Switches to the current Grok flagship reasoning model (grok-4.5).
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
            if (profile.DisableReasoning != true)
                return base.ApplyProviderSpecificRequestProfile(profile);

            var backupReasoningEffort = ReasoningEffort;
            ReasoningEffort = GetMinimumReasoningEffortForModel();

            return () =>
            {
                ReasoningEffort = backupReasoningEffort;
            };
        }

        /// <summary>
        /// Gets completion with Chain of Thought prompting
        /// </summary>
        public async Task<string> GetCompletionWithCoTAsync(string prompt)
        {
            var cotPrompt = $"{prompt}\n\nPlease think step by step and show your reasoning process.";
            return await GetCompletionAsync(cotPrompt);
        }

        #endregion
    }
}
