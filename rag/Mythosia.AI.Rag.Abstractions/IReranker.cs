using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Re-ranks retrieval results to improve relevance ordering.
    /// Applied after the initial retrieval strategy and before context building.
    /// </summary>
    public interface IReranker
    {
        /// <summary>
        /// Re-ranks the given search results based on their relevance to the query.
        /// </summary>
        /// <param name="query">The original text query.</param>
        /// <param name="results">The initial retrieval results to re-rank.</param>
        /// <param name="topK">Maximum number of results to return after re-ranking.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Re-ranked results ordered by relevance (descending).</returns>
        Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
            string query,
            IReadOnlyList<VectorSearchResult> results,
            int topK,
            CancellationToken cancellationToken = default);
    }
}
