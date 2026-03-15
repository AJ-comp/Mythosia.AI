namespace Mythosia.AI.Rag
{
    /// <summary>
    /// The result of a query rewrite operation, including a search gate decision.
    /// </summary>
    public class QueryRewriteResult
    {
        /// <summary>
        /// The query to use for search. When <see cref="NeedsSearch"/> is false, this is the original query unchanged.
        /// </summary>
        public string Query { get; }

        /// <summary>
        /// Whether the query requires document search.
        /// When false, the RAG pipeline should be skipped entirely.
        /// </summary>
        public bool NeedsSearch { get; }

        public QueryRewriteResult(string query, bool needsSearch)
        {
            Query = query;
            NeedsSearch = needsSearch;
        }

        /// <summary>
        /// Creates a result indicating the query should bypass document search.
        /// </summary>
        public static QueryRewriteResult Pass(string originalQuery)
            => new QueryRewriteResult(originalQuery, needsSearch: false);

        /// <summary>
        /// Creates a result with a (possibly rewritten) query that needs document search.
        /// </summary>
        public static QueryRewriteResult Search(string query)
            => new QueryRewriteResult(query, needsSearch: true);
    }
}
