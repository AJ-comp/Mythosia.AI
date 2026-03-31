using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// Default implementation of <see cref="INamespaceContext"/> that delegates
    /// all operations to the underlying <see cref="IVectorStore"/>,
    /// automatically setting <see cref="VectorRecord.Namespace"/> and <see cref="VectorFilter.Namespace"/>.
    /// </summary>
    [System.Obsolete("NamespaceContext will be removed in a future major version. Use VectorFilter.Where(\"namespace\", value) and Metadata for logical isolation instead.")]
    internal sealed class NamespaceContext : INamespaceContext
    {
        private readonly IVectorStore _store;

        public string Namespace { get; }

        internal NamespaceContext(IVectorStore store, string @namespace)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
        }

        public IScopeContext InScope(string scope) => new ScopeContext(_store, Namespace, scope);

        public Task DeleteAllAsync(CancellationToken cancellationToken = default)
            => _store.DeleteByFilterAsync(new VectorFilter { Namespace = Namespace }, cancellationToken);

        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        {
            record.Namespace = Namespace;
            return _store.UpsertAsync(record, cancellationToken);
        }

        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            var scoped = records.Select(r => { r.Namespace = Namespace; return r; });
            return _store.UpsertBatchAsync(scoped, cancellationToken);
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var merged = MergeNamespace(filter);
            return _store.SearchAsync(queryVector, topK, merged, cancellationToken);
        }

        public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var merged = MergeNamespace(filter);
            return _store.HybridSearchAsync(denseVector, query, topK, merged, cancellationToken);
        }

        public Task<VectorRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
            => _store.GetAsync(id, new VectorFilter { Namespace = Namespace }, cancellationToken);

        public Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
            => _store.GetBatchAsync(ids, new VectorFilter { Namespace = Namespace }, cancellationToken);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => _store.DeleteAsync(id, new VectorFilter { Namespace = Namespace }, cancellationToken);

        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            var merged = MergeNamespace(filter);
            return _store.DeleteByFilterAsync(merged, cancellationToken);
        }

        public Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            foreach (var r in records) r.Namespace = Namespace;
            var merged = MergeNamespace(filter);
            return _store.ReplaceByFilterAsync(merged, records, cancellationToken);
        }

        public Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            var merged = MergeNamespace(filter);
            return _store.CountAsync(merged, cancellationToken);
        }

        private VectorFilter MergeNamespace(VectorFilter? filter)
        {
            if (filter == null)
                return new VectorFilter { Namespace = Namespace };

            filter.Namespace = Namespace;
            return filter;
        }
    }
}
