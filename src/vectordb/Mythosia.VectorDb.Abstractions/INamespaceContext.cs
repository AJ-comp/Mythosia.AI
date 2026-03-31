using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// Fluent context scoped to a specific namespace.
    /// Obtained via <c>store.InNamespace("ns")</c>.
    /// Automatically sets <see cref="VectorRecord.Namespace"/> on upsert
    /// and <see cref="VectorFilter.Namespace"/> on search/delete.
    /// </summary>
    [System.Obsolete("INamespaceContext will be removed in a future major version. Use VectorFilter.Where(\"namespace\", value) and Metadata for logical isolation instead.")]
    public interface INamespaceContext
    {
        /// <summary>
        /// The namespace this context is scoped to.
        /// </summary>
        string Namespace { get; }

        /// <summary>
        /// Narrows this context to a specific scope within the namespace.
        /// </summary>
        IScopeContext InScope(string scope);

        /// <summary>
        /// Deletes all records in this namespace.
        /// </summary>
        Task DeleteAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts or updates a single vector record.
        /// </summary>
        Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts or updates multiple vector records in a batch.
        /// </summary>
        Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a similarity search within this namespace.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a hybrid search combining dense vector similarity and keyword matching within this namespace.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a single record by its Id.
        /// </summary>
        Task<VectorRecord?> GetAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves multiple records by their Ids in a single batch operation.
        /// Records that do not exist are omitted from the result.
        /// </summary>
        Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a single record by its Id.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all records matching the specified filter.
        /// </summary>
        Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically replaces all records matching the filter with new records within this namespace.
        /// </summary>
        Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the number of records in this namespace, optionally narrowed by additional filter criteria.
        /// </summary>
        Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
    }
}
