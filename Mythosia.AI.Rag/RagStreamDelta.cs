using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Represents a single delta event during a streaming RAG query.
    /// </summary>
    public class RagStreamDelta
    {
        /// <summary>
        /// The type of this delta event.
        /// </summary>
        public RagStreamDeltaType Type { get; set; }

        /// <summary>
        /// LLM-generated content token (for Content deltas).
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// A retrieved search result (for SearchResult deltas).
        /// </summary>
        public VectorSearchResult? SearchResult { get; set; }

        /// <summary>
        /// The assembled context string (for ContextBuilt deltas).
        /// </summary>
        public string? Context { get; set; }

        /// <summary>
        /// All search results (for SearchComplete deltas).
        /// </summary>
        public IReadOnlyList<VectorSearchResult>? SearchResults { get; set; }

        public static RagStreamDelta ForContent(string content)
            => new RagStreamDelta { Type = RagStreamDeltaType.Content, Content = content };

        public static RagStreamDelta ForSearchResult(VectorSearchResult result)
            => new RagStreamDelta { Type = RagStreamDeltaType.SearchResult, SearchResult = result };

        public static RagStreamDelta ForSearchComplete(IReadOnlyList<VectorSearchResult> results)
            => new RagStreamDelta { Type = RagStreamDeltaType.SearchComplete, SearchResults = results };

        public static RagStreamDelta ForContextBuilt(string context)
            => new RagStreamDelta { Type = RagStreamDeltaType.ContextBuilt, Context = context };
    }

    /// <summary>
    /// Types of events emitted during a streaming RAG query.
    /// </summary>
    public enum RagStreamDeltaType
    {
        /// <summary>
        /// A search result has been retrieved.
        /// </summary>
        SearchResult,

        /// <summary>
        /// All search results have been retrieved.
        /// </summary>
        SearchComplete,

        /// <summary>
        /// The context has been assembled from search results.
        /// </summary>
        ContextBuilt,

        /// <summary>
        /// A content token from the LLM response.
        /// </summary>
        Content
    }
}
