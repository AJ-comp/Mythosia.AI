# Mythosia.VectorDb.Abstractions

Core contracts for the **Mythosia VectorDb** abstraction layer.
Defines `IVectorStore`, all model types, and the fluent `InNamespace` / `InScope` API.
Consumed by `Mythosia.AI.Rag` and all concrete store implementations (InMemory, Postgres, Qdrant, Pinecone).

## Installation

```bash
dotnet add package Mythosia.VectorDb.Abstractions
```

Install this package directly only when writing a **custom `IVectorStore` implementation** or when consuming the interface in a library. Applications normally take a transitive dependency through a concrete store package.

---

## Core Types

### `VectorRecord`

The unit of storage. Holds the embedding vector, content text, metadata, and isolation fields.

```csharp
var record = new VectorRecord
{
    Id        = "doc-1",
    Vector    = new float[] { 0.1f, 0.2f, 0.3f },
    Content   = "Original text content",
    Metadata  = new Dictionary<string, string> { ["source"] = "manual.txt" },
    Namespace = "my-namespace",   // 1st-tier isolation (optional)
    Scope     = "tenant-1"        // 2nd-tier isolation (optional)
};
```

| Property | Type | Description |
| --- | --- | --- |
| `Id` | `string` | Unique record ID (unique within a namespace) |
| `Vector` | `float[]` | Embedding vector |
| `Content` | `string` | Original text (nullable in some stores) |
| `Metadata` | `Dictionary<string, string>` | Arbitrary key-value pairs for filtering/display |
| `Namespace` | `string?` | First-tier logical partition |
| `Scope` | `string?` | Second-tier logical partition within a namespace |

---

### `VectorFilter`

Criteria for scoping searches, gets, deletes, and counts. All non-null fields are combined with AND logic.

```csharp
// Factory methods
var f1 = VectorFilter.ByNamespace("docs");
var f2 = VectorFilter.ByScope("tenant-1");
var f3 = VectorFilter.ByMetadata("source", "manual.txt");

// Combined filter (constructor)
var combined = new VectorFilter
{
    Namespace     = "docs",
    Scope         = "tenant-1",
    MetadataMatch = new Dictionary<string, string>
    {
        ["source"]   = "manual.txt",
        ["category"] = "policy"
    },
    MinScore = 0.7
};
```

| Property | Type | Description |
| --- | --- | --- |
| `Namespace` | `string?` | Filter by namespace |
| `Scope` | `string?` | Filter by scope |
| `MetadataMatch` | `Dictionary<string, string>?` | All pairs must match (AND) |
| `MinScore` | `double?` | Exclude results below this similarity score |

---

### `VectorSearchResult`

A single result from `SearchAsync` or `HybridSearchAsync`.

```csharp
foreach (var result in results)
{
    Console.WriteLine($"Score: {result.Score:F4}");
    Console.WriteLine($"Content: {result.Record.Content}");
}
```

---

## `IVectorStore` Contract

Full interface surface area:

```csharp
public interface IVectorStore
{
    // Write
    Task UpsertAsync(VectorRecord record, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default);

    // Read — single
    Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken ct = default);

    // Read — batch  (default: sequential GetAsync fallback)
    Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids, VectorFilter? filter = null, CancellationToken ct = default);

    // Search — dense vector only
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken ct = default);

    // Search — hybrid dense + keyword  (default: throws NotSupportedException)
    Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(float[] denseVector, string query, int topK = 5, VectorFilter? filter = null, CancellationToken ct = default);

    // Delete
    Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken ct = default);
    Task DeleteByFilterAsync(VectorFilter filter, CancellationToken ct = default);

    // Atomic replace  (default: DeleteByFilterAsync → UpsertBatchAsync, non-transactional)
    Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records, CancellationToken ct = default);

    // Count  (default: throws NotSupportedException)
    Task<long> CountAsync(VectorFilter? filter = null, CancellationToken ct = default);

    // Connectivity
    Task VerifyConnectionAsync(CancellationToken ct = default);
}
```

### Default implementations

| Method | Default behavior |
| --- | --- |
| `HybridSearchAsync` | Throws `NotSupportedException` |
| `GetBatchAsync` | Sequential loop over `GetAsync` |
| `ReplaceByFilterAsync` | `DeleteByFilterAsync` → `UpsertBatchAsync` (non-transactional) |
| `CountAsync` | Throws `NotSupportedException` |
| `VerifyConnectionAsync` | `Task.CompletedTask` (no-op) |

Concrete stores override these defaults where a more efficient or transactional implementation is available.

---

## Fluent API — `InNamespace` / `InScope`

The recommended way to use a store with fixed namespace and/or scope. Namespace and scope are injected automatically into every record and filter.

