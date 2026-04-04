# Mythosia.VectorDb.Abstractions - Release Notes

## v4.0.1

### Fixed

- Fixed stale XML doc comments in `IVectorStore` and `VectorRecord` that still referenced removed Namespace/Scope concepts.

---

## v4.0.0

### Breaking Changes

- **Removed `VectorRecord.Namespace`** and **`VectorRecord.Scope`** properties — namespace and scope isolation is now exclusively via `Metadata["namespace"]` and `Metadata["scope"]`.
- **Removed `VectorFilter.Namespace`**, **`VectorFilter.Scope`**, and **`VectorFilter.WithNamespace()`** — use `VectorFilter.Where("namespace", value)` and `.Where("scope", value)` instead.
- **Removed `INamespaceContext`**, **`IScopeContext`**, **`NamespaceContext`**, **`ScopeContext`**, and **`InNamespace()` / `InScope()`** extension methods.

These APIs were deprecated in v3.0.1 and are now fully removed.

### Migration Guide

```csharp
// Before (v3.x)
var record = new VectorRecord { Id = "1", Namespace = "docs", Scope = "public" };
var filter = new VectorFilter { Namespace = "docs" };
var filter2 = new VectorFilter().WithNamespace("docs");

// After (v4.0.0)
var record = new VectorRecord { Id = "1", Metadata = { ["namespace"] = "docs", ["scope"] = "public" } };
var filter = new VectorFilter().Where("namespace", "docs");
var filter2 = new VectorFilter().Where("namespace", "docs");
```

---

## v3.0.1

### Deprecated

- **`VectorRecord.Namespace`**, **`VectorRecord.Scope`**, **`VectorFilter.Namespace`**, **`VectorFilter.Scope`**, **`VectorFilter.WithNamespace()`**, **`INamespaceContext`**, **`IScopeContext`**, **`NamespaceContext`**, **`ScopeContext`**, and **`InNamespace()` / `InScope()`** are now marked `[Obsolete]`.
  - These will be removed in a future major version.
  - Use `Metadata` entries (e.g. `Metadata["namespace"]`) and `VectorFilter.Where("namespace", value)` for logical isolation instead.
  - This aligns with industry-standard vector database designs (Qdrant payload, Pinecone metadata, LangChain PGVector).

### Compatibility

- Backward compatible with v3.0.0. All deprecated APIs still function but produce `CS0618` compiler warnings.

---

## v3.0.0

### Breaking Changes — VectorFilter Fluent API

`VectorFilter` has been redesigned from a simple dictionary-based exact-match model to a full operator-based fluent builder with recursive logical grouping.

| Removed (v2.x) | Replacement (v3.0.0) |
| --- | --- |
| `filter.MetadataMatch = new Dictionary<string, string> { ["k"] = "v" }` | `filter.Where("k", "v")` |
| `VectorFilter.ByMetadata("k", "v")` | `new VectorFilter().Where("k", "v")` |
| `VectorFilter.ByNamespace("ns")` | `new VectorFilter { Namespace = "ns" }` |
| `VectorFilter.ByScope("s")` | `new VectorFilter { Scope = "s" }` |

#### New types

