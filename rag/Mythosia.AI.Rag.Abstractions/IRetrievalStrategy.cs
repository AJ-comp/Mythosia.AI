using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Defines a retrieval strategy for the RAG pipeline.
    /// Implementations decide how to fetch relevant documents (pure vector, hybrid BM25+vector, etc.).
    /// This is an orchestration-layer abstraction — it uses <see cref="IVectorStore"/> internally
    /// but is not part of the storage contract.
    /// </summary>
    public interface IRetrievalStrategy
    {
        /// <summary>
        /// Retrieves the most relevant documents for the given query.
        /// </summary>
        /// <param name="denseVector">The dense embedding vector for the query.</param>
        /// <param name="query">The original text query (used for keyword-based retrieval).</param>
        /// <param name="topK">Maximum number of results to return.</param>
        /// <param name="filter">Optional filter criteria.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Results ordered by relevance score (descending).</returns>
        Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
            float[] denseVector,
            string query,
            int topK,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default);
    }
}
