using Mythosia.AI.Models;
using System.Collections.Generic;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        private Gpt5Reasoning RequestGpt5ReasoningEffort => RequestSetting(nameof(Gpt5ReasoningEffort), Gpt5ReasoningEffort);
        private ReasoningSummary? RequestGpt5ReasoningSummary => RequestSetting(nameof(Gpt5ReasoningSummary), Gpt5ReasoningSummary);
        private ReasoningSummary? RequestO3ReasoningSummary => RequestSetting(nameof(O3ReasoningSummary), O3ReasoningSummary);
        private Gpt5_1Reasoning RequestGpt5_1ReasoningEffort => RequestSetting(nameof(Gpt5_1ReasoningEffort), Gpt5_1ReasoningEffort);
        private ReasoningSummary? RequestGpt5_1ReasoningSummary => RequestSetting(nameof(Gpt5_1ReasoningSummary), Gpt5_1ReasoningSummary);
        private Verbosity? RequestGpt5_1Verbosity => RequestSetting(nameof(Gpt5_1Verbosity), Gpt5_1Verbosity);
        private Gpt5_2Reasoning RequestGpt5_2ReasoningEffort => RequestSetting(nameof(Gpt5_2ReasoningEffort), Gpt5_2ReasoningEffort);
        private ReasoningSummary? RequestGpt5_2ReasoningSummary => RequestSetting(nameof(Gpt5_2ReasoningSummary), Gpt5_2ReasoningSummary);
        private Verbosity? RequestGpt5_2Verbosity => RequestSetting(nameof(Gpt5_2Verbosity), Gpt5_2Verbosity);
        private Gpt5_3Reasoning RequestGpt5_3ReasoningEffort => RequestSetting(nameof(Gpt5_3ReasoningEffort), Gpt5_3ReasoningEffort);
        private ReasoningSummary? RequestGpt5_3ReasoningSummary => RequestSetting(nameof(Gpt5_3ReasoningSummary), Gpt5_3ReasoningSummary);
        private Verbosity? RequestGpt5_3Verbosity => RequestSetting(nameof(Gpt5_3Verbosity), Gpt5_3Verbosity);
        private Gpt5_4Reasoning RequestGpt5_4ReasoningEffort => RequestSetting(nameof(Gpt5_4ReasoningEffort), Gpt5_4ReasoningEffort);
        private ReasoningSummary? RequestGpt5_4ReasoningSummary => RequestSetting(nameof(Gpt5_4ReasoningSummary), Gpt5_4ReasoningSummary);
        private Verbosity? RequestGpt5_4Verbosity => RequestSetting(nameof(Gpt5_4Verbosity), Gpt5_4Verbosity);
        private Gpt5_5Reasoning RequestGpt5_5ReasoningEffort => RequestSetting(nameof(Gpt5_5ReasoningEffort), Gpt5_5ReasoningEffort);
        private ReasoningSummary? RequestGpt5_5ReasoningSummary => RequestSetting(nameof(Gpt5_5ReasoningSummary), Gpt5_5ReasoningSummary);
        private Verbosity? RequestGpt5_5Verbosity => RequestSetting(nameof(Gpt5_5Verbosity), Gpt5_5Verbosity);
        private Gpt5_6Reasoning RequestGpt5_6ReasoningEffort => RequestSetting(nameof(Gpt5_6ReasoningEffort), Gpt5_6ReasoningEffort);
        private ReasoningSummary? RequestGpt5_6ReasoningSummary => RequestSetting(nameof(Gpt5_6ReasoningSummary), Gpt5_6ReasoningSummary);
        private Verbosity? RequestGpt5_6Verbosity => RequestSetting(nameof(Gpt5_6Verbosity), Gpt5_6Verbosity);
        private Gpt5_6ReasoningMode RequestGpt5_6ReasoningMode => RequestSetting(nameof(Gpt5_6ReasoningMode), Gpt5_6ReasoningMode);
        private Gpt6Reasoning RequestGpt6ReasoningEffort => RequestSetting(nameof(Gpt6ReasoningEffort), Gpt6ReasoningEffort);
        private ReasoningSummary? RequestGpt6ReasoningSummary => RequestSetting(nameof(Gpt6ReasoningSummary), Gpt6ReasoningSummary);
        private Verbosity? RequestGpt6Verbosity => RequestSetting(nameof(Gpt6Verbosity), Gpt6Verbosity);
        private Gpt6ReasoningMode RequestGpt6ReasoningMode => RequestSetting(nameof(Gpt6ReasoningMode), Gpt6ReasoningMode);

        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[nameof(Gpt5ReasoningEffort)] = Gpt5ReasoningEffort;
            settings[nameof(Gpt5ReasoningSummary)] = Gpt5ReasoningSummary;
            settings[nameof(O3ReasoningSummary)] = O3ReasoningSummary;
            settings[nameof(Gpt5_1ReasoningEffort)] = Gpt5_1ReasoningEffort;
            settings[nameof(Gpt5_1ReasoningSummary)] = Gpt5_1ReasoningSummary;
            settings[nameof(Gpt5_1Verbosity)] = Gpt5_1Verbosity;
            settings[nameof(Gpt5_2ReasoningEffort)] = Gpt5_2ReasoningEffort;
            settings[nameof(Gpt5_2ReasoningSummary)] = Gpt5_2ReasoningSummary;
            settings[nameof(Gpt5_2Verbosity)] = Gpt5_2Verbosity;
            settings[nameof(Gpt5_3ReasoningEffort)] = Gpt5_3ReasoningEffort;
            settings[nameof(Gpt5_3ReasoningSummary)] = Gpt5_3ReasoningSummary;
            settings[nameof(Gpt5_3Verbosity)] = Gpt5_3Verbosity;
            settings[nameof(Gpt5_4ReasoningEffort)] = Gpt5_4ReasoningEffort;
            settings[nameof(Gpt5_4ReasoningSummary)] = Gpt5_4ReasoningSummary;
            settings[nameof(Gpt5_4Verbosity)] = Gpt5_4Verbosity;
            settings[nameof(Gpt5_5ReasoningEffort)] = Gpt5_5ReasoningEffort;
            settings[nameof(Gpt5_5ReasoningSummary)] = Gpt5_5ReasoningSummary;
            settings[nameof(Gpt5_5Verbosity)] = Gpt5_5Verbosity;
            settings[nameof(Gpt5_6ReasoningEffort)] = Gpt5_6ReasoningEffort;
            settings[nameof(Gpt5_6ReasoningSummary)] = Gpt5_6ReasoningSummary;
            settings[nameof(Gpt5_6Verbosity)] = Gpt5_6Verbosity;
            settings[nameof(Gpt5_6ReasoningMode)] = Gpt5_6ReasoningMode;
            settings[nameof(Gpt6ReasoningEffort)] = Gpt6ReasoningEffort;
            settings[nameof(Gpt6ReasoningSummary)] = Gpt6ReasoningSummary;
            settings[nameof(Gpt6Verbosity)] = Gpt6Verbosity;
            settings[nameof(Gpt6ReasoningMode)] = Gpt6ReasoningMode;
        }
    }
}
