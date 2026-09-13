using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private static readonly HashSet<string> KnownGeminiChatModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AIModels.Google.Gemini2_5Pro, AIModels.Google.Gemini2_5Flash, AIModels.Google.Gemini2_5FlashLite,
            AIModels.Google.Gemini3FlashPreview, AIModels.Google.Gemini3_1ProPreview,
            AIModels.Google.Gemini3_1FlashLite, AIModels.Google.Gemini3_5Flash, AIModels.Google.Gemini3_5FlashLite,
            AIModels.Google.Gemini3_6Flash, AIModels.Google.Gemini3_7Flash, AIModels.Google.Gemini3_8Flash
        };

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            var known = KnownGeminiChatModels.Contains(RequestModel);
            var support = known ? CapabilitySupport.Supported : CapabilitySupport.Unknown;
            var levels = known ? GetCommonGeminiReasoningLevels() : Array.Empty<ReasoningLevel>();
            var budgetPresets = !known || IsGemini3Model() ? Array.Empty<int>()
                : RequestModel.Equals(AIModels.Google.Gemini2_5Pro, StringComparison.OrdinalIgnoreCase)
                    ? new[] { -1, 128, 1024, 4096, 8192, 16384, 32768 }
                    : new[] { 0, -1, 512, 1024, 4096, 8192, 16384, 24576 };
            return new AIModelCapabilities(
                provider: Provider, model: GetRunRequestedModel(), streaming: support, functionCalling: support,
                asyncFunctionCalling: CapabilitySupport.Unsupported, steering: CapabilitySupport.Unsupported,
                reasoning: support, reasoningLevels: levels,
                nativeReasoning: support, nativeReasoningLevels: known && IsGemini3Model() ? levels : Array.Empty<ReasoningLevel>(),
                thinkingBudgetPresets: budgetPresets,
                thinkingToggle: known ? (IsGemini3Model() || HasRequiredGeminiThinkingBudget()
                    ? CapabilitySupport.Unsupported : CapabilitySupport.Supported) : CapabilitySupport.Unknown,
                webSearch: IsGeminiSearchAdapterModel() ? support : CapabilitySupport.Unsupported,
                fileSearch: IsGeminiSearchAdapterModel() ? support : CapabilitySupport.Unsupported,
                reasoningCachePreservation: CapabilitySupport.Unsupported, imageInput: support, structuredOutput: support,
                temperature: UsesLatestSamplingContract() ? CapabilitySupport.Unsupported : support,
                topP: UsesLatestSamplingContract() ? CapabilitySupport.Unsupported : support,
                frequencyPenalty: CapabilitySupport.Unsupported, presencePenalty: CapabilitySupport.Unsupported,
                maxOutputTokens: known ? GetModelMaxOutputTokens() : (uint?)null);
        }

        private ReasoningLevel[] GetCommonGeminiReasoningLevels()
        {
            if (IsGemini3Model())
                return HasLowThinkingFloor()
                    ? new[] { ReasoningLevel.Auto, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High }
                    : new[] { ReasoningLevel.Auto, ReasoningLevel.Minimal, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High };
            return RequestModel.StartsWith("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase)
                ? new[] { ReasoningLevel.Auto, ReasoningLevel.None } : new[] { ReasoningLevel.Auto };
        }

        private bool IsGeminiSearchAdapterModel() =>
            (IsGemini3Model() || RequestModel.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase)) &&
            !RequestModel.Contains("image", StringComparison.OrdinalIgnoreCase);

        private bool HasRequiredGeminiThinkingBudget() =>
            RequestModel.Equals(AIModels.Google.Gemini2_5Pro, StringComparison.OrdinalIgnoreCase);
    }
}
