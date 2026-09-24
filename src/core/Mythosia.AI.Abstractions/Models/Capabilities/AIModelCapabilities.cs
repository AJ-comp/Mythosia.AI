using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models.Capabilities
{
    /// <summary>Whether this adapter knows that a feature is supported by the selected model.</summary>
    public enum CapabilitySupport
    {
        Unknown = 0,
        Supported = 1,
        Unsupported = 2
    }

    /// <summary>Immutable, locally resolved capabilities of a selected chat model and request configuration.</summary>
    /// <remarks>Supported describes availability, not whether an option is enabled or authorized for an account.
    /// Unknown is not Unsupported. StructuredOutput includes the common prompt/repair fallback and does not
    /// promise native constrained decoding. Numeric thinking presets are suggestions, not an exhaustive range.</remarks>
    public sealed class AIModelCapabilities
    {
        public static AIModelCapabilities Unknown { get; } = new AIModelCapabilities();
        public string? Provider { get; }
        public string? Model { get; }
        public CapabilitySupport Streaming { get; }
        public CapabilitySupport FunctionCalling { get; }
        public CapabilitySupport AsyncFunctionCalling { get; }
        public CapabilitySupport Steering { get; }
        public CapabilitySupport Reasoning { get; }
        public CapabilitySupport WebSearch { get; }
        public CapabilitySupport FileSearch { get; }
        public CapabilitySupport ReasoningCachePreservation { get; }
        public CapabilitySupport ImageInput { get; }
        public CapabilitySupport StructuredOutput { get; }
        public CapabilitySupport Temperature { get; }
        public CapabilitySupport TopP { get; }
        public CapabilitySupport FrequencyPenalty { get; }
        public CapabilitySupport PresencePenalty { get; }
        public CapabilitySupport NativeReasoning { get; }
        public CapabilitySupport ThinkingToggle { get; }
        public IReadOnlyList<ReasoningLevel> ReasoningLevels { get; }
        public IReadOnlyList<ReasoningLevel> NativeReasoningLevels { get; }
        public IReadOnlyList<int> ThinkingBudgetPresets { get; }
        public uint? MaxOutputTokens { get; }
        public CapabilitySupport StandardSpeed { get; private set; }
        public CapabilitySupport FastSpeed { get; private set; }

        public AIModelCapabilities(
            string? provider = null, string? model = null,
            CapabilitySupport streaming = CapabilitySupport.Unknown,
            CapabilitySupport functionCalling = CapabilitySupport.Unknown,
            CapabilitySupport asyncFunctionCalling = CapabilitySupport.Unknown,
            CapabilitySupport steering = CapabilitySupport.Unknown,
            CapabilitySupport reasoning = CapabilitySupport.Unknown,
            CapabilitySupport webSearch = CapabilitySupport.Unknown,
            CapabilitySupport fileSearch = CapabilitySupport.Unknown,
            CapabilitySupport reasoningCachePreservation = CapabilitySupport.Unknown,
            CapabilitySupport imageInput = CapabilitySupport.Unknown,
            CapabilitySupport structuredOutput = CapabilitySupport.Unknown,
            CapabilitySupport temperature = CapabilitySupport.Unknown,
            CapabilitySupport topP = CapabilitySupport.Unknown,
            CapabilitySupport frequencyPenalty = CapabilitySupport.Unknown,
            CapabilitySupport presencePenalty = CapabilitySupport.Unknown,
            CapabilitySupport nativeReasoning = CapabilitySupport.Unknown,
            CapabilitySupport thinkingToggle = CapabilitySupport.Unknown,
            IEnumerable<ReasoningLevel>? reasoningLevels = null,
            IEnumerable<ReasoningLevel>? nativeReasoningLevels = null,
            IEnumerable<int>? thinkingBudgetPresets = null,
            uint? maxOutputTokens = null)
        {
            Provider = provider;
            Model = model;
            Streaming = streaming;
            FunctionCalling = functionCalling;
            AsyncFunctionCalling = asyncFunctionCalling;
            Steering = steering;
            Reasoning = reasoning;
            WebSearch = webSearch;
            FileSearch = fileSearch;
            ReasoningCachePreservation = reasoningCachePreservation;
            ImageInput = imageInput;
            StructuredOutput = structuredOutput;
            Temperature = temperature;
            TopP = topP;
            FrequencyPenalty = frequencyPenalty;
            PresencePenalty = presencePenalty;
            NativeReasoning = nativeReasoning;
            ThinkingToggle = thinkingToggle;
            ReasoningLevels = Copy(reasoningLevels);
            NativeReasoningLevels = Copy(nativeReasoningLevels);
            ThinkingBudgetPresets = Copy(thinkingBudgetPresets);
            MaxOutputTokens = maxOutputTokens;
        }

        /// <summary>Checks a level for the common WithReasoning API. Unknown must be handled separately from rejection.</summary>
        public CapabilitySupport GetReasoningSupport(ReasoningLevel level)
        {
            if (!Enum.IsDefined(typeof(ReasoningLevel), level)) return CapabilitySupport.Unsupported;
            if (Reasoning != CapabilitySupport.Supported) return Reasoning;
            return ReasoningLevels.Contains(level) ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
        }

        /// <summary>Checks processing-mode support for this model and endpoint, not account entitlement or capacity.</summary>
        public CapabilitySupport GetSpeedSupport(InferenceSpeed speed)
            => speed == InferenceSpeed.ProviderDefault ? CapabilitySupport.Supported
                : speed == InferenceSpeed.Standard ? StandardSpeed
                : speed == InferenceSpeed.Fast ? FastSpeed : CapabilitySupport.Unsupported;

        /// <summary>Returns an independent capability snapshot with processing-mode support.
        /// Retains the original constructor for source and binary compatibility.</summary>
        public AIModelCapabilities WithSpeedSupport(CapabilitySupport standard, CapabilitySupport fast)
        {
            if (!Enum.IsDefined(typeof(CapabilitySupport), standard)) throw new ArgumentOutOfRangeException(nameof(standard));
            if (!Enum.IsDefined(typeof(CapabilitySupport), fast)) throw new ArgumentOutOfRangeException(nameof(fast));
            if (StandardSpeed == standard && FastSpeed == fast) return this;
            var copy = (AIModelCapabilities)MemberwiseClone();
            copy.StandardSpeed = standard;
            copy.FastSpeed = fast;
            return copy;
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values)
            => Array.AsReadOnly(values?.Distinct().ToArray() ?? Array.Empty<T>());
    }
}
