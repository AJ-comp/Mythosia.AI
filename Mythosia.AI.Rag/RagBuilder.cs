using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Document;
using Mythosia.AI.Loaders.Office;
using Mythosia.AI.Loaders.Office.Excel;
using Mythosia.AI.Loaders.Office.PowerPoint;
using Mythosia.AI.Loaders.Office.Word;
using Mythosia.AI.Loaders.Pdf;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Loaders;
using Mythosia.AI.Rag.Splitters;
using Mythosia.AI.VectorDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private enum DocumentSourceKind
        {
            SingleFile = 0,
            Directory = 1,
            Other = 2
        }

        private sealed class DocumentSourceRegistration
        {
            public Func<CancellationToken, Task<IReadOnlyList<RagDocument>>> Loader { get; }
            public ITextSplitter? TextSplitter { get; }
            public DocumentSourceKind Kind { get; }

            public DocumentSourceRegistration(
                Func<CancellationToken, Task<IReadOnlyList<RagDocument>>> loader,
                ITextSplitter? textSplitter,
                DocumentSourceKind kind)
            {
                Loader = loader ?? throw new ArgumentNullException(nameof(loader));
                TextSplitter = textSplitter;
                Kind = kind;
            }
        }

        private readonly List<DocumentSourceRegistration> _documentSources = new List<DocumentSourceRegistration>();

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

        private RagBuilder AddDocumentSource(
            Func<CancellationToken, Task<IReadOnlyList<RagDocument>>> loader,
            ITextSplitter? textSplitter = null,
            DocumentSourceKind kind = DocumentSourceKind.Other)
        {
            _documentSources.Add(new DocumentSourceRegistration(loader, textSplitter, kind));
            return this;
        }

        private static IDocumentLoader CreateLoaderForExtension(string extension)
        {
            var normalized = DocumentSourceBuilder.NormalizeExtension(extension);
            return normalized switch
            {
                ".docx" => new WordDocumentLoader(),
                ".xlsx" => new ExcelDocumentLoader(),
                ".pptx" => new PowerPointDocumentLoader(),
                ".pdf" => new PdfDocumentLoader(),
                _ => new PlainTextDocumentLoader()
            };
        }

        private static IDocumentLoader GetOrCreateDefaultLoader(
            string extension,
            Dictionary<string, IDocumentLoader> cache)
        {
            var normalized = DocumentSourceBuilder.NormalizeExtension(extension);
            if (cache.TryGetValue(normalized, out var loader))
                return loader;

            loader = CreateLoaderForExtension(normalized);
            cache[normalized] = loader;
            return loader;
        }

        private static DocumentSourceKind ResolveSourceKind(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return DocumentSourceKind.Other;

            if (Directory.Exists(source))
                return DocumentSourceKind.Directory;

            if (File.Exists(source) || Path.HasExtension(source))
                return DocumentSourceKind.SingleFile;

            return DocumentSourceKind.Other;
        }

        private static async Task<IReadOnlyList<RagDocument>> LoadDocumentsFromDirectoryAsync(
            string directoryPath,
            DocumentSourceBuilder.DocumentSourceOptions options,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"Document directory not found: {directoryPath}");

            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var docs = new List<RagDocument>();
            var defaultLoaders = options.Loader == null
                ? new Dictionary<string, IDocumentLoader>(StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(file);
                if (!options.MatchesExtension(extension))
                    continue;

                var loader = options.Loader ?? GetOrCreateDefaultLoader(extension, defaultLoaders!);
                var loaded = await loader.LoadAsync(file, cancellationToken);
                if (loaded.Count > 0)
                    docs.AddRange(loaded);
            }

            return docs;
        }

        /// <summary>
        /// Adds a single document file to the RAG index.
        /// Automatically selects a loader based on file extension (.docx/.xlsx/.pptx/.pdf).
        /// </summary>
        public RagBuilder AddDocument(string filePath)
        {
            return AddDocumentSource(async ct =>
            {
                var extension = Path.GetExtension(filePath);
                var loader = CreateLoaderForExtension(extension);
                return await loader.LoadAsync(filePath, ct);
            }, kind: DocumentSourceKind.SingleFile);
        }

        /// <summary>
        /// Adds all supported text files from a directory (recursive).
        /// </summary>
        public RagBuilder AddDocuments(string directoryPath)
        {
            return AddDocumentSource(async ct =>
            {
                var loader = new DirectoryDocumentLoader();
                return await loader.LoadAsync(directoryPath, ct);
            }, kind: DocumentSourceKind.Directory);
        }

        /// <summary>
        /// Adds documents from a directory with per-extension routing (loader/splitter).
        /// </summary>
        public RagBuilder AddDocuments(string directoryPath, Action<DocumentSourceBuilder> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            var builder = new DocumentSourceBuilder();
            configure(builder);
            var options = builder.Build();

            return AddDocumentSource(
                ct => LoadDocumentsFromDirectoryAsync(directoryPath, options, ct),
                options.TextSplitter,
                DocumentSourceKind.Directory);
        }

        /// <summary>
        /// Adds a Word document file to the RAG index.
        /// </summary>
        public RagBuilder AddWord(string filePath, IDocumentParser? parser = null, OfficeParserOptions? options = null)
        {
            return AddDocumentSource(async ct =>
            {
                var loader = new WordDocumentLoader(parser, options);
                return await loader.LoadAsync(filePath, ct);
            }, kind: DocumentSourceKind.SingleFile);
        }

        /// <summary>
        /// Adds an Excel document file to the RAG index.
        /// </summary>
        public RagBuilder AddExcel(string filePath, IDocumentParser? parser = null, OfficeParserOptions? options = null)
        {
            return AddDocumentSource(async ct =>
            {
                var loader = new ExcelDocumentLoader(parser, options);
                return await loader.LoadAsync(filePath, ct);
            }, kind: DocumentSourceKind.SingleFile);
        }

        /// <summary>
        /// Adds a PowerPoint document file to the RAG index.
        /// </summary>
        public RagBuilder AddPowerPoint(string filePath, IDocumentParser? parser = null, OfficeParserOptions? options = null)
        {
            return AddDocumentSource(async ct =>
            {
                var loader = new PowerPointDocumentLoader(parser, options);
                return await loader.LoadAsync(filePath, ct);
            }, kind: DocumentSourceKind.SingleFile);
        }

        /// <summary>
        /// Adds raw text content directly to the RAG index.
        /// </summary>
        /// <param name="text">The text content.</param>
        /// <param name="id">Optional unique identifier. Auto-generated if null.</param>
        public RagBuilder AddText(string text, string? id = null)
        {
            return AddDocumentSource(ct =>
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
        }

        /// <summary>
        /// Adds a document from a URL. Content is fetched via HTTP GET.
        /// </summary>
        public RagBuilder AddUrl(string url)
        {
            return AddDocumentSource(async ct =>
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
        }

        /// <summary>
        /// Adds documents using a custom IDocumentLoader.
        /// </summary>
        public RagBuilder AddDocuments(IDocumentLoader loader, string source)
        {
            if (loader == null)
                throw new ArgumentNullException(nameof(loader));

            return AddDocumentSource(
                async ct => await loader.LoadAsync(source, ct),
                kind: ResolveSourceKind(source));
        }

        /// <summary>
        /// Adds documents using a custom IDocumentLoader with a per-source text splitter.
        /// </summary>
        public RagBuilder AddDocuments(IDocumentLoader loader, string source, ITextSplitter textSplitter)
        {
            if (loader == null)
                throw new ArgumentNullException(nameof(loader));
            if (textSplitter == null)
                throw new ArgumentNullException(nameof(textSplitter));

            return AddDocumentSource(
                async ct => await loader.LoadAsync(source, ct),
                textSplitter: textSplitter,
                kind: ResolveSourceKind(source));
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

            // 3. Load and index all documents (single-file priority + per-source routing)
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderedSources = _documentSources
                .Select((source, index) => new { source, index })
                .OrderBy(entry => GetSourceOrder(entry.source.Kind))
                .ThenBy(entry => entry.index);

            foreach (var entry in orderedSources)
            {
                var source = entry.source;
                cancellationToken.ThrowIfCancellationRequested();
                var docs = await source.Loader(cancellationToken);
                if (docs.Count == 0)
                    continue;

                IReadOnlyList<RagDocument> docsToIndex = docs;
                if (source.Kind == DocumentSourceKind.SingleFile || source.Kind == DocumentSourceKind.Directory)
                    docsToIndex = FilterByProcessedPaths(docs, processedPaths);

                if (docsToIndex.Count == 0)
                    continue;

                if (source.TextSplitter != null)
                {
                    await pipeline.IndexDocumentsAsync(
                        docsToIndex,
                        source.TextSplitter,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await pipeline.IndexDocumentsAsync(docsToIndex, cancellationToken: cancellationToken);
                }

                if (source.Kind == DocumentSourceKind.SingleFile)
                    TrackProcessedPaths(docsToIndex, processedPaths);
            }

            return new RagStore(pipeline, vectorStore);
        }

        #endregion

        private static int GetSourceOrder(DocumentSourceKind kind)
        {
            return kind switch
            {
                DocumentSourceKind.SingleFile => 0,
                DocumentSourceKind.Directory => 1,
                _ => 2
            };
        }

        private static IReadOnlyList<RagDocument> FilterByProcessedPaths(
            IReadOnlyList<RagDocument> documents,
            HashSet<string> processedPaths)
        {
            if (documents.Count == 0 || processedPaths.Count == 0)
                return documents;

            List<RagDocument>? filtered = null;
            foreach (var doc in documents)
            {
                var normalized = TryNormalizePath(doc.Source);
                if (normalized != null && processedPaths.Contains(normalized))
                    continue;

                filtered ??= new List<RagDocument>();
                filtered.Add(doc);
            }

            return filtered ?? documents;
        }

        private static void TrackProcessedPaths(
            IReadOnlyList<RagDocument> documents,
            HashSet<string> processedPaths)
        {
            foreach (var doc in documents)
            {
                var normalized = TryNormalizePath(doc.Source);
                if (normalized != null)
                    processedPaths.Add(normalized);
            }
        }

        private static string? TryNormalizePath(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            try
            {
                return Path.GetFullPath(source);
            }
            catch
            {
                return null;
            }
        }
    }
}
