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
        /// Returns all records for diagnostic analysis.
        /// Use a <see cref="VectorFilter"/> at the pipeline level if scoping is needed.
        /// </summary>
        Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns similarity scores against all records,
        /// ordered by descending score, without TopK filtering.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
            float[] queryVector,
            CancellationToken cancellationToken = default);
    }
}
