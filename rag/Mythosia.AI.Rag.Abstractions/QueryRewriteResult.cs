using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// The result of rewriting a query into retrieval-ready form,
    /// including optional retrieval-oriented keywords and a search gate decision.
    /// </summary>
    public class QueryRewriteResult
    {
        /// <summary>
        /// The semantic query to use for retrieval.
        /// When <see cref="NeedsSearch"/> is false, this is the original query unchanged.
        /// </summary>
        public string Query { get; }

        /// <summary>
        /// Whether the query requires document search.
        /// When false, the RAG pipeline should be skipped entirely.
        /// </summary>
        public bool NeedsSearch { get; }

        /// <summary>
        /// Optional retrieval-oriented search terms extracted from the query for text/keyword search.
        /// When set, the hybrid search text leg uses these shaped keywords instead of the raw query,
        /// helping lexical retrieval handle language-particle and formatting mismatches.
        /// Null when the rewriter does not provide keyword shaping.
        /// </summary>
        public IReadOnlyList<string>? Keywords { get; }

        public QueryRewriteResult(string query, bool needsSearch, IReadOnlyList<string>? keywords = null)
        {
            Query = query;
            NeedsSearch = needsSearch;
            Keywords = keywords;
        }

        /// <summary>
        /// Creates a result indicating the query should bypass document search.
        /// </summary>
        public static QueryRewriteResult Pass(string originalQuery)
            => new QueryRewriteResult(originalQuery, needsSearch: false);

        /// <summary>
        /// Creates a result with a (possibly rewritten) semantic query that needs document search.
        /// </summary>
        public static QueryRewriteResult Search(string query)
            => new QueryRewriteResult(query, needsSearch: true);

        /// <summary>
        /// Creates a result with a semantic query and retrieval-oriented keywords for text search.
        /// </summary>
        public static QueryRewriteResult Search(string query, IReadOnlyList<string>? keywords)
            => new QueryRewriteResult(query, needsSearch: true, keywords);
    }
}
