using Mythosia.AI.Models;
using Mythosia.AI.Services.Base;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Rewrites follow-up queries into standalone queries using an LLM.
    /// Uses StatelessMode internally so the rewriting call does not pollute conversation history.
    /// </summary>
    public class LlmQueryRewriter : IQueryRewriter
    {
        private readonly AIService _aiService;

        private const string DefaultSystemPrompt =
            "You are a query rewriter with a search gate. Decide whether the follow-up question " +
            "needs document search, and if so, whether it needs rewriting.\n\n" +
            "RULES (follow strictly):\n" +
            "1. If the question is a greeting, chitchat, or does NOT need document search " +
            "(e.g. \"hi\", \"hello\", \"thanks\", general conversation), return exactly: [PASS]\n" +
            "2. If the question is a NEW standalone topic that DOES need document search, " +
            "return the question EXACTLY as-is.\n" +
            "3. ONLY rewrite when the question contains pronouns (it, that, this, they) or " +
            "phrases (tell me more, explain further, what about) that clearly reference " +
            "the conversation history AND needs document search.\n" +
            "4. If the user explicitly asks to search, look up, find, or retrieve information " +
            "(e.g. \"search for...\", \"find me...\", \"look up...\"), NEVER return [PASS]. " +
            "Always treat it as needing document search.\n" +
            "5. Return ONLY one of: [PASS], the original question, or the rewritten question. " +
            "No explanation, no prefix, no quotes.";

        /// <summary>
        /// Creates a query rewriter that uses the specified AIService for rewriting.
        /// </summary>
        /// <param name="aiService">The AIService to use for query rewriting.</param>
        public LlmQueryRewriter(AIService aiService)
        {
            _aiService = aiService ?? throw new System.ArgumentNullException(nameof(aiService));
        }

        private const string PassToken = "[PASS]";

        /// <inheritdoc/>
        public async Task<QueryRewriteResult> RewriteAsync(
            string query,
            IReadOnlyList<ConversationTurn>? conversationHistory,
            CancellationToken cancellationToken = default)
        {
            var prompt = BuildPrompt(query, conversationHistory);
            var rewritten = await _aiService.GetCompletionAsync(prompt, RequestProfiles.QueryRewrite);

            if (string.IsNullOrWhiteSpace(rewritten))
                return QueryRewriteResult.Search(query);

            var trimmed = rewritten.Trim();

            if (trimmed.Contains(PassToken, System.StringComparison.OrdinalIgnoreCase))
                return QueryRewriteResult.Pass(query);

            return QueryRewriteResult.Search(trimmed);
        }

        private string BuildPrompt(string query, IReadOnlyList<ConversationTurn>? history)
        {
            var sb = new StringBuilder();
            sb.AppendLine(DefaultSystemPrompt);
            sb.AppendLine();

            if (history != null && history.Count > 0)
            {
                sb.AppendLine("--- Conversation History ---");

                // Take only the last few turns to keep the prompt small
                var recentHistory = history.Count > 10
                    ? history.Skip(history.Count - 10).ToList()
                    : history;

                foreach (var turn in recentHistory)
                {
                    sb.AppendLine($"{turn.Role}: {turn.Content}");
                }

                sb.AppendLine();
            }

            sb.AppendLine($"Question: {query}");
            sb.AppendLine("Output:");

            return sb.ToString();
        }
    }
}
