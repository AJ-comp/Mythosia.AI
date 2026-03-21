using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Rewrites a query into retrieval-ready form using conversation history,
    /// and decides whether the query needs document search (search gate).
    /// This solves the classic multi-turn RAG problem where pronouns like "that", "it",
    /// or "tell me more" fail to retrieve relevant documents because they lack key terms.
    /// Implementations may also derive retrieval-oriented query terms for keyword or hybrid search.
    /// </summary>
    public interface IQueryRewriter
    {
        /// <summary>
        /// Rewrites the query into retrieval-ready form without depending on the
        /// caller's conversation state, and determines whether document search is needed.
        /// </summary>
        /// <param name="query">The current user query (e.g., "Tell me more about that").</param>
        /// <param name="conversationHistory">Previous conversation turns for context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// A <see cref="QueryRewriteResult"/> containing the processed semantic query,
        /// optional retrieval-oriented keywords, and search gate decision.
        /// </returns>
        Task<QueryRewriteResult> RewriteAsync(
            string query,
            IReadOnlyList<ConversationTurn>? conversationHistory,
            CancellationToken cancellationToken = default);
    }
}
