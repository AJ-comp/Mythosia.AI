using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private int RequestThinkingBudget => RequestSetting(nameof(ThinkingBudget), ThinkingBudget);
        private GeminiThinkingLevel RequestThinkingLevel => RequestSetting(nameof(ThinkingLevel), ThinkingLevel);
        private GeminiSafetyThreshold RequestHarassmentSafetyThreshold => RequestSetting(nameof(HarassmentSafetyThreshold), HarassmentSafetyThreshold);
        private GeminiSafetyThreshold RequestHateSpeechSafetyThreshold => RequestSetting(nameof(HateSpeechSafetyThreshold), HateSpeechSafetyThreshold);
        private GeminiSafetyThreshold RequestSexuallyExplicitSafetyThreshold => RequestSetting(nameof(SexuallyExplicitSafetyThreshold), SexuallyExplicitSafetyThreshold);
        private GeminiSafetyThreshold RequestDangerousContentSafetyThreshold => RequestSetting(nameof(DangerousContentSafetyThreshold), DangerousContentSafetyThreshold);

        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[nameof(ThinkingBudget)] = ThinkingBudget;
            settings[nameof(ThinkingLevel)] = ThinkingLevel;
            settings[nameof(HarassmentSafetyThreshold)] = HarassmentSafetyThreshold;
            settings[nameof(HateSpeechSafetyThreshold)] = HateSpeechSafetyThreshold;
            settings[nameof(SexuallyExplicitSafetyThreshold)] = SexuallyExplicitSafetyThreshold;
            settings[nameof(DangerousContentSafetyThreshold)] = DangerousContentSafetyThreshold;
        }
    }
}
