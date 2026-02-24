using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Assembles vector search results into a context string suitable for LLM prompts.
    /// </summary>
    public interface IContextBuilder
    {
        /// <summary>
        /// Builds a context string from the user query and retrieved search results.
        /// The returned string is typically prepended to the LLM prompt as reference material.
        /// </summary>
        /// <param name="query">The original user query.</param>
        /// <param name="searchResults">The top-K search results from the vector store.</param>
        /// <returns>A formatted context string for the LLM.</returns>
        string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults);
    }
}
