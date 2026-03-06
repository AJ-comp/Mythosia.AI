using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// Fluent context scoped to a specific namespace + scope.
    /// Obtained via <c>store.InNamespace("ns").InScope("scope")</c>.
    /// </summary>
    public interface IScopeContext
    {
        /// <summary>
        /// The namespace this context is scoped to.
        /// </summary>
        string Namespace { get; }

        /// <summary>
        /// The scope this context is scoped to.
        /// </summary>
        string Scope { get; }

        /// <summary>
        /// Inserts or updates a single vector record.
        /// The record's <see cref="VectorRecord.Scope"/> is automatically set.
        /// </summary>
        Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts or updates multiple vector records in a batch.
        /// Each record's <see cref="VectorRecord.Scope"/> is automatically set.
        /// </summary>
        Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a similarity search filtered to this scope.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector,
            int topK = 5,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a single record by its Id.
        /// </summary>
        Task<VectorRecord?> GetAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a single record by its Id.
        /// </summary>
        Task DeleteAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all records matching the specified filter within this scope.
        /// </summary>
        Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
    }
}
