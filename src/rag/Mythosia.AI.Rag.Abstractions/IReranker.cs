using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Re-ranks retrieval results to improve relevance ordering.
    /// Applied after the initial retrieval strategy and before context building.
    /// The reranker only re-scores and reorders; topK trimming is the pipeline's responsibility.
    /// </summary>
    public interface IReranker
    {
        /// <summary>
        /// Re-ranks the given search results based on their relevance to the query.
        /// Returns all results with updated scores, ordered by relevance (descending).
        /// </summary>
        /// <param name="query">The original text query.</param>
        /// <param name="results">The initial retrieval results to re-rank.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>All results re-scored and ordered by relevance (descending).</returns>
        Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
            string query,
            IReadOnlyList<VectorSearchResult> results,
            CancellationToken cancellationToken = default);
    }
}
