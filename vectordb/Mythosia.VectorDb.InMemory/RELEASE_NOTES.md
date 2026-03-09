# Mythosia.VectorDb.InMemory - Release Notes

## v2.1.0

### Added

- `Bm25Index` — thread-safe in-memory BM25 keyword search index for hybrid retrieval.
  - `Index(string id, string content)` — indexes a document's content for keyword search.
  - `Search(string query, int topK)` — returns BM25-scored results ranked by keyword relevance.
  - `Remove(string id)` — removes a document from the index.
  - Supports IDF (inverse document frequency) and term-frequency scoring with document length normalization (k1=1.2, b=0.75).
- Used automatically by `HybridRetrievalStrategy` when `UseHybridSearch()` is called with `InMemoryVectorStore` (store without native `IVectorStore.HybridSearchAsync` support).

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
