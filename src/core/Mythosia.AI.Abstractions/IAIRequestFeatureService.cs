using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Services
{
    /// <summary>Optional capability for reasoning and provider-hosted search. Existing IAIService implementations need not implement it.</summary>
    public interface IAIRequestFeatureService
    {
        /// <summary>Merges and copies non-null settings for the next logical request.</summary>
        void ConfigureRequestFeatures(AIRequestFeatures features);
        /// <summary>Validates pending settings without consuming them or changing conversation history.</summary>
        void ValidatePendingRequestFeatures();
        /// <summary>Captures options before wrapper preparation. Dispose after the logical request; nested calls share the scope.</summary>
        /// <remarks>For orchestration adapters such as RAG. Call synchronously inside the adapter's async request method.</remarks>
        IDisposable BeginRequestFeaturesScope(Message message);
        /// <summary>A snapshot of source references collected by the most recent request.</summary>
        IReadOnlyList<AICitation> LastCitations { get; }
    }
}
