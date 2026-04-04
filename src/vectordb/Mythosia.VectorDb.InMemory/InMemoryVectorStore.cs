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
    /// Supports metadata storage, filtering, upsert, and delete operations.
    /// Suitable for development, testing, and small-scale workloads.
    /// </summary>
    public class InMemoryVectorStore : IVectorStore, IRagDiagnosticsStore, IDisposable
    {
        private const int RrfK = 60;
        private const float HybridVectorWeight = 0.5f;

        private readonly ConcurrentDictionary<string, VectorRecord> _records
            = new ConcurrentDictionary<string, VectorRecord>(StringComparer.Ordinal);
        private readonly Bm25Index _bm25Index = new Bm25Index();

        #region Upsert

        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        {
            _records[record.Id] = record;
            _bm25Index.Index(record.Id, record.Content);
            return Task.CompletedTask;
        }

        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _records[record.Id] = record;
                _bm25Index.Index(record.Id, record.Content);
            }
            return Task.CompletedTask;
        }

        #endregion

        #region Get / Delete

        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (!_records.TryGetValue(id, out var record))
                return Task.FromResult<VectorRecord?>(null);

            if (filter != null && !MatchesFilter(record, filter))
                return Task.FromResult<VectorRecord?>(null);

            return Task.FromResult<VectorRecord?>(record);
        }

        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (filter != null && _records.TryGetValue(id, out var existing) && !MatchesFilter(existing, filter))
                return Task.CompletedTask;

            if (_records.TryRemove(id, out _))
                _bm25Index.Remove(id);

            return Task.CompletedTask;
        }

        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            var keysToRemove = _records.Values
                .Where(r => MatchesFilter(r, filter))
                .Select(r => r.Id)
                .ToList();

            foreach (var key in keysToRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _records.TryRemove(key, out _);
                _bm25Index.Remove(key);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Search

        public Task<IReadOnlyList<VectorRecord>> GetBatchAsync(
            IEnumerable<string> ids,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var idList = ids.ToList();
            if (idList.Count == 0)
                return Task.FromResult<IReadOnlyList<VectorRecord>>(Array.Empty<VectorRecord>());

            var results = new List<VectorRecord>();
            foreach (var id in idList)
            {
                if (_records.TryGetValue(id, out var record) &&
                    (filter == null || MatchesFilter(record, filter)))
                {
                    results.Add(record);
                }
            }

            return Task.FromResult<IReadOnlyList<VectorRecord>>(results);
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var results = _records.Values
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
            if (_records.Count == 0)
                return Array.Empty<VectorSearchResult>();

            var expandedTopK = Math.Max(topK * 2, topK);

            var bm25Candidates = _bm25Index.Search(query, expandedTopK).ToList();

            if (bm25Candidates.Count == 0)
                return await SearchAsync(denseVector, topK, filter, cancellationToken);

            var vectorFilter = WithoutMinScore(filter);

            var vectorResults = await SearchAsync(denseVector, expandedTopK, vectorFilter, cancellationToken);
            var bm25Results = bm25Candidates
                .Where(r => _records.TryGetValue(r.Id, out var record) && (vectorFilter == null || MatchesFilter(record, vectorFilter)))
                .ToList();

            var merged = RrfMerge(vectorResults, bm25Results, topK);

            if (filter?.MinScore.HasValue == true)
            {
                merged = merged.Where(r => r.Score >= filter.MinScore.Value).ToList();
            }

            return merged;
        }

        #endregion

        #region Diagnostics

        /// <summary>
        /// Returns ALL records.
        /// For diagnostic/debugging use only.
        /// </summary>
        public Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<VectorRecord>>(_records.Values.ToList());
        }

        /// <summary>
        /// Returns the total number of records.
        /// </summary>
        public int GetTotalRecordCount()
        {
            return _records.Count;
        }

        public Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            if (filter == null || filter.Conditions.Count == 0)
                return Task.FromResult((long)_records.Count);

            return Task.FromResult((long)_records.Values.Count(r => MatchesFilter(r, filter)));
        }

        /// <summary>
        /// Computes cosine similarity scores for a query vector against ALL records.
        /// Results are sorted by descending score. No TopK or MinScore filtering is applied.
        /// </summary>
        public Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
            float[] queryVector,
            CancellationToken cancellationToken = default)
        {
            var results = _records.Values
                .Select(r => new VectorSearchResult(r, CosineSimilarity(queryVector, r.Vector)))
                .OrderByDescending(r => r.Score)
                .ToList();

            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _bm25Index.Dispose();
        }

        #endregion

        #region Private Helpers

        private static VectorFilter? WithoutMinScore(VectorFilter? filter)
        {
            if (filter == null || !filter.MinScore.HasValue)
                return filter;

            var copy = new VectorFilter
            {
                MinScore = null
            };
            copy.AppendConditionsFrom(filter);
            return copy;
        }

        private IReadOnlyList<VectorSearchResult> RrfMerge(
            IReadOnlyList<VectorSearchResult> vectorResults,
            IReadOnlyList<Bm25Index.Bm25Result> bm25Results,
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
                    if (_records.TryGetValue(bm25.Id, out var record))
                        return new VectorSearchResult(record, normalizedScore);

                    return new VectorSearchResult(new VectorRecord { Id = bm25.Id, Content = bm25.Content }, normalizedScore);
                })
                .ToList();
        }

        private static bool MatchesFilter(VectorRecord record, VectorFilter filter)
        {
            if (filter.Conditions.Count > 0 && !EvaluateConditions(record, filter.Conditions, FilterLogic.And))
                return false;

            return true;
        }

        private static bool EvaluateConditions(VectorRecord record, IReadOnlyList<FilterCondition> conditions, FilterLogic logic)
        {
            if (logic == FilterLogic.And)
            {
                foreach (var condition in conditions)
                    if (!EvaluateCondition(record, condition))
                        return false;
                return true;
            }
            else
            {
                foreach (var condition in conditions)
                    if (EvaluateCondition(record, condition))
                        return true;
                return false;
            }
        }

        private static bool EvaluateCondition(VectorRecord record, FilterCondition condition)
        {
            if (condition is MetadataCondition mc)
                return EvaluateMetadataCondition(record, mc);
            if (condition is FilterGroup group)
                return EvaluateConditions(record, group.Conditions, group.Logic);
            return true;
        }

        private static bool EvaluateMetadataCondition(VectorRecord record, MetadataCondition mc)
        {
            switch (mc.Operator)
            {
                case FilterOperator.Eq:
                    return record.Metadata.TryGetValue(mc.Key, out var eqVal) &&
                           string.Equals(eqVal, mc.Value, StringComparison.Ordinal);

                case FilterOperator.Ne:
                    return record.Metadata.TryGetValue(mc.Key, out var neVal) &&
                           !string.Equals(neVal, mc.Value, StringComparison.Ordinal);

                case FilterOperator.In:
                    return record.Metadata.TryGetValue(mc.Key, out var inVal) &&
                           mc.Values != null &&
                           ContainsOrdinal(mc.Values, inVal);

                case FilterOperator.NotIn:
                    return record.Metadata.TryGetValue(mc.Key, out var ninVal) &&
                           (mc.Values == null || !ContainsOrdinal(mc.Values, ninVal));

                case FilterOperator.Like:
                    return record.Metadata.TryGetValue(mc.Key, out var likeVal) &&
                           LikeMatch(likeVal, mc.Value ?? string.Empty);

                case FilterOperator.Gt:
                    return record.Metadata.TryGetValue(mc.Key, out var gtVal) &&
                           string.Compare(gtVal, mc.Value, StringComparison.Ordinal) > 0;

                case FilterOperator.Gte:
                    return record.Metadata.TryGetValue(mc.Key, out var gteVal) &&
                           string.Compare(gteVal, mc.Value, StringComparison.Ordinal) >= 0;

                case FilterOperator.Lt:
                    return record.Metadata.TryGetValue(mc.Key, out var ltVal) &&
                           string.Compare(ltVal, mc.Value, StringComparison.Ordinal) < 0;

                case FilterOperator.Lte:
                    return record.Metadata.TryGetValue(mc.Key, out var lteVal) &&
                           string.Compare(lteVal, mc.Value, StringComparison.Ordinal) <= 0;

                case FilterOperator.Exists:
                    return record.Metadata.ContainsKey(mc.Key);

                case FilterOperator.NotExists:
                    return !record.Metadata.ContainsKey(mc.Key);

                default:
                    return true;
            }
        }

        private static bool ContainsOrdinal(IReadOnlyList<string> values, string target)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], target, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// SQL LIKE pattern matching: <c>%</c> matches any sequence, <c>_</c> matches any single character.
        /// </summary>
        private static bool LikeMatch(string text, string pattern)
        {
            return LikeMatchCore(text, pattern, 0, 0);
        }

        private static bool LikeMatchCore(string text, string pattern, int t, int p)
        {
            while (t < text.Length && p < pattern.Length)
            {
                if (pattern[p] == '%')
                {
                    // Collapse consecutive '%'
                    while (p < pattern.Length && pattern[p] == '%') p++;
                    if (p == pattern.Length) return true;
                    // Try matching the remainder of the pattern at every position
                    for (int i = t; i <= text.Length; i++)
                        if (LikeMatchCore(text, pattern, i, p))
                            return true;
                    return false;
                }

                if (pattern[p] == '_' || pattern[p] == text[t])
                {
                    t++;
                    p++;
                }
                else
                {
                    return false;
                }
            }

            // Consume any trailing '%'
            while (p < pattern.Length && pattern[p] == '%') p++;
            return t == text.Length && p == pattern.Length;
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
