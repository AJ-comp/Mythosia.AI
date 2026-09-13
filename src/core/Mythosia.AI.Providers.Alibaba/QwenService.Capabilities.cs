using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Providers.Alibaba
{
    public partial class QwenService
    {
        private static readonly HashSet<string> KnownQwenChatModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AlibabaModels.QwenMax, AlibabaModels.QwenPlus, AlibabaModels.QwenTurbo,
            AlibabaModels.Qwen3_235B, AlibabaModels.Qwen3_30B, AlibabaModels.Qwen3_32B, AlibabaModels.Qwen3_14B,
            AlibabaModels.Qwen3_8B, AlibabaModels.Qwen3_4B, AlibabaModels.Qwen3_1_7B, AlibabaModels.Qwen3_0_6B,
            AlibabaModels.Qwen3_5_397B, AlibabaModels.Qwen3_5_122B, AlibabaModels.Qwen3_5_35B, AlibabaModels.Qwen3_5_27B,
            AlibabaModels.Qwen3_5_9B, AlibabaModels.Qwen3_5_4B, AlibabaModels.Qwen3_5_2B, AlibabaModels.Qwen3_5_0_8B
        };

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            var model = GetEffectiveModelId();
            // A local server controls aliases, templates, and the actual deployed model.
            var known = _usesDefaultDashScopeEndpoint && KnownQwenChatModels.Contains(model);
            var support = known ? CapabilitySupport.Supported : CapabilitySupport.Unknown;
            var thinking = known && model.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase)
                ? CapabilitySupport.Supported : CapabilitySupport.Unknown;
            // The shared function request body forwards temperature but omits these sampling fields.
            var fullSampling = ShouldUseFunctions ? CapabilitySupport.Unsupported : support;
            return new AIModelCapabilities(
                provider: Provider, model: model, streaming: support, functionCalling: support,
                asyncFunctionCalling: CapabilitySupport.Unsupported, steering: CapabilitySupport.Unsupported,
                reasoning: CapabilitySupport.Unsupported, nativeReasoning: thinking,
                thinkingToggle: _endpointPlatform == EndpointPlatform.Ollama ? CapabilitySupport.Unsupported : thinking,
                webSearch: CapabilitySupport.Unsupported, fileSearch: CapabilitySupport.Unsupported,
                reasoningCachePreservation: CapabilitySupport.Unsupported,
                imageInput: CapabilitySupport.Unknown, structuredOutput: support,
                temperature: support, topP: fullSampling, frequencyPenalty: fullSampling, presencePenalty: fullSampling,
                maxOutputTokens: known ? GetModelMaxOutputTokens() : (uint?)null);
        }
    }
}
