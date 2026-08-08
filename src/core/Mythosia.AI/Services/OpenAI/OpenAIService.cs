using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService : OpenAICompatibleService, IImageGenerationService
    {
        private const uint MinimumGpt5ProInternalProfileOutputTokens = 4096;

        public override string Provider => nameof(AIProvider.OpenAI);

        protected override uint GetModelMaxOutputTokens()
        {
            var model = Model?.ToLower() ?? "";
            if (model.StartsWith("o3")) return 100000;
            if (model == "gpt-5-pro") return 272000;
            if (model.StartsWith("gpt-5.4")) return 128000;
            if (model.StartsWith("gpt-5.3")) return 128000;
            if (model.StartsWith("gpt-5")) return 128000;
            if (model.StartsWith("gpt-4.1")) return 32768;
            if (model.Contains("4o-mini")) return 16384;
            if (model.Contains("4o")) return 16384;
            if (model.Contains("vision")) return 4096;
            return 16384;  // safe default
        }

        public OpenAIService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.openai.com/v1/", httpClient)
        {
            Model = AIModels.OpenAI.Gpt4_1;
            MaxTokens = 16000;

            // The per-request FunctionCallingPolicy timeout (resolved via ResolveRequestTimeoutSeconds)
            // is the single timeout authority. Disable HttpClient's own timeout so it never caps it.
            // Every OpenAI request path bounds itself with CreateRequestTimeoutCts.
            try { HttpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan; }
            catch (InvalidOperationException) { /* client already used elsewhere; leave as configured */ }
        }

        /// <summary>
        /// OpenAI timeout policy. GPT-5 and slow pro reasoning workloads (legacy *-pro model IDs
        /// and GPT-5.6 reasoning.mode=pro) can legitimately take longer than the 100s default, so
        /// when the default is in effect they get longer timeouts. Explicit non-default timeouts
        /// are respected.
        /// </summary>
        protected override int? ResolveRequestTimeoutSeconds(FunctionCallingPolicy policy)
        {
            const int DefaultTimeout = 100;
            const int Gpt5Timeout = 300;
            const int ProModelTimeout = 600;
            var seconds = policy?.TimeoutSeconds;
            var model = Model?.ToLowerInvariant() ?? string.Empty;
            if (seconds == DefaultTimeout)
            {
                if (model.Contains("-pro") ||
                    (model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase) &&
                     Gpt5_6ReasoningMode == global::Mythosia.AI.Models.Gpt5_6ReasoningMode.Pro))
                {
                    return ProModelTimeout;
                }

                if (string.Equals(model, "gpt-5", StringComparison.OrdinalIgnoreCase))
                    return Gpt5Timeout;
            }

            return seconds;
        }

        /// <summary>
        /// Creates a OpenAIService with a specific model.
        /// </summary>
        public OpenAIService(string apiKey, string model, HttpClient httpClient)
            : this(apiKey, httpClient)
        {
            ChangeModel(model);
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            LastReasoningSummary = null;

            var policy = (CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            CurrentPolicy = null;

            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var cts = CreateRequestTimeoutCts(policy);

            // Stateless mode handling
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

                // Main loop for function calling
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
                throw new AIServiceException($"Request timeout after {timeoutSeconds} seconds");
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        /// <summary>
        /// Process a single round of API interaction
        /// </summary>
        private async Task<RoundResult> ProcessSingleRoundAsync(
            int round,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            if (policy.EnableLogging)
                Console.WriteLine($"[Round {round + 1}/{policy.MaxRounds}]");

            // 1. Send API request
            var response = await SendApiRequestAsync(cancellationToken);

            // 2. Process response
            var responseContent = await response.Content.ReadAsStringAsync();
            bool isResponsesApi = IsNewApiModel(Model);

            // A Responses API payload is not safe to consume until the top-level response has
            // completed successfully. Validate before extracting or executing any tool call.
            if (isResponsesApi)
                EnsureCompletedResponsesApiResponse(responseContent);

            // 3. Handle based on function support
            bool useFunctions = ShouldUseFunctions;

            if (useFunctions)
                return await ProcessFunctionResponseAsync(
                    responseContent,
                    policy,
                    isResponsesApi,
                    cancellationToken);

            return ProcessRegularResponseAsync(responseContent, isResponsesApi);
        }

        /// <summary>
        /// Send API request
        /// </summary>
        private async Task<HttpResponseMessage> SendApiRequestAsync(CancellationToken cancellationToken)
        {
            bool useFunctions = ShouldUseFunctions;

            var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, errorContent);
            }

            return response;
        }

        /// <summary>
        /// Process response with function calling
        /// </summary>
        private async Task<RoundResult> ProcessFunctionResponseAsync(
            string responseContent,
            FunctionCallingPolicy policy,
            bool isResponsesApi,
            CancellationToken cancellationToken)
        {
            var (content, functionCalls) = ExtractFunctionCalls(responseContent);

            if (functionCalls.Calls.Count > 0)
            {
                var results = await ProcessFunctionCallsAsync(
                    functionCalls,
                    policy,
                    cancellationToken);
                var functionMessageMetadata = new Dictionary<string, object>
                {
                    ["model"] = Model
                };
                if (functionCalls.Metadata?.TryGetValue(
                        "function_finish_reason_mismatch",
                        out var finishReasonMismatch) == true)
                {
                    functionMessageMetadata["function_finish_reason_mismatch"] =
                        finishReasonMismatch;
                }
                AddFunctionCallBatchToHistory(
                    content,
                    functionCalls,
                    functionMessageMetadata);
                AddFunctionResultBatchToHistory(
                    results,
                    new Dictionary<string, object> { ["model"] = Model });
                return RoundResult.Continue();
            }

            if (!isResponsesApi)
            {
                var finishReason = ExtractLegacyFinishReason(responseContent);
                if (!string.IsNullOrEmpty(finishReason))
                {
                    if (!string.Equals(finishReason, "stop", StringComparison.Ordinal))
                    {
                        throw new AIServiceException(
                            $"OpenAI Chat Completions ended with finish_reason={finishReason}; the partial response was not saved.");
                    }

                    if (!string.IsNullOrEmpty(content))
                        ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));
                    return RoundResult.Complete(content ?? string.Empty);
                }
            }

            if (string.IsNullOrEmpty(content) && !isResponsesApi)
                return RoundResult.Continue();

            if (!string.IsNullOrEmpty(content))
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, content));

            // A completed empty Responses result is terminal. Retrying it would only repeat
            // billing and cannot manufacture text or a missing function call.
            return RoundResult.Complete(content ?? string.Empty);
        }

        /// <summary>
        /// Process regular response (no functions)
        /// </summary>
        private RoundResult ProcessRegularResponseAsync(string responseContent, bool isResponsesApi)
        {
            var result = ExtractResponseContent(responseContent);
            if (!isResponsesApi)
            {
                var finishReason = ExtractLegacyFinishReason(responseContent);
                if (!string.IsNullOrEmpty(finishReason) &&
                    !string.Equals(finishReason, "stop", StringComparison.Ordinal))
                {
                    throw new AIServiceException(
                        $"OpenAI Chat Completions ended with finish_reason={finishReason}; the partial response was not saved.");
                }

                if (string.Equals(finishReason, "stop", StringComparison.Ordinal) &&
                    string.IsNullOrEmpty(result))
                {
                    return RoundResult.Complete(string.Empty);
                }
            }

            if (string.IsNullOrEmpty(result) && !isResponsesApi)
                return RoundResult.Continue();

            if (!string.IsNullOrEmpty(result))
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, result));

            return RoundResult.Complete(result ?? string.Empty);
        }

        #endregion

        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Determine endpoint based on model
            string endpoint = IsNewApiModel(Model)
                ? (Stream ? "responses?stream=true" : "responses")
                : "chat/completions";

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };

            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        #endregion

        #region Token Counting

        public override async Task<uint> GetInputTokenCountAsync()
        {
            var encoding = TikToken.EncodingForModel("gpt-4o");
            var allMessagesBuilder = new StringBuilder();

            // Add system message
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                allMessagesBuilder.Append(ActivateChat.SystemMessage).Append('\n');
            }

            // Add all messages
            foreach (var message in GetLatestMessages())
            {
                if (!message.HasMultimodalContent)
                {
                    allMessagesBuilder.Append(message.Role).Append('\n')
                                      .Append(message.Content).Append('\n');
                    continue;
                }

                foreach (var content in message.Contents)
                {
                    if (content is TextContent textContent)
                        allMessagesBuilder.Append(textContent.Text).Append('\n');
                    else if (content is ImageContent)
                        allMessagesBuilder.Append("[IMAGE]").Append('\n');
                }
            }

            var textTokens = (uint)encoding.Encode(allMessagesBuilder.ToString()).Count;

            // Add image tokens
            var imageTokens = ActivateChat.Messages
                .SelectMany(m => m.Contents)
                .OfType<ImageContent>()
                .Sum(img => img.EstimateTokens());

            return await Task.FromResult(textTokens + (uint)imageTokens);
        }

        public override async Task<uint> GetInputTokenCountAsync(string prompt)
        {
            var encoding = TikToken.EncodingForModel("gpt-4o");
            return await Task.FromResult((uint)encoding.Encode(prompt).Count);
        }

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            var currentModel = Model;

            bool supportsVision = currentModel.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                                 currentModel.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                                 currentModel.StartsWith("gpt-4.1", StringComparison.OrdinalIgnoreCase) ||
                                 currentModel.Contains("gpt-4o") ||
                                 currentModel.Contains("gpt-4-turbo") ||
                                 currentModel.Contains("vision");

            if (!supportsVision)
            {
                ChangeModel(AIModels.OpenAI.Gpt4_1);
                Console.WriteLine($"[GetCompletionWithImageAsync] Switched from {currentModel} to {Model} for vision support");
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        #endregion

        #region OpenAI-Specific Features

        /// <summary>
        /// Fine-tunes the response with specific OpenAI parameters
        /// </summary>
        public OpenAIService WithOpenAIParameters(float? presencePenalty = null, float? frequencyPenalty = null, int? bestOf = null)
        {
            if (presencePenalty.HasValue) PresencePenalty = presencePenalty.Value;
            if (frequencyPenalty.HasValue) FrequencyPenalty = frequencyPenalty.Value;
            return this;
        }

        /// <summary>
        /// GPT-5 reasoning effort level.
        /// GPT-5 defaults to Medium.
        /// </summary>
        public Gpt5Reasoning Gpt5ReasoningEffort { get; set; } = Gpt5Reasoning.Auto;

        /// <summary>
        /// GPT-5 reasoning summary mode.
        /// Defaults to Auto. Set to null to disable reasoning summaries.
        /// </summary>
        public ReasoningSummary? Gpt5ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// o3 reasoning summary mode. This is opt-in because OpenAI requires a verified
        /// organization for reasoning summaries. Leave null to request ordinary o3 reasoning
        /// without a summary.
        /// </summary>
        public ReasoningSummary? O3ReasoningSummary { get; set; }

        /// <summary>
        /// GPT-5.1 reasoning effort level.
        /// GPT-5.1 defaults to None.
        /// </summary>
        public Gpt5_1Reasoning Gpt5_1ReasoningEffort { get; set; } = Gpt5_1Reasoning.Auto;

        /// <summary>
        /// GPT-5.1 reasoning summary mode.
        /// Defaults to Auto. Set to null to disable reasoning summaries.
        /// </summary>
        public ReasoningSummary? Gpt5_1ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// GPT-5.1 verbosity level.
        /// GPT-5.1 defaults to Medium.
        /// </summary>
        public Verbosity? Gpt5_1Verbosity { get; set; }

        /// <summary>
        /// GPT-5.2 reasoning effort level.
        /// GPT-5.2 defaults to None. GPT-5.2 Pro defaults to Medium.
        /// </summary>
        public Gpt5_2Reasoning Gpt5_2ReasoningEffort { get; set; } = Gpt5_2Reasoning.Auto;

        /// <summary>
        /// GPT-5.2 reasoning summary mode.
        /// Defaults to Auto. Set to null to disable reasoning summaries.
        /// </summary>
        public ReasoningSummary? Gpt5_2ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// GPT-5.2 verbosity level.
        /// GPT-5.2 defaults to Medium.
        /// </summary>
        public Verbosity? Gpt5_2Verbosity { get; set; }

        /// <summary>
        /// GPT-5.3 reasoning effort level.
        /// GPT-5.3 Codex defaults to Medium.
        /// </summary>
        public Gpt5_3Reasoning Gpt5_3ReasoningEffort { get; set; } = Gpt5_3Reasoning.Auto;

        /// <summary>
        /// GPT-5.3 reasoning summary mode.
        /// Defaults to Auto. Set to null to disable reasoning summaries.
        /// </summary>
        public ReasoningSummary? Gpt5_3ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// GPT-5.3 verbosity level.
        /// GPT-5.3 defaults to Medium.
        /// </summary>
        public Verbosity? Gpt5_3Verbosity { get; set; }

        /// <summary>
        /// GPT-5.4 reasoning effort level.
        /// GPT-5.4 defaults to None. GPT-5.4 Pro defaults to Medium.
        /// </summary>
        public Gpt5_4Reasoning Gpt5_4ReasoningEffort { get; set; } = Gpt5_4Reasoning.Auto;

        /// <summary>
        /// GPT-5.4 reasoning summary mode.
        /// Defaults to Auto. Set to null to disable reasoning summaries.
        /// </summary>
        public ReasoningSummary? Gpt5_4ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// GPT-5.4 verbosity level.
        /// GPT-5.4 defaults to Medium.
        /// </summary>
        public Verbosity? Gpt5_4Verbosity { get; set; }

        /// <summary>
        /// GPT-5.5 reasoning effort level.
        /// GPT-5.5 defaults to Medium. GPT-5.5 Pro defaults to High.
        /// </summary>
        public Gpt5_5Reasoning Gpt5_5ReasoningEffort { get; set; } = Gpt5_5Reasoning.Auto;

        /// <summary>
        /// GPT-5.5 reasoning summary mode.
        /// Defaults to Auto. Set to null to disable reasoning summaries.
        /// </summary>
        public ReasoningSummary? Gpt5_5ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// GPT-5.5 verbosity level.
        /// GPT-5.5 defaults to Medium.
        /// </summary>
        public Verbosity? Gpt5_5Verbosity { get; set; }

        /// <summary>
        /// GPT-5.6 reasoning effort level. The model default is Medium.
        /// </summary>
        public Gpt5_6Reasoning Gpt5_6ReasoningEffort { get; set; } = Gpt5_6Reasoning.Auto;

        /// <summary>
        /// GPT-5.6 reasoning summary mode.
        /// Defaults to Auto. Set to null to disable reasoning summaries.
        /// </summary>
        public ReasoningSummary? Gpt5_6ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// GPT-5.6 verbosity level. The model default is Medium.
        /// </summary>
        public Verbosity? Gpt5_6Verbosity { get; set; }

        /// <summary>
        /// GPT-5.6 reasoning execution mode. Pro mode is an API parameter, not a separate model ID.
        /// </summary>
        public Gpt5_6ReasoningMode Gpt5_6ReasoningMode { get; set; } = global::Mythosia.AI.Models.Gpt5_6ReasoningMode.Standard;

        /// <summary>
        /// Contains the reasoning summary from the last non-streaming API call when the provider
        /// returns a reasoning output item. Remains null when summaries are disabled or the
        /// provider omits the optional summary output despite accepting reasoning.summary.
        /// </summary>
        public string? LastReasoningSummary { get; private set; }

        /// <summary>
        /// Sets GPT-5 specific parameters.
        /// Reasoning effort: Minimal, Low, Medium (default), High.
        /// Reasoning summary: Auto (default), Concise, Detailed, or null to disable.
        /// </summary>
        public OpenAIService WithGpt5Parameters(Gpt5Reasoning reasoningEffort = Gpt5Reasoning.Medium, ReasoningSummary? reasoningSummary = ReasoningSummary.Auto)
        {
            Gpt5ReasoningEffort = reasoningEffort;
            Gpt5ReasoningSummary = reasoningSummary;
            Console.WriteLine($"[GPT-5 Config] Reasoning: {reasoningEffort}, Summary: {reasoningSummary?.ToString() ?? "disabled"}");
            return this;
        }

        /// <summary>
        /// Sets o3 reasoning parameters. Reasoning summaries are disabled by default and require
        /// an OpenAI organization that is verified for summary generation.
        /// </summary>
        public OpenAIService WithO3Parameters(
            Gpt5Reasoning reasoningEffort = Gpt5Reasoning.Medium,
            ReasoningSummary? reasoningSummary = null)
        {
            Gpt5ReasoningEffort = reasoningEffort;
            O3ReasoningSummary = reasoningSummary;
            return this;
        }

        /// <summary>
        /// Sets GPT-5.1 specific parameters.
        /// Reasoning effort: None (default), Low, Medium, High.
        /// Verbosity: Low, Medium (default), High.
        /// Reasoning summary: Auto (default), Concise, Detailed, or null to disable.
        /// </summary>
        public OpenAIService WithGpt5_1Parameters(Gpt5_1Reasoning reasoningEffort = Gpt5_1Reasoning.None, Verbosity verbosity = Verbosity.Medium, ReasoningSummary? reasoningSummary = ReasoningSummary.Auto)
        {
            Gpt5_1ReasoningEffort = reasoningEffort;
            Gpt5_1Verbosity = verbosity;
            Gpt5_1ReasoningSummary = reasoningSummary;
            Console.WriteLine($"[GPT-5.1 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}, Summary: {reasoningSummary?.ToString() ?? "disabled"}");
            return this;
        }

        /// <summary>
        /// Sets GPT-5.2 specific parameters.
        /// Reasoning effort: None (default), Low, Medium, High, XHigh. GPT-5.2 Pro supports Medium, High, XHigh. GPT-5.2 Codex supports Low, Medium (default), High, XHigh.
        /// Verbosity: Low, Medium (default), High.
        /// Reasoning summary: Auto (default), Concise, Detailed, or null to disable.
        /// </summary>
        public OpenAIService WithGpt5_2Parameters(Gpt5_2Reasoning reasoningEffort = Gpt5_2Reasoning.None, Verbosity verbosity = Verbosity.Medium, ReasoningSummary? reasoningSummary = ReasoningSummary.Auto)
        {
            Gpt5_2ReasoningEffort = reasoningEffort;
            Gpt5_2Verbosity = verbosity;
            Gpt5_2ReasoningSummary = reasoningSummary;
            Console.WriteLine($"[GPT-5.2 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}, Summary: {reasoningSummary?.ToString() ?? "disabled"}");
            return this;
        }

        /// <summary>
        /// Sets GPT-5.3 specific parameters.
        /// Reasoning effort: None (default for Instant), Low, Medium (default for Codex), High, XHigh. GPT-5.3 Codex supports Low, Medium (default), High, XHigh.
        /// Verbosity: Low, Medium (default), High.
        /// Reasoning summary: Auto (default), Concise, Detailed, or null to disable.
        /// </summary>
        public OpenAIService WithGpt5_3Parameters(Gpt5_3Reasoning reasoningEffort = Gpt5_3Reasoning.None, Verbosity verbosity = Verbosity.Medium, ReasoningSummary? reasoningSummary = ReasoningSummary.Auto)
        {
            Gpt5_3ReasoningEffort = reasoningEffort;
            Gpt5_3Verbosity = verbosity;
            Gpt5_3ReasoningSummary = reasoningSummary;
            Console.WriteLine($"[GPT-5.3 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}, Summary: {reasoningSummary?.ToString() ?? "disabled"}");
            return this;
        }

        /// <summary>
        /// Sets GPT-5.4 specific parameters.
        /// Reasoning effort: None (default), Low, Medium, High, XHigh. GPT-5.4 Pro supports Medium, High, XHigh.
        /// Verbosity: Low, Medium (default), High.
        /// Reasoning summary: Auto (default), Concise, Detailed, or null to disable.
        /// </summary>
        public OpenAIService WithGpt5_4Parameters(Gpt5_4Reasoning reasoningEffort = Gpt5_4Reasoning.None, Verbosity verbosity = Verbosity.Medium, ReasoningSummary? reasoningSummary = ReasoningSummary.Auto)
        {
            Gpt5_4ReasoningEffort = reasoningEffort;
            Gpt5_4Verbosity = verbosity;
            Gpt5_4ReasoningSummary = reasoningSummary;
            Console.WriteLine($"[GPT-5.4 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}, Summary: {reasoningSummary?.ToString() ?? "disabled"}");
            return this;
        }

        /// <summary>
        /// Sets GPT-5.5 specific parameters.
        /// Reasoning effort: None, Low, Medium (default), High, XHigh. GPT-5.5 Pro defaults to High.
        /// Verbosity: Low, Medium (default), High.
        /// Reasoning summary: Auto (default), Concise, Detailed, or null to disable.
        /// </summary>
        public OpenAIService WithGpt5_5Parameters(Gpt5_5Reasoning reasoningEffort = Gpt5_5Reasoning.Auto, Verbosity verbosity = Verbosity.Medium, ReasoningSummary? reasoningSummary = ReasoningSummary.Auto)
        {
            Gpt5_5ReasoningEffort = reasoningEffort;
            Gpt5_5Verbosity = verbosity;
            Gpt5_5ReasoningSummary = reasoningSummary;
            Console.WriteLine($"[GPT-5.5 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}, Summary: {reasoningSummary?.ToString() ?? "disabled"}");
            return this;
        }

        /// <summary>
        /// Sets GPT-5.6 specific parameters.
        /// Reasoning effort: None, Low, Medium (default), High, XHigh, Max.
        /// Verbosity: Low, Medium (default), High.
        /// Pro is a reasoning mode and does not change the selected model ID.
        /// </summary>
        public OpenAIService WithGpt5_6Parameters(
            Gpt5_6Reasoning reasoningEffort = Gpt5_6Reasoning.Medium,
            Verbosity verbosity = Verbosity.Medium,
            ReasoningSummary? reasoningSummary = ReasoningSummary.Auto,
            Gpt5_6ReasoningMode reasoningMode = Gpt5_6ReasoningMode.Standard)
        {
            Gpt5_6ReasoningEffort = reasoningEffort;
            Gpt5_6Verbosity = verbosity;
            Gpt5_6ReasoningSummary = reasoningSummary;
            Gpt5_6ReasoningMode = reasoningMode;
            Console.WriteLine($"[GPT-5.6 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}, Summary: {reasoningSummary?.ToString() ?? "disabled"}, Mode: {reasoningMode}");
            return this;
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return base.ApplyProviderSpecificRequestProfile(profile);

            var backupGpt5 = Gpt5ReasoningEffort;
            var backupGpt5Summary = Gpt5ReasoningSummary;
            var backupO3Summary = O3ReasoningSummary;
            var backupGpt51 = Gpt5_1ReasoningEffort;
            var backupGpt51Summary = Gpt5_1ReasoningSummary;
            var backupGpt52 = Gpt5_2ReasoningEffort;
            var backupGpt52Summary = Gpt5_2ReasoningSummary;
            var backupGpt53 = Gpt5_3ReasoningEffort;
            var backupGpt53Summary = Gpt5_3ReasoningSummary;
            var backupGpt54 = Gpt5_4ReasoningEffort;
            var backupGpt54Summary = Gpt5_4ReasoningSummary;
            var backupGpt55 = Gpt5_5ReasoningEffort;
            var backupGpt55Summary = Gpt5_5ReasoningSummary;
            var backupGpt56 = Gpt5_6ReasoningEffort;
            var backupGpt56Summary = Gpt5_6ReasoningSummary;
            var backupGpt56Mode = Gpt5_6ReasoningMode;

            Gpt5ReasoningEffort = Gpt5Reasoning.Minimal;
            Gpt5ReasoningSummary = null;
            O3ReasoningSummary = null;
            Gpt5_1ReasoningEffort = Gpt5_1Reasoning.None;
            Gpt5_1ReasoningSummary = null;
            Gpt5_2ReasoningEffort = Gpt5_2Reasoning.None;
            Gpt5_2ReasoningSummary = null;
            Gpt5_3ReasoningEffort = Gpt5_3Reasoning.None;
            Gpt5_3ReasoningSummary = null;
            Gpt5_4ReasoningEffort = Gpt5_4Reasoning.None;
            Gpt5_4ReasoningSummary = null;
            Gpt5_5ReasoningEffort = Gpt5_5Reasoning.None;
            Gpt5_5ReasoningSummary = null;
            Gpt5_6ReasoningEffort = Gpt5_6Reasoning.None;
            Gpt5_6ReasoningSummary = null;
            Gpt5_6ReasoningMode = global::Mythosia.AI.Models.Gpt5_6ReasoningMode.Standard;

            return () =>
            {
                Gpt5ReasoningEffort = backupGpt5;
                Gpt5ReasoningSummary = backupGpt5Summary;
                O3ReasoningSummary = backupO3Summary;
                Gpt5_1ReasoningEffort = backupGpt51;
                Gpt5_1ReasoningSummary = backupGpt51Summary;
                Gpt5_2ReasoningEffort = backupGpt52;
                Gpt5_2ReasoningSummary = backupGpt52Summary;
                Gpt5_3ReasoningEffort = backupGpt53;
                Gpt5_3ReasoningSummary = backupGpt53Summary;
                Gpt5_4ReasoningEffort = backupGpt54;
                Gpt5_4ReasoningSummary = backupGpt54Summary;
                Gpt5_5ReasoningEffort = backupGpt55;
                Gpt5_5ReasoningSummary = backupGpt55Summary;
                Gpt5_6ReasoningEffort = backupGpt56;
                Gpt5_6ReasoningSummary = backupGpt56Summary;
                Gpt5_6ReasoningMode = backupGpt56Mode;
            };
        }

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            var restore = base.ApplyRequestProfile(profile);

            if (profile.DisableReasoning == true &&
                (profile.Purpose == AIRequestPurpose.Summarization ||
                 profile.Purpose == AIRequestPurpose.QueryRewrite) &&
                profile.MaxTokens.HasValue &&
                Model.StartsWith("gpt-5-pro", StringComparison.OrdinalIgnoreCase))
            {
                // gpt-5-pro always uses high reasoning. Library-owned profiles that try
                // to disable reasoning (for example summarization and query rewriting)
                // still count hidden reasoning against the same output budget and can
                // finish as `incomplete` before producing text. Reserve enough room only
                // for this internal request, then restore the caller's MaxTokens value.
                MaxTokens = Math.Min(
                    GetModelMaxOutputTokens(),
                    Math.Max(profile.MaxTokens.Value, MinimumGpt5ProInternalProfileOutputTokens));
            }

            return restore;
        }

        #endregion
    }
}
