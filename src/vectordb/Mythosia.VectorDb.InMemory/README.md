# Mythosia.VectorDb.InMemory

## Package Summary

Provides `InMemoryVectorStore`, a thread-safe in-memory implementation of `IVectorStore` using cosine similarity search.  
Suitable for development, testing, and small-scale workloads.

## Usage

Automatically used as the default vector store in `Mythosia.AI.Rag`:

```csharp
// Explicit selection (same as default)
.WithRag(rag => rag
    .AddDocument("docs.txt")
    .UseInMemoryStore()
)
```

## Features

- **Thread-safe** — Uses `ConcurrentDictionary` for safe concurrent access
- **Cosine similarity** — TopK search with configurable result count
- **Hybrid search** — BM25 + dense vector fusion via weighted RRF, scores normalized to `[0, 1]`
- **Metadata filtering** — Full `VectorFilter` operator set (Eq/Ne/In/NotIn/Gt/Gte/Lt/Lte/Like/Exists/NotExists, And/Or groups)
- **Minimum score** — Discard results below a similarity threshold
- **Upsert** — Single and batch upsert operations
- **CountAsync** — Count records, optionally narrowed by filter criteria
- **Diagnostics** — `IRagDiagnosticsStore`: `ListAllRecordsAsync`, `ScoredListAsync`, `GetTotalRecordCount`

## Standalone Usage

### Recommended — Metadata-based isolation

```csharp
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();

await store.UpsertAsync(new VectorRecord
{
    Id = "doc-1",
    Content = "Some text content",
    Vector = new float[] { 0.1f, 0.2f, 0.3f },
    Metadata =
    {
        ["source"] = "manual.txt",
        ["namespace"] = "my-namespace",
        ["scope"] = "tenant-1"
    }
});

var filter = new VectorFilter()
    .Where("namespace", "my-namespace")
    .Where("scope", "tenant-1");
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

## BM25 Index

`Bm25Index` provides in-memory BM25 keyword search for hybrid retrieval. When `UseHybridSearch()` is called with `InMemoryVectorStore`, the RAG pipeline automatically builds a BM25 index alongside the vector store and merges results via RRF.

```csharp
// Automatic — just enable hybrid search in the builder
var store = await RagStore.BuildAsync(config => config
    .AddText("환불은 14일 이내 가능합니다.", id: "refund")
    .UseLocalEmbedding(512)
    .UseInMemoryStore()
    .UseHybridSearch()     // BM25 index is built automatically
);
```

Standalone usage:

```csharp
using Mythosia.VectorDb.InMemory;

var bm25 = new Bm25Index();
bm25.Index("doc1", "machine learning neural network");
bm25.Index("doc2", "cooking recipe pasta");

var results = bm25.Search("machine learning", topK: 5);
// results[0].Id == "doc1", results[0].Score > 0
```

When hybrid search is used, fused RRF scores are normalized to the `[0, 1]` range so `VectorFilter.MinScore` is applied consistently to the final merged score.

## VectorFilter

For the full operator reference and fluent API examples (`Where`, `WhereNot`, `WhereIn`, `WhereLike`, `WhereExists`, `Or`, `And`, `WithMinScore`, etc.), see the [Mythosia.VectorDb.Abstractions README](../Mythosia.VectorDb.Abstractions/README.md#vectorfilter).

> **InMemory-specific note**: Range operators (`WhereGreaterThan`, `WhereLessThan`, etc.) use `string.Compare` (ordinal). Store numeric values zero-padded (e.g. `"0042"`) for correct ordering.

## Batch Get & Count

```csharp
// Fetch multiple records by ID in one call
var filter = new VectorFilter().Where("namespace", "docs");
var records = await store.GetBatchAsync(new[] { "id-1", "id-2", "id-3" }, filter);

// Count all records matching a filter
long count = await store.CountAsync(new VectorFilter().Where("namespace", "docs"));

// Count with additional metadata filter
long filtered = await store.CountAsync(
    new VectorFilter().Where("namespace", "docs").Where("storage_id", storageId));
```

`GetBatchAsync` performs O(1)-per-ID lookups via `ConcurrentDictionary.TryGetValue` — no vector scoring, just direct key access. Records not found or not matching the filter are omitted.

## Resource Disposal

`InMemoryVectorStore` implements `IDisposable`. A `Bm25Index` (Lucene writer, analyzer, RAMDirectory) is maintained alongside the vector store. Dispose the store when it is no longer needed to release these resources:

```csharp
using var store = new InMemoryVectorStore();
// ... use store
// Lucene resources released on Dispose
```

## Vector Replacement

`ReplaceByFilterAsync` is available via the `IVectorStore` default interface method. It performs sequential `DeleteByFilterAsync` → `UpsertBatchAsync` (non-transactional, suitable for in-memory usage):

```csharp
IVectorStore store = new InMemoryVectorStore();

var filter = new VectorFilter()
    .Where("full_path", "/docs/file.md");

await store.ReplaceByFilterAsync(filter, newRecords);
```

For transactional guarantees (zero query gap), use `PostgresStore` which wraps both operations in a single database transaction.

## Limitations

- Data is **not persisted** — lost when the process exits
- Not suitable for large-scale production workloads (millions of vectors)
- For persistence or scale, implement a custom `IVectorStore` (e.g., Qdrant, Chroma, Pinecone)
