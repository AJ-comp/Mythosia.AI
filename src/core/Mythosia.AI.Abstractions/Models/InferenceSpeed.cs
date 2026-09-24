using System;

namespace Mythosia.AI.Models
{
    /// <summary>Selects a provider processing mode without changing the model or reasoning effort.</summary>
    public enum InferenceSpeed
    {
        /// <summary>Omit the override and retain the provider's account/project default.</summary>
        ProviderDefault = 0,
        /// <summary>Explicitly request standard processing.</summary>
        Standard = 1,
        /// <summary>Opt into the provider's premium low-latency processing. Neither latency nor availability is guaranteed.</summary>
        Fast = 2
    }

    /// <summary>An immutable processing-mode observation for one provider inference attempt.</summary>
    /// <remarks>The index counts inference attempts, including server continuations, rather than HTTP requests or LLM rounds.
    /// Retries and format repairs can add attempts.
    /// AppliedSpeed is null when the server did not report a recognized mode, including failed attempts.
    /// This describes a service mode, not a measured token generation rate.</remarks>
    public sealed class AIProcessingInfo
    {
        public int RequestIndex { get; }
        public InferenceSpeed RequestedSpeed { get; }
        public InferenceSpeed? AppliedSpeed { get; }
        public string? RawAppliedMode { get; }
        public string? ResponseId { get; }
        /// <summary>True only when a Fast request was explicitly reported as Standard.</summary>
        public bool IsDowngraded => RequestedSpeed == InferenceSpeed.Fast && AppliedSpeed == InferenceSpeed.Standard;

        public AIProcessingInfo(int requestIndex, InferenceSpeed requestedSpeed,
            InferenceSpeed? appliedSpeed = null, string? rawAppliedMode = null, string? responseId = null)
        {
            if (requestIndex <= 0) throw new ArgumentOutOfRangeException(nameof(requestIndex));
            if (!Enum.IsDefined(typeof(InferenceSpeed), requestedSpeed)) throw new ArgumentOutOfRangeException(nameof(requestedSpeed));
            if (appliedSpeed.HasValue && appliedSpeed != InferenceSpeed.Standard && appliedSpeed != InferenceSpeed.Fast)
                throw new ArgumentOutOfRangeException(nameof(appliedSpeed));
            RequestIndex = requestIndex;
            RequestedSpeed = requestedSpeed;
            AppliedSpeed = appliedSpeed;
            RawAppliedMode = rawAppliedMode;
            ResponseId = responseId;
        }
    }
}
