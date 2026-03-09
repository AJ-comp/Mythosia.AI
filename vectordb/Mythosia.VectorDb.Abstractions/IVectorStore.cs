using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// Abstracts vector storage and similarity search operations.
    /// Implementations handle only storage and retrieval — they have no knowledge of RAG pipelines.
    /// <para>
    /// Namespace and scope are optional properties on <see cref="VectorRecord"/> and <see cref="VectorFilter"/>.
    /// Use the fluent API (<c>store.InNamespace("ns").InScope("scope")</c>) for convenient scoped access.
    /// </para>
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Inserts or updates a single vector record.
        /// If a record with the same Id (and namespace, when applicable) exists, it is overwritten.
        /// </summary>
        Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts or updates multiple vector records in a batch.
        /// </summary>
        Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a similarity search.
        /// Implementations should respect <see cref="VectorFilter.Namespace"/> and <see cref="VectorFilter.Scope"/>
        /// when present.
        /// </summary>
        /// <param name="queryVector">The query embedding vector.</param>
        /// <param name="topK">Maximum number of results to return.</param>
        /// <param name="filter">Optional filter criteria (namespace, scope, metadata, min score).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Results ordered by descending similarity score.</returns>
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a hybrid search combining dense vector similarity and keyword matching.
        /// Implementations with native hybrid support should override this method.
        /// </summary>
        /// <param name="denseVector">The dense embedding vector for semantic similarity.</param>
        /// <param name="query">The original text query for keyword-based matching.</param>
        /// <param name="topK">Maximum number of results to return.</param>
        /// <param name="filter">Optional filter criteria (namespace, scope, metadata, min score).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Results ordered by descending hybrid relevance score.</returns>
        Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector,
            string query,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                $"Store {GetType().Name} does not support native hybrid search.");

        /// <summary>
        /// Retrieves a single record by its Id.
        /// Implementations may use <paramref name="filter"/> to narrow by namespace/scope.
        /// </summary>
        Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a single record by its Id.
        /// Implementations may use <paramref name="filter"/> to narrow by namespace/scope.
        /// </summary>
        Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all records matching the specified filter.
        /// </summary>
        Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
    }
}
