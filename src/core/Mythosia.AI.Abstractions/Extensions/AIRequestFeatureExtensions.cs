using Mythosia.AI.Models;
using Mythosia.AI.Services;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Extensions
{
    /// <summary>Configure the next request while retaining the service's concrete type.</summary>
    public static class AIRequestFeatureExtensions
    {
        public static TService WithReasoning<TService>(this TService service, ReasoningLevel level,
            CachePreservation cache = CachePreservation.None) where TService : IAIService
        {
            Require(service).ConfigureRequestFeatures(new AIRequestFeatures { Reasoning = new ReasoningOptions { Level = level, Cache = cache } });
            return service;
        }

        public static TService WithWebSearch<TService>(this TService service, WebSearchOptions? options = null) where TService : IAIService
        {
            Require(service).ConfigureRequestFeatures(new AIRequestFeatures { WebSearch = options ?? new WebSearchOptions() });
            return service;
        }

        public static TService WithFileSearch<TService>(this TService service, params FileSearchStore[] stores) where TService : IAIService
        {
            if (stores == null) throw new ArgumentNullException(nameof(stores));
            Require(service).ConfigureRequestFeatures(new AIRequestFeatures { FileSearch = new FileSearchOptions { Stores = stores } });
            return service;
        }

        /// <summary>Reads source references when the service implements the optional request-feature capability.</summary>
        public static IReadOnlyList<AICitation> GetLastCitations(this IAIService service) => Require(service).LastCitations;

        private static IAIRequestFeatureService Require(IAIService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            return service as IAIRequestFeatureService ?? throw new NotSupportedException(
                "This AI service does not implement the optional IAIRequestFeatureService capability.");
        }
    }
}
