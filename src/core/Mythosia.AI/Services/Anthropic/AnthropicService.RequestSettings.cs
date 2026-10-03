using Mythosia.AI.Models;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        private int RequestThinkingBudget => ProfileThinking != null ? -1 : RequestSetting(nameof(ThinkingBudget), ThinkingBudget);
        private bool RequestThinkingBudgetExplicitlySet => ProfileThinking != null || RequestSetting(nameof(_thinkingBudgetExplicitlySet), _thinkingBudgetExplicitlySet);
        private ClaudeThinkingMode RequestThinkingMode => ProfileThinking != null ? ClaudeThinkingMode.Auto : RequestSetting(nameof(ThinkingMode), ThinkingMode);
        private ClaudeReasoningEffort RequestAdaptiveThinkingEffort => ProfileThinking?.Effort ?? RequestSetting(nameof(AdaptiveThinkingEffort), AdaptiveThinkingEffort);
        private ClaudeThinkingDisplay RequestAdaptiveThinkingDisplay => ProfileThinking != null ? ClaudeThinkingDisplay.Omitted : RequestSetting(nameof(AdaptiveThinkingDisplay), AdaptiveThinkingDisplay);
        private ClaudeThinkingPrefixMismatchBehavior? RequestThinkingPrefixMismatchBehavior => ProfileThinking is ClaudeProfileThinking profile ? profile.Binding : RequestSetting(nameof(ThinkingPrefixMismatchBehavior), ThinkingPrefixMismatchBehavior);
        private bool RequestAdaptiveThinkingExplicitlyRequested => ProfileThinking == null && RequestSetting(nameof(_adaptiveThinkingExplicitlyRequested), _adaptiveThinkingExplicitlyRequested);

        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[nameof(ThinkingBudget)] = ThinkingBudget;
            settings[nameof(_thinkingBudgetExplicitlySet)] = _thinkingBudgetExplicitlySet;
            settings[nameof(ThinkingMode)] = ThinkingMode;
            settings[nameof(AdaptiveThinkingEffort)] = AdaptiveThinkingEffort;
            settings[nameof(AdaptiveThinkingDisplay)] = AdaptiveThinkingDisplay;
            settings[nameof(ThinkingPrefixMismatchBehavior)] = ThinkingPrefixMismatchBehavior;
            settings[nameof(_adaptiveThinkingExplicitlyRequested)] = _adaptiveThinkingExplicitlyRequested;
        }
    }
}
