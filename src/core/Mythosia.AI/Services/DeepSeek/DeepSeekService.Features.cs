using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using System;
using System.Linq;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            if (features.WebSearch != null || features.FileSearch != null)
                throw new NotSupportedException("DeepSeek does not implement hosted web or file search through this adapter.");
            if (features.Reasoning?.Cache == CachePreservation.Required)
                throw new NotSupportedException("DeepSeek does not guarantee cache preservation when changing thinking settings.");
            GetEffectiveThinkingOptions(features);
        }

        private void ValidateDeepSeekToolSelection()
        {
            // Request profiles are applied after common feature validation. Check this combination
            // at provider entry so DisableReasoning can make a named tool request valid.
            var options = GetEffectiveThinkingOptions(CurrentRequestFeatures);
            if (ShouldUseFunctions && RequestFunctionCallMode != FunctionCallMode.None && options.ThinkingEnabled &&
                !string.IsNullOrWhiteSpace(RequestForceFunctionName))
                throw new NotSupportedException("DeepSeek thinking mode does not support a forced named tool. Disable thinking or use automatic tool selection.");
        }

        private DeepSeekRequestOptions GetEffectiveThinkingOptions(AIRequestFeatures features)
        {
            var captured = CurrentProviderRequestOptions as DeepSeekRequestOptions;
            var options = new DeepSeekRequestOptions
            {
                ThinkingEnabled = captured?.ThinkingEnabled ?? RequestSetting(nameof(ThinkingEnabled), ThinkingEnabled),
                ReasoningEffort = captured?.ReasoningEffort ?? RequestSetting(nameof(ReasoningEffort), ReasoningEffort)
            };
            if (!Enum.IsDefined(typeof(DeepSeekReasoning), options.ReasoningEffort))
                throw new ArgumentOutOfRangeException(nameof(ReasoningEffort));
            if (DisableReasoningForProfile)
            {
                options.ThinkingEnabled = false;
                return options;
            }
            if (features.Reasoning == null || features.Reasoning.Level == ReasoningLevel.Auto)
                return options;
            options.ThinkingEnabled = IsEffectiveDeepSeekThinkingEnabled(features);
            // These aliases match DeepSeek's published effort mapping.
            if (!CommonDeepSeekReasoning.TryGetValue(features.Reasoning.Level, out var effort))
                throw new NotSupportedException($"DeepSeek does not support reasoning level '{features.Reasoning.Level}'.");
            options.ReasoningEffort = effort;
            return options;
        }

        protected override string? GetConversationCompactionBlockReason()
        {
            // Tool requests concatenate reasoning from every earlier assistant turn. A summary
            // cannot replace those protocol fields, even after a tool's final answer was emitted.
            if (ShouldUseFunctions && ActivateChat.Messages.Any(message =>
                message.Metadata?.ContainsKey(ReasoningMetadataKey) == true))
                return "deepseek-tool-reasoning-history";
            return base.GetConversationCompactionBlockReason();
        }
    }
}
