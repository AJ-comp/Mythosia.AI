using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Rewrites a follow-up query into a standalone query using conversation history.
    /// This solves the classic multi-turn RAG problem where pronouns like "that", "it",
    /// or "tell me more" fail to retrieve relevant documents because they lack key terms.
    /// </summary>
    public interface IQueryRewriter
    {
        /// <summary>
        /// Rewrites the query so it can stand alone without conversation context.
        /// If rewriting is unnecessary (e.g., no history or query is already standalone),
        /// the original query should be returned as-is.
        /// </summary>
        /// <param name="query">The current user query (e.g., "Tell me more about that").</param>
        /// <param name="conversationHistory">Previous conversation turns for context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A standalone query suitable for vector search (e.g., "Tell me more about OPM").</returns>
        Task<string> RewriteAsync(
            string query,
            IReadOnlyList<ConversationTurn> conversationHistory,
            CancellationToken cancellationToken = default);
    }
}
