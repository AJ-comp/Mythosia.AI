using System.Collections.Generic;
using System.Text;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// IContextBuilder that uses a user-defined template with {context} and {question} placeholders.
    /// </summary>
    public class TemplateContextBuilder : IContextBuilder
    {
        private readonly string _template;

        /// <summary>
        /// Creates a template-based context builder.
        /// </summary>
        /// <param name="template">
        /// Template string with placeholders:
        /// {context} — replaced with numbered search results.
        /// {question} — replaced with the user query.
        /// </param>
        public TemplateContextBuilder(string template)
        {
            _template = template;
        }

        public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
        {
            var contextBuilder = new StringBuilder();

            for (int i = 0; i < searchResults.Count; i++)
            {
                var result = searchResults[i];
                contextBuilder.AppendLine($"[{i + 1}] {result.Record.Content}");
                contextBuilder.AppendLine();
            }

            return _template
                .Replace("{context}", contextBuilder.ToString().TrimEnd())
                .Replace("{question}", query);
        }
    }
}
