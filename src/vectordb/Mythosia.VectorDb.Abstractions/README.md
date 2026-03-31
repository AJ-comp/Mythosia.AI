# Mythosia.VectorDb.Abstractions

Core contracts for the **Mythosia VectorDb** abstraction layer.
Defines `IVectorStore`, all model types, and the metadata-based filtering API.
Consumed by `Mythosia.AI.Rag` and all concrete store implementations (InMemory, Postgres, Qdrant, Pinecone).

> **⚠ Deprecation Notice**
>
> `VectorRecord.Namespace`, `VectorRecord.Scope`, `VectorFilter.Namespace`, `VectorFilter.Scope`,
> `VectorFilter.WithNamespace()`, `INamespaceContext`, `IScopeContext`, and `InNamespace()` / `InScope()`
> are **deprecated** and will be removed in a future major version.
>
> **Use `Metadata` for logical isolation instead.** Store partition keys (namespace, scope, tenant, etc.)
> as metadata entries and filter them with `VectorFilter.Where("key", "value")`.
> This aligns with industry-standard vector database designs (Qdrant payload, Pinecone metadata, LangChain PGVector).
>
> ```csharp
> // Before (deprecated)
> record.Namespace = "docs";
> record.Scope = "tenant-1";
> var filter = new VectorFilter { Namespace = "docs", Scope = "tenant-1" };
>
> // After (recommended)
> record.Metadata["namespace"] = "docs";
> record.Metadata["scope"] = "tenant-1";
> var filter = new VectorFilter().Where("namespace", "docs").Where("scope", "tenant-1");
> ```

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
    Metadata  = new Dictionary<string, string>
    {
        ["source"]    = "manual.txt",
        ["namespace"] = "my-namespace",  // logical isolation via metadata
        ["scope"]     = "tenant-1"       // logical isolation via metadata
    }
};
```

| Property | Type | Description |
| --- | --- | --- |
| `Id` | `string` | Unique record ID (globally unique, GUID-based) |
| `Vector` | `float[]` | Embedding vector |
| `Content` | `string` | Original text (nullable in some stores) |
| `Metadata` | `Dictionary<string, string>` | Arbitrary key-value pairs for filtering/display |
| ~~`Namespace`~~ | `string?` | **Deprecated.** Use `Metadata["namespace"]` instead |
| ~~`Scope`~~ | `string?` | **Deprecated.** Use `Metadata["scope"]` instead |

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
// Recommended style — use Where() for all filtering including namespace/scope
var filter = new VectorFilter()
    .Where("namespace", "docs")
    .Where("scope", "tenant-1")
    .Where("lang", "ko")
    .WithMinScore(0.75);
```

| Property | Type | Description |
| --- | --- | --- |
| ~~`Namespace`~~ | `string?` | **Deprecated.** Use `.Where("namespace", value)` instead |
| ~~`Scope`~~ | `string?` | **Deprecated.** Use `.Where("scope", value)` instead |
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

## ~~Fluent API — `InNamespace` / `InScope`~~ (Deprecated)

> **⚠ Deprecated.** `InNamespace()` / `InScope()`, `INamespaceContext`, and `IScopeContext` will be removed in a future major version.
> Use `VectorFilter.Where()` and `Metadata` entries directly instead.

The following still works but produces compiler warnings:

```csharp
// Deprecated — still functional but will be removed
var ns = store.InNamespace("docs");
await ns.UpsertAsync(record);
```

**Recommended replacement:**

```csharp
var store = new InMemoryVectorStore();  // or PostgresStore, QdrantStore, PineconeStore

// Set isolation via Metadata
record.Metadata["namespace"] = "docs";
record.Metadata["scope"] = "tenant-1";
await store.UpsertAsync(record);

// Filter via Where()
var filter = new VectorFilter()
    .Where("namespace", "docs")
    .Where("scope", "tenant-1");
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);

// Atomic replace
var replaceFilter = new VectorFilter().Where("full_path", "/docs/file.md");
await store.ReplaceByFilterAsync(replaceFilter, newRecords);

// Delete by filter
await store.DeleteByFilterAsync(new VectorFilter().Where("namespace", "docs"));

// Count
long count = await store.CountAsync(new VectorFilter().Where("namespace", "docs"));
```

---

## `VectorFilter` in Practice

Top-level conditions are AND-combined. `MinScore` is ignored by `CountAsync` and `DeleteByFilterAsync`.

```csharp
// Exact match + isolation
var filter = new VectorFilter()
    .Where("storage_id", "abc")
    .Where("file_type", "pdf")
    .Where("namespace", "docs")
    .Where("scope", "tenant-1");

// Use directly
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);

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
