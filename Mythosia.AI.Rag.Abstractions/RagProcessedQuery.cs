using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// The output of IRagPipeline.ProcessAsync — contains the augmented prompt ready for the LLM,
    /// along with the original query and retrieved references.
    /// </summary>
    public class RagProcessedQuery
    {
        /// <summary>
        /// The original user query.
        /// </summary>
        public string OriginalQuery { get; set; } = string.Empty;

        /// <summary>
        /// The fully assembled prompt (context + query) to send to the LLM.
        /// </summary>
        public string AugmentedPrompt { get; set; } = string.Empty;

        /// <summary>
        /// The retrieved search results used to build the context.
        /// </summary>
        public IReadOnlyList<VectorSearchResult> References { get; set; } = System.Array.Empty<VectorSearchResult>();

        public RagProcessedQuery() { }

        public RagProcessedQuery(string originalQuery, string augmentedPrompt, IReadOnlyList<VectorSearchResult> references)
        {
            OriginalQuery = originalQuery;
            AugmentedPrompt = augmentedPrompt;
            References = references;
        }
    }
}
