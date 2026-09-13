using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        private static readonly IReadOnlyDictionary<ReasoningLevel, DeepSeekReasoning> CommonDeepSeekReasoning =
            new Dictionary<ReasoningLevel, DeepSeekReasoning>
            {
                [ReasoningLevel.None] = DeepSeekReasoning.Auto,
                [ReasoningLevel.Minimal] = DeepSeekReasoning.Low,
                [ReasoningLevel.Low] = DeepSeekReasoning.Low,
                [ReasoningLevel.Medium] = DeepSeekReasoning.High,
                [ReasoningLevel.High] = DeepSeekReasoning.High,
                [ReasoningLevel.XHigh] = DeepSeekReasoning.High,
                [ReasoningLevel.Max] = DeepSeekReasoning.Max
            };

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            // Retired aliases and arbitrary deployment names do not establish remote capabilities.
            var known = string.Equals(RequestModel, AIModels.DeepSeek.Flash, StringComparison.OrdinalIgnoreCase);
            var support = known ? CapabilitySupport.Supported : CapabilitySupport.Unknown;
            var thinking = IsEffectiveDeepSeekThinkingEnabled(CurrentRequestFeatures);
            return new AIModelCapabilities(
                provider: Provider, model: GetRunRequestedModel(), streaming: support, functionCalling: support,
                asyncFunctionCalling: CapabilitySupport.Unsupported, steering: CapabilitySupport.Unsupported,
                reasoning: support,
                reasoningLevels: known ? new[] { ReasoningLevel.Auto }.Concat(CommonDeepSeekReasoning.Keys) : Array.Empty<ReasoningLevel>(),
                nativeReasoning: support,
                nativeReasoningLevels: known ? new[] { ReasoningLevel.Auto, ReasoningLevel.Low, ReasoningLevel.High, ReasoningLevel.Max } : Array.Empty<ReasoningLevel>(),
                thinkingToggle: support, webSearch: CapabilitySupport.Unsupported, fileSearch: CapabilitySupport.Unsupported,
                reasoningCachePreservation: CapabilitySupport.Unsupported,
                imageInput: string.Equals(RequestModel, "deepseek-v4-pro", StringComparison.OrdinalIgnoreCase)
                    ? CapabilitySupport.Unsupported : support,
                structuredOutput: support, temperature: thinking ? CapabilitySupport.Unsupported : support,
                topP: thinking ? support : CapabilitySupport.Unsupported,
                frequencyPenalty: CapabilitySupport.Unsupported, presencePenalty: CapabilitySupport.Unsupported,
                maxOutputTokens: known ? GetModelMaxOutputTokens() : (uint?)null);
        }

        private bool IsEffectiveDeepSeekThinkingEnabled(AIRequestFeatures features)
        {
            if (DisableReasoningForProfile) return false;
            if (features.Reasoning != null && features.Reasoning.Level != ReasoningLevel.Auto)
                return features.Reasoning.Level != ReasoningLevel.None;
            return (CurrentProviderRequestOptions as DeepSeekRequestOptions)?.ThinkingEnabled ??
                RequestSetting(nameof(ThinkingEnabled), ThinkingEnabled);
        }
    }
}
