using System.Collections.Generic;
using Mythosia.VectorDb;

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

        /// <summary>
        /// Whether the query returned any references from the vector store.
        /// When false, <see cref="AugmentedPrompt"/> contains the original query unchanged.
        /// </summary>
        public bool HasReferences => References.Count > 0;

        public RagProcessedQuery() { }

        public RagProcessedQuery(string originalQuery, string augmentedPrompt, IReadOnlyList<VectorSearchResult> references)
        {
            OriginalQuery = originalQuery;
            AugmentedPrompt = augmentedPrompt;
            References = references;
        }
    }
}
