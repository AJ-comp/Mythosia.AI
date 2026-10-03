using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        // Settings and input do not identify an execution. Only an explicit,
        // one-use framework handoff continues a prepared logical request.
        private enum RequestEntry { StringCompletion, MessageCompletion, StructuredOutput, Provider, Streaming, Run, Reservation }
        private readonly AsyncLocal<RequestHandoff?> _requestHandoff = new AsyncLocal<RequestHandoff?>();

        private sealed class RequestOperation
        {
            internal readonly object Identity = new object();
            internal Message SourceInput;
            internal Message Input;
            internal bool OwnsInput;
            internal AIRequestProfile? AppliedProfile;
            internal Dictionary<string, object?>? ProfileBaseline;
            internal Dictionary<string, object?>? ProfileAppliedSettings;
            internal Dictionary<string, object?>? ProfileSettingWrites;
            internal bool SummaryBeforeSend;
            internal AIRequestContext? EffectiveContext;
            internal ChatBlock? InputChat;
            internal int InputIndex = -1;
            internal RequestOperation(Message source, Message input, bool ownsInput)
            { SourceInput = source; Input = input; OwnsInput = ownsInput; }
        }

        private sealed class RequestHandoff
        {
            internal readonly RequestFeatureExecution Execution;
            internal readonly Message? Input;
            internal readonly RequestEntry Entry;
            internal readonly bool AllowInputReplacement;
            internal readonly bool ReplacesInitialInput;
            private int _consumed;
            internal RequestHandoff(RequestFeatureExecution execution, Message? input, RequestEntry entry,
                bool allowInputReplacement = false, bool? replacesInitialInput = null)
            {
                Execution = execution; Input = input; Entry = entry; AllowInputReplacement = allowInputReplacement;
                ReplacesInitialInput = replacesInitialInput ?? (input != null &&
                    (ReferenceEquals(input, execution.Operation?.Input) || ReferenceEquals(input, execution.Operation?.SourceInput)));
            }
            internal bool TryConsume(RequestFeatureExecution? execution, Message input, RequestEntry entry, AIRequestProfile? profile)
                => ReferenceEquals(Execution, execution) &&
                   (Entry == entry || (Entry == RequestEntry.Reservation && profile == null && entry != RequestEntry.Reservation)) &&
                   (AllowInputReplacement || Input == null || ReferenceEquals(Input, input) || ReferenceEquals(Execution.Operation?.Input, input)) &&
                   Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
        }

        private IDisposable ContinueRequest(Message? message, RequestEntry entry, bool allowInputReplacement = false,
            bool? replacesInitialInput = null)
        {
            var execution = _requestFeatureExecution.Value;
            if (execution?.Prepared != true || execution.Operation == null)
                throw new InvalidOperationException("A request must be prepared before it can continue.");
            return UseRequestHandoff(new RequestHandoff(execution, message, entry, allowInputReplacement, replacesInitialInput));
        }

        /// <summary>Starts an independent helper request inside a provider's request adapter.</summary>
        /// <remarks>
        /// A framework invocation of a virtual adapter and its first matching base call belong to
        /// the same request, even when the adapter replaces the input. If an override calls that
        /// same base entry for unrelated work before forwarding the outer input, wrap the helper
        /// and its await (or full stream enumeration) in this scope. Its settings come from service
        /// defaults; disposing restores the outer dispatch. This does not isolate conversation
        /// history or permit concurrent use of a service. Use a stateless profile for helper history.
        /// </remarks>
        protected IDisposable BeginIndependentRequestScope()
        {
            var settings = _requestExecution.Value;
            var features = _requestFeatureExecution.Value;
            var handoff = _requestHandoff.Value;
            var context = _currentRequestContext.Value;
            _requestExecution.Value = null;
            _requestFeatureExecution.Value = null;
            _requestHandoff.Value = null;
            _currentRequestContext.Value = null;
            return new FeatureScope(() =>
            {
                _requestExecution.Value = settings;
                _requestFeatureExecution.Value = features;
                _requestHandoff.Value = handoff;
                _currentRequestContext.Value = context;
            });
        }

        private IDisposable UseRequestHandoff(RequestHandoff handoff)
        {
            var previous = _requestHandoff.Value;
            _requestHandoff.Value = handoff;
            return new FeatureScope(() => _requestHandoff.Value = previous);
        }

        // Capture -> own -> apply actual profile -> validate effective settings.
        // Callers, including nested callers, enter independently; implementation
        // delegation, retries and repairs receive explicit one-use handoffs.
        private IDisposable BeginRequestPreparation(Message message, AIRequestProfile? profile = null,
            RequestEntry entry = RequestEntry.MessageCompletion, bool captured = false, Action? configure = null)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var previous = _requestFeatureExecution.Value;
            var handoff = _requestHandoff.Value;
            // A builder is always a new execution, including when invoked by an override
            // before that override forwards the outer request to its base implementation.
            if (!captured && handoff?.TryConsume(previous, message, entry, profile) == true)
                return ContinuePreparedRequest(previous!, handoff, message, profile, entry);

            var auxiliary = profile != null && profile.Purpose != AIRequestPurpose.Default;
            var sourceSettings = captured || auxiliary || previous?.Operation == null
                ? _requestExecution.Value?.Settings ?? SnapshotRequestSettings()
                : SnapshotRequestSettings();
            if (!captured && !auxiliary) CurrentPolicy = null;
            var settings = UseRequestSettings(sourceSettings);
            IDisposable? features = null;
            Action? restoreProfile = null;
            var previousContext = _currentRequestContext.Value;
            try
            {
                // Keep the existing third-party provider hook compatible. Built-ins
                // explicitly resolve an owned input before retaining it; higher-level
                // callers and builders already supply a captured input.
                var ownsInput = entry != RequestEntry.Provider;
                var input = captured || !ownsInput ? message : CaptureRunMessage(message);
                var execution = auxiliary
                    ? new RequestFeatureExecution(new AIRequestFeatures(), input, isAuxiliary: true)
                    : captured && previous != null
                        ? previous
                        : CaptureRequestFeatures(input, inheritCurrent: false);
                var capturedProfile = profile == null ? null : CopyPreparationProfile(profile);
                execution.Operation = new RequestOperation(message, input, ownsInput) { AppliedProfile = capturedProfile };
                execution.Message = input;
                features = UseRequestFeatureExecution(execution);
                _currentRequestContext.Value = null;
                if (capturedProfile != null)
                {
                    restoreProfile = ApplyPreparationProfile(execution.Operation, CopyPreparationProfile(capturedProfile));
                }
                if (auxiliary) SetExecutionSetting<string?>(nameof(_structuredOutputSchemaJson), null);
                configure?.Invoke();
                ValidateRequestPreparation(execution);
                return new FeatureScope(() =>
                {
                    try { restoreProfile?.Invoke(); }
                    finally
                    {
                        _currentRequestContext.Value = previousContext;
                        try { features.Dispose(); } finally { settings.Dispose(); }
                    }
                });
            }
            catch
            {
                try { restoreProfile?.Invoke(); }
                finally
                {
                    _currentRequestContext.Value = previousContext;
                    try { features?.Dispose(); } finally { settings.Dispose(); }
                }
                throw;
            }
        }

        private IDisposable ContinuePreparedRequest(RequestFeatureExecution previous, RequestHandoff handoff,
            Message message, AIRequestProfile? profile, RequestEntry entry)
        {
            var operation = previous!.Operation!;
            // A virtual adapter can supply a different profile while forwarding the
            // same logical request. Compare values, not caller-owned profile identity:
            // forwarding an unchanged profile must not run provider hooks twice.
            var forwardedProfile = profile == null ? null : CopyPreparationProfile(profile);
            var profileChanged = forwardedProfile != null && !SamePreparationProfile(operation.AppliedProfile, forwardedProfile);
            var oldProfile = operation.AppliedProfile;
            var oldBaseline = operation.ProfileBaseline;
            var oldAppliedSettings = operation.ProfileAppliedSettings;
            var oldSettingWrites = operation.ProfileSettingWrites;
            IDisposable? forwardedSettings = null;
            IDisposable? forwardedFeatures = null;
            Action? restoreForwardedProfile = null;
            try
            {
                var execution = previous;
                if (profileChanged)
                {
                    // Replace the previous profile layer, rather than stacking its native
                    // flags indefinitely. Retain later execution adjustments (such as an
                    // output schema), but never recapture changing service defaults.
                    var baseline = RebasePreparationSettings(operation);
                    forwardedSettings = UseRequestSettings(baseline);
                    var forwardedAuxiliary = forwardedProfile!.Purpose != AIRequestPurpose.Default;
                    if (forwardedAuxiliary != previous.IsAuxiliary)
                    {
                        execution = new RequestFeatureExecution(
                            forwardedAuxiliary ? new AIRequestFeatures() : previous.Features.Clone(),
                            previous.Message, forwardedAuxiliary ? null : previous.ProviderOptions, forwardedAuxiliary)
                        { Operation = operation };
                        forwardedFeatures = UseRequestFeatureExecution(execution);
                    }
                    operation.AppliedProfile = CopyPreparationProfile(forwardedProfile);
                    restoreForwardedProfile = ApplyPreparationProfile(operation, forwardedProfile);
                    if (forwardedAuxiliary) SetExecutionSetting<string?>(nameof(_structuredOutputSchemaJson), null);
                }
                if (handoff.AllowInputReplacement)
                {
                    // Adopt a transformed initial input before it enters history. Repairs and
                    // streaming rounds forward different messages but retain their original anchor.
                    if (handoff.ReplacesInitialInput && CurrentRequestInputIndex < 0 &&
                        !ReferenceEquals(message, operation.Input) && !ReferenceEquals(message, operation.SourceInput))
                    {
                        operation.SourceInput = message;
                        operation.OwnsInput = entry != RequestEntry.Provider;
                        operation.Input = operation.OwnsInput ? CaptureRunMessage(message) : message;
                        execution.Message = operation.Input;
                        previous.Message = operation.Input;
                    }
                    // Virtual adapters may change the payload after the initial preflight.
                    // Never send a replacement solely on the strength of the old validation.
                    ValidateRequestPreparation(execution, message);
                }
                else if (profileChanged) ValidateRequestPreparation(execution, message);
                return new FeatureScope(() =>
                {
                    try { restoreForwardedProfile?.Invoke(); }
                    finally
                    {
                        operation.AppliedProfile = oldProfile;
                        operation.ProfileBaseline = oldBaseline;
                        operation.ProfileAppliedSettings = oldAppliedSettings;
                        operation.ProfileSettingWrites = oldSettingWrites;
                        try { forwardedFeatures?.Dispose(); } finally { forwardedSettings?.Dispose(); }
                    }
                });
            }
            catch
            {
                try { restoreForwardedProfile?.Invoke(); }
                finally
                {
                    operation.AppliedProfile = oldProfile;
                    operation.ProfileBaseline = oldBaseline;
                    operation.ProfileAppliedSettings = oldAppliedSettings;
                    operation.ProfileSettingWrites = oldSettingWrites;
                    try { forwardedFeatures?.Dispose(); } finally { forwardedSettings?.Dispose(); }
                }
                throw;
            }
        }

        private static AIRequestProfile CopyPreparationProfile(AIRequestProfile profile) => new AIRequestProfile
        {
            Purpose = profile.Purpose, Stateless = profile.Stateless, DisableFunctions = profile.DisableFunctions,
            DisableReasoning = profile.DisableReasoning, Temperature = profile.Temperature, MaxTokens = profile.MaxTokens
        };

        private static bool SamePreparationProfile(AIRequestProfile? applied, AIRequestProfile candidate)
            => applied != null && applied.Purpose == candidate.Purpose && applied.Stateless == candidate.Stateless &&
               applied.DisableFunctions == candidate.DisableFunctions && applied.DisableReasoning == candidate.DisableReasoning &&
               Nullable.Equals(applied.Temperature, candidate.Temperature) && applied.MaxTokens == candidate.MaxTokens;

        private void ValidateRequestPreparation(RequestFeatureExecution execution, Message? input = null)
        {
            ValidateFeatureValues(execution.Features);
            ValidateProviderRequestOptions(execution.ProviderOptions, input ?? execution.Message!);
            ValidateRequestSpeed(execution.Features);
            ValidateRequestFeatures(execution.Features);
            ValidateEffectiveRequestSettings();
            execution.Prepared = true;
        }

        private Dictionary<string, object?> RebasePreparationSettings(RequestOperation operation)
        {
            var current = new Dictionary<string, object?>(_requestExecution.Value!.Settings, StringComparer.Ordinal);
            if (operation.ProfileBaseline == null || operation.ProfileAppliedSettings == null) return current;
            foreach (var setting in operation.ProfileAppliedSettings)
            {
                // Revert only the profile's own layer. Both explicit assignments and
                // in-place mutations made by a later adapter retain their precedence.
                _requestExecution.Value.SettingWrites.TryGetValue(setting.Key, out var write);
                if (!ReferenceEquals(write, operation.ProfileSettingWrites![setting.Key]) ||
                    !current.TryGetValue(setting.Key, out var value) || !SamePreparationValue(value, setting.Value)) continue;
                if (operation.ProfileBaseline.TryGetValue(setting.Key, out var original)) current[setting.Key] = original;
                else current.Remove(setting.Key);
            }
            return current;
        }

        internal object? CurrentRequestOperationIdentity => _requestFeatureExecution.Value?.Operation?.Identity;

        // Record after the provider's insertion and any stateless chat switch.
        // A retry updates the same anchor. A format repair keeps the original input.
        internal void RecordRequestInput(Message message)
        {
            var operation = _requestFeatureExecution.Value?.Operation;
            if (operation == null || !ReferenceEquals(operation.Input, message)) return;
            operation.InputChat = ActivateChat;
            operation.InputIndex = ActivateChat.Messages.Count - 1;
        }

        internal int CurrentRequestInputIndex
        {
            get
            {
                var operation = _requestFeatureExecution.Value?.Operation;
                if (operation == null || !ReferenceEquals(operation.InputChat, ActivateChat)) return -1;
                var index = operation.InputIndex;
                if (index >= 0 && index < ActivateChat.Messages.Count && ReferenceEquals(ActivateChat.Messages[index], operation.Input))
                    return index;
                // Owned inputs remain unique when preceding history is compacted.
                if (operation.OwnsInput)
                    for (var candidate = 0; candidate < ActivateChat.Messages.Count; candidate++)
                        if (ReferenceEquals(ActivateChat.Messages[candidate], operation.Input)) return candidate;
                return -1;
            }
        }

        internal virtual void ValidateEffectiveRequestSettings() { }
        internal bool IsIsolatedAuxiliaryRequest =>
            _requestFeatureExecution.Value?.IsAuxiliary == true && RequestStatelessMode;
    }
}
