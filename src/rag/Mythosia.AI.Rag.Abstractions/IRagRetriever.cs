using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Retrieves documents from a text request. The retriever owns query analysis,
    /// embedding, and backend selection; the pipeline does not require a dense vector.
    /// </summary>
    public interface IRagRetriever
    {
        /// <summary>
        /// Returns ranked candidates, enforcing the request's metadata filter before
        /// candidate selection. Scores are retriever-specific, not probabilities.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
            RagRetrievalRequest request, CancellationToken cancellationToken = default);
    }
}
