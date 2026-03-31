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
        /// The final TopK target retained after retrieval and optional re-ranking.
        /// </summary>
        public int FinalTopK { get; set; }

        /// <summary>
        /// The TopK value actually sent to the retrieval strategy.
        /// When a reranker is configured this is typically FinalTopK × RetrievalMultiplier;
        /// otherwise it equals <see cref="FinalTopK"/>.
        /// </summary>
        public int RetrievalTopK { get; set; }

        /// <summary>
        /// The final score threshold that was applied after retrieval and optional re-ranking.
        /// </summary>
        public double? AppliedFinalMinScore { get; set; }

        /// <summary>
        /// The retrieval score threshold that was applied before final selection.
        /// </summary>
        public double? AppliedRetrievalMinScore { get; set; }

        /// <summary>
        /// End-to-end processing time for retrieval/context assembly in milliseconds.
        /// </summary>
        public long ElapsedMs { get; set; }

        /// <summary>
        /// Time spent on query rewriting in milliseconds. Zero when rewriting was skipped.
        /// </summary>
        public long RewriteElapsedMs { get; set; }
    }

    /// <summary>
    /// The output of IRagPipeline.ProcessAsync — contains the transient request message content
    /// ready for the LLM, along with the original query and retrieved references.
    /// </summary>
    public class RagProcessedQuery
    {
        /// <summary>
        /// The original user query.
        /// </summary>
        public string OriginalQuery { get; set; } = string.Empty;

        /// <summary>
        /// The rewritten semantic query produced by <see cref="IQueryRewriter"/>, or null if no rewriting occurred.
        /// When set, this standalone query was used for embedding/vector retrieval instead of <see cref="OriginalQuery"/>.
        /// </summary>
        public string? RewrittenQuery { get; set; }

        /// <summary>
        /// Retrieval-oriented keywords extracted by the query rewriter for the text/keyword search leg of hybrid search.
        /// When set, hybrid search uses these shaped terms instead of the raw query,
        /// helping lexical retrieval handle language-particle and formatting mismatches.
        /// Null when no keyword shaping was produced or no rewriter was invoked.
        /// </summary>
        public IReadOnlyList<string>? SearchKeywords { get; set; }

        /// <summary>
        /// The request-only user input assembled from retrieval context and the query.
        /// This value is intended for transient LLM request transport and should not be
        /// persisted back into conversation history.
        /// </summary>
        public string RequestMessageContent { get; set; } = string.Empty;

        /// <summary>
        /// The retrieved search results used to build the context.
        /// </summary>
        public IReadOnlyList<VectorSearchResult> References { get; set; } = System.Array.Empty<VectorSearchResult>();

        /// <summary>
        /// The raw retrieval candidates returned before re-ranking was applied.
        /// When no reranker is configured this matches <see cref="References"/>.
        /// </summary>
        public IReadOnlyList<VectorSearchResult> RetrievalCandidates { get; set; } = System.Array.Empty<VectorSearchResult>();

        /// <summary>
        /// All results after re-ranking (re-scored and reordered) but before final selection (topK + minScore).
        /// When no reranker is configured this is null.
        /// </summary>
        public IReadOnlyList<VectorSearchResult>? RerankedCandidates { get; set; }

        /// <summary>
        /// Whether the query returned any references from the vector store.
        /// When false, <see cref="RequestMessageContent"/> contains the original query unchanged.
        /// </summary>
        public bool HasReferences => References.Count > 0;

        /// <summary>
        /// Whether the search gate determined that document search was not needed for this query.
        /// When true, the RAG pipeline was skipped entirely (no embedding, no vector search).
        /// </summary>
        public bool SearchSkipped { get; set; }

        /// <summary>
        /// The raw result from the query rewriter, or null if no rewriter was invoked.
        /// This can include a rewritten semantic query, retrieval-oriented keywords, and the search gate decision.
        /// </summary>
        public QueryRewriteResult? RewriteResult { get; set; }

        /// <summary>
        /// Processing diagnostics for this query.
        /// </summary>
        public RagQueryDiagnostics Diagnostics { get; set; } = new RagQueryDiagnostics();

        public RagProcessedQuery(
            string originalQuery,
            string requestMessageContent,
            IReadOnlyList<VectorSearchResult> references,
            IReadOnlyList<VectorSearchResult> retrievalCandidates,
            RagQueryDiagnostics diagnostics)
        {
            OriginalQuery = originalQuery;
            RequestMessageContent = requestMessageContent;
            References = references;
            RetrievalCandidates = retrievalCandidates;
            Diagnostics = diagnostics ?? new RagQueryDiagnostics();
        }
    }
}
