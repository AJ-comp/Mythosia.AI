using Mythosia.Documents;
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

namespace Mythosia.AI.Samples.ChatUi;

public sealed class RagReferenceState
{
    private readonly object _lock = new();
    private const int MaxHistory = 20;
    private readonly List<RagReferenceHistoryEntry> _history = new();
    private RagPipelineSettings _settings = new();

    public RagStore? Store { get; private set; }
    public RagReferenceTrace? LastTrace { get; private set; }
    public RagReferenceConfig? LastConfig { get; private set; }
    public DateTimeOffset? LastUpdated { get; private set; }

    public void Update(RagStore store, RagReferenceTrace trace, RagReferenceConfig config)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (trace == null) throw new ArgumentNullException(nameof(trace));
        if (config == null) throw new ArgumentNullException(nameof(config));

        lock (_lock)
        {
            Store = store;
            LastTrace = trace;
            LastConfig = config;
            var updatedAt = DateTimeOffset.UtcNow;
            LastUpdated = updatedAt;
            _history.Insert(0, new RagReferenceHistoryEntry(
                Guid.NewGuid(),
                updatedAt,
                trace.Summary,
                config.Sources.ToList(),
                config,
                trace));
            if (_history.Count > MaxHistory)
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
        }
    }

    public bool TryGetSnapshot(out RagReferenceTrace? trace, out RagReferenceConfig? config)
    {
        lock (_lock)
        {
            trace = LastTrace;
            config = LastConfig;
            return trace != null && config != null;
        }
    }

    public RagPipelineSettings GetSettings()
    {
        lock (_lock)
        {
            return _settings;
        }
    }

    public void UpdateSettings(RagPipelineSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        lock (_lock)
        {
            _settings = settings;
        }
    }

    public bool TryApplyQuerySettings(RagPipelineSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        lock (_lock)
        {
            if (Store == null)
                return false;

            var updated = Store.UpdateOptions(opt =>
            {
                if (settings.FinalFilter.TopK > 0) opt.DefaultQuery.FinalFilter.TopK = settings.FinalFilter.TopK;
                opt.DefaultQuery.FinalFilter.MinScore = settings.FinalFilter.MinScore;
                if (settings.RetrievalDerivation.TopKMultiplier > 0) opt.DefaultQuery.RetrievalDerivation.TopKMultiplier = settings.RetrievalDerivation.TopKMultiplier;
                if (settings.RetrievalDerivation.MinScoreDivider > 0d) opt.DefaultQuery.RetrievalDerivation.MinScoreDivider = settings.RetrievalDerivation.MinScoreDivider;
                opt.DefaultQuery.FinalSelection = new RagFinalSelectionOptions
                {
                    Mode = settings.FinalSelectionMode,
                    RetrievalWeight = settings.FinalSelectionRetrievalWeight
                };
                opt.PromptTemplate = settings.PromptTemplate;
            });

            // Switch retrieval strategy at runtime when hybrid search is toggled
            Store.UpdateRetrievalStrategy(settings.HybridSearchEnabled, settings.HybridSearchVectorWeight);

            if (updated && LastConfig != null)
            {
                LastConfig = LastConfig with
                {
                    FinalFilter = settings.FinalFilter,
                    RetrievalDerivation = settings.RetrievalDerivation,
                    PromptTemplate = settings.PromptTemplate
                };
            }

            return updated;
        }
    }

    /// <summary>
    /// Sets the RAG store for an external database connection (e.g., PostgreSQL)
    /// without requiring a full document upload trace.
    /// </summary>
    public void SetExternalStore(RagStore store)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        lock (_lock)
        {
            Store = store;
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Clears the RAG store (e.g., when disconnecting from an external database).
    /// </summary>
    public void ClearStore()
    {
        lock (_lock)
        {
            Store = null;
        }
    }

    public bool HasStore
    {
        get { lock (_lock) { return Store != null; } }
    }

    public IReadOnlyList<RagReferenceHistoryEntry> GetHistory()
    {
        lock (_lock)
        {
            return _history.ToList();
        }
    }

    public RagReferenceTrace? GetHistoryTrace(Guid id)
    {
        lock (_lock)
        {
            return _history.FirstOrDefault(e => e.Id == id)?.Trace;
        }
    }
}

