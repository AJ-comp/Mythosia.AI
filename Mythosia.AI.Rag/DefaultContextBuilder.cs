using System.Collections.Generic;
using System.Text;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Default implementation of IContextBuilder that formats search results into a numbered
    /// reference block suitable for LLM prompts.
    /// </summary>
    public class DefaultContextBuilder : IContextBuilder
    {
        /// <summary>
        /// Header text prepended to the context block.
        /// </summary>
        public string Header { get; set; } = "Answer the question based on the following context:";

        /// <summary>
        /// Footer text appended after context, before the query.
        /// </summary>
        public string QueryPrefix { get; set; } = "Question:";

        /// <summary>
        /// Whether to include similarity scores in the context.
        /// </summary>
        public bool IncludeScores { get; set; } = false;

        /// <summary>
        /// Whether to include source metadata in the context.
        /// </summary>
        public bool IncludeSource { get; set; } = true;

        public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            sb.AppendLine();

            for (int i = 0; i < searchResults.Count; i++)
            {
                var result = searchResults[i];
                sb.Append($"[{i + 1}] ");

                if (IncludeSource && result.Record.Metadata.TryGetValue("source", out var source))
                {
                    sb.Append($"(Source: {source}) ");
                }

                if (IncludeScores)
                {
                    sb.Append($"[Score: {result.Score:F3}] ");
                }

                sb.AppendLine();
                sb.AppendLine(result.Record.Content);
                sb.AppendLine();
            }

            sb.AppendLine($"{QueryPrefix} {query}");

            return sb.ToString();
        }
    }
}
