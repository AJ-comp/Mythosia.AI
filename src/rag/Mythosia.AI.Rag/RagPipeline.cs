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
    /// <remarks>
    /// With default persistence, successfully splitting a document into zero chunks clears
    /// its previously stored chunks by document ID without requesting embeddings.
    /// Loader/splitter exceptions and cancellation observed before persistence leave that
    /// document's previous index unchanged. Rollback after storage begins depends on the store.
    /// Stored chunk metadata uses the source document's ID for the reserved document_id key;
    /// conflicting input metadata is normalized without changing the input dictionaries.
    /// Document and chunk IDs must be nonblank, and chunk IDs must be unique within each
    /// document. Splitter output is validated and captured before embedding. Every embedding
    /// batch must match its input count and the provider's positive Dimensions, with finite
    /// values in every vector. Invalid output fails before persistence or a custom callback.
    /// </remarks>
    public class RagPipeline : IRagPipeline
    {
        private readonly IEmbeddingProvider _embeddingProvider;
        private readonly IVectorStore _vectorStore;
        private readonly ITextSplitter _textSplitter;
        private readonly IContextBuilder _defaultContextBuilder;
        private IRagRetriever _retriever;
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
            _retriever = retrievalStrategy == null
                ? (IRagRetriever)new RagRetrievers.Vector(embeddingProvider, vectorStore)
                : new RagRetrievers.Legacy(retrievalStrategy, embeddingProvider);
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
            SetRetriever(retrievalStrategy == null ? null : new RagRetrievers.Legacy(retrievalStrategy, _embeddingProvider));
        }

        /// <summary>
        /// Selects a retriever that receives the query text and decides which representations
        /// it needs. Null restores vector search. In-flight queries keep their selected retriever.
        /// </summary>
        public void SetRetriever(IRagRetriever? retriever)
        {
            Volatile.Write(ref _retriever, retriever ?? new RagRetrievers.Vector(_embeddingProvider, _vectorStore));
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
            cancellationToken.ThrowIfCancellationRequested();
            var embeddingBatchSize = CaptureEmbeddingBatchSize();
            var doclingDocs = await loader.LoadAsync(source, cancellationToken);
            var documents = DoclingDocumentConverter.ToRagDocuments(doclingDocs);
            await IndexDocumentsInternalAsync(documents, textSplitter: null, cancellationToken,
                embeddingBatchSize: embeddingBatchSize);
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
            Func<IReadOnlyList<VectorRecord>, Task>? onDocumentEmbedded = null,
            int? embeddingBatchSize = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchSize = embeddingBatchSize ?? CaptureEmbeddingBatchSize();
            var effectiveSplitter = textSplitter ?? _textSplitter;

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IndexSingleDocumentAsync(document, effectiveSplitter, batchSize,
                    cancellationToken, onDocumentEmbedded);
            }
        }

        private async Task IndexDocumentInternalAsync(
            RagDocument document,
            ITextSplitter? textSplitter,
            CancellationToken cancellationToken,
            Func<IReadOnlyList<VectorRecord>, Task>? onDocumentEmbedded = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embeddingBatchSize = CaptureEmbeddingBatchSize();
            var effectiveSplitter = textSplitter ?? _textSplitter;
            await IndexSingleDocumentAsync(document, effectiveSplitter, embeddingBatchSize,
                cancellationToken, onDocumentEmbedded);
        }

        private int CaptureEmbeddingBatchSize()
        {
            var batchSize = Options.EmbeddingBatchSize;
            if (batchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(RagPipelineOptions.EmbeddingBatchSize),
                    batchSize, "Embedding batch size must be positive.");
            return batchSize;
        }

        private async Task IndexSingleDocumentAsync(
            RagDocument document,
            ITextSplitter textSplitter,
            int embeddingBatchSize,
            CancellationToken cancellationToken,
            Func<IReadOnlyList<VectorRecord>, Task>? onDocumentEmbedded = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document == null) throw new ArgumentNullException(nameof(document));
            var documentId = document.Id;
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentException("The document ID must not be null, empty, or whitespace.", nameof(document));

            // 1. Split. A successful empty result still replaces this document's old chunks.
            IReadOnlyList<RagChunk> chunks = textSplitter.Split(document);
            cancellationToken.ThrowIfCancellationRequested();
            if (chunks == null)
                throw new InvalidOperationException("The text splitter returned a null chunk list.");
            if (chunks.Count == 0)
            {
                // A custom persistence callback receives only embedded records, not document identity.
                // Preserve its existing zero-chunk behavior and never mutate its store implicitly.
                if (onDocumentEmbedded == null)
                    await _vectorStore.ReplaceByFilterAsync(
                        new VectorFilter().Where("document_id", documentId),
                        Array.Empty<VectorRecord>(), cancellationToken);
                return;
            }

            // Capture validated splitter output before calling an external component.
            // A custom splitter may retain and reuse its mutable chunks and dictionaries.
            var records = PrepareIndexRecords(chunks, documentId, cancellationToken);
            var chunkTexts = records.Select(record => record.Content).ToList();
            var dimensions = _embeddingProvider.Dimensions;
            if (dimensions <= 0)
                throw new InvalidOperationException("The embedding provider must declare a positive Dimensions value.");

            // 2. Embed in batches, validating each response before accepting any of it.
            for (int i = 0; i < chunkTexts.Count;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The captured option stays stable across awaits. Advancing by the actual
                // batch length also prevents overflow for very large configured sizes.
                var batchCount = Math.Min(embeddingBatchSize, chunkTexts.Count - i);
                var batch = chunkTexts.GetRange(i, batchCount);
                var embeddings = await _embeddingProvider.GetEmbeddingsAsync(batch, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (embeddings == null || embeddings.Count != batchCount)
                    throw new InvalidOperationException(
                        $"Embedding batch at offset {i} must return exactly {batchCount} vectors; "
                        + (embeddings == null ? "the response was null." : $"received {embeddings.Count}."));

                for (int j = 0; j < batchCount; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var vector = embeddings[j];
                    if (vector == null || vector.Length != dimensions)
                        throw new InvalidOperationException(
                            $"Embedding vector at offset {i + j} must have {dimensions} dimensions; "
                            + (vector == null ? "the vector was null." : $"received {vector.Length}."));

                    // Own the accepted vector before another batch can reuse its buffer.
                    var ownedVector = (float[])vector.Clone();
                    for (int k = 0; k < ownedVector.Length; k++)
                        if (float.IsNaN(ownedVector[k]) || float.IsInfinity(ownedVector[k]))
                            throw new InvalidOperationException(
                                $"Embedding vector at offset {i + j} contains a non-finite value.");
                    records[i + j].Vector = ownedVector;
                }
                i += batchCount;
            }

            // 3. Persist only after every chunk and embedding batch has been accepted.
            cancellationToken.ThrowIfCancellationRequested();
            if (onDocumentEmbedded != null)
                await onDocumentEmbedded(records);
            else
                await _vectorStore.ReplaceByFilterAsync(
                    new VectorFilter().Where("document_id", documentId), records, cancellationToken);
        }

        private static List<VectorRecord> PrepareIndexRecords(
            IReadOnlyList<RagChunk> chunks, string documentId, CancellationToken cancellationToken)
        {
            var records = new List<VectorRecord>(chunks.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = chunks[i];
                if (chunk == null)
                    throw new InvalidOperationException($"The text splitter returned a null chunk at offset {i}.");
                var id = chunk.Id;
                var content = chunk.Content;
                var inputMetadata = chunk.Metadata;
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException($"The text splitter returned a blank chunk ID at offset {i}.");
                if (!ids.Add(id))
                    throw new InvalidOperationException($"The text splitter returned a duplicate chunk ID at offset {i}.");
                if (content == null || inputMetadata == null)
                    throw new InvalidOperationException($"The text splitter returned null content or metadata at offset {i}.");

                // Stored identity must agree with the replacement filter. A loader or custom
                // splitter may own this dictionary, so normalize a copy rather than its input.
                var metadata = new Dictionary<string, string>(inputMetadata)
                {
                    ["document_id"] = documentId
                };

                records.Add(new VectorRecord
                {
                    Id = id,
                    Content = content,
                    Metadata = metadata
                });
            }

            return records;
        }

        #endregion

        #region Query Pipeline: query ??search ??context build

        /// <summary>
        /// Performs a RAG query: retrieve documents, build context, and return the result.
        /// The selected retriever creates a query embedding only when needed.
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
        /// retrieve documents and build context, preparing embeddings only when needed.
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
        /// When <paramref name="textSearchQuery"/> is set, built-in text and hybrid retrievers
        /// use it for their text search. The original <paramref name="query"/> supplies any
        /// required semantic embedding. Null uses the query text; empty disables the text leg.
        /// </summary>
        internal async Task<RagQueryResult> QueryAsync(
            string query,
            string? textSearchQuery,
            RagQueryOptions? queryOptions,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default,
            RagPipelineOptions? pipelineOptionsSnapshot = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retriever = Volatile.Read(ref _retriever);
            var pipelineOptions = pipelineOptionsSnapshot ?? Options;
            var effectiveOptions = queryOptions ?? pipelineOptions.DefaultQuery;

            var k = effectiveOptions.FinalFilter.TopK;
            var finalMinScore = effectiveOptions.FinalFilter.MinScore;
            var retrievalFilter = effectiveOptions.GetRetrievalFilter(_reranker != null);
            var retrievalMinScore = retrievalFilter.MinScore;
            var retrievalK = retrievalFilter.TopK;

            async Task ReportAsync(RagProgressStage stage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (effectiveOptions.ProgressAsync != null)
                    await effectiveOptions.ProgressAsync(stage);
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Apply constraints before the retriever chooses query analysis/embedding.
            await ReportAsync(RagProgressStage.Filtering);
            var effectiveFilter = MergeStoreFilter(filter, effectiveOptions.StoreFilter);
            effectiveFilter.MinScore = retrievalMinScore;

            var request = new RagRetrievalRequest(query, textSearchQuery, retrievalK,
                effectiveFilter, effectiveOptions.ProgressAsync);
            var searchResults = await retriever.RetrieveAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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
            var merged = new VectorFilter();

            // storeFilter conditions first (permission/tenant constraints), then per-query conditions
            if (storeFilter != null)
                merged.AppendConditionsFrom(storeFilter);
            if (filter != null)
                merged.AppendConditionsFrom(filter);

            return merged;
        }

        /// <summary>
        /// Retrieves documents, builds context, and calls the LLM.
        /// </summary>
        public async Task<string> QueryAndGenerateAsync(
            IAIService aiService,
            string query,
            int? topK = null,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await QueryAsync(query, topK, filter, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await aiService.GetCompletionAsync(result.Context, cancellationToken: cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
            var result = await QueryAsync(query, queryOptions, filter, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await aiService.GetCompletionAsync(result.Context, cancellationToken: cancellationToken);
        }

        #endregion

        #region IRagPipeline Implementation

        /// <summary>
        /// Implements IRagPipeline: retrieve documents, build context, and return request message content.
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
