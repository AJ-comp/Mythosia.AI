using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;

namespace Mythosia.AI.Samples.ChatUi;

internal static class ChatUiSettingsHelpers
{
    internal static void ApplyReasoningSettings(AIService service, SettingsRequest request)
    {
        if (request.ReasoningEnabled == true &&
            request.ReasoningType == "qwen_thinking" &&
            service is QwenService qwenOn)
        {
            qwenOn.ThinkingMode = QwenThinking.On;
        }
        else if (request.ReasoningEnabled == true &&
                 request.ReasoningLevel != null &&
                 request.ReasoningType != null)
        {
            ApplyEnabledReasoningSettings(service, request.ReasoningType, request.ReasoningLevel);
        }
        else if (request.ReasoningEnabled == false)
        {
            DisableReasoning(service);
        }
    }

    private static void ApplyEnabledReasoningSettings(
        AIService service,
        string reasoningType,
        string reasoningLevel)
    {
        if (service is OpenAIService gpt)
        {
            switch (reasoningType)
            {
                case "o3":
                    if (Enum.TryParse<Gpt5Reasoning>(reasoningLevel, out var o3))
                        gpt.Gpt5ReasoningEffort = o3;
                    break;
                case "gpt5":
                    if (Enum.TryParse<Gpt5Reasoning>(reasoningLevel, out var g5))
                        gpt.Gpt5ReasoningEffort = g5;
                    gpt.Gpt5ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_1":
                    if (Enum.TryParse<Gpt5_1Reasoning>(reasoningLevel, out var g51))
                        gpt.Gpt5_1ReasoningEffort = g51;
                    gpt.Gpt5_1ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_2":
                    if (Enum.TryParse<Gpt5_2Reasoning>(reasoningLevel, out var g52))
                        gpt.Gpt5_2ReasoningEffort = g52;
                    gpt.Gpt5_2ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_3":
                    if (Enum.TryParse<Gpt5_3Reasoning>(reasoningLevel, out var g53))
                        gpt.Gpt5_3ReasoningEffort = g53;
                    gpt.Gpt5_3ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_4":
                    if (Enum.TryParse<Gpt5_4Reasoning>(reasoningLevel, out var g54))
                        gpt.Gpt5_4ReasoningEffort = g54;
                    gpt.Gpt5_4ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_5":
                    if (Enum.TryParse<Gpt5_5Reasoning>(reasoningLevel, out var g55))
                        gpt.Gpt5_5ReasoningEffort = g55;
                    gpt.Gpt5_5ReasoningSummary = ReasoningSummary.Detailed;
                    break;
                case "gpt5_6":
                    if (Enum.TryParse<Gpt5_6Reasoning>(reasoningLevel, out var g56))
                        gpt.Gpt5_6ReasoningEffort = g56;
                    gpt.Gpt5_6ReasoningSummary = ReasoningSummary.Detailed;
                    break;
            }
        }
        else if (service is AnthropicService claude)
        {
            if (reasoningType == "claude_adaptive" || reasoningType == "claude_always")
            {
                if (Enum.TryParse<ClaudeReasoningEffort>(reasoningLevel, true, out var effort))
                    claude.AdaptiveThinkingEffort = effort;
                claude.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Summarized;
                if (claude.ThinkingBudget < 1024)
                    claude.ThinkingBudget = 1024;
            }
            else if (int.TryParse(reasoningLevel, out var budget))
            {
                claude.ThinkingBudget = budget;
                claude.AdaptiveThinkingEffort = ClaudeReasoningEffort.Auto;
                claude.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Summarized;
            }
        }
        else if (service is XAIService grok)
        {
            if (Enum.TryParse<GrokReasoning>(reasoningLevel, true, out var grokEffort))
                grok.ReasoningEffort = grokEffort;
        }
        else if (service is GoogleAIService gemini)
        {
            switch (reasoningType)
            {
                case "gemini3":
                    if (Enum.TryParse<GeminiThinkingLevel>(reasoningLevel, out var thinkingLevel))
                        gemini.ThinkingLevel = thinkingLevel;
                    gemini.ThinkingBudget = -1;
                    break;
                case "gemini25":
                    if (int.TryParse(reasoningLevel, out var thinkingBudget))
                        gemini.ThinkingBudget = thinkingBudget;
                    gemini.ThinkingLevel = GeminiThinkingLevel.Auto;
                    break;
            }
        }
    }

