using Mythosia.AI.Models;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        private int RequestThinkingBudget => RequestSetting(nameof(ThinkingBudget), ThinkingBudget);
        private ClaudeReasoningEffort RequestAdaptiveThinkingEffort => RequestSetting(nameof(AdaptiveThinkingEffort), AdaptiveThinkingEffort);
        private ClaudeThinkingDisplay RequestAdaptiveThinkingDisplay => RequestSetting(nameof(AdaptiveThinkingDisplay), AdaptiveThinkingDisplay);
        private ClaudeThinkingPrefixMismatchBehavior? RequestThinkingPrefixMismatchBehavior => RequestSetting(nameof(ThinkingPrefixMismatchBehavior), ThinkingPrefixMismatchBehavior);
        private bool RequestAdaptiveThinkingExplicitlyRequested => RequestSetting(nameof(_adaptiveThinkingExplicitlyRequested), _adaptiveThinkingExplicitlyRequested);

        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[nameof(ThinkingBudget)] = ThinkingBudget;
            settings[nameof(AdaptiveThinkingEffort)] = AdaptiveThinkingEffort;
            settings[nameof(AdaptiveThinkingDisplay)] = AdaptiveThinkingDisplay;
            settings[nameof(ThinkingPrefixMismatchBehavior)] = ThinkingPrefixMismatchBehavior;
            settings[nameof(_adaptiveThinkingExplicitlyRequested)] = _adaptiveThinkingExplicitlyRequested;
        }
    }
}
