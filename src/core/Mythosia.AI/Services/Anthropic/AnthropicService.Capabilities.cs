using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        private static readonly HashSet<string> KnownClaudeModels = new HashSet<string>(
            typeof(AIModels.Anthropic).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)
                .Concat(new[] { "claude-opus-4-5", "claude-sonnet-4-5", "claude-haiku-4-5" }),
            StringComparer.OrdinalIgnoreCase);

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            var model = RequestModel;
            if (!IsKnownClaudeModel(model))
                return new AIModelCapabilities(provider: Provider, model: model,
                    asyncFunctionCalling: CapabilitySupport.Unsupported, steering: CapabilitySupport.Unsupported,
                    fileSearch: CapabilitySupport.Unsupported, topP: CapabilitySupport.Unsupported,
                    frequencyPenalty: CapabilitySupport.Unsupported, presencePenalty: CapabilitySupport.Unsupported);

            var levels = Enum.GetValues(typeof(ReasoningLevel)).Cast<ReasoningLevel>()
                .Where(level => GetClaudeCommonReasoningError(level) == null).ToArray();
            var nativeLevels = ModelSupportsAdaptiveThinking()
                ? levels.Where(level => level != ReasoningLevel.None).ToArray()
                : Array.Empty<ReasoningLevel>();
            var temperature = !ModelRejectsCustomTemperature() && !IsThinkingEnabled &&
                !RequiresAdaptiveThinkingTemperaturePolicy();
            return new AIModelCapabilities(provider: Provider, model: model,
                streaming: CapabilitySupport.Supported, functionCalling: CapabilitySupport.Supported,
                asyncFunctionCalling: CapabilitySupport.Unsupported, steering: CapabilitySupport.Unsupported,
                reasoning: CapabilitySupport.Supported, reasoningLevels: levels,
                nativeReasoning: CapabilitySupport.Supported, nativeReasoningLevels: nativeLevels,
                thinkingToggle: IsAlwaysOnAdaptiveThinkingModel() ? CapabilitySupport.Unsupported : CapabilitySupport.Supported,
                thinkingBudgetPresets: ModelRequiresAdaptiveThinking()
                    ? Array.Empty<int>() : new[] { 1024, 2048, 4096, 8192, 16384 },
                webSearch: CapabilitySupport.Supported, fileSearch: CapabilitySupport.Unsupported,
                reasoningCachePreservation: SupportsPerMessageClaudeEffort()
                    ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                imageInput: CapabilitySupport.Supported, structuredOutput: CapabilitySupport.Supported,
                temperature: temperature ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                topP: CapabilitySupport.Unsupported, frequencyPenalty: CapabilitySupport.Unsupported,
                presencePenalty: CapabilitySupport.Unsupported, maxOutputTokens: GetModelMaxOutputTokens());
        }

        private bool RequiresAdaptiveThinkingTemperaturePolicy()
        {
            var commonEffort = CurrentRequestFeatures.Reasoning?.Level;
            var commonAdaptive = commonEffort.HasValue && commonEffort != ReasoningLevel.Auto &&
                commonEffort != ReasoningLevel.None && ModelSupportsAdaptiveThinking();
            // Binding controls can create an adaptive thinking object even when the legacy
            // budget and explicit effort leave IsThinkingEnabled false. Read the same captured
            // options as ApplyClaudeRequestOptions, including native defaults during inspection.
            var bindingAdaptive = !IsThinkingEnabled && ModelSupportsOptionalAdaptiveThinking() &&
                commonEffort != ReasoningLevel.None && ClaudeOptions.Binding.HasValue;
            return (IsThinkingEnabled && UsesAdaptiveThinkingForRequest()) || commonAdaptive || bindingAdaptive;
        }

        private static bool IsKnownClaudeModel(string model)
        {
            if (KnownClaudeModels.Contains(model)) return true;
            if (!HasClaudeSnapshotDate(model)) return false;
            var alias = model.Substring(0, model.Length - 9);
            // Opus 5.5 is a fixed ID; Anthropic does not publish date-suffixed snapshots for it.
            if (string.Equals(alias, AIModels.Anthropic.ClaudeOpus5_5, StringComparison.OrdinalIgnoreCase))
                return false;
            return !HasClaudeSnapshotDate(alias) && KnownClaudeModels.Contains(alias);
        }

        private static bool HasClaudeSnapshotDate(string model) =>
            model.Length > 9 && model[model.Length - 9] == '-' &&
            DateTime.TryParseExact(model.Substring(model.Length - 8), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        private string? GetClaudeCommonReasoningError(ReasoningLevel level)
        {
            if (level == ReasoningLevel.Auto) return null;
            if (level == ReasoningLevel.Minimal || (level == ReasoningLevel.None && IsAlwaysOnAdaptiveThinkingModel()))
                return $"Claude model '{RequestModel}' does not support reasoning level {level}.";
            if (level == ReasoningLevel.None)
                return SupportsExtendedThinking ? null : $"Claude model '{RequestModel}' has no supported thinking control.";
            var opus45 = RequestModel.Contains("opus-4-5", StringComparison.OrdinalIgnoreCase);
            if (!ModelSupportsAdaptiveThinking() && !opus45)
                return $"Claude model '{RequestModel}' has no native effort level. Use its provider-specific ThinkingBudget where supported.";
            if ((level == ReasoningLevel.XHigh && (ModelSupportsOptionalAdaptiveThinking() || opus45)) ||
                (level == ReasoningLevel.Max && opus45))
                return $"Claude model '{RequestModel}' does not support reasoning level {level}.";
            return null;
        }
    }
}
