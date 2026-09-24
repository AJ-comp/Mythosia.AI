# Mythosia.VectorDb.InMemory

> **v4.2.0:** Includes text-only and configurable hybrid search. See the [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.InMemory/RELEASE_NOTES.md#v420) for compatibility and fixes.

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

- **Consistent concurrent access** — Synchronizes record storage and the BM25 index across writes, deletes and reads; both hybrid search legs use the same state
- **Independent records** — Copies input and returned records, including vector arrays and metadata
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

## Concurrent Updates and Record Ownership

A query must not return a new document body with keyword matches from its old contents. The store synchronizes writes, deletes and reads so every vector, text or hybrid query sees a consistent state. Both legs of a hybrid query run against that same state. Separate API calls may see later writes.

The store copies incoming records, including vector arrays and metadata. Lookups, search results and diagnostic methods return independent copies. Keep input records, vectors and metadata unchanged while a call copies or reads them. Editing an input object or a returned record after the call does not change stored data; save edits explicitly:

```csharp
var record = await store.GetAsync("doc-1");
if (record is not null)
{
    record.Content = "Updated text content";
    record.Vector = await embeddingService.GetEmbeddingAsync(record.Content);
    await store.UpsertAsync(record);
}
```

A supplied `CancellationToken` can cancel a call while it waits for another operation to release the store lock. Canceling that wait does not itself abort the operation currently using the store. Once a record update has begun, cancellation does not interrupt it between updating the body and keyword index.

Cancellation is also checked before writes and between records in batch writes. A canceled batch can retain records already written; their bodies and keyword index entries remain consistent. This is not a transaction or a rollback guarantee for the whole batch.

## BM25 Index

`Bm25Index` provides in-memory BM25 keyword search for hybrid retrieval. `InMemoryVectorStore` maintains its BM25 index alongside stored vectors. `TextSearchAsync` uses it without a dense query vector; configurable `HybridSearchAsync` combines it with dense results through normalized weighted RRF. Selecting a query mode does not change document ingestion.

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

When hybrid search is used, fused RRF scores are normalized to `[0, 1]`, including a single active leg or no keyword matches. `VectorFilter.MinScore` applies after fusion, while metadata filters restrict candidates before top-K. `HybridSearchOptions` controls vector weight, candidate multiplier and RRF smoothing. Pure `TextSearchAsync` returns native BM25 scores, which are not interchangeable with vector or fusion scores.

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

`ReplaceByFilterAsync` is available via the `IVectorStore` default interface method. It performs sequential `DeleteByFilterAsync` → `UpsertBatchAsync` (non-transactional):

```csharp
IVectorStore store = new InMemoryVectorStore();

var filter = new VectorFilter()
    .Where("full_path", "/docs/file.md");

await store.ReplaceByFilterAsync(filter, newRecords);
```

A query can observe the gap between deletion and insertion. Failure or cancellation can leave a partial replacement; completed writes are not rolled back. Synchronizing each operation keeps records and the BM25 index consistent, but does not make the whole replacement transactional. For transactional replacement, use `PostgresStore`, which wraps both operations in a single database transaction.

## Limitations

- Data is **not persisted** — lost when the process exits
- Not suitable for large-scale production workloads (millions of vectors)
- For persistence or scale, implement a custom `IVectorStore` (e.g., Qdrant, Chroma, Pinecone)
