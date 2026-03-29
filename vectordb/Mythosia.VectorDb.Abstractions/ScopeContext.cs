using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// Default implementation of <see cref="IScopeContext"/> that delegates
    /// all operations to the underlying <see cref="IVectorStore"/>,
    /// automatically applying namespace and scope.
    /// </summary>
    internal sealed class ScopeContext : IScopeContext
    {
        private readonly IVectorStore _store;

        public string Namespace { get; }
        public string Scope { get; }

        internal ScopeContext(IVectorStore store, string @namespace, string scope)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Namespace = @namespace ?? throw new ArgumentNullException(nameof(@namespace));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
        {
            record.Namespace = Namespace;
            record.Scope = Scope;
            return _store.UpsertAsync(record, cancellationToken);
        }

        public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            var scoped = records.Select(r => { r.Namespace = Namespace; r.Scope = Scope; return r; });
            return _store.UpsertBatchAsync(scoped, cancellationToken);
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var merged = MergeFilter(filter);
            return _store.SearchAsync(queryVector, topK, merged, cancellationToken);
        }

        public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var merged = MergeFilter(filter);
            return _store.HybridSearchAsync(denseVector, query, topK, merged, cancellationToken);
        }

        public Task<VectorRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
            => _store.GetAsync(id, new VectorFilter { Namespace = Namespace, Scope = Scope }, cancellationToken);

        public Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
            => _store.GetBatchAsync(ids, new VectorFilter { Namespace = Namespace, Scope = Scope }, cancellationToken);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => _store.DeleteAsync(id, new VectorFilter { Namespace = Namespace, Scope = Scope }, cancellationToken);

        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            var merged = MergeFilter(filter);
            return _store.DeleteByFilterAsync(merged, cancellationToken);
        }

        public Task DeleteAllAsync(CancellationToken cancellationToken = default)
            => _store.DeleteByFilterAsync(new VectorFilter { Namespace = Namespace, Scope = Scope }, cancellationToken);

        public Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            foreach (var r in records) { r.Namespace = Namespace; r.Scope = Scope; }
            var merged = MergeFilter(filter);
            return _store.ReplaceByFilterAsync(merged, records, cancellationToken);
        }

        public Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default)
        {
            var merged = MergeFilter(filter);
            return _store.CountAsync(merged, cancellationToken);
        }

        private VectorFilter MergeFilter(VectorFilter? filter)
        {
            if (filter == null)
                return new VectorFilter { Namespace = Namespace, Scope = Scope };

            filter.Namespace = Namespace;
            filter.Scope = Scope;
            return filter;
        }
    }
}
