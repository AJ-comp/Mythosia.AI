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

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService : AIService
    {
        private const string AnthropicApiVersion = "2023-06-01";
        private const string DefaultImageMimeType = "image/jpeg";
        private const string SseDataPrefix = "data:";
        private const string SseEventPrefix = "event:";
        private const uint MinimumAnthropicSummaryOutputTokens = 1024;
        private bool _adaptiveThinkingExplicitlyRequested;

        public override string Provider => nameof(AIProvider.Anthropic);

        /// <summary>
        /// Controls the thinking token budget for Claude extended thinking.
        /// -1 requests disabled thinking where the model permits it. Fable 5 and Mythos 5 cannot
        /// disable thinking, so this setting uses their lowest effort and omits the readable summary instead.
        /// 1024+ is an exact token budget on manual-thinking models. On adaptive-thinking models,
        /// it retains the legacy high/xhigh/max effort mapping unless
        /// <see cref="AdaptiveThinkingEffort"/> is set explicitly.
        /// Supported models: Claude Fable 5, Claude Mythos 5, Claude Sonnet 5 / 4+, Claude Opus 5 / 4+,
        /// and Claude Haiku 4.5+.
        /// Note: adaptive-thinking models omit temperature; legacy manual-thinking models force it to 1.
        /// </summary>
        public int ThinkingBudget { get; set; } = -1;

        /// <summary>
        /// Selects the low/medium/high/xhigh/max effort range on adaptive-thinking Claude models.
        /// XHigh is rejected for Opus 4.6 and Sonnet 4.6 because those models do not support it.
        /// Auto preserves the legacy <see cref="ThinkingBudget"/> mapping using effort levels the
        /// selected model supports.
        /// </summary>
        public ClaudeReasoningEffort AdaptiveThinkingEffort { get; set; } = ClaudeReasoningEffort.Auto;

        /// <summary>
        /// Controls whether adaptive-thinking models return readable summarized reasoning.
        /// This value is sent only when adaptive thinking is explicitly enabled.
        /// </summary>
        public ClaudeThinkingDisplay AdaptiveThinkingDisplay { get; set; } = ClaudeThinkingDisplay.Summarized;

        /// <summary>
        /// Contains the thinking/reasoning content from the last non-streaming API call.
        /// Populated when the response contains a thinking block. This can occur even when
        /// <see cref="ThinkingBudget"/> is disabled for models whose adaptive thinking is always on.
        /// </summary>
        public string? LastThinkingContent { get; private set; }

        protected override uint GetModelMaxOutputTokens()
        {
            var model = Model?.ToLower() ?? "";
            if (model.Contains("fable-5")) return 128000;
            if (model.Contains("mythos-5")) return 128000;
            if (model.Contains("opus-5")) return 128000;
            if (model.Contains("sonnet-5")) return 128000;
            if (model.Contains("opus-4-8")) return 128000;
            if (model.Contains("opus-4-7")) return 128000;
            if (model.Contains("opus-4-6")) return 128000;
            if (model.Contains("sonnet-4-6")) return 128000;
            if (model.Contains("opus-4-5")) return 64000;
            if (model.Contains("sonnet-4-5")) return 64000;
            if (model.Contains("haiku-4-5")) return 64000;
            if (model.Contains("opus-4")) return 32768;
            if (model.Contains("sonnet-4")) return 16384;
            if (model.Contains("haiku-4")) return 8192;
            return 8192;  // safe default
        }

        public AnthropicService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.anthropic.com/v1/", httpClient)
        {
            Model = AIModels.Anthropic.ClaudeSonnet4_6;
            MaxTokens = 8192;
            Temperature = 0.7f;
        }

        /// <summary>
        /// Creates a AnthropicService with a specific model.
        /// </summary>
        public AnthropicService(string apiKey, string model, HttpClient httpClient)
            : this(apiKey, httpClient)
        {
            ChangeModel(model);
        }

        #region Core Completion Methods

        public override async Task<string> GetCompletionAsync(Message message)
        {
            // Get policy (current or default)
            var policy = (CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            CurrentPolicy = null;

            using var cts = policy.TimeoutSeconds.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(policy.TimeoutSeconds.Value))
                : new CancellationTokenSource();

            // Stateless 모드 처리 (ChatGpt 방식)
            ChatBlock? originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                LastThinkingContent = null;
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
                        throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, errorContent);
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (TryCreateRefusalException(responseContent, out var refusalException))
                    {
                        throw refusalException!;
                    }

                    var stopReason = ExtractStopReason(responseContent);
                    if (IsTruncationStopReason(stopReason))
                    {
                        throw CreateStopReasonException(
                            stopReason!,
                            "Claude stopped before completing the response; the partial response was not saved.");
                    }

                    if (string.Equals(stopReason, "pause_turn", StringComparison.Ordinal))
                    {
                        throw CreateStopReasonException(
                            stopReason!,
                            "Claude paused a server-tool turn, which this client-side tool loop cannot resume automatically.");
                    }

                    if (useFunctions)
                    {
                        var (textContent, functionCalls) = ExtractFunctionCalls(responseContent);
                        LastThinkingContent = ExtractThinkingContent(responseContent);

                        if (functionCalls.Calls.Count > 0)
                        {
                            if (!string.Equals(stopReason, "tool_use", StringComparison.Ordinal))
                            {
                                throw new AIServiceException(
                                    "Claude returned tool calls without stop_reason=tool_use; no tools were executed.",
                                    JsonSerializer.Serialize(new
                                    {
                                        stop_reason = stopReason ?? "missing",
                                        tool_use_count = functionCalls.Calls.Count
                                    }),
                                    nameof(AIProvider.Anthropic));
                            }

                            if (policy.EnableLogging)
                            {
                                Console.WriteLine($"  Executing {functionCalls.Calls.Count} function(s)");
                            }

                            await ProcessToolUseBatchAsync(
                                functionCalls,
                                textContent,
                                policy,
                                cts.Token);

                            // Continue the loop to get AI's response based on function results
                            continue;
                        }

                        if (string.Equals(stopReason, "tool_use", StringComparison.Ordinal))
                        {
                            throw CreateStopReasonException(
                                stopReason!,
                                "Claude reported stop_reason=tool_use without a usable tool call; no tool was executed.");
                        }

                        // No more function calls: end the request even when Claude intentionally
                        // returns content:[] with end_turn. Retrying that empty terminal response
                        // would repeat billing and can never manufacture a missing tool call.
                        if (!string.IsNullOrEmpty(textContent))
                            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textContent));
                        return textContent;
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

        private async Task<FunctionCallResultBatch> ProcessToolUseBatchAsync(
            FunctionCallBatch functionCalls,
            string textContent,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            var functionResults = await ProcessFunctionCallsAsync(
                functionCalls,
                policy,
                cancellationToken);
            AddFunctionCallBatchToHistory(textContent, functionCalls);
            AddFunctionResultBatchToHistory(functionResults);
            return functionResults;
        }

        private static string? ExtractStopReason(string response)
        {
            try
            {
                using var document = JsonDocument.Parse(response);
                return document.RootElement.TryGetProperty("stop_reason", out var stopReason) &&
                       stopReason.ValueKind == JsonValueKind.String
                    ? stopReason.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsTruncationStopReason(string? stopReason)
        {
            return string.Equals(stopReason, "max_tokens", StringComparison.Ordinal) ||
                   string.Equals(stopReason, "model_context_window_exceeded", StringComparison.Ordinal);
        }

        private static AIServiceException CreateStopReasonException(string stopReason, string message)
        {
            return new AIServiceException(
                message,
                JsonSerializer.Serialize(new { stop_reason = stopReason }),
                nameof(AIProvider.Anthropic));
        }

        private static string? ExtractThinkingContent(string response)
        {
            try
            {
                using var document = JsonDocument.Parse(response);
                if (!document.RootElement.TryGetProperty("content", out var contentArray) ||
                    contentArray.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var thinking = new StringBuilder();
                foreach (var item in contentArray.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var type) &&
                        string.Equals(type.GetString(), "thinking", StringComparison.Ordinal) &&
                        item.TryGetProperty("thinking", out var value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        thinking.Append(value.GetString());
                    }
                }

                return thinking.Length == 0 ? null : thinking.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
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
        /// </summary>
        private void ApplySystemMessage(Dictionary<string, object> requestBody)
        {
            var systemMsg = GetEffectiveSystemMessageWithRequestContext();

            if (!string.IsNullOrEmpty(systemMsg))
            {
                requestBody["system"] = systemMsg;
            }
        }

        /// <summary>
        /// Returns true if the current model supports a Claude thinking mode.
        /// Despite this legacy property name, a true value can mean either manual extended
        /// thinking (<c>thinking.type=enabled</c>) or adaptive thinking
        /// (<c>thinking.type=adaptive</c>). Claude Fable 5, Mythos 5, Opus 5, Sonnet 5, and Opus 4.7+
        /// are adaptive-only and do not accept <c>budget_tokens</c>.
        /// </summary>
        public bool SupportsExtendedThinking => IsExtendedThinkingModel();

        private bool IsExtendedThinkingModel()
        {
            var model = Model?.ToLower() ?? "";
            if (model.Contains("fable-5")) return true;
            if (model.Contains("mythos-5")) return true;
            if (model.Contains("sonnet-5")) return true;
            if (model.Contains("opus-5")) return true;
            if (model.Contains("sonnet-4")) return true;
            if (model.Contains("opus-4")) return true;
            if (model.Contains("haiku-4")) return true;
            return false;
        }

        /// <summary>
        /// Returns true if a supported manual or adaptive thinking mode is enabled.
        /// </summary>
        private bool IsThinkingEnabled => IsExtendedThinkingModel() &&
            (ThinkingBudget >= 1024 ||
             (ModelSupportsAdaptiveThinking() && IsAdaptiveThinkingExplicitlyRequested()));

        /// <summary>
        /// Applies thinking configuration to the request body when enabled.
        /// Claude 5, Fable 5, Mythos 5, and Opus 4.7+ use adaptive thinking
        /// (<c>thinking.type=adaptive</c> + <c>output_config.effort</c>);
        /// older models use manual thinking (<c>thinking.type=enabled</c> + <c>budget_tokens</c>,
        /// temperature forced to 1, with max_tokens auto-adjusted to keep budget_tokens &lt; max_tokens).
        /// When thinking is disabled, Opus 5 and Sonnet 5 receive an explicit
        /// <c>thinking.type=disabled</c> because their API default is thinking-on. Other adaptive
        /// models omit the parameter. Fable 5 and Mythos 5 cannot disable thinking, so their closest equivalent
        /// is adaptive thinking at low effort with readable thinking omitted.
        /// </summary>
        private void ApplyThinkingConfig(Dictionary<string, object> requestBody)
        {
            if (IsAdaptiveThinkingExplicitlyRequested() && !ModelSupportsAdaptiveThinking())
            {
                throw new NotSupportedException(
                    $"Claude model '{Model}' does not support adaptive thinking. " +
                    $"Use {nameof(WithThinkingParameters)} with a manual token budget instead.");
            }

            if (!IsThinkingEnabled)
            {
                if (IsAlwaysOnAdaptiveThinkingModel())
                {
                    requestBody["thinking"] = new Dictionary<string, object> { ["type"] = "adaptive" };
                    requestBody["output_config"] = new Dictionary<string, object> { ["effort"] = "low" };
                    return;
                }

                if (ModelRequiresExplicitThinkingDisabled())
                {
                    requestBody["thinking"] = new Dictionary<string, object> { ["type"] = "disabled" };
                }

                return;
            }

            if (UsesAdaptiveThinkingForRequest())
            {
                // Claude 5, Fable 5, Mythos 5, and Opus 4.7+ require adaptive thinking.
                // Opus 4.6 and Sonnet 4.6 also use this branch when callers explicitly select
                // adaptive thinking; their legacy budget_tokens form remains available for compatibility.
                var thinking = new Dictionary<string, object>
                {
                    ["type"] = "adaptive",
                    ["display"] = AdaptiveThinkingDisplay == ClaudeThinkingDisplay.Summarized
                        ? "summarized"
                        : "omitted"
                };

                requestBody["thinking"] = thinking;
                requestBody["output_config"] = new Dictionary<string, object>
                {
                    ["effort"] = ResolveAdaptiveThinkingEffort()
                };
                return;
            }

            // Manual thinking (Opus 4.6 / Sonnet 4.x / Haiku 4.5 and earlier).
            // Claude requires budget_tokens < max_tokens
            var effectiveMaxTokens = GetEffectiveMaxTokens();
            if ((uint)ThinkingBudget >= effectiveMaxTokens)
            {
                var modelMax = GetModelMaxOutputTokens();
                if ((uint)ThinkingBudget >= modelMax)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ThinkingBudget),
                        ThinkingBudget,
                        $"Claude manual thinking requires ThinkingBudget to be lower than the " +
                        $"model's maximum output tokens ({modelMax}) so max_tokens can remain larger.");
                }

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
        /// Returns true for models that require adaptive thinking
        /// (<c>thinking.type=adaptive</c> + <c>output_config.effort</c>) and reject the legacy
        /// <c>thinking.type=enabled</c> + <c>budget_tokens</c> form (HTTP 400).
        /// Opus 4.6 and Sonnet 4.6 still accept the legacy form (deprecated), so they stay on it.
        /// Add future models here as Anthropic retires manual thinking for them.
        /// </summary>
        private bool ModelRequiresAdaptiveThinking()
        {
            var model = Model?.ToLowerInvariant() ?? string.Empty;
            return model.Contains("fable-5") ||
                   model.Contains("mythos-5") ||
                   model.Contains("opus-5") ||
                   model.Contains("sonnet-5") ||
                   model.Contains("opus-4-7") ||
                   model.Contains("opus-4-8");
        }

        /// <summary>
        /// Opus 4.6 and Sonnet 4.6 support both adaptive thinking and the deprecated manual
        /// budget-token form. Keep direct <see cref="ThinkingBudget"/> assignments compatible,
        /// while allowing callers to select the recommended adaptive form explicitly.
        /// </summary>
        private bool ModelSupportsOptionalAdaptiveThinking()
        {
            var model = Model?.ToLowerInvariant() ?? string.Empty;
            return model.Contains("opus-4-6") || model.Contains("sonnet-4-6");
        }

        private bool ModelSupportsAdaptiveThinking()
        {
            return ModelRequiresAdaptiveThinking() || ModelSupportsOptionalAdaptiveThinking();
        }

        private bool IsAdaptiveThinkingExplicitlyRequested()
        {
            return _adaptiveThinkingExplicitlyRequested ||
                   AdaptiveThinkingEffort != ClaudeReasoningEffort.Auto;
        }

        private bool UsesAdaptiveThinkingForRequest()
        {
            return ModelRequiresAdaptiveThinking() ||
                   (ModelSupportsOptionalAdaptiveThinking() && IsAdaptiveThinkingExplicitlyRequested());
        }

        /// <summary>
        /// Opus 5 and Sonnet 5 enable adaptive thinking when the parameter is omitted. Preserve the
        /// library's long-standing <see cref="ThinkingBudget"/> value of -1 as an explicit opt-out.
        /// </summary>
        private bool ModelRequiresExplicitThinkingDisabled()
        {
            var model = Model?.ToLowerInvariant() ?? string.Empty;
            return model.Contains("opus-5") || model.Contains("sonnet-5");
        }

        private bool IsAlwaysOnAdaptiveThinkingModel()
        {
            var model = Model ?? string.Empty;
            return model.Contains("fable-5", StringComparison.OrdinalIgnoreCase) ||
                   model.Contains("mythos-5", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Maps the legacy token-based <see cref="ThinkingBudget"/> onto an adaptive-thinking
        /// effort level. Enabled thinking floors at "high" (the API default, which almost always
        /// thinks) and scales up with larger budgets so existing "thinking on" callers keep
        /// producing reasoning. Allowed values: low, medium, high, xhigh, max.
        /// </summary>
        private string ResolveAdaptiveThinkingEffort()
        {
            if (AdaptiveThinkingEffort == ClaudeReasoningEffort.XHigh &&
                ModelSupportsOptionalAdaptiveThinking())
            {
                throw new ArgumentOutOfRangeException(
                    nameof(AdaptiveThinkingEffort),
                    AdaptiveThinkingEffort,
                    $"Claude model '{Model}' does not support xhigh effort. Use low, medium, high, or max.");
            }

            switch (AdaptiveThinkingEffort)
            {
                case ClaudeReasoningEffort.Low: return "low";
                case ClaudeReasoningEffort.Medium: return "medium";
                case ClaudeReasoningEffort.High: return "high";
                case ClaudeReasoningEffort.XHigh: return "xhigh";
                case ClaudeReasoningEffort.Max: return "max";
            }

            if (ThinkingBudget >= 100_000) return "max";
            if (ThinkingBudget >= 32_768 && !ModelSupportsOptionalAdaptiveThinking()) return "xhigh";
            return "high";
        }

        /// <summary>
        /// Returns true for models that reject a custom <c>temperature</c> value regardless of
        /// whether thinking is enabled. Claude 5, Fable 5, Mythos 5, and Opus 4.7+ reject custom
        /// sampling parameters, so the parameter must be omitted for them.
        /// Add future models here as Anthropic extends this behavior.
        /// </summary>
        private bool ModelRejectsCustomTemperature()
        {
            var model = Model?.ToLowerInvariant() ?? string.Empty;
            return model.Contains("fable-5") ||
                   model.Contains("mythos-5") ||
                   model.Contains("opus-5") ||
                   model.Contains("sonnet-5") ||
                   model.Contains("opus-4-7") ||
                   model.Contains("opus-4-8");
        }

        /// <summary>
        /// Removes the <c>temperature</c> parameter for models that reject a custom value
        /// so the API applies its default (1.0). Must run after <see cref="ApplyThinkingConfig"/>.
        /// </summary>
        private void ApplyTemperaturePolicy(Dictionary<string, object> requestBody)
        {
            // Anthropic requires temperature=1 or omission whenever any thinking mode is enabled.
            // Manual mode writes 1 explicitly above; adaptive mode omits temperature so callers'
            // sampling settings cannot accidentally make an otherwise valid request fail.
            if (!ModelRejectsCustomTemperature() &&
                !(IsThinkingEnabled && UsesAdaptiveThinkingForRequest()))
            {
                return;
            }

            if (requestBody.TryGetValue("temperature", out var current) &&
                current is float t && Math.Abs(t - 1.0f) > 0.0001f)
            {
                Console.WriteLine($"[Claude] Model '{Model}' does not support a custom temperature; ignoring temperature={t}.");
            }

            requestBody.Remove("temperature");
        }

        /// <summary>
        /// Sets Claude thinking through the legacy token-budget API.
        /// On manual-thinking models, the budget must be at least 1024 and is sent as
        /// <c>budget_tokens</c>; <c>max_tokens</c> is adjusted when necessary and temperature is
        /// forced to 1. On adaptive-only models, the numeric budget is retained for compatibility
        /// and mapped to high/xhigh/max effort instead of being sent to Anthropic.
        /// </summary>
        public AnthropicService WithThinkingParameters(int budgetTokens)
        {
            ThinkingBudget = budgetTokens;
            AdaptiveThinkingEffort = ClaudeReasoningEffort.Auto;
            AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Summarized;
            _adaptiveThinkingExplicitlyRequested = false;
            return this;
        }

        /// <summary>
        /// Enables adaptive thinking with an explicit Claude effort and display policy.
        /// </summary>
        public AnthropicService WithAdaptiveThinkingParameters(
            ClaudeReasoningEffort effort,
            ClaudeThinkingDisplay display = ClaudeThinkingDisplay.Summarized)
        {
            AdaptiveThinkingEffort = effort;
            AdaptiveThinkingDisplay = display;
            _adaptiveThinkingExplicitlyRequested = true;
            if (ThinkingBudget < 1024)
                ThinkingBudget = 1024;
            return this;
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            if (profile.DisableReasoning != true)
                return base.ApplyProviderSpecificRequestProfile(profile);

            var backupThinkingBudget = ThinkingBudget;
            var backupAdaptiveThinkingEffort = AdaptiveThinkingEffort;
            var backupAdaptiveThinkingDisplay = AdaptiveThinkingDisplay;
            var backupAdaptiveThinkingExplicitlyRequested = _adaptiveThinkingExplicitlyRequested;

            ThinkingBudget = -1;
            _adaptiveThinkingExplicitlyRequested = false;
            AdaptiveThinkingEffort = IsAlwaysOnAdaptiveThinkingModel()
                ? ClaudeReasoningEffort.Low
                : ClaudeReasoningEffort.Auto;
            AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Omitted;

            return () =>
            {
                ThinkingBudget = backupThinkingBudget;
                AdaptiveThinkingEffort = backupAdaptiveThinkingEffort;
                AdaptiveThinkingDisplay = backupAdaptiveThinkingDisplay;
                _adaptiveThinkingExplicitlyRequested = backupAdaptiveThinkingExplicitlyRequested;
            };
        }

        protected override Action ApplyRequestProfile(AIRequestProfile profile)
        {
            var restore = base.ApplyRequestProfile(profile);

            if (profile.Purpose == AIRequestPurpose.Summarization && profile.MaxTokens.HasValue)
            {
                // The common 256-token summary profile is routinely exhausted by Claude before it
                // can close a concise summary. Keep the caller-facing profile unchanged and reserve
                // a provider-specific completion budget for this library-owned request only.
                MaxTokens = Math.Min(
                    GetModelMaxOutputTokens(),
                    Math.Max(profile.MaxTokens.Value, MinimumAnthropicSummaryOutputTokens));
            }

            return restore;
        }

        #endregion

        #region Vision Support

        public override async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            // Ensure we're using a vision-capable Claude model
            if (!Model.Contains("claude-3") &&
                !Model.Contains("claude-4") &&
                !Model.Contains("opus-4") &&
                !Model.Contains("sonnet-4") &&
                !Model.Contains("haiku-4") &&
                !Model.Contains("fable-5") &&
                !Model.Contains("mythos-5") &&
                !Model.Contains("opus-5") &&
                !Model.Contains("sonnet-5"))
            {
                ChangeModel(AIModels.Anthropic.ClaudeSonnet4_6);
            }

            return await base.GetCompletionWithImageAsync(prompt, imagePath);
        }

        /// <summary>
        /// Sets Claude-specific parameters
        /// </summary>
        public AnthropicService WithClaudeParameters(int? topK = null)
        {
            // Claude supports top_k parameter
            // This would require extending ChatBlock to support Claude-specific parameters
            return this;
        }

        /// <summary>
        /// Sets temperature for different use cases
        /// </summary>
        public AnthropicService WithTemperaturePreset(TemperaturePreset preset)
        {
            Temperature = preset switch
            {
                TemperaturePreset.Deterministic => 0.0f,
                TemperaturePreset.Analytical => 0.1f,
                TemperaturePreset.Factual => 0.3f,
                TemperaturePreset.Balanced => 0.7f,
                TemperaturePreset.Creative => 1.0f,
                TemperaturePreset.VeryCreative => 1.0f,
                _ => 0.7f
            };
            return this;
        }

        /// <summary>
        /// Enables or disables Claude's constitutional AI features
        /// </summary>
        public AnthropicService WithConstitutionalAI(bool enabled = true)
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
