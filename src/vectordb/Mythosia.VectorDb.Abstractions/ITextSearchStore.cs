using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    /// <summary>Optional capability for searching stored text without generating a dense embedding.</summary>
    public interface ITextSearchStore
    {
        /// <summary>
        /// Searches text using the backend's configured analyzer and relevance score.
        /// Metadata conditions restrict candidates before top-K selection. MinScore applies to
        /// the backend's text score, whose scale is not interchangeable with vector or fusion scores.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> TextSearchAsync(
            string query, int topK = 5, VectorFilter? filter = null,
            CancellationToken cancellationToken = default);
    }
}
