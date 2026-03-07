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
            "Given the conversation history below, rewrite the follow-up question " +
            "as a standalone question that contains all necessary context for search. " +
            "Return ONLY the rewritten question, nothing else. " +
            "If the question is already standalone, return it unchanged.";

        /// <summary>
        /// Creates a query rewriter that uses the specified AIService for rewriting.
        /// </summary>
        /// <param name="aiService">The AIService to use for query rewriting.</param>
        public LlmQueryRewriter(AIService aiService)
        {
            _aiService = aiService ?? throw new System.ArgumentNullException(nameof(aiService));
        }

        /// <inheritdoc/>
        public async Task<string> RewriteAsync(
            string query,
            IReadOnlyList<ConversationTurn> conversationHistory,
            CancellationToken cancellationToken = default)
        {
            if (conversationHistory == null || conversationHistory.Count == 0)
                return query;

            var prompt = BuildPrompt(query, conversationHistory);

            var backupStateless = _aiService.StatelessMode;
            var backupFunctions = _aiService.FunctionsDisabled;
            _aiService.StatelessMode = true;
            _aiService.FunctionsDisabled = true;
            try
            {
                var rewritten = await _aiService.GetCompletionAsync(prompt);
                return string.IsNullOrWhiteSpace(rewritten) ? query : rewritten.Trim();
            }
            finally
            {
                _aiService.StatelessMode = backupStateless;
                _aiService.FunctionsDisabled = backupFunctions;
            }
        }

        private string BuildPrompt(string query, IReadOnlyList<ConversationTurn> history)
        {
            var sb = new StringBuilder();
            sb.AppendLine(DefaultSystemPrompt);
            sb.AppendLine();
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
            sb.AppendLine($"Follow-up question: {query}");
            sb.AppendLine("Standalone question:");

            return sb.ToString();
        }
    }
}
