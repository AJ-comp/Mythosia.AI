using Mythosia.AI.Rag;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.VectorDB
{
    /// <summary>
    /// Thread-safe in-memory implementation of IVectorStore using cosine similarity for TopK search.
    /// Supports metadata storage, namespace isolation, filtering, upsert, and delete operations.
    /// Suitable for development, testing, and small-scale workloads.
    /// </summary>
    public class InMemoryVectorStore : IVectorStore
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, VectorRecord>> _collections
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, VectorRecord>>(StringComparer.OrdinalIgnoreCase);

        #region Collection Management

        public Task<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_collections.ContainsKey(collection));
        }

        public Task CreateCollectionAsync(string collection, CancellationToken cancellationToken = default)
        {
            _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, VectorRecord>(StringComparer.Ordinal));
            return Task.CompletedTask;
        }

        public Task DeleteCollectionAsync(string collection, CancellationToken cancellationToken = default)
        {
            _collections.TryRemove(collection, out _);
            return Task.CompletedTask;
        }

        #endregion

        #region Upsert

        public Task UpsertAsync(string collection, VectorRecord record, CancellationToken cancellationToken = default)
        {
            var store = GetOrCreateCollection(collection);
            store[record.Id] = record;
            return Task.CompletedTask;
        }

        public Task UpsertBatchAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            var store = GetOrCreateCollection(collection);
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                store[record.Id] = record;
            }
            return Task.CompletedTask;
        }

        #endregion

        #region Get / Delete

        public Task<VectorRecord?> GetAsync(string collection, string id, CancellationToken cancellationToken = default)
        {
            if (_collections.TryGetValue(collection, out var store) && store.TryGetValue(id, out var record))
                return Task.FromResult<VectorRecord?>(record);

            return Task.FromResult<VectorRecord?>(null);
        }

        public Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default)
        {
            if (_collections.TryGetValue(collection, out var store))
                store.TryRemove(id, out _);

            return Task.CompletedTask;
        }

        public Task DeleteByFilterAsync(string collection, VectorFilter filter, CancellationToken cancellationToken = default)
        {
            if (!_collections.TryGetValue(collection, out var store))
                return Task.CompletedTask;

            var keysToRemove = store.Values
                .Where(r => MatchesFilter(r, filter))
                .Select(r => r.Id)
                .ToList();

            foreach (var key in keysToRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();
                store.TryRemove(key, out _);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Search

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string collection,
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            if (!_collections.TryGetValue(collection, out var store))
                return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());

            var results = store.Values
                .Where(r => filter == null || MatchesFilter(r, filter))
                .Select(r => new VectorSearchResult(r, CosineSimilarity(queryVector, r.Vector)))
                .Where(r => !filter?.MinScore.HasValue ?? true || r.Score >= (filter?.MinScore ?? 0))
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();

            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
        }

        #endregion

        #region Private Helpers

        private ConcurrentDictionary<string, VectorRecord> GetOrCreateCollection(string collection)
        {
            return _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, VectorRecord>(StringComparer.Ordinal));
        }

        private static bool MatchesFilter(VectorRecord record, VectorFilter filter)
        {
            if (filter.Namespace != null && !string.Equals(record.Namespace, filter.Namespace, StringComparison.Ordinal))
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
