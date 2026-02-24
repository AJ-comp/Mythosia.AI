using Mythosia.AI.Loaders;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.AI.VectorDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Fluent builder for configuring a RAG pipeline.
    /// Follows the same builder pattern as Mythosia.AI's WithFunction, WithSystemMessage, etc.
    /// </summary>
    public class RagBuilder
    {
        private readonly List<Func<CancellationToken, Task<IReadOnlyList<RagDocument>>>> _documentSources = new List<Func<CancellationToken, Task<IReadOnlyList<RagDocument>>>>();

        private IEmbeddingProvider? _embeddingProvider;
        private IVectorStore? _vectorStore;
        private ITextSplitter? _textSplitter;
        private IContextBuilder? _contextBuilder;

        private int _topK = 3;
        private int _chunkSize = 300;
        private int _chunkOverlap = 30;
        private double? _scoreThreshold;
        private string? _promptTemplate;

        #region Document Sources

        /// <summary>
        /// Adds a single document file to the RAG index.
        /// </summary>
        public RagBuilder AddDocument(string filePath)
        {
            _documentSources.Add(async ct =>
            {
                var loader = new PlainTextDocumentLoader();
                return await loader.LoadAsync(filePath, ct);
            });
            return this;
        }

        /// <summary>
        /// Adds all supported text files from a directory (recursive).
        /// </summary>
        public RagBuilder AddDocuments(string directoryPath)
        {
            _documentSources.Add(async ct =>
            {
                var loader = new DirectoryDocumentLoader();
                return await loader.LoadAsync(directoryPath, ct);
            });
            return this;
        }

        /// <summary>
        /// Adds raw text content directly to the RAG index.
        /// </summary>
        /// <param name="text">The text content.</param>
        /// <param name="id">Optional unique identifier. Auto-generated if null.</param>
        public RagBuilder AddText(string text, string? id = null)
        {
            _documentSources.Add(ct =>
            {
                var doc = new RagDocument
                {
                    Id = id ?? $"text_{Guid.NewGuid():N}",
                    Content = text,
                    Source = "inline-text",
                    Metadata = { ["type"] = "inline" }
                };
                return Task.FromResult<IReadOnlyList<RagDocument>>(new[] { doc });
            });
            return this;
        }

        /// <summary>
        /// Adds a document from a URL. Content is fetched via HTTP GET.
        /// </summary>
        public RagBuilder AddUrl(string url)
        {
            _documentSources.Add(async ct =>
            {
                using var httpClient = new HttpClient();
                var content = await httpClient.GetStringAsync(url);
                var doc = new RagDocument
                {
                    Id = url,
                    Content = content,
                    Source = url,
                    Metadata = { ["type"] = "url", ["url"] = url }
                };
                return new[] { doc };
            });
            return this;
        }

        /// <summary>
        /// Adds documents using a custom IDocumentLoader.
        /// </summary>
        public RagBuilder AddDocuments(IDocumentLoader loader, string source)
        {
            _documentSources.Add(async ct => await loader.LoadAsync(source, ct));
            return this;
        }

        #endregion

        #region Search Settings

        /// <summary>
        /// Sets the number of top results to retrieve. Default is 3.
        /// </summary>
        public RagBuilder WithTopK(int topK)
        {
            _topK = topK;
            return this;
        }

        /// <summary>
        /// Sets the chunk size in characters. Default is 300.
        /// </summary>
        public RagBuilder WithChunkSize(int chunkSize)
        {
            _chunkSize = chunkSize;
            return this;
        }

        /// <summary>
        /// Sets the overlap between consecutive chunks. Default is 30.
        /// </summary>
        public RagBuilder WithChunkOverlap(int overlap)
        {
            _chunkOverlap = overlap;
            return this;
        }

        /// <summary>
        /// Sets the minimum similarity score threshold. Results below this are discarded.
        /// </summary>
        public RagBuilder WithScoreThreshold(double threshold)
        {
            _scoreThreshold = threshold;
            return this;
        }

        #endregion

        #region Embedding Provider

        /// <summary>
        /// Uses the local feature-hashing embedding provider (no API key required).
        /// This is the default if no embedding provider is specified.
        /// </summary>
        public RagBuilder UseLocalEmbedding(int dimensions = 1024)
        {
            _embeddingProvider = new LocalEmbeddingProvider(dimensions);
            return this;
        }

        /// <summary>
        /// Uses OpenAI's embedding API.
        /// </summary>
        /// <param name="apiKey">OpenAI API key.</param>
        /// <param name="model">Embedding model. Default is "text-embedding-3-small".</param>
        /// <param name="dimensions">Vector dimensions. Default is 1536.</param>
        public RagBuilder UseOpenAIEmbedding(string apiKey, string model = "text-embedding-3-small", int dimensions = 1536)
        {
            _embeddingProvider = new OpenAIEmbeddingProvider(apiKey, new HttpClient(), model, dimensions);
            return this;
        }

        /// <summary>
        /// Uses a custom embedding provider.
        /// </summary>
        public RagBuilder UseEmbedding(IEmbeddingProvider provider)
        {
            _embeddingProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            return this;
        }

        #endregion

        #region Vector Store

        /// <summary>
        /// Uses the in-memory vector store. This is the default.
        /// Data is lost when the process exits.
        /// </summary>
        public RagBuilder UseInMemoryStore()
        {
            _vectorStore = new InMemoryVectorStore();
            return this;
        }

        /// <summary>
        /// Uses a file-based vector store for persistence across restarts.
        /// </summary>
        public RagBuilder UseFileStore(string directory)
        {
            // TODO: Implement FileBasedVectorStore in a future release
            throw new NotSupportedException(
                "File-based vector store is not yet available. " +
                "Use .UseInMemoryStore() or provide a custom IVectorStore via .UseStore().");
        }

        /// <summary>
        /// Uses a custom vector store implementation (e.g., Qdrant, Chroma, Pinecone).
        /// </summary>
        public RagBuilder UseStore(IVectorStore store)
        {
            _vectorStore = store ?? throw new ArgumentNullException(nameof(store));
            return this;
        }

        #endregion

        #region Prompt Template

        /// <summary>
        /// Sets a custom prompt template. Use {context} and {question} placeholders.
        /// </summary>
        /// <example>
        /// <code>
        /// .WithPromptTemplate(@"
        ///     [참고 문서]
        ///     {context}
        ///     
        ///     [질문]
        ///     {question}
        ///     
        ///     반드시 문서 내용을 근거로 답변하세요.
        /// ")
        /// </code>
        /// </example>
        public RagBuilder WithPromptTemplate(string template)
        {
            _promptTemplate = template;
            return this;
        }

        /// <summary>
        /// Sets a custom context builder implementation.
        /// </summary>
        public RagBuilder WithContextBuilder(IContextBuilder contextBuilder)
        {
            _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
            return this;
        }

        /// <summary>
        /// Sets a custom text splitter implementation.
        /// </summary>
        public RagBuilder WithTextSplitter(ITextSplitter textSplitter)
        {
            _textSplitter = textSplitter ?? throw new ArgumentNullException(nameof(textSplitter));
            return this;
        }

        #endregion

        #region Internal Build

        /// <summary>
        /// Builds the RAG store: applies defaults, loads documents, and indexes them.
        /// </summary>
        internal async Task<RagStore> BuildAsync(CancellationToken cancellationToken = default)
        {
            // 1. Apply defaults
            var embeddingProvider = _embeddingProvider ?? new LocalEmbeddingProvider();
            var vectorStore = _vectorStore ?? new InMemoryVectorStore();
            var textSplitter = _textSplitter ?? new CharacterTextSplitter(_chunkSize, _chunkOverlap);

            IContextBuilder contextBuilder;
            if (_contextBuilder != null)
                contextBuilder = _contextBuilder;
            else if (_promptTemplate != null)
                contextBuilder = new TemplateContextBuilder(_promptTemplate);
            else
                contextBuilder = new DefaultContextBuilder();

            var options = new RagPipelineOptions
            {
                TopK = _topK,
                MinScore = _scoreThreshold
            };

            // 2. Create pipeline
            var pipeline = new RagPipeline(embeddingProvider, vectorStore, textSplitter, contextBuilder, options);

            // 3. Load and index all documents
            var allDocs = new List<RagDocument>();
            foreach (var source in _documentSources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var docs = await source(cancellationToken);
                allDocs.AddRange(docs);
            }

            if (allDocs.Count > 0)
            {
                await pipeline.IndexDocumentsAsync(allDocs, cancellationToken: cancellationToken);
            }

            return new RagStore(pipeline, vectorStore);
        }

        #endregion
    }
}
