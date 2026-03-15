using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Rewrites a follow-up query into a standalone query using conversation history,
    /// and decides whether the query needs document search (search gate).
    /// This solves the classic multi-turn RAG problem where pronouns like "that", "it",
    /// or "tell me more" fail to retrieve relevant documents because they lack key terms.
    /// </summary>
    public interface IQueryRewriter
    {
        /// <summary>
        /// Rewrites the query so it can stand alone without conversation context,
        /// and determines whether document search is needed.
        /// </summary>
        /// <param name="query">The current user query (e.g., "Tell me more about that").</param>
        /// <param name="conversationHistory">Previous conversation turns for context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="QueryRewriteResult"/> containing the processed query and search gate decision.</returns>
        Task<QueryRewriteResult> RewriteAsync(
            string query,
            IReadOnlyList<ConversationTurn>? conversationHistory,
            CancellationToken cancellationToken = default);
    }
}
