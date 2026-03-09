# Mythosia.VectorDb.InMemory

## Migration from v1.0.0

v2.0.0 renames logical separation units:

- **`collection` → `namespace`** (terminology update in public API/docs)
- **`namespace` → `scope`** (for second-tier logical isolation)

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
- **Scope isolation** — Filter by scope for multi-tenant scenarios
- **Metadata filtering** — Filter search results by key-value metadata
- **Minimum score** — Discard results below a similarity threshold
- **Upsert** — Single and batch upsert operations
- **Namespace-aware operations** — Use `InNamespace(...)` or `VectorFilter.Namespace`

## Standalone Usage

### Fluent API (recommended)

```csharp
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
var ns = store.InNamespace("my-namespace");

// Namespace-only
await ns.UpsertAsync(new VectorRecord
{
    Id = "doc-1",
    Content = "Some text content",
    Vector = new float[] { 0.1f, 0.2f, 0.3f },
    Metadata = { ["source"] = "manual.txt" }
});

var results = await ns.SearchAsync(queryVector, topK: 5);

// Namespace + Scope
var scoped = ns.InScope("tenant-1");
await scoped.UpsertAsync(record);   // record.Scope is set automatically
var scopedResults = await scoped.SearchAsync(queryVector);
```

### Direct `IVectorStore` API

```csharp
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();

await store.UpsertAsync(new VectorRecord
{
    Namespace = "my-namespace",
    Id = "doc-1",
    Content = "Some text content",
    Vector = new float[] { 0.1f, 0.2f, 0.3f },
    Metadata = { ["source"] = "manual.txt" }
});

var results = await store.SearchAsync(
    queryVector,
    topK: 5,
    filter: VectorFilter.ByNamespace("my-namespace"));
```

## BM25 Index (v2.1.0)

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

## Limitations

- Data is **not persisted** — lost when the process exits
- Not suitable for large-scale production workloads (millions of vectors)
- For persistence or scale, implement a custom `IVectorStore` (e.g., Qdrant, Chroma, Pinecone)
