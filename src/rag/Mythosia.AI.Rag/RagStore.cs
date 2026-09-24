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
        private IQueryRewriter? _queryRewriter;

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
        internal IQueryRewriter? QueryRewriter => Volatile.Read(ref _queryRewriter);

        internal RagStore(IRagPipeline pipeline, IVectorStore vectorStore, IQueryRewriter? queryRewriter = null)
        {
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            VectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _queryRewriter = queryRewriter;
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
            cancellationToken.ThrowIfCancellationRequested();
            // Runtime changes apply to subsequent requests, including while this
            // request is waiting for a progress callback or a rewrite to complete.
            var queryRewriter = QueryRewriter;
            string searchQuery = query;
            string? rewrittenQuery = null;
            bool searchSkipped = false;
            QueryRewriteResult? rewriteResult = null;

            long rewriteElapsedMs = 0;
            if (queryRewriter != null)
            {
                if (options?.ProgressAsync != null)
                    await options.ProgressAsync(RagProgressStage.QueryRewrite);

                cancellationToken.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                var result = await queryRewriter.RewriteAsync(query, conversationHistory, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
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
        /// Requests that have already selected a rewriter keep using that instance;
        /// subsequent requests use the new setting.
        /// </summary>
        public void SetQueryRewriter(IQueryRewriter? queryRewriter)
        {
            Volatile.Write(ref _queryRewriter, queryRewriter);
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
        ///     opt.DefaultQuery.FinalFilter.TopK = 8;
        ///     opt.DefaultQuery.FinalFilter.MinScore = 0.4;
        ///     opt.DefaultQuery.RetrievalDerivation.TopKMultiplier = 3;
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
        /// <param name="useHybridSearch">True to enable hybrid (vector + text) search, false for vector-only.</param>
        /// <param name="vectorWeight">Weight for vector similarity [0, 1] when hybrid is enabled. 0.5 = equal weight.</param>
        public bool UpdateRetrievalStrategy(bool useHybridSearch, float vectorWeight = 0.5f)
        {
            var ragPipeline = Pipeline as RagPipeline;
            if (ragPipeline == null)
                return false;

            if (useHybridSearch)
            {
                if (float.IsNaN(vectorWeight) || float.IsInfinity(vectorWeight))
                    throw new ArgumentOutOfRangeException(nameof(vectorWeight));
                ragPipeline.SetRetriever(new RagRetrievers.Hybrid(ragPipeline.EmbeddingProvider,
                    VectorStore, new HybridSearchOptions { VectorWeight = Math.Max(0f, Math.Min(1f, vectorWeight)) },
                    allowLegacyDefaults: true));
            }
            else
            {
                ragPipeline.SetRetrievalStrategy(null);
            }

            return true;
        }

        /// <summary>Changes the request-based retriever without rebuilding the index. Null restores vector search.</summary>
        public bool UpdateRetriever(IRagRetriever? retriever)
        {
            if (!(Pipeline is RagPipeline pipeline)) return false;
            pipeline.SetRetriever(retriever);
            return true;
        }

        /// <summary>Switches to keyword-only querying of the existing index, with no query embedding.</summary>
        public bool UseKeywordSearch()
        {
            if (!(Pipeline is RagPipeline pipeline)) return false;
            pipeline.SetRetriever(new RagRetrievers.Keyword(VectorStore));
            return true;
        }

        /// <summary>Applies a snapshot of weighted RRF settings to subsequent queries.</summary>
        public bool UpdateRetrievalStrategy(HybridSearchOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!(Pipeline is RagPipeline pipeline)) return false;
            pipeline.SetRetriever(new RagRetrievers.Hybrid(pipeline.EmbeddingProvider, VectorStore, options));
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
        /// // With callback: replace by the document identity supplied on every record.
        /// // Atomic replacement/rollback depends on the chosen vector store.
        /// var customStore = await RagStore.BuildAsync(config => config
        ///     .AddDocuments(loader, filePath)
        ///     .UseEmbedding(embeddingProvider)
        ///     .UseStore(vectorStore),
        ///     onDocumentEmbedded: records =>
        ///         vectorStore.ReplaceByFilterAsync(
        ///             new VectorFilter().Where("document_id", records[0].Metadata["document_id"]),
        ///             records, cancellationToken),
        ///     cancellationToken: cancellationToken);
        /// // The callback is only invoked for nonempty, validated document record lists.
        /// // For an empty document, custom persistence must explicitly delete using its known ID.
        /// // Omit the callback to let default persistence handle empty-document deletion too.
        /// </code>
        /// </example>
        /// <param name="configure">Configuration delegate for the RAG builder.</param>
        /// <param name="onDocumentEmbedded">
        /// Optional callback invoked after each document's embedding is complete.
        /// When provided, replaces default persistence — the callback receives the generated
        /// <see cref="VectorRecord"/> list and decides how to persist it. Zero-chunk documents
        /// do not invoke this callback or modify the configured store; custom persistence must
        /// handle their removal explicitly using the document identity it owns.
        /// When omitted, records replace the document's previous chunks in the configured store,
        /// including removal of previous chunks when a document successfully produces zero chunks.
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
