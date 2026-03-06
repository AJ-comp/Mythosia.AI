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

        public Task<VectorRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
            => _store.GetAsync(id, new VectorFilter { Namespace = Namespace }, cancellationToken);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => _store.DeleteAsync(id, new VectorFilter { Namespace = Namespace }, cancellationToken);

        public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        {
            var merged = MergeNamespace(filter);
            return _store.DeleteByFilterAsync(merged, cancellationToken);
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
