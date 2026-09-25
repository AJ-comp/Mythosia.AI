using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Builders;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.xAI;

namespace Mythosia.AI.Samples.ChatUi;

internal static class ChatUiSettingsHelpers
{
    internal static AIRequestBuilder CreateChatRequest(AIService service, Message message,
        InferenceSpeed speed, AIRequestContext? context = null)
    {
        ResolveSpeed(service, speed.ToString(), InferenceSpeed.ProviderDefault);
        var request = service.CreateRequest(message);
        if (speed != InferenceSpeed.ProviderDefault) request = request.WithSpeed(speed);
        if (context != null) request = request.WithContext(context);
        return request;
    }

    internal static InferenceSpeed ResolveSpeed(AIService service, string? requested, InferenceSpeed current)
    {
        if (requested == null) return current;
        if (!Enum.TryParse<InferenceSpeed>(requested, out var speed) || !Enum.IsDefined(speed))
            throw new ArgumentException("Unknown response speed.");
        var support = service.GetCapabilities().GetSpeedSupport(speed);
        if (support != CapabilitySupport.Supported)
            throw new ArgumentException($"{speed} processing is not verified for this model and endpoint (support: {support}).");
        return speed;
    }

    internal static void ApplyReasoningSettings(AIService service, SettingsRequest request)
    {
        if (service is PerplexityService perplexity)
        {
            var options = perplexity.AgentOptions.Clone();
            if (request.PerplexityPreset != null)
            {
                if (request.PerplexityPreset == "Model") options.Preset = null;
                else if (Enum.TryParse<PerplexityPreset>(request.PerplexityPreset, out var preset) && Enum.IsDefined(preset)) options.Preset = preset;
                else throw new ArgumentException("Unknown Perplexity research preset.");
            }
            if (request.PerplexityMaxSteps.HasValue)
            {
                if (request.PerplexityMaxSteps < 0 || request.PerplexityMaxSteps > 100) throw new ArgumentException("Research steps must be between 0 and 100.");
                options.MaxSteps = request.PerplexityMaxSteps.Value;
            }
            if (request.PerplexityWebSearch.HasValue) options.DisableWebSearch = !request.PerplexityWebSearch.Value;
            if (request.ReasoningEnabled == false) options.ReasoningEffort = ReasoningLevel.Auto;
            else if (request.ReasoningEnabled == true && request.ReasoningLevel != null)
            {
                if (!Enum.TryParse<ReasoningLevel>(request.ReasoningLevel, out var effort) || !Enum.IsDefined(effort) || effort == ReasoningLevel.None)
                    throw new ArgumentException("Unsupported Perplexity reasoning effort.");
                if (options.Preset == null && service.Model == AIModels.Perplexity.Sonar && effort != ReasoningLevel.Auto)
                    throw new ArgumentException("Sonar does not expose reasoning effort. Select a research preset or another Agent model.");
                options.ReasoningEffort = effort;
            }
            perplexity.WithPerplexityOptions(options);
            return;
        }
        if (request.ReasoningEnabled == true &&
            request.ReasoningType == "deepseek_thinking" &&
            service is DeepSeekService deepSeekOn)
        {
            deepSeekOn.ThinkingEnabled = true;
            if (Enum.TryParse<DeepSeekReasoning>(request.ReasoningLevel, out var effort) &&
                Enum.IsDefined(effort))
                deepSeekOn.ReasoningEffort = effort;
        }
        else if (request.ReasoningEnabled == true &&
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

    internal static object? GetReasoningState(AIService service)
    {
        if (service is PerplexityService perplexity)
            return new { type = "perplexity", enabled = perplexity.AgentOptions.ReasoningEffort != ReasoningLevel.Auto,
                effort = perplexity.AgentOptions.ReasoningEffort.ToString(), preset = perplexity.AgentOptions.Preset?.ToString() ?? "Model",
                maxSteps = perplexity.AgentOptions.MaxSteps, webSearch = !perplexity.AgentOptions.DisableWebSearch };
        if (service is DeepSeekService deepSeek)
            return new { type = "deepseek_thinking", enabled = deepSeek.ThinkingEnabled,
                effort = deepSeek.ReasoningEffort.ToString(), defaultEffort = "High" };

        if (service is GoogleAIService gemini &&
            gemini.Model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
            return new { type = "gemini3", alwaysOn = true, effort = gemini.ThinkingLevel.ToString() };

        if (service is XAIService grok &&
            (grok.Model.Equals(AIModels.xAI.Grok4_7, StringComparison.OrdinalIgnoreCase) ||
             grok.Model.Equals(AIModels.xAI.Grok4_6, StringComparison.OrdinalIgnoreCase)))
            return new { type = "grok_always", alwaysOn = true, effort = grok.ReasoningEffort.ToString(), defaultEffort = "High" };

        if (service is not OpenAIService gpt ||
            !gpt.Model.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase))
            return null;

        return new
        {
            type = "gpt6",
            alwaysOn = !gpt.GetCapabilities().NativeReasoningLevels.Contains(ReasoningLevel.None),
            effort = gpt.Gpt6ReasoningEffort.ToString(),
            summary = gpt.Gpt6ReasoningSummary?.ToString(),
            mode = gpt.Gpt6ReasoningMode.ToString(),
            verbosity = gpt.Gpt6Verbosity?.ToString()
        };
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
                case "gpt6":
                    if (Enum.TryParse<Gpt6Reasoning>(reasoningLevel, out var g6) &&
                        Enum.IsDefined(g6) &&
                        Enum.TryParse<ReasoningLevel>(reasoningLevel, out var commonLevel) &&
                        gpt.GetCapabilities().NativeReasoningLevels.Contains(commonLevel))
                        gpt.Gpt6ReasoningEffort = g6;
                    gpt.Gpt6ReasoningSummary = gpt.Gpt6ReasoningEffort == Gpt6Reasoning.None
                        ? null : ReasoningSummary.Detailed;
                    break;
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
            // Astra retains its lowest effort; Sol and Luna can disable reasoning.
            gptOff.Gpt6ReasoningEffort = gptOff.GetCapabilities().NativeReasoningLevels.Contains(ReasoningLevel.None)
                ? Gpt6Reasoning.None : Gpt6Reasoning.Low;
            gptOff.Gpt6ReasoningSummary = null;
            gptOff.Gpt6ReasoningMode = Gpt6ReasoningMode.Standard;
        }
        else if (service is AnthropicService claudeOff)
        {
            claudeOff.ThinkingBudget = -1;
            claudeOff.AdaptiveThinkingEffort =
                claudeOff.Model.Contains("fable-5", StringComparison.OrdinalIgnoreCase) ||
                claudeOff.Model.Contains("mythos-5", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claudeOff.Model, AIModels.Anthropic.ClaudeOpus5_5, StringComparison.OrdinalIgnoreCase)
                ? ClaudeReasoningEffort.Low
                : ClaudeReasoningEffort.Auto;
            claudeOff.AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Omitted;
        }
        else if (service is XAIService grokOff)
        {
            var model = grokOff.Model ?? string.Empty;
            if (model.Equals(AIModels.xAI.Grok4_7, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.Grok4_6, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.Grok4_5, StringComparison.OrdinalIgnoreCase) ||
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
                geminiOff.ThinkingLevel = ChatUiModelHelpers.RequiresLowGeminiThinking(model)
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
        else if (service is DeepSeekService deepSeekOff)
        {
            deepSeekOff.ThinkingEnabled = false;
        }
        else if (service is QwenService qwenOff)
        {
            qwenOff.ThinkingMode = QwenThinking.Off;
        }
    }
}
