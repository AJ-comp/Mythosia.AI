# Mythosia.AI.VectorDB

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
- **Namespace isolation** — Filter by namespace for multi-tenant scenarios
- **Metadata filtering** — Filter search results by key-value metadata
- **Minimum score** — Discard results below a similarity threshold
- **Upsert** — Single and batch upsert operations
- **Collection management** — Create, check, and delete collections

## Standalone Usage

```csharp
using Mythosia.AI.VectorDB;

var store = new InMemoryVectorStore();

await store.CreateCollectionAsync("my-collection");

await store.UpsertAsync("my-collection", new VectorRecord
{
    Id = "doc-1",
    Content = "Some text content",
    Vector = new float[] { 0.1f, 0.2f, 0.3f },
    Metadata = { ["source"] = "manual.txt" }
});

var results = await store.SearchAsync("my-collection", queryVector, topK: 5);
```

## Limitations

- Data is **not persisted** — lost when the process exits
- Not suitable for large-scale production workloads (millions of vectors)
- For persistence or scale, implement a custom `IVectorStore` (e.g., Qdrant, Chroma, Pinecone)
