using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Retrieval
{
    /// <summary>
    /// Hybrid retrieval strategy that combines dense vector search with BM25 keyword search.
    /// Delegates to <see cref="IVectorStore.HybridSearchAsync(float[], string, int, VectorFilter?, CancellationToken)"/>
    /// when supported by the store and falls back to application-level BM25 + RRF merge otherwise.
    /// </summary>
    internal sealed class HybridRetrievalStrategy : IRetrievalStrategy
    {
        private const int RrfK = 60;

        private readonly IVectorStore _vectorStore;
        private readonly float _vectorWeight;
        private readonly Bm25Index? _bm25Index;

        /// <summary>
        /// Creates a hybrid retrieval strategy.
        /// </summary>
        /// <param name="vectorStore">The underlying vector store.</param>
        /// <param name="vectorWeight">Weight for vector similarity [0, 1]. 0.5 = equal weight.</param>
        /// <param name="bm25Index">
        /// BM25 index for stores that do not support native hybrid search.
        /// </param>
        public HybridRetrievalStrategy(IVectorStore vectorStore, float vectorWeight, Bm25Index? bm25Index)
        {
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _vectorWeight = Math.Max(0f, Math.Min(1f, vectorWeight));
            _bm25Index = bm25Index;
        }

        public async Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
            float[] denseVector,
            string? lexicalQuery,
            int topK,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            // When no lexical query is provided (keywords were null), skip BM25 entirely
            // and fall back to dense-only search even in hybrid mode.
            if (string.IsNullOrEmpty(lexicalQuery))
            {
                return await _vectorStore.SearchAsync(denseVector, topK, filter, cancellationToken);
            }

            // Native hybrid delegation via IVectorStore contract (if implemented by store)
            try
            {
                return await _vectorStore.HybridSearchAsync(
                    denseVector, lexicalQuery, topK, filter, cancellationToken);
            }
            catch (NotSupportedException)
            {
                // Fall through to application-level hybrid fallback.
            }

            // Fallback: application-level BM25 + vector search + RRF merge
            if (_bm25Index == null)
            {
                // No BM25 index available — fall back to pure vector search
                return await _vectorStore.SearchAsync(denseVector, topK, filter, cancellationToken);
            }

            var expandedTopK = topK * 2;

            // Strip MinScore before vector search so RRF fusion operates on the full candidate set.
            // MinScore is applied after the merge to the final RRF-normalized scores.
            var searchFilter = WithoutMinScore(filter);
            var vectorResults = await _vectorStore.SearchAsync(denseVector, expandedTopK, searchFilter, cancellationToken);
            var bm25Results = _bm25Index.Search(lexicalQuery, expandedTopK);

            var merged = RrfMerge(vectorResults, bm25Results, topK);

            // Apply MinScore filter to the final RRF-normalized scores
            if (filter?.MinScore.HasValue == true)
            {
                merged = merged.Where(r => r.Score >= filter.MinScore.Value).ToList();
            }

            return merged;
        }

        /// <summary>
        /// Reciprocal Rank Fusion: score(d) = Σ 1 / (k + rank_i(d)), k = 60.
        /// Applies vector weight to balance the two result sets.
        /// </summary>
        private IReadOnlyList<VectorSearchResult> RrfMerge(
            IReadOnlyList<VectorSearchResult> vectorResults,
            IReadOnlyList<Bm25Index.Bm25Result> bm25Results,
            int topK)
        {
            var scores = new Dictionary<string, (double rrfScore, VectorSearchResult? vectorResult, Bm25Index.Bm25Result? bm25Result)>(StringComparer.Ordinal);
            float keywordWeight = 1f - _vectorWeight;

            // Score vector results
            for (int i = 0; i < vectorResults.Count; i++)
            {
                var id = vectorResults[i].Record.Id;
                var rrfScore = _vectorWeight * (1.0 / (RrfK + i + 1));

                if (scores.TryGetValue(id, out var existing))
                    scores[id] = (existing.rrfScore + rrfScore, vectorResults[i], existing.bm25Result);
                else
                    scores[id] = (rrfScore, vectorResults[i], null);
            }

            // Score BM25 results
            for (int i = 0; i < bm25Results.Count; i++)
            {
                var id = bm25Results[i].Id;
                var rrfScore = keywordWeight * (1.0 / (RrfK + i + 1));

                if (scores.TryGetValue(id, out var existing))
                    scores[id] = (existing.rrfScore + rrfScore, existing.vectorResult, bm25Results[i]);
                else
                    scores[id] = (rrfScore, null, bm25Results[i]);
            }

            // Normalize RRF scores to [0, 1]: max raw RRF = 1/(k+1), so multiply by (k+1).
            double normalizer = RrfK + 1;

            var merged = scores
                .OrderByDescending(kvp => kvp.Value.rrfScore)
                .Take(topK)
                .Select(kvp =>
                {
                    var normalizedScore = kvp.Value.rrfScore * normalizer;

                    // Prefer vector result (has full VectorRecord with embedding)
                    if (kvp.Value.vectorResult != null)
                    {
                        return new VectorSearchResult(kvp.Value.vectorResult.Record, normalizedScore);
                    }

                    // BM25-only result — construct a minimal record
                    var bm25 = kvp.Value.bm25Result!;
                    var record = new VectorRecord
                    {
                        Id = bm25.Id,
                        Content = bm25.Content
                    };
                    return new VectorSearchResult(record, normalizedScore);
                })
                .ToList();

            return merged;
        }

        private static VectorFilter? WithoutMinScore(VectorFilter? filter)
        {
            if (filter == null || !filter.MinScore.HasValue)
                return filter;

            return new VectorFilter
            {
                Namespace = filter.Namespace,
                Scope = filter.Scope,
                MetadataMatch = filter.MetadataMatch,
                MinScore = null
            };
        }
    }
}
