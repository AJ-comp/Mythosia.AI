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
                (features.Reasoning != null && !IsGrok46Or47(family)))
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
            else if (IsGrok46Or47(family) || RequestSetting(nameof(ReasoningEffort), ReasoningEffort) == GrokReasoning.XHigh)
            {
                GetReasoningEffortParameter(family, RequestSetting(nameof(ReasoningEffort), ReasoningEffort));
            }
        }

        private GrokReasoning GetEffectiveReasoningEffort()
        {
            var reasoning = CurrentRequestFeatures.Reasoning;
            return reasoning != null && IsGrok46Or47(GetModelFamily())
                ? MapCommonReasoning(reasoning.Level)
                : RequestSetting(nameof(ReasoningEffort), ReasoningEffort);
        }

        private GrokReasoning MapCommonReasoning(ReasoningLevel level)
        {
            if (GetNativeGrokReasoningLevels(GetModelFamily()).Contains(level) &&
                Enum.TryParse<GrokReasoning>(level.ToString(), out var effort))
                return effort;
            throw new NotSupportedException($"{RequestModel} does not support reasoning level '{level}'.");
        }
    }
}
