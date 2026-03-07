using System.Collections.Generic;
using Mythosia.VectorDb;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Diagnostics metadata captured during RAG query processing.
    /// </summary>
    public class RagQueryDiagnostics
    {
        /// <summary>
        /// The namespace that was actually applied during retrieval.
        /// </summary>
        public string AppliedNamespace { get; set; } = string.Empty;

        /// <summary>
        /// The TopK value that was actually applied during retrieval.
        /// </summary>
        public int AppliedTopK { get; set; }

        /// <summary>
        /// The MinScore value that was actually applied during retrieval.
        /// </summary>
        public double? AppliedMinScore { get; set; }

        /// <summary>
        /// End-to-end processing time for retrieval/context assembly in milliseconds.
        /// </summary>
        public long ElapsedMs { get; set; }
    }

    /// <summary>
    /// The output of IRagPipeline.ProcessAsync — contains the augmented prompt ready for the LLM,
    /// along with the original query and retrieved references.
    /// </summary>
    public class RagProcessedQuery
    {
        /// <summary>
        /// The original user query.
        /// </summary>
        public string OriginalQuery { get; set; } = string.Empty;

        /// <summary>
        /// The rewritten query produced by <see cref="IQueryRewriter"/>, or null if no rewriting occurred.
        /// When set, this standalone query was used for vector search instead of <see cref="OriginalQuery"/>.
        /// </summary>
        public string? RewrittenQuery { get; set; }

        /// <summary>
        /// The fully assembled prompt (context + query) to send to the LLM.
        /// </summary>
        public string AugmentedPrompt { get; set; } = string.Empty;

        /// <summary>
        /// The retrieved search results used to build the context.
        /// </summary>
        public IReadOnlyList<VectorSearchResult> References { get; set; } = System.Array.Empty<VectorSearchResult>();

        /// <summary>
        /// Whether the query returned any references from the vector store.
        /// When false, <see cref="AugmentedPrompt"/> contains the original query unchanged.
        /// </summary>
        public bool HasReferences => References.Count > 0;

        /// <summary>
        /// Processing diagnostics for this query.
        /// </summary>
        public RagQueryDiagnostics Diagnostics { get; set; } = new RagQueryDiagnostics();

        public RagProcessedQuery(
            string originalQuery,
            string augmentedPrompt,
            IReadOnlyList<VectorSearchResult> references,
            RagQueryDiagnostics diagnostics)
        {
            OriginalQuery = originalQuery;
            AugmentedPrompt = augmentedPrompt;
            References = references;
            Diagnostics = diagnostics ?? new RagQueryDiagnostics();
        }
    }
}
