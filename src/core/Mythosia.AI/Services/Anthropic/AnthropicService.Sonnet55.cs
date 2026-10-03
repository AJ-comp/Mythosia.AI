using Mythosia.AI.Models;
using System;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        // Anthropic publishes this as a fixed ID, without dated snapshots.
        private bool IsClaudeSonnet55Model() =>
            RequestModel.Equals(AIModels.Anthropic.ClaudeSonnet5_5, StringComparison.OrdinalIgnoreCase);

        private bool UsesSonnet55BetweenTools(AIRequestFeatures? features = null)
        {
            if (!IsClaudeSonnet55Model()) return false;
            var common = (features ?? CurrentRequestFeatures).Reasoning?.Level;
            if (common == ReasoningLevel.None) return true;
            if (common.HasValue && common != ReasoningLevel.Auto) return false;
            if (RequestThinkingMode == ClaudeThinkingMode.BetweenTools) return true;
            if (RequestThinkingMode == ClaudeThinkingMode.Adaptive) return false;
            return RequestThinkingBudgetExplicitlySet && RequestThinkingBudget < 1024 &&
                !IsAdaptiveThinkingExplicitlyRequested();
        }
    }
}
