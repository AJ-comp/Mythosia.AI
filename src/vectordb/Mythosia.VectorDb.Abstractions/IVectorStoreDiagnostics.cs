using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>
    /// Optional inspection capabilities for a vector store.
    /// Provides records and similarity scores without performing application-specific analysis.
    /// </summary>
    /// <remarks>
    /// These operations inspect the entire store without a query filter or a TopK limit.
    /// Implementations may be expensive for large stores. Ordinary storage and retrieval
    /// do not require this interface.
    /// </remarks>
    public interface IVectorStoreDiagnostics
    {
        /// <summary>
        /// Returns all records in the store for diagnostic analysis.
        /// </summary>
        Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns similarity scores against all records, ordered by descending score,
        /// without TopK or minimum-score filtering.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
            float[] queryVector,
            CancellationToken cancellationToken = default);
    }
}
