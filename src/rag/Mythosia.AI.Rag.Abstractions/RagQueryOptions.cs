using Mythosia.VectorDb;
using System;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// RAG pipeline stages that can be reported for progress diagnostics.
    /// </summary>
    public enum RagProgressStage
    {
        QueryRewrite,
        Embedding,
        Filtering,
        Retrieval,
        Reranking,
        ContextBuild
    }

    /// <summary>
    /// Public request model for per-query RAG overrides.
    /// </summary>
    public sealed class RagQueryOptions
    {
        /// <summary>
        /// Override for final selection policy applied after retrieval and optional re-ranking.
        /// </summary>
        public RagFilter FinalFilter { get; set; } = new RagFilter();

        /// <summary>
        /// Override for how retrieval candidate settings are derived from <see cref="FinalFilter"/>.
        /// </summary>
        public RagRetrievalDerivation RetrievalDerivation { get; set; } = new RagRetrievalDerivation();

        /// <summary>
        /// Controls how final references are selected after optional re-ranking.
        /// Defaults to reranker-only selection for backward compatibility.
        /// </summary>
        public RagFinalSelectionOptions FinalSelection { get; set; } = new RagFinalSelectionOptions();

        /// <summary>
        /// Optional async progress callback invoked when the RAG pipeline
        /// enters each processing stage.
        /// </summary>
        public Func<RagProgressStage, Task>? ProgressAsync { get; set; }

        /// <summary>
        /// Optional metadata filter passed directly to <see cref="IVectorStore.SearchAsync"/> /
        /// <see cref="IVectorStore.HybridSearchAsync"/> on every retrieval call.
        /// Use this to scope searches by tenant, user, category, time range, etc.
        /// When null the retrieval is unfiltered (same as before).
        /// For logical isolation (e.g. tenant, category), add conditions via
        /// <see cref="VectorFilter.Where(string, string)"/>.
        /// </summary>
        public VectorFilter? StoreFilter { get; set; }

        public RagRetrievalFilter GetRetrievalFilter(bool hasReranker)
        {
            var divider = RetrievalDerivation.MinScoreDivider <= 0d ? 1d : RetrievalDerivation.MinScoreDivider;

            return hasReranker
                ? new RagRetrievalFilter(
                    FinalFilter.TopK * Math.Max(1, RetrievalDerivation.TopKMultiplier),
                    FinalFilter.MinScore.HasValue ? FinalFilter.MinScore.Value / divider : (double?)null)
                : new RagRetrievalFilter(FinalFilter.TopK, FinalFilter.MinScore);
        }
    }
}