public record RagReferenceTrace(
    RagReferenceSummary Summary,
    IReadOnlyList<RagReferenceDocument> Documents,
    IReadOnlyList<RagReferenceChunk> Chunks,
    IReadOnlyList<RagReferenceEmbedding> Embeddings,
    IReadOnlyList<RagReferenceRecord> Records);

public record RagReferenceSummary(
    int DocumentCount,
    int ChunkCount,
    int EmbeddingCount,
    int RecordCount,
    int Dimensions);

public record RagReferenceConfig(
    IReadOnlyList<string> Sources,
    int ChunkSize,
    int ChunkOverlap,
    string Chunker,
    string EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string EmbeddingBaseUrl,
    RagFilter FinalFilter,
    RagRetrievalDerivation RetrievalDerivation,
    string? PromptTemplate);

public record RagPipelineSettings(
    int ChunkSize = 300,
    int ChunkOverlap = 30,
    string Chunker = "recursive",
    string EmbeddingProvider = "",
    string EmbeddingModel = "",
    int EmbeddingDimensions = 0,
    string EmbeddingBaseUrl = "",
    RagFilter FinalFilter = null!,
    RagRetrievalDerivation RetrievalDerivation = null!,
    string? PromptTemplate = null,
    bool QueryRewriterEnabled = true,
    int QueryRewriteMaxTokens = 250,
    bool ExtractKeywords = true,
    string? RewriterModelOverride = null,
    bool HybridSearchEnabled = true,
    float HybridSearchVectorWeight = 0.5f,
    bool RerankEnabled = false,
    string RerankProvider = "",
    string RerankModel = "",
    string RerankBaseUrl = "",
    string? RerankApiKey = null,
    RagFinalSelectionMode FinalSelectionMode = RagFinalSelectionMode.RerankerOnly,
    double FinalSelectionRetrievalWeight = RagFinalSelectionOptions.DefaultRetrievalWeight)
{
    public RagPipelineSettings()
        : this(
            ChunkSize: 300,
            ChunkOverlap: 30,
            Chunker: "recursive",
            EmbeddingProvider: "",
            EmbeddingModel: "",
            EmbeddingDimensions: 0,
            EmbeddingBaseUrl: "",
            FinalFilter: new RagFilter { TopK = 5, MinScore = 0.2 },
            RetrievalDerivation: new RagRetrievalDerivation(),
            PromptTemplate: null,
            QueryRewriterEnabled: true,
            QueryRewriteMaxTokens: 250,
            ExtractKeywords: true,
            RewriterModelOverride: null,
            HybridSearchEnabled: true,
            HybridSearchVectorWeight: 0.5f,
            RerankEnabled: false,
            RerankProvider: "",
            RerankModel: "",
            RerankBaseUrl: "",
            RerankApiKey: null,
            FinalSelectionMode: RagFinalSelectionMode.RerankerOnly,
            FinalSelectionRetrievalWeight: RagFinalSelectionOptions.DefaultRetrievalWeight)
    {
    }
}

public record RagReferenceHistoryEntry(
    Guid Id,
    DateTimeOffset CreatedAt,
    RagReferenceSummary Summary,
    IReadOnlyList<string> Sources,
    RagReferenceConfig Config,
    RagReferenceTrace Trace);

public record RagReferenceDocument(
    string Id,
    string Source,
    int ContentLength,
    string Preview,
    IReadOnlyDictionary<string, string> Metadata);

public record RagReferenceChunk(
    string Id,
    string DocumentId,
    int Index,
    int ContentLength,
    string Content,
    IReadOnlyDictionary<string, string> Metadata);

public record RagReferenceEmbedding(
    string ChunkId,
    int Dimensions,
    IReadOnlyList<float> Sample,
    IReadOnlyList<float> Vector);

public record RagReferenceRecord(
    string Id,
    string? Scope,
    int ContentLength,
    string Content,
    IReadOnlyDictionary<string, string> Metadata);

