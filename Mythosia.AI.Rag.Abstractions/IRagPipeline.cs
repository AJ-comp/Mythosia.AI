using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Core RAG pipeline interface. Processes a user query through the retrieval-augmented generation pipeline.
    /// This is the key abstraction that allows swapping the entire RAG strategy
    /// (e.g., simple retrieval, multi-step, function-calling based RAG) without touching AIService code.
    /// </summary>
    public interface IRagPipeline
    {
        /// <summary>
        /// Processes the user query: embed → search → build context → return augmented prompt.
        /// </summary>
        /// <param name="query">The original user query.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The processed query with augmented prompt and references.</returns>
        Task<RagProcessedQuery> ProcessAsync(string query, CancellationToken cancellationToken = default);
    }
}
