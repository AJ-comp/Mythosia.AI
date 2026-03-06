# Mythosia.VectorDb.InMemory

## Migration from v1.0.0

v10.1.0 renames logical separation units:

- **`collection` → `namespace`**: All `IVectorStore` method parameters and `CollectionExistsAsync` / `CreateCollectionAsync` / `DeleteCollectionAsync` → `NamespaceExistsAsync` / `CreateNamespaceAsync` / `DeleteNamespaceAsync`.
- **`namespace` → `scope`**: `VectorRecord.Namespace` → `VectorRecord.Scope`, `VectorFilter.ByNamespace(...)` → `VectorFilter.ByScope(...)`.

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
- **Namespace management** — Create, check, and delete namespaces

## Standalone Usage

### Fluent API (recommended)

```csharp
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
var ns = store.InNamespace("my-namespace");

await ns.CreateAsync();

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

### Legacy (flat) API

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();

await store.CreateNamespaceAsync("my-namespace");

await store.UpsertAsync("my-namespace", new VectorRecord
{
    Id = "doc-1",
    Content = "Some text content",
    Vector = new float[] { 0.1f, 0.2f, 0.3f },
    Metadata = { ["source"] = "manual.txt" }
});

var results = await store.SearchAsync("my-namespace", queryVector, topK: 5);
```

## Limitations

- Data is **not persisted** — lost when the process exits
- Not suitable for large-scale production workloads (millions of vectors)
- For persistence or scale, implement a custom `IVectorStore` (e.g., Qdrant, Chroma, Pinecone)
