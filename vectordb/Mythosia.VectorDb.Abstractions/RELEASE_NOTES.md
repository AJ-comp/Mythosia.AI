# Mythosia.VectorDb.Abstractions - Release Notes

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
