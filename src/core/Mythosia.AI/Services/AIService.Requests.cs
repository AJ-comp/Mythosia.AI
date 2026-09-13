using Mythosia.AI.Builders;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        private readonly AsyncLocal<RequestExecution?> _requestExecution = new AsyncLocal<RequestExecution?>();

        /// <summary>Captures the service defaults and input without starting a request.</summary>
        /// <remarks>Each With method returns a new builder. Conversation history remains owned by the service
        /// and is selected at execution. Legacy next-call feature/policy settings are consumed into this builder.
        /// Custom content implementations and delegate targets must remain immutable or externally synchronized.</remarks>
        public AIRequestBuilder CreateRequest(string prompt)
            => CreateRequest(new Message(ActorRole.User, prompt ?? throw new ArgumentNullException(nameof(prompt))));

        /// <summary>Captures a message and service defaults for an independent request configuration.</summary>
        public AIRequestBuilder CreateRequest(Message message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var settings = SnapshotRequestSettings();
            var input = CaptureRunMessage(message);
            AIRequestFeatures features;
            lock (_featureGate)
            {
                features = _pendingRequestFeatures.Clone();
                _pendingRequestFeatures = new AIRequestFeatures();
            }
            CurrentPolicy = null;
            using var scope = UseRequestSettings(settings);
            var providerOptions = CaptureProviderRequestOptions(input);
            return new AIRequestBuilder(this, new AIRequest(input, settings, features, providerOptions));
        }

        /// <summary>Captures provider and common defaults. Overrides copy native mutable option values.</summary>
        protected virtual void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            settings[nameof(Model)] = Model;
            settings[nameof(Temperature)] = Temperature;
            settings[nameof(TopP)] = TopP;
            settings[nameof(MaxTokens)] = MaxTokens;
            settings[nameof(FrequencyPenalty)] = FrequencyPenalty;
            settings[nameof(PresencePenalty)] = PresencePenalty;
            settings[nameof(SystemMessage)] = SystemMessage;
            settings[nameof(StatelessMode)] = StatelessMode;
            settings[nameof(FunctionsDisabled)] = FunctionsDisabled;
            settings[nameof(EnableFunctions)] = EnableFunctions;
            settings[nameof(FunctionCallMode)] = FunctionCallMode;
            settings[nameof(ForceFunctionName)] = ForceFunctionName;
            settings[nameof(Functions)] = Functions.Select(AIRequest.CopyFunction).ToList();
            settings[nameof(DefaultPolicy)] = (CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            settings[nameof(Stream)] = Stream;
            settings[nameof(SystemMessageProvider)] = SystemMessageProvider;
            settings[nameof(ContextRecoveryMaxRetries)] = ContextRecoveryMaxRetries;
            settings[nameof(StructuredOutputMaxRetries)] = StructuredOutputMaxRetries;
            settings[nameof(_structuredOutputSchemaJson)] = _structuredOutputSchemaJson;
        }

        private Dictionary<string, object?> SnapshotRequestSettings()
        {
            var settings = new Dictionary<string, object?>(StringComparer.Ordinal);
            CaptureRequestSettings(settings);
            return settings;
        }

        /// <summary>Reads the executing request's setting, falling back to a service default outside execution.</summary>
        protected T RequestSetting<T>(string name, T defaultValue)
            => _requestExecution.Value != null && _requestExecution.Value.Settings.TryGetValue(name, out var value)
                ? (T)value! : defaultValue;

        /// <summary>Changes only an execution-local setting (for internal profiles or protocol preparation).</summary>
        protected void SetExecutionSetting<T>(string name, T value)
        {
            if (_requestExecution.Value == null) _requestExecution.Value = new RequestExecution(SnapshotRequestSettings());
            _requestExecution.Value.Settings[name] = value;
        }

        /// <summary>Begins a settings snapshot for legacy entry points; nested calls reuse their logical request.</summary>
        protected IDisposable BeginRequestSettingsScope()
        {
            if (_requestExecution.Value != null) return new FeatureScope(() => { });
            var settings = SnapshotRequestSettings();
            CurrentPolicy = null;
            return UseRequestSettings(settings);
        }

        private IDisposable UseRequestSettings(IEnumerable<KeyValuePair<string, object?>> settings, bool copyFunctions = true)
        {
            var previous = _requestExecution.Value;
            var copy = settings.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (copy.TryGetValue(nameof(DefaultPolicy), out var policy))
                copy[nameof(DefaultPolicy)] = ((FunctionCallingPolicy)policy!).Clone();
            if (copyFunctions && copy.TryGetValue(nameof(Functions), out var functions))
                copy[nameof(Functions)] = ((IEnumerable<FunctionDefinition>)functions!).Select(AIRequest.CopyFunction).ToList();
            _requestExecution.Value = new RequestExecution(copy);
            return new FeatureScope(() => _requestExecution.Value = previous);
        }

        protected FunctionCallingPolicy GetExecutionPolicy()
            => RequestSetting(nameof(DefaultPolicy), CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();

        private IDisposable UseRequestExecution(RequestExecution execution)
        {
            var previous = _requestExecution.Value;
            _requestExecution.Value = execution;
            return new FeatureScope(() => _requestExecution.Value = previous);
        }

        protected string RequestModel => RequestSetting(nameof(Model), Model);
        protected float RequestTemperature => RequestSetting(nameof(Temperature), Temperature);
        protected float RequestTopP => RequestSetting(nameof(TopP), TopP);
        protected uint RequestMaxTokens => RequestSetting(nameof(MaxTokens), MaxTokens);
        protected float RequestFrequencyPenalty => RequestSetting(nameof(FrequencyPenalty), FrequencyPenalty);
        protected float RequestPresencePenalty => RequestSetting(nameof(PresencePenalty), PresencePenalty);
        protected string RequestSystemMessage => RequestSetting(nameof(SystemMessage), SystemMessage);
        protected bool RequestStatelessMode => RequestSetting(nameof(StatelessMode), StatelessMode);
        protected bool RequestFunctionsDisabled => RequestSetting(nameof(FunctionsDisabled), FunctionsDisabled);
        protected bool RequestEnableFunctions => RequestSetting(nameof(EnableFunctions), EnableFunctions);
        protected FunctionCallMode RequestFunctionCallMode => RequestSetting(nameof(FunctionCallMode), FunctionCallMode);
        protected string? RequestForceFunctionName => RequestSetting(nameof(ForceFunctionName), ForceFunctionName);
        protected List<FunctionDefinition> RequestFunctions => RequestSetting(nameof(Functions), Functions);
        protected bool RequestStream => RequestSetting(nameof(Stream), Stream);
        protected string? RequestStructuredOutputSchemaJson => RequestSetting(nameof(_structuredOutputSchemaJson), _structuredOutputSchemaJson);

        internal static AIRequestContext? CopyRequestContext(AIRequestContext? context) => context == null ? null : new AIRequestContext
        {
            SystemMessagePrefix = context.SystemMessagePrefix, SystemMessageSuffix = context.SystemMessageSuffix,
            RequestMessageOverride = context.RequestMessageOverride == null ? null : CaptureRunMessage(context.RequestMessageOverride),
            AdditionalMessages = context.AdditionalMessages?.Select(CaptureRunMessage).ToArray()
        };

        internal async Task<string> ExecuteRequestAsync(AIRequest request, bool applySummary = true, CancellationToken cancellationToken = default)
        {
            using var cancellationScope = BeginRequestCancellationScope(cancellationToken);
            using var settingsScope = UseRequestSettings(request.Settings);
            var input = CaptureRunMessage(request.Input);
            using var features = UsePreparedRequestFeatures(request, input);
            if (applySummary) await ApplySummaryPolicyIfNeededAsync().ConfigureAwait(false);
            return await GetCompletionAsync(input, EffectiveProfile(request), CopyRequestContext(request.Context), RequestCancellationToken).ConfigureAwait(false);
        }

        internal async Task<AIRun> StartRequestRunAsync(AIRequest request, Action<string>? onText,
            StreamOptions? options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var settingsScope = UseRequestSettings(request.Settings);
            var input = CaptureRunMessage(request.Input);
            using var features = UsePreparedRequestFeatures(request, input);
            var profile = EffectiveProfile(request);
            var restore = profile == null ? null : ApplyRequestProfile(profile);
            try
            {
                return await StartRunAsync(input, onText, options, CopyRequestContext(request.Context), cancellationToken).ConfigureAwait(false);
            }
            finally { restore?.Invoke(); }
        }

        private AIRequestProfile? EffectiveProfile(AIRequest request) => request.Profile == null ? null : new AIRequestProfile
        {
            Purpose = request.Profile.Purpose, DisableReasoning = request.Profile.DisableReasoning,
            MaxTokens = request.Profile.MaxTokens.HasValue ? RequestMaxTokens : (uint?)null
        };

        internal async IAsyncEnumerable<string> StreamRequestAsync(AIRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var settingsScope = UseRequestSettings(request.Settings);
            var input = CaptureRunMessage(request.Input);
            using var features = UsePreparedRequestFeatures(request, input);
            var profile = EffectiveProfile(request);
            var restore = profile == null ? null : ApplyRequestProfile(profile);
            try
            {
                await foreach (var chunk in StreamAsync(input, CopyRequestContext(request.Context), cancellationToken).ConfigureAwait(false))
                    yield return chunk;
            }
            finally { restore?.Invoke(); }
        }

        private IDisposable UsePreparedRequestFeatures(AIRequest request, Message input)
        {
            if (request.Profile != null && request.Profile.Purpose != AIRequestPurpose.Default)
                return SuppressRequestFeatures(input);
            var features = request.Features.Clone();
            ValidateFeatureValues(features);
            var execution = new RequestFeatureExecution(features, input, CloneProviderRequestOptions(request.ProviderOptions));
            var scope = UseRequestFeatureExecution(execution);
            try
            {
                ValidateProviderRequestOptions(execution.ProviderOptions, input);
                ValidateRequestFeatures(features);
                _lastFeatureExecution = execution;
                return scope;
            }
            catch { scope.Dispose(); throw; }
        }

        /// <summary>Creates fresh provider execution state when a prepared request is executed again.</summary>
        protected virtual object? CloneProviderRequestOptions(object? options) => options;

        /// <summary>Validates captured native options against the completed request before history or HTTP changes.</summary>
        protected virtual void ValidateProviderRequestOptions(object? options, Message message) { }

        private sealed class RequestExecution
        {
            internal Dictionary<string, object?> Settings { get; }
            internal RequestExecution(Dictionary<string, object?> settings) { Settings = settings; }
        }
    }
}
