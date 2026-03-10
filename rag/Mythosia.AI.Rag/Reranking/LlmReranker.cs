using Mythosia.AI.Services.Base;
using Mythosia.VectorDb;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Reranking
{
    /// <summary>
    /// Re-ranker that uses an existing LLM (AIService) to score and reorder search results.
    /// Sends a prompt asking the LLM to rate each document's relevance on a 0–10 scale.
    /// </summary>
    public class LlmReranker : IReranker
    {
        private readonly AIService _aiService;

        /// <summary>
        /// Creates an LLM-based re-ranker using the provided AI service.
        /// The service should be configured (model, temperature, etc.) before passing in.
        /// A low temperature (e.g., 0.0–0.2) is recommended for consistent scoring.
        /// </summary>
        /// <param name="aiService">The AI service to use for scoring.</param>
        public LlmReranker(AIService aiService)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        }

        public async Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
            string query,
            IReadOnlyList<VectorSearchResult> results,
            int topK,
            CancellationToken cancellationToken = default)
        {
            if (results.Count == 0)
                return results;

            // Build the scoring prompt
            var sb = new StringBuilder();
            sb.AppendLine("Rate the relevance of each document to the query on a scale of 0-10.");
            sb.AppendLine("Return ONLY a comma-separated list of scores in the same order as the documents.");
            sb.AppendLine("Example output: 8,3,9,1,7");
            sb.AppendLine();
            sb.AppendLine($"Query: {query}");
            sb.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                var content = results[i].Record.Content ?? string.Empty;
                // Truncate long documents to avoid token limits
                if (content.Length > 500)
                    content = content.Substring(0, 500) + "...";

                sb.AppendLine($"Document {i + 1}: {content}");
                sb.AppendLine();
            }

            // Get LLM response
            var response = await _aiService.GetCompletionAsync(sb.ToString());

            // Parse scores
            var scores = ParseScores(response, results.Count);

            // Pair results with scores, sort by score descending, take topK
            var scored = results
                .Select((r, i) => new { Result = r, Score = i < scores.Count ? scores[i] : 0.0 })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => new VectorSearchResult(x.Result.Record, x.Score / 10.0))
                .ToList();

            return scored;
        }

        /// <summary>
        /// Parses comma-separated scores from the LLM response.
        /// Robust to extra whitespace, newlines, and non-numeric prefixes.
        /// </summary>
        private static List<double> ParseScores(string response, int expectedCount)
        {
            var scores = new List<double>();

            // Extract numbers from the response
            var matches = Regex.Matches(response, @"\d+\.?\d*");
            foreach (Match match in matches)
            {
                if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                {
                    scores.Add(Math.Min(10.0, Math.Max(0.0, score)));
                }

                if (scores.Count >= expectedCount)
                    break;
            }

            // Pad with zeros if the LLM returned fewer scores than expected
            while (scores.Count < expectedCount)
                scores.Add(0.0);

            return scores;
        }
    }
}
