# Mythosia.VectorDb.InMemory - Release Notes

## v2.3.0

### Added

- **`InMemoryVectorStore.GetBatchAsync`** — O(n) lookup via the namespace `ConcurrentDictionary`. Applies scope and metadata filter conditions; omits records that do not match.
- **`InMemoryVectorStore.CountAsync`** — counts records by namespace, scope, and/or metadata filter. When namespace is provided, scans only that namespace's dictionary; otherwise aggregates across all namespaces.
- **`IDisposable` on `InMemoryVectorStore`** — disposes all `Bm25Index` instances held in the namespace-keyed dictionary. Call `Dispose()` when the store is no longer needed to release Lucene resources.
- **`IDisposable` on `Bm25Index`** — disposes the underlying Lucene `IndexWriter`, `Analyzer`, and `RAMDirectory`. Previously these were not explicitly released.

### Changed

- **`InMemoryVectorStore.DeleteAsync`** — now validates scope and metadata filter conditions before removing a record. If the stored record does not satisfy the filter, the delete is a no-op (consistent with how other stores apply filter-aware deletes).

### Compatibility

- Fully backward compatible with v2.2.0. `IDisposable` is additive; existing code that does not call `Dispose()` continues to work (GC finalizers still run). The `DeleteAsync` behavior change only affects callers that pass a scope or metadata filter alongside the id; plain `DeleteAsync(id)` is unchanged.

---

## v2.2.0

### Compatibility

- Compatible with `Mythosia.VectorDb.Abstractions` v2.3.0.
- `ReplaceByFilterAsync` is available via the default interface method (sequential `DeleteByFilterAsync` → `UpsertBatchAsync`). No override needed for in-memory usage.

---

## v2.1.0

### Added

- `Bm25Index` — thread-safe in-memory BM25 keyword search index for hybrid retrieval.
  - `Index(string id, string content)` — indexes a document's content for keyword search.
  - `Search(string query, int topK)` — returns BM25-scored results ranked by keyword relevance.
  - `Remove(string id)` — removes a document from the index.
  - Supports IDF (inverse document frequency) and term-frequency scoring with document length normalization (k1=1.2, b=0.75).
- Used automatically by `HybridRetrievalStrategy` when `UseHybridSearch()` is called with `InMemoryVectorStore` (store without native `IVectorStore.HybridSearchAsync` support).

### Changed

- Hybrid RRF scores returned by `InMemoryVectorStore` are normalized to the `[0, 1]` range before results are returned.
- `VectorFilter.MinScore` is now applied to the final merged hybrid score, making threshold filtering consistent for hybrid search.

### Compatibility

- Fully backward compatible with v2.0.0. No breaking changes — `InMemoryVectorStore` API unchanged.

---

## v2.0.0

### Added

- Implements `IRagDiagnosticsStore` to provide contract-based diagnostic operations used by `Mythosia.AI.Rag` diagnostics.

### Breaking Changes — Namespace Now Optional

Aligned with `IVectorStore` v2.0.0: namespace moved from method parameter to `VectorRecord.Namespace` / `VectorFilter.Namespace` properties.

- All methods no longer take `string @namespace` as a parameter.
- Namespace is read from `record.Namespace` (defaults to `"default"` when null).
- `NamespaceExistsAsync` / `CreateNamespaceAsync` / `DeleteNamespaceAsync` removed.
- Diagnostic methods `ListAllRecordsAsync` and `ScoredListAsync` now take `string? @namespace = null`.

### Fluent API

`InNamespace()` / `InScope()` fluent builder pattern works seamlessly:

```csharp
var store = new InMemoryVectorStore();
await store.InNamespace("docs").InScope("tenant-1").UpsertAsync(record);
```

## v1.0.0

### Initial Release

- `InMemoryVectorStore` — thread-safe in-memory implementation of `IVectorStore`.
- Cosine similarity TopK search with configurable result count.
- Thread-safe concurrent access via `ConcurrentDictionary`.
- Scope isolation and metadata key-value filtering.
- Minimum score threshold support.
- Single and batch upsert/delete operations.
- Diagnostic helpers: `ListAllRecordsAsync`, `ScoredListAsync`, `GetTotalRecordCount`.
