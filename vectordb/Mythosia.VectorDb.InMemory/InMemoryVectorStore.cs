using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb.InMemory
{
    /// <summary>
    /// Thread-safe in-memory implementation of IVectorStore using cosine similarity for TopK search.
    /// Supports metadata storage, scope isolation, filtering, upsert, and delete operations.
    /// Suitable for development, testing, and small-scale workloads.
    /// </summary>
    public class InMemoryVectorStore : IVectorStore, IRagDiagnosticsStore
    {
        private const string DefaultNamespace = "default";
        private const int RrfK = 60;
        private const float HybridVectorWeight = 0.5f;

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, VectorRecord>> _namespaces
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, VectorRecord>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Bm25Index> _bm25Indexes
            = new ConcurrentDictionary<string, Bm25Index>(StringComparer.OrdinalIgnoreCase);

        #region Upsert

        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        {
            var ns = record.Namespace ?? DefaultNamespace;
            var store = GetOrCreateNamespace(ns);
            store[record.Id] = record;

            var bm25 = GetOrCreateBm25Index(ns);
            bm25.Index(record.Id, record.Content);

            return Task.CompletedTask;
        }

        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ns = record.Namespace ?? DefaultNamespace;
                var store = GetOrCreateNamespace(ns);
                store[record.Id] = record;

                var bm25 = GetOrCreateBm25Index(ns);
                bm25.Index(record.Id, record.Content);
            }
            return Task.CompletedTask;
        }

        #endregion

        #region Get / Delete

        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            var ns = filter?.Namespace ?? DefaultNamespace;
            if (_namespaces.TryGetValue(ns, out var store) && store.TryGetValue(id, out var record))
            {
                if (filter != null && !MatchesFilter(record, filter))
                    return Task.FromResult<VectorRecord?>(null);
                return Task.FromResult<VectorRecord?>(record);
            }

            return Task.FromResult<VectorRecord?>(null);
        }

        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            var ns = filter?.Namespace ?? DefaultNamespace;
            if (_namespaces.TryGetValue(ns, out var store))
                store.TryRemove(id, out _);

            if (_bm25Indexes.TryGetValue(ns, out var bm25Index))
                bm25Index.Remove(id);

            return Task.CompletedTask;
        }

        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            var targetNamespaces = filter.Namespace != null
                ? _namespaces.Where(kvp => string.Equals(kvp.Key, filter.Namespace, StringComparison.OrdinalIgnoreCase))
                : _namespaces;

            foreach (var nsKvp in targetNamespaces)
            {
                _bm25Indexes.TryGetValue(nsKvp.Key, out var bm25Index);

                var keysToRemove = nsKvp.Value.Values
                    .Where(r => MatchesFilter(r, filter))
                    .Select(r => r.Id)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    nsKvp.Value.TryRemove(key, out _);
                    bm25Index?.Remove(key);
                }
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Search

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var ns = filter?.Namespace ?? DefaultNamespace;
            if (!_namespaces.TryGetValue(ns, out var store))
                return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());

            var results = store.Values
                .Where(r => filter == null || MatchesFilter(r, filter))
                .Select(r => new VectorSearchResult(r, CosineSimilarity(queryVector, r.Vector)))
                .Where(r => filter?.MinScore == null || r.Score >= (filter?.MinScore ?? 0))
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();

            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
        }

        public async Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var ns = filter?.Namespace ?? DefaultNamespace;
            if (!_namespaces.TryGetValue(ns, out var store) || store.Count == 0)
                return Array.Empty<VectorSearchResult>();

            if (!_bm25Indexes.TryGetValue(ns, out var bm25Index))
                return await SearchAsync(denseVector, topK, filter, cancellationToken);

            var expandedTopK = Math.Max(topK * 2, topK);
            var vectorFilter = WithoutMinScore(filter);

            var vectorResults = await SearchAsync(denseVector, expandedTopK, vectorFilter, cancellationToken);
            var bm25Candidates = bm25Index.Search(query, expandedTopK);
            var bm25Results = bm25Candidates
                .Where(r => store.TryGetValue(r.Id, out var record) && (vectorFilter == null || MatchesFilter(record, vectorFilter)))
                .ToList();

            var merged = RrfMerge(vectorResults, bm25Results, store, topK);

            // Apply MinScore filter to the final RRF-normalized scores
            if (filter?.MinScore.HasValue == true)
            {
                merged = merged.Where(r => r.Score >= filter.MinScore.Value).ToList();
            }

            return merged;
        }

        #endregion

        #region Diagnostics

        /// <summary>
        /// Returns ALL records in a namespace. For diagnostic/debugging use only.
        /// </summary>
        public Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(string? @namespace = null, CancellationToken cancellationToken = default)
        {
            var ns = @namespace ?? DefaultNamespace;
            if (!_namespaces.TryGetValue(ns, out var store))
                return Task.FromResult<IReadOnlyList<VectorRecord>>(Array.Empty<VectorRecord>());

            return Task.FromResult<IReadOnlyList<VectorRecord>>(store.Values.ToList());
        }

        /// <summary>
        /// Returns the total number of records across all namespaces.
        /// </summary>
        public int GetTotalRecordCount()
        {
            return _namespaces.Values.Sum(s => s.Count);
        }

        /// <summary>
        /// Computes cosine similarity scores for a query vector against ALL records in a namespace.
        /// Results are sorted by descending score. No TopK or MinScore filtering is applied.
        /// </summary>
        public Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
            float[] queryVector,
            string? @namespace = null,
            CancellationToken cancellationToken = default)
        {
            var ns = @namespace ?? DefaultNamespace;
            if (!_namespaces.TryGetValue(ns, out var store))
                return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());

            var results = store.Values
                .Select(r => new VectorSearchResult(r, CosineSimilarity(queryVector, r.Vector)))
                .OrderByDescending(r => r.Score)
                .ToList();

            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
        }

        #endregion

        #region Private Helpers

        private ConcurrentDictionary<string, VectorRecord> GetOrCreateNamespace(string @namespace)
        {
            return _namespaces.GetOrAdd(@namespace, _ => new ConcurrentDictionary<string, VectorRecord>(StringComparer.Ordinal));
        }

        private Bm25Index GetOrCreateBm25Index(string @namespace)
        {
            return _bm25Indexes.GetOrAdd(@namespace, _ => new Bm25Index());
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

        private static IReadOnlyList<VectorSearchResult> RrfMerge(
            IReadOnlyList<VectorSearchResult> vectorResults,
            IReadOnlyList<Bm25Index.Bm25Result> bm25Results,
            ConcurrentDictionary<string, VectorRecord> namespaceStore,
            int topK)
        {
            var scores = new Dictionary<string, (double score, VectorSearchResult? vectorResult, Bm25Index.Bm25Result? bm25Result)>(StringComparer.Ordinal);
            var keywordWeight = 1f - HybridVectorWeight;

            for (int i = 0; i < vectorResults.Count; i++)
            {
                var id = vectorResults[i].Record.Id;
                var rrf = HybridVectorWeight * (1.0 / (RrfK + i + 1));

                if (scores.TryGetValue(id, out var existing))
                    scores[id] = (existing.score + rrf, vectorResults[i], existing.bm25Result);
                else
                    scores[id] = (rrf, vectorResults[i], null);
            }

            for (int i = 0; i < bm25Results.Count; i++)
            {
                var id = bm25Results[i].Id;
                var rrf = keywordWeight * (1.0 / (RrfK + i + 1));

                if (scores.TryGetValue(id, out var existing))
                    scores[id] = (existing.score + rrf, existing.vectorResult, bm25Results[i]);
                else
                    scores[id] = (rrf, null, bm25Results[i]);
            }

            // Normalize RRF scores to [0, 1]: max raw RRF = 1/(k+1), so multiply by (k+1).
            double normalizer = RrfK + 1;

            return scores
                .OrderByDescending(kvp => kvp.Value.score)
                .Take(topK)
                .Select(kvp =>
                {
                    var normalizedScore = kvp.Value.score * normalizer;

                    if (kvp.Value.vectorResult != null)
                        return new VectorSearchResult(kvp.Value.vectorResult.Record, normalizedScore);

                    var bm25 = kvp.Value.bm25Result!;
                    if (namespaceStore.TryGetValue(bm25.Id, out var record))
                        return new VectorSearchResult(record, normalizedScore);

                    return new VectorSearchResult(new VectorRecord { Id = bm25.Id, Content = bm25.Content }, normalizedScore);
                })
                .ToList();
        }

        private static bool MatchesFilter(VectorRecord record, VectorFilter filter)
        {
            if (filter.Scope != null && !string.Equals(record.Scope, filter.Scope, StringComparison.Ordinal))
                return false;

            if (filter.MetadataMatch != null)
            {
                foreach (var kvp in filter.MetadataMatch)
                {
                    if (!record.Metadata.TryGetValue(kvp.Key, out var value) ||
                        !string.Equals(value, kvp.Value, StringComparison.Ordinal))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Computes cosine similarity between two vectors. Returns 0 if either vector is zero-length.
        /// </summary>
        internal static double CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0)
                return 0.0;

            double dot = 0.0, normA = 0.0, normB = 0.0;

            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * (double)b[i];
                normA += a[i] * (double)a[i];
                normB += b[i] * (double)b[i];
            }

            if (normA == 0.0 || normB == 0.0)
                return 0.0;

            return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        #endregion
    }
}
