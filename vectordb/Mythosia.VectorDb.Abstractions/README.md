# Mythosia.VectorDb.Abstractions

Core contracts for the **Mythosia VectorDb** abstraction layer.
Defines `IVectorStore`, all model types, and the fluent `InNamespace` / `InScope` API.
Consumed by `Mythosia.AI.Rag` and all concrete store implementations (InMemory, Postgres, Qdrant, Pinecone).

### Filter operator coverage vs. industry libraries

| Operator | Pinecone | Weaviate¹ | Chroma | Semantic Kernel² | **Mythosia** |
| --- | :---: | :---: | :---: | :---: | :---: |
| `Eq` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `Ne` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `Gt / Gte` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `Lt / Lte` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `In` | ✓ | ✓ (v1.22+) | ✓ | ✓ | ✓ |
| `NotIn` | ✓ | — | ✓ | — | ✓ |
| `Like` | — | ✓ (`*` wildcard) | — | — | ✓ (`%` / `_`) |
| `Exists / NotExists` | — | — | — | — | ✓ |
| `And / Or groups` | ✓ | ✓ | ✓ | ✓ | ✓ |

> ¹ Weaviate `ContainsAny` (v1.22+) maps to `In`. Wildcard `Like` uses `*` not `%`.
> ² Semantic Kernel's `VectorSearchFilter` is a framework abstraction; operator availability depends on the underlying store connector.

---

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

Fluent criteria builder for scoping searches, gets, deletes, and counts. Top-level conditions are AND-combined by default. Use `.And()` / `.Or()` for explicit logical grouping.

#### Comparison operators

```csharp
var filter = new VectorFilter()
    .Where("category", "policy")               // Eq  — exact match
    .WhereNot("status", "archived")            // Ne
    .WhereGreaterThan("year", "2023")          // Gt
    .WhereGreaterThanOrEqual("year", "2023")   // Gte
    .WhereLessThan("priority", "5")            // Lt
    .WhereLessThanOrEqual("priority", "5")     // Lte
    .WhereIn("type", "pdf", "docx", "txt")     // In
    .WhereNotIn("lang", "zh", "ja")            // NotIn
    .WhereLike("title", "%report%")            // LIKE — % and _ wildcards
    .WhereExists("thumbnail")                  // key must be present
    .WhereNotExists("deleted_at");             // key must be absent
```

#### Logical grouping

```csharp
// OR group: (type = 'policy' OR type = 'manual')
var filter = new VectorFilter()
    .Or(g => g
        .Where("type", "policy")
        .Where("type", "manual")
    );

// Nested AND inside OR: (a=1 OR (b=2 AND c=3))
var filter = new VectorFilter()
    .Or(g => g
        .Where("a", "1")
        .And(inner => inner
            .Where("b", "2")
            .Where("c", "3")
        )
    );
```

#### First-class properties

```csharp
// Object-initializer style
var filter = new VectorFilter
{
    Namespace = "docs",    // first-tier isolation
    Scope     = "tenant-1",// second-tier isolation — set by IScopeContext automatically
    MinScore  = 0.7        // exclude results below this similarity score
};

// Fluent style
var filter = new VectorFilter()
    .Where("lang", "ko")
    .WithNamespace("docs")  // sets Namespace for chaining
    .WithMinScore(0.75);    // sets MinScore for chaining
```

| Property | Type | Description |
| --- | --- | --- |
| `Namespace` | `string?` | Filter by namespace (injected by `INamespaceContext`) |
| `Scope` | `string?` | Filter by scope (injected by `IScopeContext`) |
| `Conditions` | `IReadOnlyList<FilterCondition>` | Top-level condition tree (AND-combined) |
| `MinScore` | `double?` | Exclude results below this similarity score |

#### Operator support by store

| Operator | InMemory | Postgres | Qdrant | Pinecone |
| --- | :---: | :---: | :---: | :---: |
| `Eq` | ✓ | ✓ (JSONB `@>`) | ✓ | ✓ (`$eq`) |
| `Ne` | ✓ | ✓ | ✓ | ✓ (`$ne`) |
| `Gt / Gte / Lt / Lte` | ✓ | ✓ | — | ✓ (`$gt` etc.) |
| `In` | ✓ | ✓ (`= ANY(...)`) | ✓ | ✓ (`$in`) |
| `NotIn` | ✓ | ✓ | ✓ | ✓ (`$nin`) |
| `Like` | ✓ | ✓ (`LIKE`) | — | — |
| `Exists / NotExists` | ✓ | ✓ (`jsonb_exists`) | — | — |
| `And / Or groups` | ✓ | ✓ | ✓ | ✓ |

Qdrant and Pinecone silently skip unsupported operators during server-side filter translation; `MatchesFilter` in both stores evaluates all operators client-side for `GetAsync` / `GetBatchAsync`.

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
long filtered = await scoped.CountAsync(new VectorFilter().Where("category", "policy"));

// Atomic replace
await ns.ReplaceByFilterAsync(new VectorFilter().Where("full_path", "/docs/file.md"), newRecords);

// Delete all in scope (also available on INamespaceContext to delete all in a namespace)
await scoped.DeleteAllAsync();
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
| `DeleteAllAsync` | Deletes all records in this namespace |
| `InScope(scope)` | Returns `IScopeContext` |

### `IScopeContext` surface

All `INamespaceContext` operations plus scope injection, and additionally:

| Method | Description |
| --- | --- |
| `DeleteAllAsync` | Deletes every record in this namespace and scope |

---

## `VectorFilter` in Practice

Top-level conditions are AND-combined. `MinScore` is ignored by `CountAsync` and `DeleteByFilterAsync`.

```csharp
// Exact match + scope
var filter = new VectorFilter()
    .Where("storage_id", "abc")
    .Where("file_type", "pdf");

// Use directly
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);

// Use via fluent API (namespace + scope injected automatically)
var results = await store
    .InNamespace("docs")
    .InScope("tenant-1")
    .SearchAsync(queryVector, topK: 5, filter: filter);

// Multi-tenant permission pattern
var permFilter = new VectorFilter()
    .WhereIn("storage_id", allowedIds)
    .WhereLike("folder_path", "/shared/%");

// MinScore filtering
var highConf = new VectorFilter().Where("lang", "ko").WithMinScore(0.75);
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