- **`FilterOperator`** — `Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `NotIn`, `Like`, `Exists`, `NotExists`
- **`FilterLogic`** — `And`, `Or`
- **`FilterCondition`** — abstract base for condition nodes
- **`MetadataCondition`** — leaf node (key, operator, value/values)
- **`FilterGroup`** — composite node (logic + child conditions)

#### New fluent methods on `VectorFilter` (all return `this`)

```csharp
.Where(key, value)                     // Eq
.WhereNot(key, value)                  // Ne
.WhereIn(key, params values)           // In
.WhereNotIn(key, params values)        // NotIn
.WhereGreaterThan(key, value)          // Gt
.WhereGreaterThanOrEqual(key, value)   // Gte
.WhereLessThan(key, value)             // Lt
.WhereLessThanOrEqual(key, value)      // Lte
.WhereLike(key, pattern)               // Like (% and _ wildcards)
.WhereExists(key)                      // Exists
.WhereNotExists(key)                   // NotExists
.And(Action<VectorFilter> configure)   // AND group
.Or(Action<VectorFilter> configure)    // OR group
.WithNamespace(ns)
.WithMinScore(score)
.AppendConditionsFrom(other)           // merge condition trees (used by MergeStoreFilter)
```

### Migration Guide

```csharp
// Before
var f = VectorFilter.ByMetadata("type", "pdf");
var f2 = new VectorFilter { MetadataMatch = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" } };

// After
var f = new VectorFilter().Where("type", "pdf");
var f2 = new VectorFilter().Where("a", "1").Where("b", "2");
```

---

## v2.4.0

### Added

- **`IVectorStore.GetBatchAsync(IEnumerable<string> ids, VectorFilter? filter, CancellationToken)`** — retrieves multiple records by ID in a single call.
  - Default interface implementation falls back to sequential `GetAsync` calls.
  - Concrete stores (InMemory, Postgres, Qdrant, Pinecone) override with a native batch fetch for efficiency.
  - Records that do not exist or do not match `filter` are omitted. Result order is not guaranteed.

- **`IVectorStore.CountAsync(VectorFilter? filter, CancellationToken)`** — returns the number of records matching the optional filter.
  - Default implementation throws `NotSupportedException` (same pattern as `HybridSearchAsync`).
  - `VectorFilter.MinScore` is ignored for counting purposes.

- **`INamespaceContext`** — completed with the following new methods (all automatically inject the context namespace into filters):
  - `HybridSearchAsync(float[], string, int, VectorFilter?, CancellationToken)`
  - `GetBatchAsync(IEnumerable<string>, CancellationToken)`
  - `ReplaceByFilterAsync(VectorFilter, IReadOnlyList<VectorRecord>, CancellationToken)`
  - `CountAsync(VectorFilter?, CancellationToken)`

- **`IScopeContext`** — completed with the following new methods (all inject namespace + scope):
  - `HybridSearchAsync(float[], string, int, VectorFilter?, CancellationToken)`
  - `GetBatchAsync(IEnumerable<string>, CancellationToken)`
  - `DeleteAllAsync(CancellationToken)` — deletes all records in this namespace and scope
  - `ReplaceByFilterAsync(VectorFilter, IReadOnlyList<VectorRecord>, CancellationToken)`
  - `CountAsync(VectorFilter?, CancellationToken)`

### Compatibility

- Fully backward compatible with v2.3.0. All additions are new interface members with implementations in `NamespaceContext` / `ScopeContext`. Existing `IVectorStore` implementations continue to work via the default fallbacks.

---

## v2.3.0

### Added

- **`ReplaceByFilterAsync(VectorFilter, IReadOnlyList<VectorRecord>, CancellationToken)`** — default interface method on `IVectorStore` that atomically replaces vectors matching a filter with new records.
  - Enables transactional DELETE + INSERT for scenarios like re-embedding a modified file without a query gap.
  - Default implementation calls `DeleteByFilterAsync` → `UpsertBatchAsync` sequentially (non-transactional).
  - Concrete stores (e.g. `PostgresStore`) override this to wrap both operations in a single database transaction.

### Compatibility

- Fully backward compatible with v2.2.0. No breaking changes — default interface method, existing `IVectorStore` implementations continue to work without modification.

---

## v2.2.0

### Added

- `VerifyConnectionAsync(CancellationToken)` default interface method on `IVectorStore`.
  - Verifies that the store can reach its backend (database, API, etc.) and throws on failure.
  - In-memory stores succeed immediately via the default implementation (`Task.CompletedTask`).
  - Concrete stores (`PostgresStore`, `QdrantStore`, `PineconeStore`) override this to perform actual connectivity checks.

### Compatibility

- Fully backward compatible with v2.1.0. No breaking changes — default interface method, existing implementations continue to work.

---

## v2.1.0

### Added

- Native hybrid search contract in `IVectorStore`.
  - `HybridSearchAsync(float[] queryVector, string queryText, int topK, VectorFilter?, CancellationToken)` — method for native hybrid search.
- `Bm25Tokenizer` static utility class for BM25 keyword indexing.
  - `Tokenize(string text)` — tokenizes text with lowercasing, punctuation stripping, and English stop-word removal.
  - `ComputeTermFrequency(string text)` — returns term-frequency dictionary for a document.

### Compatibility

- Fully backward compatible with v2.0.0. No breaking changes — `IVectorStore` interface unchanged. New types are additive only.

---

## v2.0.0

### Breaking Changes — Namespace Now Optional

Namespace has been moved from a mandatory `IVectorStore` method parameter to **optional properties** on `VectorRecord.Namespace` and `VectorFilter.Namespace`, symmetric with how `Scope` already works.

| Before (v1.0.0) | After (v2.0.0) |
|---|---|
| `store.UpsertAsync("ns", record)` | `record.Namespace = "ns"; store.UpsertAsync(record)` |
| `store.SearchAsync("ns", vector, topK)` | `store.SearchAsync(vector, topK, new VectorFilter { Namespace = "ns" })` |
| `store.GetAsync("ns", id)` | `store.GetAsync(id, new VectorFilter { Namespace = "ns" })` |
| `store.DeleteAsync("ns", id)` | `store.DeleteAsync(id, new VectorFilter { Namespace = "ns" })` |
| `store.DeleteByFilterAsync("ns", filter)` | `filter.Namespace = "ns"; store.DeleteByFilterAsync(filter)` |
| `NamespaceExistsAsync` / `CreateNamespaceAsync` / `DeleteNamespaceAsync` | Removed — use `DeleteByFilterAsync(new VectorFilter { Namespace = "ns" })` |

### Model Changes

- **`VectorRecord`** — added `string? Namespace` property (first-tier logical isolation).
- **`VectorFilter`** — added `string? Namespace` property and `VectorFilter.ByNamespace()` factory.
- **`IVectorStore.GetAsync` / `DeleteAsync`** — now accept optional `VectorFilter? filter` for namespace/scope narrowing.
- **`INamespaceContext`** — removed `ExistsAsync()` / `CreateAsync()`. `DeleteAllAsync()` now delegates to `DeleteByFilterAsync`.

### Fluent Builder API

The fluent API (`InNamespace()` / `InScope()`) automatically sets `Namespace` and `Scope` on records and filters. Usage unchanged:

```csharp
await store.InNamespace("docs").UpsertAsync(record);
await store.InNamespace("docs").InScope("tenant-1").SearchAsync(queryVector);
```

## v1.0.0

### Initial Release

- `IVectorStore` — vector storage and similarity search contract.
- `VectorRecord` — record model with embedding vector, content, and metadata.
- `VectorFilter` — filter criteria for scope, metadata, and minimum score.
- `VectorSearchResult` — search result with matched record and similarity score.
