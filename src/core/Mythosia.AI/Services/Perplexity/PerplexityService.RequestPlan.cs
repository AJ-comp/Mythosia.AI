using Mythosia.AI.Models;
using Mythosia.AI.Models.Perplexity;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        private const string AgentRequestPlanSetting = "Perplexity.RequestPlan";

        // Profile application precedes this selection. Auxiliary work intentionally
        // owns empty options; null does not mean to inherit the parent's captured ones.
        // Capability inspection has no input and can inspect defaults without cloning.
        private PerplexityAgentOptions EffectiveAgentOptions()
            => SuppressAgentTools || (CurrentFeatureRequestMessage != null && CurrentProviderRequestOptions == null)
                ? new PerplexityAgentOptions { DisableWebSearch = true }
                : CurrentProviderRequestOptions as PerplexityAgentOptions ?? RequestAgentOptions;

        private AgentRequestPlan ResolveAgentRequestPlan(AIRequestFeatures features)
        {
            var options = EffectiveAgentOptions().Clone();
            var effectiveFeatures = SuppressAgentTools ? new AIRequestFeatures() : features.Clone();
            var model = ResolveAgentRequestedModel(RequestModel, options);
            var effort = effectiveFeatures.Reasoning?.Level ?? ReasoningLevel.Auto;
            if (effort == ReasoningLevel.Auto) effort = options.ReasoningEffort;
            // Presets, profiles and fallback lists do not identify one model. Do not
            // infer their minimum effort from the unused service model or override.
            if (DisableAgentReasoning) effort = MinimumAgentReasoning(model);
            if (effort != ReasoningLevel.Auto) ValidateAgentReasoning(effort, model);
            var advanced = new Dictionary<string, object>();
            ApplyAdvancedAgentOptions(advanced, options);
            return new AgentRequestPlan(options, effectiveFeatures, model, effort, advanced);
        }

        private AgentRequestPlan PreparedAgentRequestPlan
            => RequestSetting<AgentRequestPlan?>(AgentRequestPlanSetting, null)
                ?? ResolveAgentRequestPlan(CurrentRequestFeatures);

        // The snapshot is private and read-only after validation. Each wire body copies
        // its collections before adding attempt-specific input, functions or output schema.
        private sealed class AgentRequestPlan
        {
            internal PerplexityAgentOptions Options { get; }
            internal AIRequestFeatures Features { get; }
            internal string? Model { get; }
            internal ReasoningLevel Reasoning { get; }
            internal IReadOnlyDictionary<string, object> Advanced { get; }

            internal AgentRequestPlan(PerplexityAgentOptions options, AIRequestFeatures features,
                string? model, ReasoningLevel reasoning, IReadOnlyDictionary<string, object> advanced)
            {
                Options = options;
                Features = features;
                Model = model;
                Reasoning = reasoning;
                Advanced = advanced;
            }
        }
    }
}
