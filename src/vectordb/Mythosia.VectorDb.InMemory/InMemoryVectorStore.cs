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
    /// <remarks>
    /// Each record update coordinates the stored body and keyword index. Searches,
    /// including both hybrid legs, read a consistent store state. Stored and returned
    /// records own copies of vectors and metadata; persist edits through upsert.
    /// Cancellation can stop waiting for another operation to release the store,
    /// without interrupting that operation or splitting a record update.
    /// Batch writes and the default interface replacement are not transactions.
    /// </remarks>
    public class InMemoryVectorStore : IVectorStore, ITextSearchStore, IConfigurableHybridSearchStore, IRagDiagnosticsStore, IDisposable
    {
        private readonly ConcurrentDictionary<string, VectorRecord> _records
            = new ConcurrentDictionary<string, VectorRecord>(StringComparer.Ordinal);
        private readonly Bm25Index _bm25Index = new Bm25Index();
        // The dictionary and Lucene index form one store state. Their individual
        // thread-safety does not make a change spanning both structures atomic.
        private readonly object _stateLock = new object();
        private bool _disposed;

        #region Upsert

        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        {
            WithState(() => UpsertCore(record), cancellationToken);
            return Task.CompletedTask;
        }

        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            WithState(() => { }, cancellationToken);
            foreach (var record in records)
            {
                // Enumerate caller code outside the gate. Cancellation may leave
                // earlier records committed, but never half of a record update.
                WithState(() => UpsertCore(record), cancellationToken);
            }
            return Task.CompletedTask;
        }

        #endregion

        #region Get / Delete

        public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WithState<VectorRecord?>(() =>
            {
                if (!_records.TryGetValue(id, out var record) ||
                    (filter != null && !MatchesFilter(record, filter)))
                    return null;
                return CopyRecord(record);
            }, cancellationToken));
        }

        public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            WithState(() =>
            {
                if (_records.TryGetValue(id, out var existing) &&
                    (filter == null || MatchesFilter(existing, filter)))
                    DeleteCore(id);
            }, cancellationToken);
            return Task.CompletedTask;
        }

        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            WithState(() =>
            {
                var keysToRemove = _records.Values
                    .Where(r => MatchesFilter(r, filter))
                    .Select(r => r.Id)
                    .ToList();
                foreach (var key in keysToRemove)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DeleteCore(key);
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        #endregion

        #region Search

        public Task<IReadOnlyList<VectorRecord>> GetBatchAsync(
            IEnumerable<string> ids,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var idList = ids.ToList();
            return Task.FromResult(WithState<IReadOnlyList<VectorRecord>>(() =>
            {
                var results = new List<VectorRecord>();
                foreach (var id in idList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_records.TryGetValue(id, out var record) &&
                        (filter == null || MatchesFilter(record, filter)))
                        results.Add(CopyRecord(record));
                }
                return results;
            }, cancellationToken));
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WithState(() => SearchCore(queryVector, topK, filter), cancellationToken));
        }

        // Call only while holding _stateLock, including both legs of hybrid search.
        private IReadOnlyList<VectorSearchResult> SearchCore(float[] queryVector, int topK, VectorFilter? filter)
        {
            if (queryVector == null) throw new ArgumentNullException(nameof(queryVector));
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));
            var results = _records.Values
                .Where(r => filter == null || MatchesFilter(r, filter))
                .Select(r => new VectorSearchResult(r, CosineSimilarity(queryVector, r.Vector)))
                .Where(r => filter?.MinScore == null || r.Score >= (filter?.MinScore ?? 0))
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Record.Id, StringComparer.Ordinal)
                .Take(topK)
                .Select(r => new VectorSearchResult(CopyRecord(r.Record), r.Score))
                .ToList();
            return results;
        }

        /// <summary>Searches text without producing or requiring a query embedding.</summary>
        public Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(
            string query, int topK = 5, VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var results = WithState(() => TextSearchCore(query, topK, filter), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(results);
        }

        private IReadOnlyList<VectorSearchResult> TextSearchCore(string query, int topK, VectorFilter? filter)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));

            // Restrict the Lucene candidates before top-K, so other tenants cannot
            // consume the candidate budget and hide relevant records in this tenant.
            var eligible = _records.Values
                .Where(record => filter == null || MatchesFilter(record, filter))
                .ToDictionary(record => record.Id, StringComparer.Ordinal);
            if (eligible.Count == 0)
                return Array.Empty<VectorSearchResult>();

            var results = _bm25Index.Search(query, topK, eligible.Keys.ToArray())
                .Where(result => eligible.ContainsKey(result.Id))
                .Select(result => new VectorSearchResult(CopyRecord(eligible[result.Id]), result.Score))
                .Where(result => filter?.MinScore == null || result.Score >= filter.MinScore.Value)
                .ToList();
            return results;
        }

        public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector, string query, int topK = 5,
            VectorFilter? filter = null, CancellationToken cancellationToken = default)
            => HybridSearchAsync(denseVector, query, new HybridSearchOptions(), topK, filter, cancellationToken);

        /// <summary>Combines only the active search legs using normalized weighted RRF.</summary>
        public async Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector, string query, HybridSearchOptions options, int topK = 5,
            VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options == null) throw new ArgumentNullException(nameof(options));
            var snapshot = options.Snapshot();
            var candidateCount = snapshot.GetCandidateCount(topK);
            var candidateFilter = WithoutMinScore(filter);
            return await Task.FromResult(WithState(() =>
            {
                var vectors = snapshot.VectorWeight > 0
                    ? SearchCore(denseVector, candidateCount, candidateFilter)
                    : Array.Empty<VectorSearchResult>();
                var text = snapshot.VectorWeight < 1
                    ? TextSearchCore(query, candidateCount, candidateFilter)
                    : Array.Empty<VectorSearchResult>();
                cancellationToken.ThrowIfCancellationRequested();
                return HybridSearchFusion.Merge(vectors, text, snapshot, topK, filter?.MinScore);
            }, cancellationToken));
        }

        #endregion

        #region Diagnostics

        /// <summary>
        /// Returns ALL records.
        /// For diagnostic/debugging use only.
        /// </summary>
        public Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WithState<IReadOnlyList<VectorRecord>>(
                () => _records.Values.Select(CopyRecord).ToList(), cancellationToken));
        }

        /// <summary>
        /// Returns the total number of records.
        /// </summary>
        public int GetTotalRecordCount()
        {
            return WithState(() => _records.Count, CancellationToken.None);
        }

        public Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WithState(() =>
                filter == null || filter.Conditions.Count == 0
                    ? (long)_records.Count
                    : _records.Values.LongCount(r => MatchesFilter(r, filter)), cancellationToken));
        }

        /// <summary>
        /// Computes cosine similarity scores for a query vector against ALL records.
        /// Results are sorted by descending score. No TopK or MinScore filtering is applied.
        /// </summary>
        public Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
            float[] queryVector,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WithState<IReadOnlyList<VectorSearchResult>>(() => _records.Values
                .Select(r => new VectorSearchResult(CopyRecord(r), CosineSimilarity(queryVector, r.Vector)))
                .OrderByDescending(r => r.Score)
                .ToList(), cancellationToken));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (_disposed) return;
                _disposed = true;
                _bm25Index.Dispose();
            }
        }

        #endregion

        #region Private Helpers

        private T WithState<T>(Func<T> action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lockTaken = false;
            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    // Check cancellation while waiting, not only after an unrelated
                    // operation completes. Keep Monitor's same-thread reentrancy.
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Monitor.TryEnter(_stateLock, millisecondsTimeout: 50, ref lockTaken);
                    }
                    while (!lockTaken);
                }
                else
                {
                    Monitor.Enter(_stateLock, ref lockTaken);
                }

                if (_disposed) throw new ObjectDisposedException(nameof(InMemoryVectorStore));
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_stateLock);
            }
        }

        private void WithState(Action action, CancellationToken cancellationToken)
            => WithState(() => { action(); return true; }, cancellationToken);

        private void UpsertCore(VectorRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.Id == null) throw new ArgumentNullException(nameof(record.Id));
            var snapshot = CopyRecord(record);
            // Publish the body only after indexing succeeds. Never observe
            // cancellation between the two halves of this update.
            _bm25Index.Index(snapshot.Id, snapshot.Content);
            _records[snapshot.Id] = snapshot;
        }

        private void DeleteCore(string id)
        {
            _bm25Index.Remove(id);
            _records.TryRemove(id, out _);
        }

        private static VectorRecord CopyRecord(VectorRecord record)
            => new VectorRecord(record.Id, (float[])record.Vector.Clone(), record.Content)
            {
                Metadata = new Dictionary<string, string>(record.Metadata, record.Metadata.Comparer)
            };

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