    private static void DisableReasoning(AIService service)
    {
        if (service is OpenAIService gptOff)
        {
            gptOff.Gpt5ReasoningEffort = Gpt5Reasoning.Auto;
            gptOff.Gpt5ReasoningSummary = null;
            gptOff.Gpt5_1ReasoningEffort = Gpt5_1Reasoning.Auto;
            gptOff.Gpt5_1ReasoningSummary = null;
            gptOff.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Auto;
            gptOff.Gpt5_2ReasoningSummary = null;
            gptOff.Gpt5_3ReasoningEffort = Gpt5_3Reasoning.Auto;
            gptOff.Gpt5_3ReasoningSummary = null;
            gptOff.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.Auto;
            gptOff.Gpt5_4ReasoningSummary = null;
            gptOff.Gpt5_5ReasoningEffort = Gpt5_5Reasoning.Auto;
            gptOff.Gpt5_5ReasoningSummary = null;
            gptOff.Gpt5_6ReasoningEffort = Gpt5_6Reasoning.None;
            gptOff.Gpt5_6ReasoningSummary = null;
            gptOff.Gpt5_6ReasoningMode = Gpt5_6ReasoningMode.Standard;
        }
        else if (service is AnthropicService claudeOff)
        {
            claudeOff.ThinkingBudget = -1;
            claudeOff.AdaptiveThinkingEffort =
                claudeOff.Model.Contains("fable-5", StringComparison.OrdinalIgnoreCase) ||
                claudeOff.Model.Contains("mythos-5", StringComparison.OrdinalIgnoreCase)
                ? ClaudeReasoningEffort.Low
                : ClaudeReasoningEffort.Auto;
            claudeOff.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Omitted;
        }
        else if (service is XAIService grokOff)
        {
            var model = grokOff.Model ?? string.Empty;
            if (model.Equals(AIModels.xAI.Grok4_5, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.Grok4_5Latest, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.GrokBuildLatest, StringComparison.OrdinalIgnoreCase))
            {
                grokOff.ReasoningEffort = GrokReasoning.Low;
            }
            else if (model.Equals(AIModels.xAI.Grok4_3, StringComparison.OrdinalIgnoreCase) ||
                     model.Equals(AIModels.xAI.Grok4_3Latest, StringComparison.OrdinalIgnoreCase) ||
                     model.Equals(AIModels.xAI.GrokLatest, StringComparison.OrdinalIgnoreCase))
            {
                grokOff.ReasoningEffort = GrokReasoning.None;
            }
            else
            {
                grokOff.ReasoningEffort = GrokReasoning.Auto;
            }
        }
        else if (service is GoogleAIService geminiOff)
        {
            var model = geminiOff.Model ?? string.Empty;
            if (model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
            {
                geminiOff.ThinkingBudget = -1;
                geminiOff.ThinkingLevel = model.Contains("-pro", StringComparison.OrdinalIgnoreCase)
                    ? GeminiThinkingLevel.Low
                    : GeminiThinkingLevel.Minimal;
            }
            else
            {
                geminiOff.ThinkingLevel = GeminiThinkingLevel.Auto;
                geminiOff.ThinkingBudget = model.Contains("-pro", StringComparison.OrdinalIgnoreCase)
                    ? 128
                    : 0;
            }
        }
        else if (service is QwenService qwenOff)
        {
            qwenOff.ThinkingMode = QwenThinking.Off;
        }
    }
}
