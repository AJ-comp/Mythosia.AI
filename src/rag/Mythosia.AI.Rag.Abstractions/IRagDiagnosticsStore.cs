using Mythosia.VectorDb;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Legacy name for vector-store inspection capabilities.
    /// New stores should implement <see cref="IVectorStoreDiagnostics"/> directly.
    /// </summary>
    /// <remarks>
    /// The original member declarations and default bridges preserve existing custom
    /// implementations. A public class method takes precedence over a default bridge;
    /// RAG diagnostics use a legacy adapter to preserve explicit legacy dispatch in that case.
    /// InMemoryVectorStore no longer implements this legacy interface.
    /// </remarks>
    [Obsolete("Implement and use Mythosia.VectorDb.IVectorStoreDiagnostics instead.")]
    public interface IRagDiagnosticsStore : IVectorStoreDiagnostics
    {
        /// <summary>
        /// Returns all records for diagnostic analysis.
        /// This operation inspects the entire store without a query filter.
        /// </summary>
        new Task<IReadOnlyList<VectorRecord>> ListAllRecordsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns similarity scores against all records,
        /// ordered by descending score, without TopK filtering.
        /// </summary>
        new Task<IReadOnlyList<VectorSearchResult>> ScoredListAsync(
            float[] queryVector,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<VectorRecord>> IVectorStoreDiagnostics.ListAllRecordsAsync(
            CancellationToken cancellationToken)
            => ListAllRecordsAsync(cancellationToken);

        Task<IReadOnlyList<VectorSearchResult>> IVectorStoreDiagnostics.ScoredListAsync(
            float[] queryVector,
            CancellationToken cancellationToken)
            => ScoredListAsync(queryVector, cancellationToken);
    }
}
