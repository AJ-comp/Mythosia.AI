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
    /// For logical isolation (e.g. tenant, category), use metadata conditions via
    /// <see cref="VectorFilter.Where(string, string)"/>.
    /// </para>
    /// </summary>
    public interface IVectorStore
    {
        /// <summary>
        /// Inserts or updates a single vector record.
        /// If a record with the same Id already exists, it is overwritten.
        /// </summary>
        Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts or updates multiple vector records in a batch.
        /// </summary>
        Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a similarity search.
        /// Implementations should respect <see cref="VectorFilter.Conditions"/> and
        /// <see cref="VectorFilter.MinScore"/> when present.
        /// </summary>
        /// <param name="queryVector">The query embedding vector.</param>
        /// <param name="topK">Maximum number of results to return.</param>
        /// <param name="filter">Optional filter criteria (metadata conditions, min score).</param>
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
        /// <param name="filter">Optional filter criteria (metadata conditions, min score).</param>
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
        /// Implementations may use <paramref name="filter"/> to narrow results by metadata conditions.
        /// </summary>
        Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves multiple records by their Ids in a single batch operation.
        /// Records that do not exist or do not match <paramref name="filter"/> are omitted from the result.
        /// The order of results is not guaranteed to match the order of <paramref name="ids"/>.
        /// </summary>
        async Task<IReadOnlyList<VectorRecord>> GetBatchAsync(
            IEnumerable<string> ids,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<VectorRecord>();
            foreach (var id in ids)
            {
                var record = await GetAsync(id, filter, cancellationToken);
                if (record != null)
                    results.Add(record);
            }
            return results;
        }

        /// <summary>
        /// Deletes a single record by its Id.
        /// Implementations may use <paramref name="filter"/> to narrow by metadata conditions.
        /// </summary>
        Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all records matching the specified filter.
        /// </summary>
        Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically replaces all records matching the filter with new records.
        /// Deletes existing records by filter, then inserts the new records — both within a single transaction
        /// where supported (e.g. PostgreSQL). On failure, the transaction rolls back and existing data remains intact.
        /// </summary>
        /// <param name="filter">Filter identifying the records to delete before inserting.</param>
        /// <param name="records">The new records to insert after deletion.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        async Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
        {
            await DeleteByFilterAsync(filter, cancellationToken);
            await UpsertBatchAsync(records, cancellationToken);
        }

        /// <summary>
        /// Returns the number of records matching the optional filter.
        /// When <paramref name="filter"/> is <see langword="null"/>, returns the total record count.
        /// Implementations may ignore <see cref="VectorFilter.MinScore"/> as it is not meaningful for counting.
        /// </summary>
        Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                $"Store {GetType().Name} does not support CountAsync.");

        /// <summary>
        /// Verifies that the store can reach its backend (e.g. database, API).
        /// Throws on failure. In-memory stores succeed immediately.
        /// </summary>
        Task VerifyConnectionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
