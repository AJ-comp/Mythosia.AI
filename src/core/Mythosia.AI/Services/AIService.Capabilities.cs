using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System.Collections.Generic;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        /// <summary>Inspects current service defaults and pending feature settings without starting or consuming a request.</summary>
        /// <remarks>No HTTP call, context callback, validation, history change or pending-option capture occurs.
        /// Use a builder's GetCapabilities to inspect its independent settings snapshot instead.</remarks>
        public AIModelCapabilities GetCapabilities()
        {
            AIRequestFeatures features;
            lock (_featureGate) features = _pendingRequestFeatures.Clone();
            // Resolve synchronously against service defaults, isolated from any ambient request.
            // Execution snapshots serialize tool defaults and native option graphs; inspection
            // must not run those serializers or provider capture hooks.
            using var settingsScope = UseRequestSettings(new Dictionary<string, object?>());
            using var featureScope = UseRequestFeatureExecution(new RequestFeatureExecution(features, null));
            return ResolveRequestCapabilities().WithSpeedSupport(
                ResolveSpeedSupport(InferenceSpeed.Standard), ResolveSpeedSupport(InferenceSpeed.Fast));
        }

        /// <summary>Resolves adapter support using RequestSetting and CurrentRequestFeatures without side effects.</summary>
        /// <remarks>Custom providers may override this hook. The default preserves compatibility by returning Unknown.
        /// Overrides must not send requests, consume options, change history or invoke user callbacks.</remarks>
        protected virtual AIModelCapabilities ResolveRequestCapabilities() => AIModelCapabilities.Unknown;

        /// <summary>Applies only execution-local profile settings needed to describe capabilities.</summary>
        /// <remarks>Common builder overrides are already captured. Custom providers may override this
        /// hook with SetExecutionSetting calls for native mode flags. Do not prepare execution, reserve
        /// token budgets, invoke callbacks, validate requests, or modify service/caller-owned state.
        /// Execution profile hooks are intentionally not called during inspection.</remarks>
        protected virtual void ApplyCapabilityRequestProfile(AIRequestProfile profile) { }

        /// <summary>Inspects an image generation model independently of the selected chat model, without HTTP.</summary>
        /// <remarks>A null model uses the provider's image default. Unknown does not mean unsupported.</remarks>
        public virtual ImageModelCapabilities GetImageCapabilities(string? model = null) => ImageModelCapabilities.Unknown;

        internal AIModelCapabilities GetRequestCapabilities(AIRequest request)
        {
            using var settingsScope = UseRequestSettings(request.Settings, copyFunctions: false);
            var suppress = request.Profile != null && request.Profile.Purpose != AIRequestPurpose.Default;
            // The execution clone hook may reset provider diagnostics. Resolvers only read the
            // already captured options, so inspection must not invoke that execution hook.
            using var featureScope = UseRequestFeatureExecution(new RequestFeatureExecution(
                suppress ? new AIRequestFeatures() : request.Features.Clone(), request.Input,
                suppress ? null : request.ProviderOptions));
            var profile = EffectiveProfile(request);
            if (profile != null) ApplyCapabilityRequestProfile(profile);
            return ResolveRequestCapabilities().WithSpeedSupport(
                ResolveSpeedSupport(InferenceSpeed.Standard), ResolveSpeedSupport(InferenceSpeed.Fast));
        }
    }
}
