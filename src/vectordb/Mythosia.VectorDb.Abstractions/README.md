# Mythosia.VectorDb.Abstractions

> **v4.1.0:** Includes text-only and configurable hybrid search. See the [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/vectordb/Mythosia.VectorDb.Abstractions/RELEASE_NOTES.md#v410) for compatibility and fixes.

Core contracts for the **Mythosia VectorDb** abstraction layer.
Defines `IVectorStore`, all model types, and the metadata-based filtering API.
Consumed by `Mythosia.AI.Rag` and all concrete store implementations (InMemory, Postgres, Qdrant, Pinecone).

> **Breaking change in v4.0.0**
>
> `VectorRecord.Namespace`, `VectorRecord.Scope`, `VectorFilter.Namespace`, `VectorFilter.Scope`,
> `VectorFilter.WithNamespace()`, `INamespaceContext`, `IScopeContext`, and `InNamespace()` / `InScope()`
> were **removed in v4.0.0**. Calls to these APIs must be migrated before upgrading.
>
> **Use `Metadata` for logical isolation instead.** Store partition keys (namespace, scope, tenant, etc.)
> as metadata entries and filter them with `VectorFilter.Where("key", "value")`.
> This aligns with industry-standard vector database designs (Qdrant payload, Pinecone metadata, LangChain PGVector).
>
> ```csharp
> // Before v4.0.0 (historical example; these APIs no longer compile)
> record.Namespace = "docs";
> record.Scope = "tenant-1";
> var filter = new VectorFilter { Namespace = "docs", Scope = "tenant-1" };
>
> // v4.0.0 and later
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

The unit of storage. Holds the embedding vector, content text, and metadata. Store logical isolation keys in metadata.

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

#### Metadata isolation and score filtering

```csharp
// Use Where() for metadata filtering, including namespace/scope
var filter = new VectorFilter()
    .Where("namespace", "docs")
    .Where("scope", "tenant-1")
    .Where("lang", "ko")
    .WithMinScore(0.75);
```

| Property | Type | Description |
| --- | --- | --- |
| `Conditions` | `IReadOnlyList<FilterCondition>` | Top-level condition tree (AND-combined) |
| `MinScore` | `double?` | Exclude search results below this score; its scale depends on the search mode |

#### Operator support for vector and legacy hybrid search

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

Qdrant and Pinecone silently skip unsupported operators during server-side filter translation for `SearchAsync` and the `HybridSearchAsync` overload without `HybridSearchOptions`; `MatchesFilter` in both stores evaluates all operators client-side for `GetAsync` / `GetBatchAsync`.

The Qdrant 4.2.0 text/configurable-hybrid paths also support `Exists` / `NotExists` and reject unsupported range/`Like` filters instead of skipping them.

---

### `VectorSearchResult`

A single result from vector, text, or hybrid search. Score scales depend on the backend and search mode; configurable hybrid search uses normalized weighted RRF scores in `[0, 1]`.

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

## Logical Isolation with Metadata

`InNamespace()` / `InScope()`, `INamespaceContext`, and `IScopeContext` were removed in v4.0.0. Set partition keys in `Metadata` and apply matching `VectorFilter` conditions on each operation:

```csharp
IVectorStore store = new InMemoryVectorStore();  // or PostgresStore, QdrantStore, PineconeStore

// Set isolation via Metadata
record.Metadata["namespace"] = "docs";
record.Metadata["scope"] = "tenant-1";
await store.UpsertAsync(record);

// Filter via Where()
var filter = new VectorFilter()
    .Where("namespace", "docs")
    .Where("scope", "tenant-1");
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);

// Replace matching records (atomicity depends on the store)
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

The custom store uses the same metadata and filter contracts:

```csharp
IVectorStore store = new MyVectorStore();
record.Metadata["namespace"] = "docs";
await store.UpsertAsync(record);
var results = await store.SearchAsync(
    queryVector, filter: new VectorFilter().Where("namespace", "docs"));
```

## Optional text and configurable hybrid search

A text-only query should not require a dummy dense vector, and hybrid settings should reach the backend unchanged. These optional contracts extend `IVectorStore` without adding requirements to existing store implementations:

- `ITextSearchStore.TextSearchAsync(query, topK, filter, cancellationToken)` searches text without a dense query vector.
- `IConfigurableHybridSearchStore.HybridSearchAsync(denseVector, query, options, topK, filter, cancellationToken)` accepts `HybridSearchOptions` for weighted RRF.
- `HybridSearchOptions` defaults to `VectorWeight = 0.5f`, `CandidateMultiplier = 2`, `RrfK = 60`. Nonfinite weights, weights outside `[0, 1]`, nonpositive candidate/smoothing settings and candidate-count overflow are rejected. Weight `0` disables dense search; weight `1` disables text search. Disabled legs do not inspect or validate their query input.

InMemory, PostgreSQL and Qdrant implement these contracts. Pinecone keeps its legacy native hybrid API; its classic dense-index adapter does not advertise text-only or configurable RRF support. A caller requesting unsupported capabilities must receive an error instead of having settings ignored. Pure text and vector results retain native scoring, while configurable hybrid scores use normalized weighted RRF; do not reuse thresholds blindly across modes.
