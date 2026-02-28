using Mythosia.AI.Loaders;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// RAG (Retrieval Augmented Generation) orchestrator.
    /// Coordinates the full pipeline: load → split → embed → store (indexing)
    /// and query → search → context build → LLM call (querying).
    /// </summary>
    public class RagPipeline : IRagPipeline
    {
        private readonly IEmbeddingProvider _embeddingProvider;
        private readonly IVectorStore _vectorStore;
        private readonly ITextSplitter _textSplitter;
        private readonly IContextBuilder _contextBuilder;

        /// <summary>
        /// Pipeline configuration options.
        /// </summary>
        public RagPipelineOptions Options { get; set; }

        /// <summary>
        /// Creates a new RAG pipeline with the specified components.
        /// </summary>
        public RagPipeline(
            IEmbeddingProvider embeddingProvider,
            IVectorStore vectorStore,
            ITextSplitter textSplitter,
            IContextBuilder contextBuilder,
            RagPipelineOptions? options = null)
        {
            _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _textSplitter = textSplitter ?? throw new ArgumentNullException(nameof(textSplitter));
            _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
            Options = options ?? new RagPipelineOptions();
        }

        #region Indexing Pipeline: load → split → embed → store

        /// <summary>
        /// Indexes documents from a loader: load → split → embed → store.
        /// </summary>
        public async Task IndexAsync(
            IDocumentLoader loader,
            string source,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            var documents = await loader.LoadAsync(source, cancellationToken);
            await IndexDocumentsAsync(documents, collection, cancellationToken);
        }

        /// <summary>
        /// Indexes pre-loaded documents: split → embed → store.
        /// </summary>
        public async Task IndexDocumentsAsync(
            IEnumerable<RagDocument> documents,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentsInternalAsync(documents, textSplitter: null, collection, cancellationToken);
        }

        /// <summary>
        /// Indexes pre-loaded documents with an optional per-source text splitter.
        /// </summary>
        public async Task IndexDocumentsAsync(
            IEnumerable<RagDocument> documents,
            ITextSplitter? textSplitter,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentsInternalAsync(documents, textSplitter, collection, cancellationToken);
        }

        /// <summary>
        /// Indexes a single document: split → embed → store.
        /// </summary>
        public async Task IndexDocumentAsync(
            RagDocument document,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentInternalAsync(document, textSplitter: null, collection, cancellationToken);
        }

        /// <summary>
        /// Indexes a single document with an optional per-source text splitter.
        /// </summary>
        public async Task IndexDocumentAsync(
            RagDocument document,
            ITextSplitter? textSplitter,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            await IndexDocumentInternalAsync(document, textSplitter, collection, cancellationToken);
        }

        private async Task IndexDocumentsInternalAsync(
            IEnumerable<RagDocument> documents,
            ITextSplitter? textSplitter,
            string? collection,
            CancellationToken cancellationToken)
        {
            var effectiveSplitter = textSplitter ?? _textSplitter;
            var col = collection ?? Options.DefaultCollection;
            await _vectorStore.CreateCollectionAsync(col, cancellationToken);

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IndexSingleDocumentAsync(document, col, effectiveSplitter, cancellationToken);
            }
        }

        private async Task IndexDocumentInternalAsync(
            RagDocument document,
            ITextSplitter? textSplitter,
            string? collection,
            CancellationToken cancellationToken)
        {
            var col = collection ?? Options.DefaultCollection;
            await _vectorStore.CreateCollectionAsync(col, cancellationToken);
            var effectiveSplitter = textSplitter ?? _textSplitter;
            await IndexSingleDocumentAsync(document, col, effectiveSplitter, cancellationToken);
        }

        private async Task IndexSingleDocumentAsync(
            RagDocument document,
            string collection,
            ITextSplitter textSplitter,
            CancellationToken cancellationToken)
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

            // 3. Store
            var records = new List<VectorRecord>(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                records.Add(new VectorRecord
                {
                    Id = chunks[i].Id,
                    Vector = allEmbeddings[i],
                    Content = chunks[i].Content,
                    Metadata = chunks[i].Metadata,
                    Namespace = Options.DefaultNamespace
                });
            }

            await _vectorStore.UpsertBatchAsync(collection, records, cancellationToken);
        }

        #endregion

        #region Query Pipeline: query → search → context build

        /// <summary>
        /// Performs a RAG query: embed query → search → build context → return context string.
        /// Use the returned context to call an LLM (e.g., via AIService.GetCompletionAsync).
        /// </summary>
        public async Task<RagQueryResult> QueryAsync(
            string query,
            string? collection = null,
            int? topK = null,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var col = collection ?? Options.DefaultCollection;
            var k = topK ?? Options.TopK;

            // 1. Embed query
            var queryVector = await _embeddingProvider.GetEmbeddingAsync(query, cancellationToken);

            // 2. Apply MinScore filter
            var effectiveFilter = filter;
            if (Options.MinScore.HasValue)
            {
                effectiveFilter = effectiveFilter ?? new VectorFilter();
                effectiveFilter.MinScore = Options.MinScore;
            }

            // 3. Search
            var searchResults = await _vectorStore.SearchAsync(col, queryVector, k, effectiveFilter, cancellationToken);

            // 4. Build context
            var context = _contextBuilder.BuildContext(query, searchResults);

            return new RagQueryResult(query, context, searchResults);
        }

        /// <summary>
        /// Performs a full RAG query and calls the LLM: embed query → search → context build → LLM call.
        /// </summary>
        public async Task<string> QueryAndGenerateAsync(
            AIService aiService,
            string query,
            string? collection = null,
            int? topK = null,
            VectorFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            var result = await QueryAsync(query, collection, topK, filter, cancellationToken);
            return await aiService.GetCompletionAsync(result.Context);
        }

        #endregion

        #region IRagPipeline Implementation

        /// <summary>
        /// Implements IRagPipeline: embed query → search → build context → return augmented prompt.
        /// </summary>
        public async Task<RagProcessedQuery> ProcessAsync(string query, CancellationToken cancellationToken = default)
        {
            var result = await QueryAsync(query, cancellationToken: cancellationToken);
            return new RagProcessedQuery(query, result.Context, result.SearchResults);
        }

        #endregion

        #region Delete

        /// <summary>
        /// Deletes a document and all its chunks from the vector store.
        /// </summary>
        public async Task DeleteDocumentAsync(
            string documentId,
            string? collection = null,
            CancellationToken cancellationToken = default)
        {
            var col = collection ?? Options.DefaultCollection;
            var filter = VectorFilter.ByMetadata("document_id", documentId);
            await _vectorStore.DeleteByFilterAsync(col, filter, cancellationToken);
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
        /// The raw search results from the vector store.
        /// </summary>
        public IReadOnlyList<VectorSearchResult> SearchResults { get; }

        public RagQueryResult(string query, string context, IReadOnlyList<VectorSearchResult> searchResults)
        {
            Query = query;
            Context = context;
            SearchResults = searchResults;
        }
    }
}
