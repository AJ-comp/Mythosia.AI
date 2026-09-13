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
        private const uint MinimumReasoningInternalProfileOutputTokens = 4096;

        public override string Provider => nameof(AIProvider.OpenAI);

        protected override uint GetModelMaxOutputTokens()
        {
            var model = RequestModel?.ToLower() ?? "";
            if (model.StartsWith("o3")) return 100000;
            if (IsGpt6Model(model)) return 128000;
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
        /// OpenAI timeout policy. GPT-5, GPT-6, and slow pro reasoning workloads (legacy *-pro
        /// model IDs and reasoning.mode=pro) can take longer than the 100s default, so
        /// when the default is in effect they get longer timeouts. Explicit non-default timeouts
        /// are respected.
        /// </summary>
        protected override int? ResolveRequestTimeoutSeconds(FunctionCallingPolicy policy)
        {
            const int DefaultTimeout = 100;
            const int ReasoningModelTimeout = 300;
            const int ProModelTimeout = 600;
            var seconds = policy?.TimeoutSeconds;
            var model = RequestModel?.ToLowerInvariant() ?? string.Empty;
            if (seconds == DefaultTimeout)
            {
                if (model.Contains("-pro") ||
                    (model.StartsWith("gpt-5.6", StringComparison.OrdinalIgnoreCase) &&
                     RequestGpt5_6ReasoningMode == global::Mythosia.AI.Models.Gpt5_6ReasoningMode.Pro) ||
                    (IsGpt6Model(model) &&
                     RequestGpt6ReasoningMode == global::Mythosia.AI.Models.Gpt6ReasoningMode.Pro))
                {
                    return ProModelTimeout;
                }

                if (string.Equals(model, "gpt-5", StringComparison.OrdinalIgnoreCase) || IsGpt6Model(model))
                    return ReasoningModelTimeout;
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
            RequestCancellationToken.ThrowIfCancellationRequested();
            using var requestScope = BeginRequestSettingsScope();
            using var featureScope = BeginRequestFeaturesScope(message);
            LastReasoningSummary = null;

            var policy = GetExecutionPolicy();

            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var cts = CreateRequestTimeoutCts(policy);

            // Stateless mode handling
            ChatBlock? originalChat = null;
            if (RequestStatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = RequestSystemMessage };
            }

            Func<Task>? cleanupAsyncFunctions = null;
            try
            {
                cleanupAsyncFunctions = BeginAsyncFunctionScope(ShouldUseFunctions);
                SetExecutionSetting(nameof(Stream), false);
                cts.Token.ThrowIfCancellationRequested();
                ActivateChat.Messages.Add(message);
                var firstResponseMessageIndex = ActivateChat.Messages.Count;

                // Main loop for function calling
                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var result = await ProcessSingleRoundAsync(round, policy, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    if (result.IsComplete)
                    {
                        if (UsedAsyncFunctions)
                        {
                            return string.Join(
                                Environment.NewLine + Environment.NewLine,
                                ActivateChat.Messages.Skip(firstResponseMessageIndex)
                                    .Where(item => item.Role == ActorRole.Assistant &&
                                                   !string.IsNullOrEmpty(item.Content))
                                    .Select(item => item.Content));
                        }
                        return result.Content;
                    }
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
                throw new AIServiceException($"Request timeout after {timeoutSeconds} seconds", exception);
            }
            finally
            {
                try
                {
                    if (cleanupAsyncFunctions != null)
                        await cleanupAsyncFunctions();
                }
                finally
                {
                    RollbackUnacceptedReasoning();
                    if (originalChat != null)
                        ActivateChat = originalChat;
                }
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
            var responseContent = await SendApiRequestAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // 2. Process response
            bool isResponsesApi = IsNewApiModel(RequestModel);

            // A Responses API payload is not safe to consume until the top-level response has
            // completed successfully. Validate before extracting or executing any tool call.
            if (isResponsesApi)
            {
                EnsureCompletedResponsesApiResponse(responseContent);
                AcceptPreservedReasoning();
                CaptureResponseCitations(responseContent);
            }

            // 3. Handle based on function support
            bool useFunctions = ShouldUseFunctions;

            var result = useFunctions
                ? await ProcessFunctionResponseAsync(
                    responseContent,
                    policy,
                    isResponsesApi,
                    cancellationToken)
                : ProcessRegularResponseAsync(responseContent, isResponsesApi);
            RememberPreservedHistory();
            return result;
        }

        /// <summary>
        /// Send API request
        /// </summary>
        private async Task<string> SendApiRequestAsync(CancellationToken cancellationToken)
        {
            bool useFunctions = ShouldUseFunctions;

            cancellationToken.ThrowIfCancellationRequested();
            using var request = useFunctions
                ? CreateFunctionMessageRequest()
                : CreateMessageRequest();

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseContent = await ReadCompletionResponseBodyAsync(response, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, responseContent);

            return responseContent;
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
            cancellationToken.ThrowIfCancellationRequested();
            var (content, functionCalls) = ExtractFunctionCalls(responseContent);

            if (functionCalls.Calls.Count > 0)
            {
                var functionMessageMetadata = new Dictionary<string, object>
                {
                    ["model"] = RequestModel
                };
                if (functionCalls.Metadata?.TryGetValue(
                        "function_finish_reason_mismatch",
                        out var finishReasonMismatch) == true)
                {
                    functionMessageMetadata["function_finish_reason_mismatch"] =
                        finishReasonMismatch;
                }
                await ProcessFunctionBatchForRoundAsync(
                    content,
                    functionCalls,
                    functionMessageMetadata,
                    policy,
                    cancellationToken);
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

            if (isResponsesApi && UsedAsyncFunctions)
            {
                // Pending calls can span responses that contain only reasoning. Replay every
                // output item so the subsequent tool result continues from the latest state.
                using var document = JsonDocument.Parse(responseContent);
                var assistantMessage = new Message(ActorRole.Assistant, content ?? string.Empty);
                if (document.RootElement.TryGetProperty("output", out var output) &&
                    output.ValueKind == JsonValueKind.Array)
                {
                    assistantMessage.Metadata = new Dictionary<string, object>
                    {
                        [ResponsesOutputItemsMetadataKey] = output.EnumerateArray()
                            .Select(item => item.Clone()).ToList()
                    };
                }
                ActivateChat.Messages.Add(assistantMessage);
            }
            else if (!string.IsNullOrEmpty(content) || (isResponsesApi && ShouldPreserveResponseItems))
            {
                ActivateChat.Messages.Add(CreateResponsesAssistantMessage(content, responseContent, isResponsesApi));
            }

            if (HasPendingAsyncFunctions)
            {
                await CollectAsyncFunctionResultsAsync(true, cancellationToken);
                return RoundResult.Continue();
            }

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

            if (!string.IsNullOrEmpty(result) || (isResponsesApi && ShouldPreserveResponseItems))
                ActivateChat.Messages.Add(CreateResponsesAssistantMessage(result, responseContent, isResponsesApi));

            return RoundResult.Complete(result ?? string.Empty);
        }

        #endregion

        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var requestBody = BuildRequestBody();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Determine endpoint based on model
            string endpoint = IsNewApiModel(RequestModel)
                ? (RequestStream ? "responses?stream=true" : "responses")
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
            if (!string.IsNullOrEmpty(RequestSystemMessage))
            {
                allMessagesBuilder.Append(RequestSystemMessage).Append('\n');
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

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCancellationToken.ThrowIfCancellationRequested();
            var currentModel = RequestModel;

            bool supportsVision = IsGpt6Model(currentModel) ||
                                 currentModel.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                                 currentModel.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                                 currentModel.StartsWith("gpt-4.1", StringComparison.OrdinalIgnoreCase) ||
                                 currentModel.Contains("gpt-4o") ||
                                 currentModel.Contains("gpt-4-turbo") ||
                                 currentModel.Contains("vision");

            if (!supportsVision)
            {
                ChangeModel(AIModels.OpenAI.Gpt4_1);
                Console.WriteLine($"[GetCompletionWithImageAsync] Switched from {currentModel} to {RequestModel} for vision support");
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath, cancellationToken);
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
        /// GPT-6 reasoning effort level. Auto uses the library default of Medium.
        /// Reasoning cannot be disabled; Low is the minimum supported effort.
        /// </summary>
        public Gpt6Reasoning Gpt6ReasoningEffort { get; set; } = Gpt6Reasoning.Auto;

        /// <summary>
        /// GPT-6 reasoning summary mode. Defaults to Auto.
        /// Set to null to omit summaries while keeping reasoning enabled.
        /// </summary>
        public ReasoningSummary? Gpt6ReasoningSummary { get; set; } = ReasoningSummary.Auto;

        /// <summary>
        /// GPT-6 verbosity level. Null uses Medium.
        /// </summary>
        public Verbosity? Gpt6Verbosity { get; set; }

        /// <summary>
        /// GPT-6 reasoning execution mode. Pro is an API parameter, not a separate model ID.
        /// </summary>
        public Gpt6ReasoningMode Gpt6ReasoningMode { get; set; } = global::Mythosia.AI.Models.Gpt6ReasoningMode.Standard;

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

        /// <summary>
        /// Sets GPT-6 specific parameters.
        /// Reasoning effort: Low, Medium (library default), High, XHigh, Max.
        /// Verbosity: Low, Medium (default), High.
        /// Pro is a reasoning mode and does not change the selected model ID.
        /// </summary>
        public OpenAIService WithGpt6Parameters(
            Gpt6Reasoning reasoningEffort = Gpt6Reasoning.Medium,
            Verbosity verbosity = Verbosity.Medium,
            ReasoningSummary? reasoningSummary = ReasoningSummary.Auto,
            Gpt6ReasoningMode reasoningMode = Gpt6ReasoningMode.Standard)
        {
            Gpt6ReasoningEffort = reasoningEffort;
            Gpt6Verbosity = verbosity;
            Gpt6ReasoningSummary = reasoningSummary;
            Gpt6ReasoningMode = reasoningMode;
            Console.WriteLine($"[GPT-6 Config] Reasoning: {reasoningEffort}, Verbosity: {verbosity}, Summary: {reasoningSummary?.ToString() ?? "disabled"}, Mode: {reasoningMode}");
            return this;
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ApplyOpenAIProfileSettings(profile);
            return () => { };
        }

        protected override void ApplyCapabilityRequestProfile(AIRequestProfile profile)
            => ApplyOpenAIProfileSettings(profile);

        // Shared native flags only; execution-specific output reservations remain outside this helper.
        private void ApplyOpenAIProfileSettings(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return;

            SetExecutionSetting(nameof(Gpt5ReasoningEffort), Gpt5Reasoning.Minimal);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt5ReasoningSummary), null);
            SetExecutionSetting<ReasoningSummary?>(nameof(O3ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt5_1ReasoningEffort), Gpt5_1Reasoning.None);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt5_1ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt5_2ReasoningEffort), Gpt5_2Reasoning.None);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt5_2ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt5_3ReasoningEffort), Gpt5_3Reasoning.None);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt5_3ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt5_4ReasoningEffort), Gpt5_4Reasoning.None);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt5_4ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt5_5ReasoningEffort), Gpt5_5Reasoning.None);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt5_5ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt5_6ReasoningEffort), Gpt5_6Reasoning.None);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt5_6ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt5_6ReasoningMode), global::Mythosia.AI.Models.Gpt5_6ReasoningMode.Standard);
            SetExecutionSetting(nameof(Gpt6ReasoningEffort), Gpt6Reasoning.Low);
            SetExecutionSetting<ReasoningSummary?>(nameof(Gpt6ReasoningSummary), null);
            SetExecutionSetting(nameof(Gpt6ReasoningMode), global::Mythosia.AI.Models.Gpt6ReasoningMode.Standard);

        }

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            var restore = base.ApplyRequestProfile(profile);

            if (profile.DisableReasoning == true &&
                (profile.Purpose == AIRequestPurpose.Summarization ||
                 profile.Purpose == AIRequestPurpose.QueryRewrite) &&
                profile.MaxTokens.HasValue &&
                (RequestModel.StartsWith("gpt-5-pro", StringComparison.OrdinalIgnoreCase) || IsGpt6Model(RequestModel)))
            {
                // gpt-5-pro and GPT-6 always reason. Library-owned profiles that try
                // to disable reasoning (for example summarization and query rewriting)
                // still count hidden reasoning against the same output budget and can
                // finish as `incomplete` before producing text. Reserve enough room only
                // for this internal request, then restore the caller's MaxTokens value.
                SetExecutionSetting(nameof(MaxTokens), Math.Min(
                    GetModelMaxOutputTokens(),
                    Math.Max(profile.MaxTokens.Value, MinimumReasoningInternalProfileOutputTokens)));
            }

            return restore;
        }

        #endregion
    }
}
