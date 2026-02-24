using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Abstracts vector storage and similarity search operations.
    /// Implementations handle only storage and retrieval — they have no knowledge of RAG pipelines.
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Inserts or updates a single vector record in the specified collection.
        /// If a record with the same Id exists, it is overwritten.
        /// </summary>
        Task UpsertAsync(string collection, VectorRecord record, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts or updates multiple vector records in a batch.
        /// </summary>
        Task UpsertBatchAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a similarity search against the specified collection.
        /// </summary>
        /// <param name="collection">The collection to search.</param>
        /// <param name="queryVector">The query embedding vector.</param>
        /// <param name="topK">Maximum number of results to return.</param>
        /// <param name="filter">Optional filter criteria.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Results ordered by descending similarity score.</returns>
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string collection,
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a single record by its Id.
        /// </summary>
        Task<VectorRecord?> GetAsync(string collection, string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a single record by its Id.
        /// </summary>
        Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all records matching the specified filter.
        /// </summary>
        Task DeleteByFilterAsync(string collection, VectorFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether the specified collection exists.
        /// </summary>
        Task<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new collection. No-op if it already exists.
        /// </summary>
        Task CreateCollectionAsync(string collection, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes an entire collection and all its records.
        /// </summary>
        Task DeleteCollectionAsync(string collection, CancellationToken cancellationToken = default);
    }
}
