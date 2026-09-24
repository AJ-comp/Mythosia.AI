using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>Optional capability for weighted reciprocal rank fusion of dense and text search.</summary>
    public interface IConfigurableHybridSearchStore
    {
        /// <summary>
        /// Searches the active legs and combines their ranks with the supplied options.
        /// A weight of zero disables dense search; a weight of one disables text search.
        /// Disabled legs must not inspect or validate their query input: denseVector may
        /// be an empty array at weight zero, and query may be empty at weight one.
        /// MinScore applies to the final normalized fusion score, not either candidate leg.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
            float[] denseVector, string query, HybridSearchOptions options,
            int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default);
    }
}
