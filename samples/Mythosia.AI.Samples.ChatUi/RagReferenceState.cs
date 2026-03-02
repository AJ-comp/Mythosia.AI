using System;
using System.Collections.Generic;
using System.Linq;
using Mythosia.AI.Loaders;
using Mythosia.AI.Rag;

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
                config));
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

            var updated = Store.UpdateQuerySettings(settings.TopK, settings.MinScore, settings.PromptTemplate);
            if (updated && LastConfig != null)
            {
                LastConfig = LastConfig with
                {
                    TopK = settings.TopK,
                    MinScore = settings.MinScore,
                    PromptTemplate = settings.PromptTemplate
                };
            }

            return updated;
        }
    }

    public IReadOnlyList<RagReferenceHistoryEntry> GetHistory()
    {
        lock (_lock)
        {
            return _history.ToList();
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
    int TopK,
    double? MinScore,
    string? PromptTemplate);

public record RagPipelineSettings(
    int ChunkSize = 300,
    int ChunkOverlap = 30,
    string Chunker = "recursive",
    string EmbeddingProvider = "openai",
    string EmbeddingModel = "text-embedding-3-small",
    int EmbeddingDimensions = 1536,
    string EmbeddingBaseUrl = "http://localhost:11434",
    int TopK = 5,
    double? MinScore = 0.2,
    string? PromptTemplate = null);

public record RagReferenceHistoryEntry(
    Guid Id,
    DateTimeOffset CreatedAt,
    RagReferenceSummary Summary,
    IReadOnlyList<string> Sources,
    RagReferenceConfig Config);

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
    string? Namespace,
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
            record.Namespace,
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
    private readonly List<RagDocument> _documents;
    private readonly string _displaySource;

    public TrackingDocumentLoader(IDocumentLoader inner, List<RagDocument> documents, string displaySource)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _displaySource = displaySource;
    }

    public async Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken cancellationToken = default)
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

    public Task UpsertAsync(string collection, VectorRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        _records.Add(record);
        return _inner.UpsertAsync(collection, record, cancellationToken);
    }

    public Task UpsertBatchAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        var recordList = records?.ToList() ?? new List<VectorRecord>();
        _records.AddRange(recordList);
        return _inner.UpsertBatchAsync(collection, recordList, cancellationToken);
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string collection, float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken cancellationToken = default)
        => _inner.SearchAsync(collection, queryVector, topK, filter, cancellationToken);

    public Task<VectorRecord?> GetAsync(string collection, string id, CancellationToken cancellationToken = default)
        => _inner.GetAsync(collection, id, cancellationToken);

    public Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(collection, id, cancellationToken);

    public Task DeleteByFilterAsync(string collection, VectorFilter filter, CancellationToken cancellationToken = default)
        => _inner.DeleteByFilterAsync(collection, filter, cancellationToken);

    public Task<bool> CollectionExistsAsync(string collection, CancellationToken cancellationToken = default)
        => _inner.CollectionExistsAsync(collection, cancellationToken);

    public Task CreateCollectionAsync(string collection, CancellationToken cancellationToken = default)
        => _inner.CreateCollectionAsync(collection, cancellationToken);

    public Task DeleteCollectionAsync(string collection, CancellationToken cancellationToken = default)
        => _inner.DeleteCollectionAsync(collection, cancellationToken);
}
