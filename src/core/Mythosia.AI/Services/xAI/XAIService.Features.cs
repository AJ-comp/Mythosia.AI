using Mythosia.AI.Models;
using System;
using System.Linq;

namespace Mythosia.AI.Services.xAI
{
    public partial class XAIService
    {
        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            var family = GetModelFamily();
            if (features.WebSearch != null || features.FileSearch != null ||
                (features.Reasoning != null && family != GrokModelFamily.Grok4_6))
            {
                base.ValidateRequestFeatures(features);
                return;
            }

            if (features.Reasoning != null)
            {
                if (features.Reasoning.Cache == CachePreservation.Required)
                    throw new NotSupportedException("Grok does not support cache-preserving reasoning changes.");
                GetReasoningEffortParameter(family, MapCommonReasoning(features.Reasoning.Level));
            }
            else if (family == GrokModelFamily.Grok4_6 || RequestSetting(nameof(ReasoningEffort), ReasoningEffort) == GrokReasoning.XHigh)
            {
                GetReasoningEffortParameter(family, RequestSetting(nameof(ReasoningEffort), ReasoningEffort));
            }
        }

        private GrokReasoning GetEffectiveReasoningEffort()
        {
            var reasoning = CurrentRequestFeatures.Reasoning;
            return reasoning != null && GetModelFamily() == GrokModelFamily.Grok4_6
                ? MapCommonReasoning(reasoning.Level)
                : RequestSetting(nameof(ReasoningEffort), ReasoningEffort);
        }

        private GrokReasoning MapCommonReasoning(ReasoningLevel level)
        {
            if (GetNativeGrokReasoningLevels(GrokModelFamily.Grok4_6).Contains(level) &&
                Enum.TryParse<GrokReasoning>(level.ToString(), out var effort))
                return effort;
            throw new NotSupportedException($"{RequestModel} does not support reasoning level '{level}'.");
        }
    }
}
