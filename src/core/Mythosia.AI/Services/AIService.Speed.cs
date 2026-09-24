using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService : Services.IAIProcessingInfoService
    {
        protected InferenceSpeed RequestSpeed => CurrentRequestFeatures.Speed ?? InferenceSpeed.ProviderDefault;

        /// <summary>Processing observations from the latest logical request, including its tools and repairs.
        /// Internal summaries and query rewriting do not replace or append to these observations.</summary>
        public IReadOnlyList<AIProcessingInfo> LastProcessing
            => _lastFeatureExecution?.ProcessingSnapshot() ?? Array.Empty<AIProcessingInfo>();

        /// <summary>Locally resolves processing-mode support without checking account entitlement or live capacity.</summary>
        protected virtual CapabilitySupport ResolveSpeedSupport(InferenceSpeed speed)
            => speed == InferenceSpeed.ProviderDefault ? CapabilitySupport.Supported : CapabilitySupport.Unknown;

        private void ValidateRequestSpeed(AIRequestFeatures features)
        {
            var speed = features.Speed ?? InferenceSpeed.ProviderDefault;
            if (!Enum.IsDefined(typeof(InferenceSpeed), speed)) throw new ArgumentOutOfRangeException(nameof(features), "Unknown inference speed.");
            if (speed == InferenceSpeed.ProviderDefault) return;
            var support = ResolveSpeedSupport(speed);
            if (support != CapabilitySupport.Supported)
                throw new NotSupportedException($"{Provider} model '{RequestModel}' does not have verified support for {speed} processing on this endpoint (support: {support}).");
        }

        /// <summary>Creates one observation for a provider inference attempt, before sending a request or
        /// when a server continuation begins. Update the same object as headers and stream events arrive.</summary>
        protected ProcessingObservation BeginProcessingObservation()
        {
            var execution = _requestFeatureExecution.Value;
            if (execution == null) return new ProcessingObservation(1, RequestSpeed);
            lock (execution.Processing)
            {
                var observation = new ProcessingObservation(execution.Processing.Count + 1, RequestSpeed);
                execution.Processing.Add(observation);
                return observation;
            }
        }

        /// <summary>A request-local observer; absent metadata never implies that the requested mode was applied.</summary>
        protected sealed class ProcessingObservation
        {
            private readonly object _gate = new object();
            private readonly int _index;
            private readonly InferenceSpeed _requested;
            private string? _rawMode;
            private InferenceSpeed? _applied;
            private string? _responseId;
            internal ProcessingObservation(int index, InferenceSpeed requested) { _index = index; _requested = requested; }

            /// <summary>Updates reported metadata. Null/empty fields preserve prior reports.
            /// An unrecognized nonempty raw mode clears the normalized mode while retaining the raw value.</summary>
            public void Record(string? rawMode, InferenceSpeed? appliedSpeed, string? responseId = null)
            {
                if (appliedSpeed.HasValue && appliedSpeed != InferenceSpeed.Standard && appliedSpeed != InferenceSpeed.Fast)
                    throw new ArgumentOutOfRangeException(nameof(appliedSpeed));
                lock (_gate)
                {
                    if (!string.IsNullOrWhiteSpace(rawMode)) { _rawMode = rawMode; _applied = appliedSpeed; }
                    else if (appliedSpeed.HasValue) _applied = appliedSpeed;
                    if (!string.IsNullOrWhiteSpace(responseId)) _responseId = responseId;
                }
            }

            internal AIProcessingInfo Snapshot()
            {
                lock (_gate) return new AIProcessingInfo(_index, _requested, _applied, _rawMode, _responseId);
            }
        }
    }
}