```csharp
var store = new InMemoryVectorStore();  // or PostgresStore, QdrantStore, PineconeStore

// Namespace-only
var ns = store.InNamespace("docs");
await ns.UpsertAsync(record);                          // record.Namespace = "docs"
var results = await ns.SearchAsync(queryVector, topK: 5);

// Namespace + scope
var scoped = store.InNamespace("docs").InScope("tenant-1");
await scoped.UpsertAsync(record);                      // record.Namespace = "docs", record.Scope = "tenant-1"
var results = await scoped.SearchAsync(queryVector);

// Hybrid search
var results = await scoped.HybridSearchAsync(queryVector, "keyword query", topK: 10);

// Get batch
var records = await ns.GetBatchAsync(new[] { "id-1", "id-2", "id-3" });

// Count
long count = await ns.CountAsync();
long filtered = await scoped.CountAsync(VectorFilter.ByMetadata("category", "policy"));

// Atomic replace
await ns.ReplaceByFilterAsync(VectorFilter.ByMetadata("full_path", "/docs/file.md"), newRecords);

// Delete all in scope
await scoped.DeleteAllAsync();    // IScopeContext only
```

### `INamespaceContext` surface

| Method | Injects |
| --- | --- |
| `UpsertAsync` | `record.Namespace` |
| `UpsertBatchAsync` | `record.Namespace` on each record |
| `SearchAsync` | `filter.Namespace` |
| `HybridSearchAsync` | `filter.Namespace` |
| `GetAsync` | `filter.Namespace` |
| `GetBatchAsync` | `filter.Namespace` |
| `DeleteAsync` | `filter.Namespace` |
| `DeleteByFilterAsync` | `filter.Namespace` |
| `ReplaceByFilterAsync` | `record.Namespace` + `filter.Namespace` |
| `CountAsync` | `filter.Namespace` |
| `InScope(scope)` | Returns `IScopeContext` |

### `IScopeContext` surface

All `INamespaceContext` operations plus scope injection, and additionally:

| Method | Description |
| --- | --- |
| `DeleteAllAsync` | Deletes every record in this namespace and scope |

---

## `VectorFilter` in Practice

All filter conditions are AND-combined. `MinScore` is ignored by `CountAsync` and `DeleteByFilterAsync`.

```csharp
// Metadata AND scope
var filter = new VectorFilter
{
    Scope         = "tenant-1",
    MetadataMatch = new Dictionary<string, string>
    {
        ["storage_id"] = "abc",
        ["file_type"]  = "pdf"
    }
};

// Use directly
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);

// Use via fluent API (namespace injected automatically)
var results = await store.InNamespace("docs").SearchAsync(queryVector, topK: 5, filter: filter);
```

---

## `Bm25Tokenizer`

Static utility backed by Lucene.Net `StandardAnalyzer`. Used internally by `Bm25Index` (InMemory) and sparse vector builders (Qdrant, Pinecone).

```csharp
using Mythosia.VectorDb;

var result = Bm25Tokenizer.Analyze("machine learning neural network");
// result.Tokens           → ["machine", "learning", "neural", "network"]
// result.TermFrequencies  → { "machine": 1, "learning": 1, ... }
```

---

## Implementing a Custom `IVectorStore`

```csharp
public class MyVectorStore : IVectorStore
{
    public Task UpsertAsync(VectorRecord record, CancellationToken ct = default)
        => /* store record */ Task.CompletedTask;

    public Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default)
        => /* store batch */ Task.CompletedTask;

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int topK = 5, VectorFilter? filter = null, CancellationToken ct = default)
        => /* cosine search */ Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());

    public Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null, CancellationToken ct = default)
        => /* lookup */ Task.FromResult<VectorRecord?>(null);

    public Task DeleteAsync(string id, VectorFilter? filter = null, CancellationToken ct = default)
        => /* delete */ Task.CompletedTask;

    public Task DeleteByFilterAsync(VectorFilter filter, CancellationToken ct = default)
        => /* filter delete */ Task.CompletedTask;

    // Optional overrides for efficiency / transactional guarantees:
    // public override Task<IReadOnlyList<VectorRecord>> GetBatchAsync(...)  { ... }
    // public override Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(...) { ... }
    // public override Task ReplaceByFilterAsync(...) { ... }
    // public override Task<long> CountAsync(...) { ... }
    // public override Task VerifyConnectionAsync(...) { ... }
}
```

The store is then usable with the fluent API automatically:

```csharp
var store = new MyVectorStore();
await store.InNamespace("docs").UpsertAsync(record);
var results = await store.InNamespace("docs").SearchAsync(queryVector);
```
