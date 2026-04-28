using Mythosia.Documents;
using Mythosia.AI.Rag.Retrieval;
using Mythosia.AI.Services;
using Mythosia.VectorDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// RAG (Retrieval Augmented Generation) orchestrator.
    /// Coordinates the full pipeline: load ??split ??embed ??store (indexing)
    /// and query ??search ??context build ??LLM call (querying).
    /// </summary>
    public class RagPipeline : IRagPipeline
    {
        private readonly IEmbeddingProvider _embeddingProvider;
        private readonly IVectorStore _vectorStore;
        private readonly ITextSplitter _textSplitter;
        private readonly IContextBuilder _defaultContextBuilder;
        private IRetrievalStrategy _retrievalStrategy;
        private readonly IReranker? _reranker;
        private RagPipelineOptions _options = null!;

        /// <summary>
        /// Pipeline configuration options.
        /// </summary>
        public RagPipelineOptions Options
        {
            get => Volatile.Read(ref _options);
            set => Volatile.Write(ref _options, value ?? throw new ArgumentNullException(nameof(value)));
        }

        internal IEmbeddingProvider EmbeddingProvider => _embeddingProvider;
        internal IVectorStore VectorStore => _vectorStore;
        internal ITextSplitter TextSplitter => _textSplitter;

        /// <summary>
        /// Creates a new RAG pipeline with the specified components.
        /// </summary>
        public RagPipeline(
            IEmbeddingProvider embeddingProvider,
            IVectorStore vectorStore,
            ITextSplitter textSplitter,
            IContextBuilder contextBuilder,
            RagPipelineOptions? options = null)
            : this(embeddingProvider, vectorStore, textSplitter, contextBuilder, null, null, options)
        {
        }

        /// <summary>
        /// Creates a new RAG pipeline with the specified components including retrieval strategy and reranker.
        /// </summary>
        public RagPipeline(
            IEmbeddingProvider embeddingProvider,
            IVectorStore vectorStore,
            ITextSplitter textSplitter,
            IContextBuilder contextBuilder,
            IRetrievalStrategy? retrievalStrategy,
            IReranker? reranker,
            RagPipelineOptions? options = null)
        {
            _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _textSplitter = textSplitter ?? throw new ArgumentNullException(nameof(textSplitter));
            _defaultContextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
            _retrievalStrategy = retrievalStrategy ?? new VectorRetrievalStrategy(vectorStore);
            _reranker = reranker;
            Options = options ?? new RagPipelineOptions();
        }

        /// <summary>
        /// Resolves the appropriate context builder based on <see cref="RagPipelineOptions.PromptTemplate"/>.
        /// Uses the default context builder when no template is set.
        /// </summary>
        private IContextBuilder ResolveContextBuilder(string? promptTemplate)
        {
            return string.IsNullOrWhiteSpace(promptTemplate)
                ? _defaultContextBuilder
                : new TemplateContextBuilder(promptTemplate);
        }

        /// <summary>
        /// Updates the retrieval strategy at runtime (e.g., switching between vector-only and hybrid search).
        /// </summary>
        public void SetRetrievalStrategy(IRetrievalStrategy? retrievalStrategy)
        {
            _retrievalStrategy = retrievalStrategy ?? new VectorRetrievalStrategy(_vectorStore);
        }

        #region Indexing Pipeline: load ??split ??embed ??store

        /// <summary>
        /// Indexes documents from a loader: load ??split ??embed ??store.
        /// </summary>
        public async Task IndexAsync(
            IDocumentLoader loader,
            string source,
            CancellationToken cancellationToken = default)
        {
            var doclingDocs = await loader.LoadAsync(source, cancellationToken);
            var documents = DoclingDocumentConverter.ToRagDocuments(doclingDocs);
            await IndexDocumentsAsync(documents, cancellationToken);
        }

        /// <summary>
        /// Indexes pre-loaded documents: split ??embed ??store.
        /// </summary>
        public async Task IndexDocumentsAsync(
            IEnumerable<RagDocument> documents,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentsInternalAsync(documents, textSplitter: null, cancellationToken);
        }

        /// <summary>
        /// Indexes pre-loaded documents with an optional per-source text splitter.
        /// </summary>
        public async Task IndexDocumentsAsync(
            IEnumerable<RagDocument> documents,
            ITextSplitter? textSplitter,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentsInternalAsync(documents, textSplitter, cancellationToken);
        }

        internal async Task IndexDocumentsAsync(
            IEnumerable<RagDocument> documents,
            ITextSplitter? textSplitter,
            Func<IReadOnlyList<VectorRecord>, Task> onDocumentEmbedded,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentsInternalAsync(documents, textSplitter, cancellationToken, onDocumentEmbedded);
        }

        /// <summary>
        /// Indexes a single document: split ??embed ??store.
        /// </summary>
        public async Task IndexDocumentAsync(
            RagDocument document,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentInternalAsync(document, textSplitter: null, cancellationToken);
        }

        /// <summary>
        /// Indexes a single document with an optional per-source text splitter.
        /// </summary>
        public async Task IndexDocumentAsync(
            RagDocument document,
            ITextSplitter? textSplitter,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentInternalAsync(document, textSplitter, cancellationToken);
        }

        private async Task IndexDocumentsInternalAsync(
            IEnumerable<RagDocument> documents,
            ITextSplitter? textSplitter,
            CancellationToken cancellationToken,
            Func<IReadOnlyList<VectorRecord>, Task>? onDocumentEmbedded = null)
        {
            var effectiveSplitter = textSplitter ?? _textSplitter;

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IndexSingleDocumentAsync(document, effectiveSplitter, cancellationToken, onDocumentEmbedded);
            }
        }

        private async Task IndexDocumentInternalAsync(
            RagDocument document,
            ITextSplitter? textSplitter,
            CancellationToken cancellationToken,
            Func<IReadOnlyList<VectorRecord>, Task>? onDocumentEmbedded = null)
        {
            var effectiveSplitter = textSplitter ?? _textSplitter;
            await IndexSingleDocumentAsync(document, effectiveSplitter, cancellationToken, onDocumentEmbedded);
        }

        private async Task IndexSingleDocumentAsync(
            RagDocument document,
            ITextSplitter textSplitter,
            CancellationToken cancellationToken,
            Func<IReadOnlyList<VectorRecord>, Task>? onDocumentEmbedded = null)
        {
            // 1. Split
            IReadOnlyList<RagChunk> chunks = textSplitter.Split(document);
            if (chunks.Count == 0) return;

            // 2. Embed in batches
            var chunkTexts = chunks.Select(c => c.Content).ToList();
            var allEmbeddings = new List<float[]>();

            for (int i = 0; i < chunkTexts.Count; i += Options.EmbeddingBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = chunkTexts.Skip(i).Take(Options.EmbeddingBatchSize);
                var embeddings = await _embeddingProvider.GetEmbeddingsAsync(batch, cancellationToken);
                allEmbeddings.AddRange(embeddings);
            }

            // 3. Store ? ensure document_id is in metadata for DeleteDocumentAsync
            var records = new List<VectorRecord>(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                var metadata = chunks[i].Metadata;
                if (!metadata.ContainsKey("document_id"))
                    metadata["document_id"] = document.Id;

                records.Add(new VectorRecord
                {
                    Id = chunks[i].Id,
                    Vector = allEmbeddings[i],
                    Content = chunks[i].Content,
                    Metadata = metadata
                });
            }

            if (onDocumentEmbedded != null)
                await onDocumentEmbedded(records);
            else
                await _vectorStore.ReplaceByFilterAsync(
                    new VectorFilter().Where("document_id", document.Id), records, cancellationToken);
        }

        #endregion

        #region Query Pipeline: query ??search ??context build

        /// <summary>
        /// Performs a RAG query: embed query ??search ??build context ??return context string.
        /// Use the returned context to call an LLM (e.g., via AIService.GetCompletionAsync).
        /// </summary>
        public async Task<RagQueryResult> QueryAsync(
            string query,
            int? topK = null,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var optionsSnapshot = Options;
            RagQueryOptions? queryOptions = null;
            if (topK.HasValue)
            {
                queryOptions = optionsSnapshot.DefaultQuery.Clone();
                queryOptions.FinalFilter.TopK = topK.Value;
            }

            return await QueryAsync(
                query,
                null,
                queryOptions,
                filter,
                cancellationToken,
                optionsSnapshot);
        }

        /// <summary>
        /// Performs a RAG query with per-request query overrides:
        /// embed query ??search ??build context.
        /// </summary>
        public async Task<RagQueryResult> QueryAsync(
            string query,
            RagQueryOptions? queryOptions,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var optionsSnapshot = Options;
            return await QueryAsync(
                query,
                null,
                queryOptions,
                filter,
                cancellationToken,
                optionsSnapshot);
        }

        /// <summary>
        /// Performs a RAG query with a separate text search query for the keyword leg of hybrid search.
        /// When <paramref name="textSearchQuery"/> is set, it is used for the text/BM25 search
        /// while the original <paramref name="query"/> is used for embedding (semantic search).
        /// </summary>
        internal async Task<RagQueryResult> QueryAsync(
            string query,
            string? textSearchQuery,
            RagQueryOptions? queryOptions,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default,
            RagPipelineOptions? pipelineOptionsSnapshot = null)
        {
            var pipelineOptions = pipelineOptionsSnapshot ?? Options;
            var effectiveOptions = queryOptions ?? pipelineOptions.DefaultQuery;

            var k = effectiveOptions.FinalFilter.TopK;
            var finalMinScore = effectiveOptions.FinalFilter.MinScore;
            var retrievalFilter = effectiveOptions.GetRetrievalFilter(_reranker != null);
            var retrievalMinScore = retrievalFilter.MinScore;
            var retrievalK = retrievalFilter.TopK;

            async Task ReportAsync(RagProgressStage stage)
            {
                if (effectiveOptions.ProgressAsync != null)
                    await effectiveOptions.ProgressAsync(stage);
            }

            // 1. Embed query (always uses the full semantic query)
            await ReportAsync(RagProgressStage.Embedding);
            var queryVector = await _embeddingProvider.GetEmbeddingAsync(query, cancellationToken);

            // 2. Apply retrieval score filter
            await ReportAsync(RagProgressStage.Filtering);
            var effectiveFilter = MergeStoreFilter(filter, effectiveOptions.StoreFilter);
            effectiveFilter.MinScore = retrievalMinScore;

            // 3. Search (via retrieval strategy) ??fetch wider pool when reranker is present
            //    textSearchQuery overrides the text leg when keywords are available from query rewriter
            await ReportAsync(RagProgressStage.Retrieval);
            var retrievalTextQuery = textSearchQuery;
            var searchResults = await _retrievalStrategy.RetrieveAsync(queryVector, retrievalTextQuery, retrievalK, effectiveFilter, cancellationToken);
            var retrievalCandidates = searchResults.ToList();

            // 4. Re-rank if configured ??reranker only re-scores, pipeline handles trimming
            IReadOnlyList<VectorSearchResult>? rerankedCandidates = null;
            if (_reranker != null)
            {
                await ReportAsync(RagProgressStage.Reranking);
                searchResults = await _reranker.RerankAsync(query, searchResults, cancellationToken);
                rerankedCandidates = searchResults.ToList();
                searchResults = ApplyFinalSelectionPolicy(
                    effectiveOptions.FinalSelection,
                    retrievalCandidates,
                    rerankedCandidates);
            }

            // 5. Final filter: apply minScore and topK
            if (finalMinScore.HasValue)
            {
                searchResults = searchResults
                    .Where(r => r.Score >= finalMinScore.Value)
                    .Take(k)
                    .ToList();
            }
            else
            {
                searchResults = searchResults
                    .Take(k)
                    .ToList();
            }

            // 6. Build context
            await ReportAsync(RagProgressStage.ContextBuild);
            var context = ResolveContextBuilder(pipelineOptions.PromptTemplate).BuildContext(query, searchResults);

            return new RagQueryResult(query, context, searchResults, retrievalCandidates, rerankedCandidates);
        }

        /// <summary>
        /// Applies the configured final selection policy after reranking.
        /// </summary>
        private static IReadOnlyList<VectorSearchResult> ApplyFinalSelectionPolicy(
            RagFinalSelectionOptions? finalSelection,
            IReadOnlyList<VectorSearchResult> retrievalCandidates,
            IReadOnlyList<VectorSearchResult> rerankedCandidates)
        {
            if (finalSelection == null || finalSelection.Mode != RagFinalSelectionMode.WeightedBlend)
                return rerankedCandidates;

            var retrievalWeight = finalSelection.GetClampedRetrievalWeight();
            var rerankWeight = 1d - retrievalWeight;

            var retrievalById = retrievalCandidates
                .GroupBy(r => r.Record.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var rerankedById = rerankedCandidates
                .GroupBy(r => r.Record.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var orderedRecords = new List<VectorRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var candidate in retrievalCandidates)
            {
                if (seen.Add(candidate.Record.Id))
                    orderedRecords.Add(candidate.Record);
            }

            foreach (var candidate in rerankedCandidates)
            {
                if (seen.Add(candidate.Record.Id))
                    orderedRecords.Add(candidate.Record);
            }

            return orderedRecords
                .Select(record =>
                {
                    var retrievalScore = retrievalById.TryGetValue(record.Id, out var retrieval)
                        ? retrieval.Score
                        : 0d;
                    var rerankScore = rerankedById.TryGetValue(record.Id, out var reranked)
                        ? reranked.Score
                        : 0d;
                    var finalScore = (retrievalWeight * retrievalScore) + (rerankWeight * rerankScore);

                    return new
                    {
                        Result = new VectorSearchResult(record, finalScore),
                        RetrievalScore = retrievalScore,
                        RerankScore = rerankScore
                    };
                })
                .OrderByDescending(x => x.Result.Score)
                .ThenByDescending(x => x.RerankScore)
                .ThenByDescending(x => x.RetrievalScore)
                .Select(x => x.Result)
                .ToList();
        }

        /// <summary>
        /// Merges a per-query <paramref name="filter"/> with a pipeline-level <paramref name="storeFilter"/>.
        /// Both filters' conditions are AND-combined in the result — neither side is silently dropped.
        /// This mirrors EF Core's Global Query Filter pattern: the store filter always applies,
        /// and the per-query filter adds further constraints on top.
        /// <see cref="VectorFilter.MinScore"/> is NOT copied
        /// here because it is always overwritten immediately after this call.
        /// </summary>
        private static VectorFilter MergeStoreFilter(VectorFilter? filter, VectorFilter? storeFilter)
        {
            if (storeFilter == null)
                return filter ?? new VectorFilter();

            var merged = new VectorFilter();

            // storeFilter conditions first (permission/tenant constraints), then per-query conditions
            merged.AppendConditionsFrom(storeFilter);
            if (filter != null)
                merged.AppendConditionsFrom(filter);

            return merged;
        }

        /// <summary>
        /// Performs a full RAG query and calls the LLM: embed query ??search ??context build ??LLM call.
        /// </summary>
        public async Task<string> QueryAndGenerateAsync(
            IAIService aiService,
            string query,
            int? topK = null,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var result = await QueryAsync(query, topK, filter, cancellationToken);
            return await aiService.GetCompletionAsync(result.Context);
        }

        /// <summary>
        /// Performs a full RAG query with per-request overrides and calls the LLM.
        /// </summary>
        public async Task<string> QueryAndGenerateAsync(
            IAIService aiService,
            string query,
            RagQueryOptions? queryOptions,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var result = await QueryAsync(query, queryOptions, filter, cancellationToken);
            return await aiService.GetCompletionAsync(result.Context);
        }

        #endregion

        #region IRagPipeline Implementation

        /// <summary>
        /// Implements IRagPipeline: embed query ??search ??build context ??return request message content.
        /// </summary>
        public async Task<RagProcessedQuery> ProcessAsync(string query, CancellationToken cancellationToken = default)
        {
            return await ProcessAsync(query, options: null, cancellationToken);
        }

        /// <summary>
        /// Implements IRagPipeline with per-request query overrides.
        /// </summary>
        public async Task<RagProcessedQuery> ProcessAsync(
            string query,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            return await ProcessAsync(query, textSearchQuery: null, options, cancellationToken);
        }

        /// <summary>
        /// Processes a query with a separate text search query for the keyword leg of hybrid search.
        /// </summary>
        internal async Task<RagProcessedQuery> ProcessAsync(
            string query,
            string? textSearchQuery,
            RagQueryOptions? options,
            CancellationToken cancellationToken = default)
        {
            var pipelineOptions = Options;
            var effectiveOptions = options ?? pipelineOptions.DefaultQuery;

            var retrievalFilter = effectiveOptions.GetRetrievalFilter(_reranker != null);
            var appliedTopK = effectiveOptions.FinalFilter.TopK;
            var appliedFinalMinScore = effectiveOptions.FinalFilter.MinScore;
            var appliedRetrievalMinScore = retrievalFilter.MinScore;
            var retrievalK = retrievalFilter.TopK;

            var stopwatch = Stopwatch.StartNew();
            var result = await QueryAsync(
                query,
                textSearchQuery,
                effectiveOptions,
                cancellationToken: cancellationToken,
                pipelineOptionsSnapshot: pipelineOptions);
            stopwatch.Stop();

            // When no references are found, return the original query as-is
            // instead of a context-less template that confuses the LLM.
            var requestMessageContent = result.SearchResults.Count > 0
                ? result.Context
                : query;

            return new RagProcessedQuery(
                query,
                requestMessageContent,
                result.SearchResults,
                result.RetrievalCandidates,
                new RagQueryDiagnostics
                {
                    FinalTopK = appliedTopK,
                    RetrievalTopK = retrievalK,
                    AppliedFinalMinScore = appliedFinalMinScore,
                    AppliedRetrievalMinScore = appliedRetrievalMinScore,
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                })
            {
                RerankedCandidates = result.RerankedCandidates
            };
        }

        #endregion

        #region Delete

        /// <summary>
        /// Deletes a document and all its chunks from the vector store.
        /// </summary>
        public async Task DeleteDocumentAsync(
            string documentId,
            CancellationToken cancellationToken = default)
        {
            var filter = new VectorFilter().Where("document_id", documentId);
            await _vectorStore.DeleteByFilterAsync(filter, cancellationToken);
        }

        #endregion
    }

    /// <summary>
    /// The result of a RAG query, containing the assembled context and search results.
    /// </summary>
    public class RagQueryResult
    {
        /// <summary>
        /// The original user query.
        /// </summary>
        public string Query { get; }

        /// <summary>
        /// The assembled context string ready to be sent to an LLM.
        /// </summary>
        public string Context { get; }

        /// <summary>
        /// The final search results after all pipeline stages (reranking + topK + minScore).
        /// </summary>
        public IReadOnlyList<VectorSearchResult> SearchResults { get; }

        /// <summary>
        /// The raw retrieval candidates returned before re-ranking was applied.
        /// When no reranker is configured this matches <see cref="SearchResults"/>.
        /// </summary>
        public IReadOnlyList<VectorSearchResult> RetrievalCandidates { get; }

        /// <summary>
        /// All results after re-ranking (re-scored and reordered) but before final selection (topK + minScore).
        /// When no reranker is configured this is null.
        /// </summary>
        public IReadOnlyList<VectorSearchResult>? RerankedCandidates { get; }

        public RagQueryResult(
            string query,
            string context,
            IReadOnlyList<VectorSearchResult> searchResults,
            IReadOnlyList<VectorSearchResult> retrievalCandidates,
            IReadOnlyList<VectorSearchResult>? rerankedCandidates = null)
        {
            Query = query;
            Context = context;
            SearchResults = searchResults;
            RetrievalCandidates = retrievalCandidates;
            RerankedCandidates = rerankedCandidates;
        }
    }
}
