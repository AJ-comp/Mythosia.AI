using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Optional vector store contract for advanced RAG diagnostics.
    /// Implement this interface to enable full diagnostic capabilities such as
    /// chunk-level text lookup and all-record scoring analysis.
    /// </summary>
    public interface IRagDiagnosticsStore
    {
        /// <summary>
        /// Returns all records in the target namespace for diagnostic analysis.
        /// </summary>
        Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(
            string? @namespace = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns similarity scores against all records in the target namespace,
        /// ordered by descending score, without TopK filtering.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
            float[] queryVector,
            string? @namespace = null,
            CancellationToken cancellationToken = default);
    }
}
