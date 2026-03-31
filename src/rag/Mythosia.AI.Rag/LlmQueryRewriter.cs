using Mythosia.AI.Models;
using Mythosia.AI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Rewrites follow-up queries into retrieval-ready form using an LLM,
    /// and can derive retrieval-oriented keywords for hybrid/text search.
    /// Uses StatelessMode internally so the rewriting call does not pollute conversation history.
    /// </summary>
    public class LlmQueryRewriter : IQueryRewriter
    {
        private readonly IAIService _aiService;
        private readonly uint _maxTokens;
        private readonly bool _extractKeywords;

        private const uint DefaultMaxTokens = 250;

        private const string BaseRules =
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
            "Always treat it as needing document search.";

        private const string KeywordRule =
            "5. After the query, output a KEYWORDS line with core search terms " +
            "extracted from the query.";

        private const string KeywordDetails =
            "KEYWORD RULES:\n" +
            "- Extract only meaningful content words (nouns, proper nouns, technical terms)\n" +
            "- Remove particles, conjunctions, inflections, stopwords " +
            "(e.g. Korean 에/을/는/에서/대해, English a/the/is/about/do/know)\n" +
            "- Keep original casing for proper nouns and abbreviations\n" +
            "- Separate terms joined by particles (e.g. \"opm에\" → OPM)\n" +
            "- EXCLUSION QUERIES: When the user wants to exclude or negate certain items " +
            "(e.g. \"A 말고\", \"A 빼고\", \"A 제외\", \"A 아닌\", \"not A\", \"except A\", \"other than A\"), " +
            "do NOT include the excluded items in KEYWORDS. " +
            "Only include positive search terms that help find the desired results.\n" +
            "- If no strong lexical keywords remain after removing excluded terms, " +
            "return an empty KEYWORDS line (\"KEYWORDS:\" with nothing after it). " +
            "Do not invent generic or low-value keywords just to fill the list.";

        private const string OutputFormatWithKeywords =
            "OUTPUT FORMAT:\n" +
            "- [PASS] (alone), OR\n" +
            "- Line 1: the query (original or rewritten)\n" +
            "- Line 2: KEYWORDS: term1, term2, ... (may be empty if no useful keywords)\n" +
            "- No other text, no explanation, no quotes.\n\n" +
            "EXAMPLES:\n" +
            "Question: opm에 대해 알아?\n" +
            "Output:\n" +
            "opm에 대해 알아?\n" +
            "KEYWORDS: OPM\n\n" +
            "Question: EyeCargo reefer middleware command 목록 알려줘\n" +
            "Output:\n" +
            "EyeCargo reefer middleware command 목록 알려줘\n" +
            "KEYWORDS: EyeCargo, reefer, middleware, command\n\n" +
            "Question: ops opm 말고 또 뭐 있나요?\n" +
            "Output:\n" +
            "ops opm 외에 다른 항목이 있나요?\n" +
            "KEYWORDS:";

        private const string OutputFormatWithoutKeywords =
            "OUTPUT FORMAT:\n" +
            "- [PASS] (alone), OR\n" +
            "- The query (original or rewritten)\n" +
            "- No other text, no explanation, no quotes.\n\n" +
            "EXAMPLES:\n" +
            "Question: opm에 대해 알아?\n" +
            "Output:\n" +
            "opm에 대해 알아?\n\n" +
            "Question: Tell me more about that\n" +
            "(with history about EyeCargo reefer)\n" +
            "Output:\n" +
            "EyeCargo reefer middleware details";

        /// <summary>
        /// Creates a query rewriter that uses the specified AIService for rewriting
        /// queries into retrieval-ready form and optionally deriving retrieval-oriented keywords.
        /// </summary>
        /// <param name="aiService">The AIService to use for query rewriting.</param>
        /// <param name="maxTokens">Maximum tokens for the rewriter LLM response. Default is 250.</param>
        /// <param name="extractKeywords">
        /// When true (default), the rewriter also extracts retrieval-oriented keywords
        /// for the text/keyword search leg of hybrid search.
        /// When false, only query rewriting and search gate are performed.
        /// </param>
        public LlmQueryRewriter(IAIService aiService, uint maxTokens = DefaultMaxTokens, bool extractKeywords = true)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _maxTokens = maxTokens;
            _extractKeywords = extractKeywords;
        }

        private const string PassToken = "[PASS]";
        private const string KeywordsPrefix = "KEYWORDS:";

        /// <inheritdoc/>
        public async Task<QueryRewriteResult> RewriteAsync(
            string query,
            IReadOnlyList<ConversationTurn>? conversationHistory,
            CancellationToken cancellationToken = default)
        {
            var prompt = BuildPrompt(query, conversationHistory);
            var profile = new AIRequestProfile
            {
                Purpose = AIRequestPurpose.QueryRewrite,
                Stateless = true,
                DisableFunctions = true,
                DisableReasoning = true,
                Temperature = 0.1f,
                MaxTokens = _maxTokens
            };
            var rewritten = await _aiService.GetCompletionAsync(prompt, profile);

            if (string.IsNullOrWhiteSpace(rewritten))
                return QueryRewriteResult.Search(query);

            var trimmed = rewritten.Trim();

            if (trimmed.Contains(PassToken, StringComparison.OrdinalIgnoreCase))
                return QueryRewriteResult.Pass(query);

            return ParseSearchResult(trimmed, query);
        }

        private static QueryRewriteResult ParseSearchResult(string response, string originalQuery)
        {
            var lines = response.Split('\n');
            string? queryLine = null;
            IReadOnlyList<string>? keywords = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith(KeywordsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var keywordsPart = line.Substring(KeywordsPrefix.Length).Trim();
                    if (string.IsNullOrEmpty(keywordsPart)) continue;

                    var parsed = keywordsPart
                        .Split(',')
                        .Select(k => k.Trim())
                        .Where(k => k.Length > 0)
                        .ToList();

                    if (parsed.Count > 0) keywords = parsed;
                }
                else if (queryLine == null)
                {
                    queryLine = line;
                }
            }

            var resultQuery = queryLine ?? originalQuery;
            return QueryRewriteResult.Search(resultQuery, keywords);
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();

            if (_extractKeywords)
            {
                sb.AppendLine("You are a query rewriter with a search gate and keyword extractor.\n");
                sb.AppendLine(BaseRules);
                sb.AppendLine(KeywordRule);
                sb.AppendLine();
                sb.AppendLine(KeywordDetails);
                sb.AppendLine();
                sb.AppendLine(OutputFormatWithKeywords);
            }
            else
            {
                sb.AppendLine("You are a query rewriter with a search gate.\n");
                sb.AppendLine(BaseRules);
                sb.AppendLine();
                sb.AppendLine(OutputFormatWithoutKeywords);
            }

            return sb.ToString();
        }

        private string BuildPrompt(string query, IReadOnlyList<ConversationTurn>? history)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildSystemPrompt());
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