internal static class RagReferenceTraceBuilder
{
    private const int PreviewLength = 280;

    public static RagReferenceTrace Build(
        IReadOnlyList<RagDocument> documents,
        IReadOnlyList<RagChunk> chunks,
        IReadOnlyList<VectorRecord> records,
        int defaultDimensions)
    {
        var documentItems = documents.Select(doc => new RagReferenceDocument(
            doc.Id,
            doc.Source,
            doc.Content?.Length ?? 0,
            BuildPreview(doc.Content ?? string.Empty),
            new Dictionary<string, string>(doc.Metadata))).ToList();

        var chunkItems = chunks.Select(chunk => new RagReferenceChunk(
            chunk.Id,
            chunk.DocumentId,
            chunk.Index,
            chunk.Content?.Length ?? 0,
            chunk.Content ?? string.Empty,
            new Dictionary<string, string>(chunk.Metadata))).ToList();

        var recordItems = records.Select(record => new RagReferenceRecord(
            record.Id,
            record.Metadata.TryGetValue("scope", out var scope) ? scope : null,
            record.Content?.Length ?? 0,
            record.Content ?? string.Empty,
            new Dictionary<string, string>(record.Metadata))).ToList();

        var embeddingItems = records.Select(record =>
        {
            var vector = record.Vector ?? Array.Empty<float>();
            var sample = vector.Length <= 8 ? vector.ToArray() : vector.Take(8).ToArray();
            var dimensions = vector.Length > 0 ? vector.Length : defaultDimensions;
            return new RagReferenceEmbedding(record.Id, dimensions, sample, vector);
        }).ToList();

        var summary = new RagReferenceSummary(
            documentItems.Count,
            chunkItems.Count,
            embeddingItems.Count,
            recordItems.Count,
            embeddingItems.FirstOrDefault()?.Dimensions ?? defaultDimensions);

        return new RagReferenceTrace(summary, documentItems, chunkItems, embeddingItems, recordItems);
    }

    private static string BuildPreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var trimmed = content.Trim();
        return trimmed.Length <= PreviewLength
            ? trimmed
            : trimmed.Substring(0, PreviewLength) + "...";
    }
}

internal sealed class TrackingDocumentLoader : IDocumentLoader
{
    private readonly IDocumentLoader _inner;
    private readonly List<DoclingDocument> _documents;
    private readonly string _displaySource;

    public TrackingDocumentLoader(IDocumentLoader inner, List<DoclingDocument> documents, string displaySource)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _displaySource = displaySource;
    }

    public async Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        var docs = await _inner.LoadAsync(source, cancellationToken);
        foreach (var doc in docs)
        {
            doc.Source = _displaySource;
        }

        _documents.AddRange(docs);
        return docs;
    }
}

internal sealed class TrackingTextSplitter : ITextSplitter
{
    private readonly ITextSplitter _inner;
    private readonly List<RagChunk> _chunks;

    public TrackingTextSplitter(ITextSplitter inner, List<RagChunk> chunks)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    }

    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var result = _inner.Split(document);
        _chunks.AddRange(result);
        return result;
    }
}

internal sealed class TrackingVectorStore : IVectorStore
{
    private readonly IVectorStore _inner;
    private readonly List<VectorRecord> _records;

    public TrackingVectorStore(IVectorStore inner, List<VectorRecord> records)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _records = records ?? throw new ArgumentNullException(nameof(records));
    }

    public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        _records.Add(record);
        return _inner.UpsertAsync(record, cancellationToken);
    }

    public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        var recordList = records?.ToList() ?? new List<VectorRecord>();
        _records.AddRange(recordList);
        return _inner.UpsertBatchAsync(recordList, cancellationToken);
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => _inner.SearchAsync(queryVector, topK, filter, cancellationToken);

    public Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => _inner.HybridSearchAsync(denseVector, query, topK, filter, cancellationToken);

    public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => _inner.GetAsync(id, filter, cancellationToken);

    public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(id, filter, cancellationToken);

    public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default)
        => _inner.DeleteByFilterAsync(filter, cancellationToken);
}
