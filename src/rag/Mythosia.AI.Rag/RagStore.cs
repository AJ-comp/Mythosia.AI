using Mythosia.AI.Rag.Retrieval;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// A pre-built, shareable RAG index. Build once, share across multiple AIService instances.
    /// </summary>
    public class RagStore
    {
        /// <summary>
        /// The underlying RAG pipeline used for query processing.
        /// </summary>
        internal IRagPipeline Pipeline { get; }

        /// <summary>
        /// The vector store containing the indexed documents.
        /// </summary>
        internal IVectorStore VectorStore { get; }

        /// <summary>
        /// The query rewriter used to rewrite queries into retrieval-ready form for
        /// multi-turn conversations, or null if disabled.
        /// </summary>
        internal IQueryRewriter? QueryRewriter { get; private set; }

        internal RagStore(IRagPipeline pipeline, IVectorStore vectorStore, IQueryRewriter? queryRewriter = null)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            VectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            QueryRewriter = queryRewriter;
        }

        #region Query

        /// <summary>
        /// Processes a query through the RAG pipeline: embed → search → build context.
        /// Returns the request message content and references without calling an LLM.
        /// </summary>
        public Task<RagProcessedQuery> QueryAsync(string query, CancellationToken cancellationToken = default)
        {
            return Pipeline.ProcessAsync(query, options: null, cancellationToken);
        }

        /// <summary>
        /// Processes a query through the RAG pipeline with per-request query overrides.
        /// </summary>
        public Task<RagProcessedQuery> QueryAsync(
            string query,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            return Pipeline.ProcessAsync(query, options, cancellationToken);
        }

        #endregion

        #region Query with Rewriting

        /// <summary>
        /// Processes a query through the RAG pipeline with automatic query rewriting
        /// for multi-turn conversations. If a <see cref="QueryRewriter"/> is configured
        /// and conversation history is provided, the query is rewritten into retrieval-ready
        /// form before search, and retrieval-oriented keywords may also be derived.
        /// </summary>
        /// <param name="query">The current user query.</param>
        /// <param name="conversationHistory">Previous conversation turns for context, or null to skip rewriting.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task<RagProcessedQuery> QueryAsync(
            string query,
            IReadOnlyList<ConversationTurn>? conversationHistory,
            CancellationToken cancellationToken = default)
        {
            return QueryAsync(query, conversationHistory, options: null, cancellationToken);
        }

        /// <summary>
        /// Processes a query through the RAG pipeline with automatic query rewriting
        /// and per-request query overrides.
        /// </summary>
        public async Task<RagProcessedQuery> QueryAsync(
            string query,
            IReadOnlyList<ConversationTurn>? conversationHistory,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            string searchQuery = query;
            string? rewrittenQuery = null;
            bool searchSkipped = false;
            QueryRewriteResult? rewriteResult = null;

            long rewriteElapsedMs = 0;
            if (QueryRewriter != null)
            {
                if (options?.ProgressAsync != null)
                    await options.ProgressAsync(RagProgressStage.QueryRewrite);

                var sw = Stopwatch.StartNew();
                var result = await QueryRewriter.RewriteAsync(query, conversationHistory, cancellationToken);
                rewriteElapsedMs = sw.ElapsedMilliseconds;
                rewriteResult = result;

                if (!result.NeedsSearch)
                {
                    searchSkipped = true;
                }
                else if (result.Query != query)
                {
                    rewrittenQuery = result.Query;
                    searchQuery = result.Query;
                }
            }

            // Build text search query from extracted keywords when available
            var keywords = rewriteResult?.Keywords;
            var textSearchQuery = keywords != null && keywords.Count > 0
                ? string.Join(" ", keywords)
                : null;

            RagProcessedQuery processed;
            if (searchSkipped)
            {
                processed = new RagProcessedQuery(
                    query, query,
                    System.Array.Empty<VectorSearchResult>(),
                    System.Array.Empty<VectorSearchResult>(),
                    new RagQueryDiagnostics());
                processed.SearchSkipped = true;
            }
            else if (textSearchQuery != null && Pipeline is RagPipeline concretePipeline)
            {
                processed = await concretePipeline.ProcessAsync(searchQuery, textSearchQuery, options, cancellationToken);
            }
            else
            {
                processed = await Pipeline.ProcessAsync(searchQuery, options, cancellationToken);
            }

            processed.RewriteResult = rewriteResult;
            processed.SearchKeywords = rewriteResult?.Keywords;

            if (rewrittenQuery != null)
            {
                processed.RewrittenQuery = rewrittenQuery;
                processed.OriginalQuery = query;
            }

            processed.Diagnostics.RewriteElapsedMs = rewriteElapsedMs;

            return processed;
        }

        /// <summary>
        /// Sets or clears the query rewriter at runtime.
        /// Pass null to disable query rewriting and retrieval keyword derivation.
        /// </summary>
        public void SetQueryRewriter(IQueryRewriter? queryRewriter)
        {
            QueryRewriter = queryRewriter;
        }

        #endregion

        #region Runtime Configuration

        /// <summary>
        /// Updates pipeline options at runtime without rebuilding the index.
        /// </summary>
        /// <example>
        /// <code>
        /// store.UpdateOptions(opt =>
        /// {
        ///     opt.TopK = 8;
        ///     opt.MinScore = 0.4;
        ///     opt.RetrievalMultiplier = 3;
        ///     opt.PromptTemplate = "Based on:\n{context}\n\nQuestion: {question}";
        /// });
        /// </code>
        /// </example>
        public bool UpdateOptions(Action<RagPipelineOptions> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var ragPipeline = Pipeline as RagPipeline;
            if (ragPipeline == null)
                return false;

            var nextOptions = ragPipeline.Options.Clone();
            configure(nextOptions);
            ragPipeline.Options = nextOptions;
            return true;
        }

        /// <summary>
        /// Switches the retrieval strategy at runtime between vector-only and hybrid search
        /// without rebuilding the entire pipeline or re-indexing documents.
        /// Returns false if the underlying pipeline does not support runtime updates.
        /// </summary>
        /// <param name="useHybridSearch">True to enable hybrid (vector + BM25) search, false for vector-only.</param>
        /// <param name="vectorWeight">Weight for vector similarity [0, 1] when hybrid is enabled. 0.5 = equal weight.</param>
        public bool UpdateRetrievalStrategy(bool useHybridSearch, float vectorWeight = 0.5f)
        {
            var ragPipeline = Pipeline as RagPipeline;
            if (ragPipeline == null)
                return false;

            if (useHybridSearch)
            {
                var bm25Index = new Bm25Index();
                ragPipeline.SetRetrievalStrategy(
                    new HybridRetrievalStrategy(VectorStore, vectorWeight, bm25Index));
            }
            else
            {
                ragPipeline.SetRetrievalStrategy(null);
            }

            return true;
        }

        #endregion

        #region Build

        /// <summary>
        /// Builds a RagStore by loading, splitting, embedding, and indexing all configured documents.
        /// The resulting store can be shared across multiple AIService instances.
        /// </summary>
        /// <example>
        /// <code>
        /// // Default: embed and save to store automatically
        /// var ragStore = await RagStore.BuildAsync(config => config
        ///     .AddDocuments("./knowledge-base/")
        ///     .UseOpenAIEmbedding(apiKey)
        /// );
        ///
        /// // With callback: replace vectors atomically per document
        /// var ragStore = await RagStore.BuildAsync(config => config
        ///     .AddDocuments(loader, filePath)
        ///     .UseEmbedding(embeddingProvider)
        ///     .UseStore(vectorStore),
        ///     onDocumentEmbedded: records =>
        ///         vectorStore.ReplaceByFilterAsync(
        ///             VectorFilter.ByMetadata("full_path", filePath), records),
        /// );
        /// </code>
        /// </example>
        /// <param name="configure">Configuration delegate for the RAG builder.</param>
        /// <param name="onDocumentEmbedded">
        /// Optional callback invoked after each document's embedding is complete.
        /// When provided, replaces the default <c>UpsertBatchAsync</c> call — the callback
        /// receives the generated <see cref="VectorRecord"/> list and decides how to persist them.
        /// When omitted, records are saved to the configured store automatically.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task<RagStore> BuildAsync(
            Action<RagBuilder> configure,
            Func<IReadOnlyList<VectorRecord>, Task>? onDocumentEmbedded = null,
            CancellationToken cancellationToken = default)
        {
            var builder = new RagBuilder();
            configure(builder);
            return await builder.BuildAsync(onDocumentEmbedded, cancellationToken);
        }

        #endregion
    }
}
