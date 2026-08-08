using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService : IAIService, IFunctionRegisterable
    {
        protected readonly string ApiKey;
        protected readonly HttpClient HttpClient;
        protected List<ChatBlock> _chatRequests = new List<ChatBlock>();
        private readonly AsyncLocal<AIRequestContext?> _currentRequestContext = new AsyncLocal<AIRequestContext?>();

        public FunctionCallingPolicy DefaultPolicy { get; set; } = FunctionCallingPolicy.Default;

        protected internal FunctionCallingPolicy? CurrentPolicy { get; set; }

        /// <summary>
        /// Optional async provider that supplies a baseline <see cref="AIRequestContext"/>
        /// for every outbound request. Invoked automatically right before each call
        /// to <see cref="GetCompletionAsync(Message, AIRequestProfile, AIRequestContext)"/>
        /// or <see cref="StreamAsync(Message, StreamOptions, AIRequestContext, CancellationToken)"/>
        /// (including agent-path calls) so callers no longer need to build and pass
        /// an <see cref="AIRequestContext"/> at every entry point.
        /// <para>
        /// The property is set through the fluent helper
        /// <see cref="AIServiceExtensions.WithSystemMessageProvider(AIService, Func{AIRequestContext?})"/>
        /// (sync) or <see cref="AIServiceExtensions.WithSystemMessageProvider(AIService, Func{CancellationToken, System.Threading.Tasks.ValueTask{AIRequestContext?}})"/>
        /// (async). Both overloads normalize to the async delegate stored here, so the
        /// runtime only deals with a single invocation shape.
        /// </para>
        /// <para>
        /// If a request also passes an explicit <see cref="AIRequestContext"/>,
        /// the two are merged field-by-field: the explicit context wins on
        /// <see cref="AIRequestContext.SystemMessagePrefix"/>, <see cref="AIRequestContext.SystemMessageSuffix"/>,
        /// and <see cref="AIRequestContext.RequestMessageOverride"/> when non-null; for
        /// <see cref="AIRequestContext.AdditionalMessages"/>, the provider's list comes first
        /// and the explicit list is appended.
        /// </para>
        /// </summary>
        public Func<CancellationToken, ValueTask<AIRequestContext?>>? SystemMessageProvider { get; internal set; }

        public IReadOnlyCollection<ChatBlock> ChatRequests => _chatRequests;
        public ChatBlock ActivateChat { get; protected set; } = null!;

        /// <summary>
        /// When true, each request is processed independently without maintaining conversation history
        /// </summary>
        public bool StatelessMode { get; set; } = false;

        /// <summary>
        /// Quick toggle for function calling (like StatelessMode)
        /// </summary>
        public bool FunctionsDisabled { get; set; } = false;

        /// <summary>
        /// The AI provider for this service
        /// </summary>
        public abstract string Provider { get; }

        #region Model & Generation Settings

        public string Model { get; protected set; } = string.Empty;

        /// <summary>
        /// Convenience property for ActivateChat.SystemMessage
        /// </summary>
        public string SystemMessage
        {
            get => ActivateChat.SystemMessage;
            set => ActivateChat.SystemMessage = value;
        }

        public float TopP { get; set; } = 1.0f;
        public float Temperature { get; set; } = 0.7f;
        public float FrequencyPenalty { get; set; } = 0.0f;
        public float PresencePenalty { get; set; } = 0.0f;
        public uint MaxTokens { get; set; } = 1024;
        public bool Stream { get; set; }

        /// <summary>
        /// How many times a rejected-for-context-length request may be compacted and re-sent.
        /// Default 1. Set to 0 to disable reactive recovery entirely — the rejection then propagates
        /// to the caller unchanged, which is the pre-6.8 behaviour.
        /// <para>
        /// The budget is per attempt unit, and the two paths count differently. Non-streaming counts
        /// whole turns: one provider call covers all of its function-calling rounds, so a turn issues
        /// at most <c>1 + ContextRecoveryMaxRetries</c> calls. Streaming counts rounds, replaying only
        /// the round that overflowed, so a turn can compact up to
        /// <c>MaxRounds × ContextRecoveryMaxRetries</c> times.
        /// </para>
        /// <para>
        /// Recovery only ever runs when the server itself reports the overflow. It does not require
        /// that nothing has reached the caller yet — a streaming round that already emitted chunks is
        /// left alone, but earlier rounds in the same turn may well have streamed.
        /// </para>
        /// </summary>
        public int ContextRecoveryMaxRetries { get; set; } = 1;

        /// <summary>
        /// Returns the maximum output tokens allowed for the current model.
        /// Override in each service to provide model-specific limits.
        /// </summary>
        protected virtual uint GetModelMaxOutputTokens() => uint.MaxValue;

        /// <summary>
        /// Returns the effective max tokens, capped by the current model's limit.
        /// Use this instead of MaxTokens when building request bodies.
        /// </summary>
        protected uint GetEffectiveMaxTokens() => Math.Min(MaxTokens, GetModelMaxOutputTokens());

        #endregion

        #region Function Settings

        public List<FunctionDefinition> Functions { get; set; } = new List<FunctionDefinition>();
        public bool EnableFunctions { get; set; } = true;
        public FunctionCallMode FunctionCallMode { get; set; } = FunctionCallMode.Auto;
        public string? ForceFunctionName { get; set; }
        public bool ShouldUseFunctions => Functions.Count > 0 && EnableFunctions && !FunctionsDisabled;

        /// <inheritdoc/>
        void IFunctionRegisterable.AddFunction(FunctionDefinition function) => Functions.Add(function);

        #endregion

        protected AIService(string? apiKey, string baseUrl, HttpClient httpClient)
        {
            ApiKey = apiKey ?? string.Empty;
            HttpClient = httpClient;
            if (!baseUrl.EndsWith("/"))
                baseUrl += "/";
            httpClient.BaseAddress = new Uri(baseUrl);
            AddNewChat(new ChatBlock());
        }

        #region Request Timeout (single control point)

        /// <summary>
        /// Single source of truth for per-request timeouts. Returns the timeout in seconds for the
        /// given policy, or <c>null</c> for no timeout. Providers override this to adjust the timeout
        /// per model (e.g. slow "pro" reasoning models that routinely exceed the default).
        /// All request paths must obtain their timeout from here via <see cref="CreateRequestTimeoutCts"/>.
        /// </summary>
        protected virtual int? ResolveRequestTimeoutSeconds(FunctionCallingPolicy policy) => policy?.TimeoutSeconds;

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that fires after the resolved request
        /// timeout, linked to an optional external cancellation token. This is the one place that
        /// turns a policy into an effective request timeout.
        /// </summary>
        protected CancellationTokenSource CreateRequestTimeoutCts(FunctionCallingPolicy policy, CancellationToken external = default)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            var seconds = ResolveRequestTimeoutSeconds(policy);
            if (seconds.HasValue)
                cts.CancelAfter(TimeSpan.FromSeconds(seconds.Value));
            return cts;
        }

        #endregion

        #region Chat Management

        public void AddNewChat(ChatBlock newChat)
        {
            _chatRequests.Add(newChat);
            ActivateChat = newChat;
        }

        public void AddNewChat()
        {
            AddNewChat(new ChatBlock());
        }

        public void SetActivateChat(string chatBlockId)
        {
            var selectedChatBlock = _chatRequests.FirstOrDefault(chat => chat.Id == chatBlockId);
            if (selectedChatBlock != null)
                ActivateChat = selectedChatBlock;
        }

        public void ChangeModel(string model)
        {
            Model = model;
        }

        /// <summary>
        /// Gets the active conversation messages for an outgoing request.
        /// Conversation trimming is handled exclusively by <see cref="ConversationPolicy"/>.
        /// </summary>
        protected internal IEnumerable<Message> GetLatestMessages()
        {
            var messages = ActivateChat.Messages.ToList();

            var requestMessageOverride = _currentRequestContext.Value?.RequestMessageOverride;
            if (requestMessageOverride != null && messages.Count > 0)
            {
                messages[messages.Count - 1] = requestMessageOverride;
            }

            var additionalMessages = _currentRequestContext.Value?.AdditionalMessages;
            if (additionalMessages != null && additionalMessages.Count > 0)
                messages.AddRange(additionalMessages);

            return messages;
        }

        /// <summary>
        /// Ensures the message list starts with a User message.
        /// Some APIs (Gemini, Claude) require conversations to begin with a user turn.
        /// If the first message is not from a user, a synthetic context message is prepended.
        /// </summary>
        protected static void EnsureUserFirstMessage(List<Message> messages)
        {
            if (messages.Count == 0) return;
            if (messages[0].Role == ActorRole.User) return;

            messages.Insert(0, new Message(ActorRole.User, "(Continuing from previous conversation context)"));
        }

        /// <summary>
        /// Gets messages for non-function path, converting function-related messages to plain text.
        /// Original messages in ChatBlock are never modified.
        /// </summary>
        protected internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
        {
            foreach (var message in GetLatestMessages())
            {
                if (message.FunctionCallBatch != null)
                {
                    var calls = string.Join(", ", message.FunctionCallBatch.Calls.Select(call =>
                        $"{call.Name}({JsonSerializer.Serialize(call.Arguments)})"));
                    yield return new Message(ActorRole.Assistant, $"[Called {calls}]");
                    continue;
                }

                if (message.FunctionCallResultBatch != null)
                {
                    var results = string.Join("; ", message.FunctionCallResultBatch.Results.Select(result =>
                        $"{result.Call.Name}: {result.Content}"));
                    yield return new Message(ActorRole.User, $"[Function results: {results}]");
                    continue;
                }

                if (message.Role == ActorRole.Assistant &&
                    message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
                    var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
                    yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
                    continue;
                }

                if (message.Role == ActorRole.Function)
                {
                    var funcName = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "function";
                    yield return new Message(ActorRole.User, $"[Function {funcName} returned: {message.Content}]");
                    continue;
                }

                yield return message;
            }
        }

        #endregion

        #region Core Completion Methods

        public virtual async Task<string> GetCompletionAsync(string prompt, AIRequestProfile? profile = null, AIRequestContext? context = null)
        {
            await ApplySummaryPolicyIfNeededAsync();
            return await GetCompletionAsync(new Message(ActorRole.User, prompt), profile, context);
        }

        public abstract Task<string> GetCompletionAsync(Message message);

        public virtual async Task<string> GetCompletionAsync(Message message, AIRequestProfile? profile = null, AIRequestContext? context = null)
        {
            var effectiveContext = await BuildEffectiveContextAsync(context, CancellationToken.None).ConfigureAwait(false);
            if (profile == null && effectiveContext == null)
                return await SendWithContextRecoveryAsync(message);

            Action restoreProfile = profile != null ? ApplyRequestProfile(profile) : () => { };
            Action restoreContext = effectiveContext != null ? ApplyRequestContext(effectiveContext) : () => { };
            try
            {
                return await SendWithContextRecoveryAsync(message);
            }
            finally
            {
                restoreContext();
                restoreProfile();
            }
        }

        /// <summary>
        /// Sends the request, and if the server rejects it for exceeding the context window,
        /// compacts the conversation and sends again.
        /// <para>
        /// The context limit belongs to the server, so being told "that did not fit" is the only
        /// authoritative signal there is — more reliable than any client-side token estimate, and
        /// it follows the server automatically when the deployment's limit changes.
        /// </para>
        /// <para>
        /// Recovery is skipped for the summarization request itself (it runs stateless and with
        /// <c>_isSummarizing</c> set), which is what stops a summary that overflows from
        /// recursively summarizing itself.
        /// </para>
        /// </summary>
        private async Task<string> SendWithContextRecoveryAsync(Message message)
        {
            var capturedPolicy = CurrentPolicy;

            for (int attempt = 0; ; attempt++)
            {
                // Per attempt, not per turn: compaction shortens the history between attempts, so a
                // baseline captured once would sit above the message count forever after and rewind
                // nothing.
                int baseline = ActivateChat.Messages.Count;

                try
                {
                    return await GetCompletionAsync(message);
                }
                catch (ContextLengthExceededException ex)
                {
                    if (attempt >= ContextRecoveryMaxRetries)
                    {
                        ex.RecoveryAttempts = attempt;
                        ex.RecoverySkipReason = attempt == 0 ? "recovery-disabled" : "retries-exhausted";
                        throw;
                    }

                    if (_isSummarizing || StatelessMode)
                    {
                        ex.RecoveryAttempts = attempt;
                        ex.RecoverySkipReason = _isSummarizing ? "summarizing" : "stateless";
                        throw;
                    }

                    // A retry re-enters the provider's round loop from zero, so any tool the failed
                    // attempt already executed would run a second time — a duplicate mail, a second
                    // record. Nothing here can tell a read-only tool from a destructive one, so the
                    // only honest move is to stop and report why. (The streaming path replays a
                    // single round in place and is not affected.)
                    if (HasFunctionMessagesPast(baseline))
                    {
                        ex.RecoveryAttempts = attempt;
                        ex.RecoverySkipReason = "tool-side-effects";
                        throw;
                    }

                    // The failed attempt already appended the user message. Rewind to where this
                    // attempt started so the retry does not send a duplicated conversation.
                    TruncateMessagesTo(baseline);

                    SummaryCompactionResult compaction;
                    try
                    {
                        compaction = await ForceCompactAsync();
                    }
                    catch
                    {
                        // A failure while summarizing must not replace the diagnosis the caller
                        // needs — the original rejection is the real story. Capture-and-rethrow keeps
                        // the provider's throw site, which a plain `throw ex` would overwrite.
                        ex.RecoveryAttempts = attempt;
                        ex.RecoverySkipReason = "compaction-threw";
                        ExceptionDispatchInfo.Capture(ex).Throw();
                        throw; // unreachable — Throw() never returns
                    }

                    if (!compaction.IsApplied)
                    {
                        ex.RecoveryAttempts = attempt;
                        ex.RecoverySkipReason = compaction.SkipReason;
                        throw;
                    }

                    // Re-arm *after* compaction: providers consume CurrentPolicy on entry and null
                    // it out, and the summarization request is itself a provider call — restoring
                    // before it would let the summary eat the policy and leave the retry running on
                    // DefaultPolicy (a different MaxRounds) without a word.
                    CurrentPolicy = capturedPolicy;
                }
            }
        }

        /// <summary>Drops everything appended past <paramref name="baseline"/>.</summary>
        private protected void TruncateMessagesTo(int baseline)
        {
            while (ActivateChat.Messages.Count > baseline)
                ActivateChat.Messages.RemoveAt(ActivateChat.Messages.Count - 1);
        }

        /// <summary>
        /// Whether a function result was appended past <paramref name="baseline"/> — i.e. whether the
        /// attempt that just failed had already run a tool.
        /// </summary>
        private bool HasFunctionMessagesPast(int baseline)
        {
            for (int i = Math.Max(0, baseline); i < ActivateChat.Messages.Count; i++)
            {
                if (ActivateChat.Messages[i].Role == ActorRole.Function) return true;
            }
            return false;
        }

        /// <summary>
        /// Detaches the ambient <see cref="AIRequestContext"/> for the duration of a request the
        /// library issues on its own behalf. Summarization is such a request: it is not a
        /// continuation of the caller's turn, and inheriting the caller's context would let
        /// <see cref="AIRequestContext.RequestMessageOverride"/> replace the summarization prompt
        /// outright.
        /// </summary>
        private protected Action SuppressRequestContext()
        {
            var backup = _currentRequestContext.Value;
            _currentRequestContext.Value = null;
            return () => _currentRequestContext.Value = backup;
        }

        #endregion

        #region Convenience Methods

        public virtual async Task<string> GetCompletionWithImageAsync(string prompt, string imagePath)
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var mimeType = MimeTypes.GetFromPath(imagePath);

            var message = new Message(ActorRole.User, new List<MessageContent>
            {
                new TextContent(prompt),
                new ImageContent(imageBytes, mimeType)
            });

            // Route through the 3-arg overload so context-overflow recovery applies here too. Passing
            // null for both arguments does not mean "no context": the overload still consults
            // SystemMessageProvider, so a service configured with WithSystemMessageProvider now gets
            // that context here as well — the same as GetCompletionAsync(string) has always done.
            return await GetCompletionAsync(message, null, null);
        }

        public virtual async Task<string> GetCompletionWithImageUrlAsync(string prompt, string imageUrl)
        {
            var message = new Message(ActorRole.User, new List<MessageContent>
            {
                new TextContent(prompt),
                new ImageContent(imageUrl)
            });

            return await GetCompletionAsync(message, null, null);
        }

        /// <summary>
        /// Copies the source service's state into this instance — conversation,
        /// function registrations, sampling parameters, conversation policy, and
        /// service-level callbacks (<see cref="SystemMessageProvider"/>,
        /// streaming diagnostics).
        ///
        /// Service-level delegates are propagated by reference, not deep-copied
        /// (deep copy of a delegate is not meaningful — its captured target
        /// objects, e.g. an <c>ILogger</c>, are external infrastructure that the
        /// library cannot clone). The typical case is callbacks wrapping a shared
        /// logger/metrics/telemetry sink, where reference sharing is the desired
        /// behavior.
        ///
        /// Caveat: if a callback closure captures the source service itself
        /// (e.g. <c>line =&gt; Log(sourceService.Provider, line)</c>), the copy will
        /// still log under the original provider's identity. Prefer capturing
        /// only stable external resources inside callbacks.
        /// </summary>
        public AIService CopyFrom(AIService sourceService)
        {
            if (sourceService == null)
                throw new ArgumentNullException(nameof(sourceService));

            ActivateChat = sourceService.ActivateChat.Clone();

            Functions = new List<FunctionDefinition>(sourceService.Functions);
            EnableFunctions = sourceService.EnableFunctions;
            FunctionCallMode = sourceService.FunctionCallMode;
            ForceFunctionName = sourceService.ForceFunctionName;
            DefaultPolicy = sourceService.DefaultPolicy;

            Temperature = sourceService.Temperature;
            TopP = sourceService.TopP;
            MaxTokens = sourceService.MaxTokens;
            FrequencyPenalty = sourceService.FrequencyPenalty;
            PresencePenalty = sourceService.PresencePenalty;
            Stream = sourceService.Stream;

            StatelessMode = sourceService.StatelessMode;
            FunctionsDisabled = sourceService.FunctionsDisabled;

            if (sourceService.ConversationPolicy != null)
            {
                var sourcePolicy = sourceService.ConversationPolicy;
                ConversationPolicy = new SummaryConversationPolicy
                {
                    TriggerTokens = sourcePolicy.TriggerTokens,
                    TriggerCount = sourcePolicy.TriggerCount,
                    KeepRecentTokens = sourcePolicy.KeepRecentTokens,
                    KeepRecentCount = sourcePolicy.KeepRecentCount,
                    CurrentSummary = sourcePolicy.CurrentSummary
                };
            }
            else
            {
                ConversationPolicy = null;
            }

            // Service-level callbacks — propagate by reference so cross-provider
            // switches (e.g. via WithStreamDiagnostics, WithSystemMessageProvider)
            // do not silently lose configuration.
            SystemMessageProvider = sourceService.SystemMessageProvider;
            StreamRawLineCallback = sourceService.StreamRawLineCallback;
            StreamCompleteCallback = sourceService.StreamCompleteCallback;

            return this;
        }

        #endregion

        #region Request Profiles

        protected virtual Action ApplyRequestProfile(AIRequestProfile profile)
        {
            if (profile == null)
                return delegate { };

            var backupStream = Stream;
            var backupStatelessMode = StatelessMode;
            var backupFunctionsDisabled = FunctionsDisabled;
            var backupTemperature = Temperature;
            var backupMaxTokens = MaxTokens;
            var restoreProvider = ApplyProviderSpecificRequestProfile(profile);

            if (profile.Stateless.HasValue)
                StatelessMode = profile.Stateless.Value;

            if (profile.DisableFunctions.HasValue)
                FunctionsDisabled = profile.DisableFunctions.Value;

            if (profile.Temperature.HasValue)
                Temperature = profile.Temperature.Value;

            if (profile.MaxTokens.HasValue)
                MaxTokens = profile.MaxTokens.Value;

            return () =>
            {
                restoreProvider();
                Stream = backupStream;
                StatelessMode = backupStatelessMode;
                FunctionsDisabled = backupFunctionsDisabled;
                Temperature = backupTemperature;
                MaxTokens = backupMaxTokens;
            };
        }

        protected virtual Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            return delegate { };
        }

        protected virtual Action ApplyRequestContext(AIRequestContext context)
        {
            var backup = _currentRequestContext.Value;
            _currentRequestContext.Value = context;
            return () => _currentRequestContext.Value = backup;
        }

        /// <summary>
        /// Resolves the effective request context by invoking <see cref="SystemMessageProvider"/>
        /// (if set) and merging its result with the caller-supplied context.
        /// Returns null when both the provider and the caller produce null, so request
        /// paths can skip the context-application overhead entirely.
        /// </summary>
        internal async ValueTask<AIRequestContext?> BuildEffectiveContextAsync(AIRequestContext? explicitContext, CancellationToken cancellationToken)
        {
            var provider = SystemMessageProvider;
            if (provider == null) return explicitContext;

            var providerContext = await provider(cancellationToken).ConfigureAwait(false);
            if (providerContext == null) return explicitContext;
            if (explicitContext == null) return providerContext;
            return MergeContexts(providerContext, explicitContext);
        }

        /// <summary>
        /// Merges a provider-supplied baseline context with a per-call explicit context.
        /// <para>
        /// Field-level override: <see cref="AIRequestContext.SystemMessagePrefix"/>,
        /// <see cref="AIRequestContext.SystemMessageSuffix"/>, and
        /// <see cref="AIRequestContext.RequestMessageOverride"/> from the explicit context
        /// win when non-null; otherwise the baseline value is used.
        /// <see cref="AIRequestContext.AdditionalMessages"/> is concatenated (baseline first,
        /// then explicit) so callers can layer on top of a provider-supplied list.
        /// </para>
        /// </summary>
        private static AIRequestContext MergeContexts(AIRequestContext baseline, AIRequestContext overlay)
        {
            var merged = new AIRequestContext
            {
                SystemMessagePrefix = overlay.SystemMessagePrefix ?? baseline.SystemMessagePrefix,
                SystemMessageSuffix = overlay.SystemMessageSuffix ?? baseline.SystemMessageSuffix,
                RequestMessageOverride = overlay.RequestMessageOverride ?? baseline.RequestMessageOverride
            };

            if (baseline.AdditionalMessages != null || overlay.AdditionalMessages != null)
            {
                var combined = new List<Message>();
                if (baseline.AdditionalMessages != null) combined.AddRange(baseline.AdditionalMessages);
                if (overlay.AdditionalMessages != null) combined.AddRange(overlay.AdditionalMessages);
                merged.AdditionalMessages = combined;
            }

            return merged;
        }

        protected internal string GetEffectiveSystemMessageWithRequestContext()
        {
            var systemMessage = GetEffectiveSystemMessage();
            var structuredOutputInstruction = GetStructuredOutputInstruction();

            if (string.IsNullOrEmpty(structuredOutputInstruction))
                return systemMessage;

            return string.IsNullOrEmpty(systemMessage)
                ? structuredOutputInstruction.TrimStart('\n')
                : systemMessage + structuredOutputInstruction;
        }

        internal Message ResolveRequestMessage(Message message)
        {
            return message;
        }

        #endregion

        #region Abstract Methods

        /// <summary>
        /// Creates the HTTP request message for the AI service
        /// </summary>
        protected abstract HttpRequestMessage CreateMessageRequest();

        /// <summary>
        /// Extracts the response content from the API response
        /// </summary>
        protected abstract string ExtractResponseContent(string responseContent);

        /// <summary>
        /// Parses streaming JSON data
        /// </summary>
        protected abstract string StreamParseJson(string jsonData);

        /// <summary>
        /// Gets the token count for the current conversation
        /// </summary>
        public abstract Task<uint> GetInputTokenCountAsync();

        /// <summary>
        /// Gets the token count for a specific prompt
        /// </summary>
        public abstract Task<uint> GetInputTokenCountAsync(string prompt);

        #endregion
    }
}
