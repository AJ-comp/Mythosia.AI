using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Perplexity;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        private static readonly HashSet<string> KnownPerplexityAgentModels = new HashSet<string>(StringComparer.Ordinal)
        {
            AIModels.Perplexity.Sonar, AIModels.Perplexity.Gpt5_6Sol, AIModels.Perplexity.Gpt5_6Terra,
            AIModels.Perplexity.Gpt5_6Luna, AIModels.Perplexity.ClaudeFable5, AIModels.Perplexity.ClaudeOpus5,
            AIModels.Perplexity.ClaudeSonnet5, AIModels.Perplexity.Gemini3_8Flash, AIModels.Perplexity.Grok4_6,
            AIModels.Perplexity.DeepSeekV4Flash0731, AIModels.Perplexity.KimiK3
        };

        protected override AIModelCapabilities ResolveRequestCapabilities()
        {
            var options = SuppressAgentTools ? new PerplexityAgentOptions { DisableWebSearch = true }
                : CurrentProviderRequestOptions as PerplexityAgentOptions ?? RequestAgentOptions;
            var model = ResolveAgentRequestedModel(RequestModel, options);
            var known = model != null && KnownPerplexityAgentModels.Contains(model);
            var support = known ? CapabilitySupport.Supported : CapabilitySupport.Unknown;
            // The gateway owns each selected model's effort validation. A preset/profile or model
            // fallback list does not identify one model whose reasoning settings can be promised.
            var reasoning = HasConfigurableAgentReasoning(model) ? CapabilitySupport.Unknown : CapabilitySupport.Unsupported;
            return new AIModelCapabilities(
                provider: Provider, model: model, streaming: support, functionCalling: support,
                asyncFunctionCalling: CapabilitySupport.Unsupported, steering: CapabilitySupport.Unsupported,
                reasoning: reasoning, nativeReasoning: reasoning, thinkingToggle: CapabilitySupport.Unsupported,
                webSearch: support, fileSearch: CapabilitySupport.Unsupported,
                reasoningCachePreservation: CapabilitySupport.Unsupported,
                imageInput: support, structuredOutput: support, temperature: support, topP: support,
                frequencyPenalty: CapabilitySupport.Unsupported, presencePenalty: CapabilitySupport.Unsupported);
        }

        private static bool HasConfigurableAgentReasoning(string? model) => model != AIModels.Perplexity.Sonar;
    }
}
