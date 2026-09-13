using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Services.xAI
{
    public partial class XAIService
    {
        private static readonly HashSet<string> KnownGrokChatModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AIModels.xAI.Grok4_6, AIModels.xAI.Grok4_5, AIModels.xAI.Grok4_5Latest, AIModels.xAI.GrokBuildLatest,
            AIModels.xAI.Grok4_3, AIModels.xAI.Grok4_3Latest, AIModels.xAI.GrokLatest,
            AIModels.xAI.Grok4_20Reasoning, AIModels.xAI.Grok4_20NonReasoning, AIModels.xAI.GrokBuild0_1
        };

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            var known = KnownGrokChatModels.Contains(RequestModel);
            var support = known ? CapabilitySupport.Supported : CapabilitySupport.Unknown;
            var family = GetModelFamily();
            var levels = known ? GetNativeGrokReasoningLevels(family) : Array.Empty<ReasoningLevel>();
            var nativeReasoning = known ? (levels.Length > 0 ? CapabilitySupport.Supported : CapabilitySupport.Unsupported)
                : CapabilitySupport.Unknown;
            return new AIModelCapabilities(
                provider: Provider, model: GetRunRequestedModel(), streaming: support, functionCalling: support,
                asyncFunctionCalling: CapabilitySupport.Unsupported, steering: CapabilitySupport.Unsupported,
                reasoning: family == GrokModelFamily.Grok4_6 ? support : CapabilitySupport.Unsupported,
                reasoningLevels: known && family == GrokModelFamily.Grok4_6 ? levels : Array.Empty<ReasoningLevel>(),
                nativeReasoning: nativeReasoning, nativeReasoningLevels: levels,
                thinkingToggle: known ? (family == GrokModelFamily.Grok4_3 ? CapabilitySupport.Supported : CapabilitySupport.Unsupported)
                    : CapabilitySupport.Unknown,
                webSearch: CapabilitySupport.Unsupported, fileSearch: CapabilitySupport.Unsupported,
                reasoningCachePreservation: CapabilitySupport.Unsupported,
                imageInput: known && family == GrokModelFamily.GrokBuild ? CapabilitySupport.Unknown : support,
                structuredOutput: support, temperature: support,
                // Only Grok 4.6 adds top_p to the shared function request body.
                topP: ShouldUseFunctions && family != GrokModelFamily.Grok4_6
                    ? CapabilitySupport.Unsupported : support,
                frequencyPenalty: RejectsPenaltyParameters(family) ? CapabilitySupport.Unsupported : CapabilitySupport.Unknown,
                presencePenalty: RejectsPenaltyParameters(family) ? CapabilitySupport.Unsupported : CapabilitySupport.Unknown,
                maxOutputTokens: known ? GetModelMaxOutputTokens() : (uint?)null);
        }

        private static ReasoningLevel[] GetNativeGrokReasoningLevels(GrokModelFamily family)
        {
            switch (family)
            {
                case GrokModelFamily.Grok4_6:
                    return new[] { ReasoningLevel.Auto, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High, ReasoningLevel.XHigh };
                case GrokModelFamily.Grok4_5:
                    return new[] { ReasoningLevel.Auto, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High };
                case GrokModelFamily.Grok4_3:
                    return new[] { ReasoningLevel.Auto, ReasoningLevel.None, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High };
                default:
                    return Array.Empty<ReasoningLevel>();
            }
        }
    }
}
