# Mythosia.VectorDb.InMemory - Release Notes

## v3.0.1

### Changed

- **Namespace filtering is now optional** — when `VectorFilter.Namespace` is null, queries operate across all namespace dictionaries instead of forcing the `"default"` namespace.
  - Affected methods: `GetAsync`, `DeleteAsync`, `GetBatchAsync`, `SearchAsync`, `HybridSearchAsync`.
  - Diagnostic methods `ListAllRecordsAsync` and `ScoredListAsync` also support null namespace for cross-namespace operation.
  - **Upsert unchanged** — `UpsertAsync` / `UpsertBatchAsync` still fall back to `"default"` when `record.Namespace` is null (dictionary key required).
  - `DeleteByFilterAsync` and `CountAsync` were already correct (no change needed).

### Deprecated

- **`VectorRecord.Namespace`**, **`VectorRecord.Scope`**, **`VectorFilter.Namespace`**, **`VectorFilter.Scope`**, **`VectorFilter.WithNamespace()`**, **`INamespaceContext`**, **`IScopeContext`**, and **`InNamespace()` / `InScope()`** are now marked `[Obsolete]`.
  - These will be removed in a future major version.
  - Use `Metadata` entries (e.g. `Metadata["namespace"]`) and `VectorFilter.Where("namespace", value)` for logical isolation instead.
  - This aligns with industry-standard vector database designs (Qdrant payload, Pinecone metadata, LangChain PGVector).

### Compatibility

- Backward compatible with v3.0.0. Existing records stored under the `"default"` namespace remain accessible.
- Deprecated APIs still function but produce `CS0618` compiler warnings.

---

## v3.0.0

### Breaking Changes

`VectorFilter` construction API changed (see `Mythosia.VectorDb.Abstractions` v3.0.0). Any code that builds a `VectorFilter` to pass to `SearchAsync`, `HybridSearchAsync`, `GetAsync`, `GetBatchAsync`, `CountAsync`, `DeleteAsync`, `DeleteByFilterAsync`, or `ReplaceByFilterAsync` must be updated:

```csharp
// Before — compile error in v3.0.0
store.SearchAsync(vector, filter: VectorFilter.ByMetadata("k", "v"));
store.CountAsync(new VectorFilter { MetadataMatch = new Dictionary<string, string> { ["k"] = "v" } });

// After
store.SearchAsync(vector, filter: new VectorFilter().Where("k", "v"));
store.CountAsync(new VectorFilter().Where("k", "v"));
```

### Changed

- **Filter evaluation engine** — updated to support the `VectorFilter` fluent condition tree introduced in `Mythosia.VectorDb.Abstractions` v3.0.0.
  - All operators are evaluated in-memory via a recursive `EvaluateConditions` / `EvaluateCondition` chain.
  - **`Eq`** — exact string match (ordinal).
  - **`Ne`** — not-equal.
  - **`Gt / Gte / Lt / Lte`** — lexicographic string comparison.
  - **`In`** — value is in the provided set.
  - **`NotIn`** — value is not in the provided set.
  - **`Like`** — recursive LIKE pattern matcher supporting `%` (any substring) and `_` (any single character) wildcards.
  - **`Exists`** — metadata key is present.
  - **`NotExists`** — metadata key is absent.
  - **`And / Or` groups** — recursive evaluation with short-circuit logic.
- **`WithoutMinScore` helper** — now uses `AppendConditionsFrom` to copy the condition tree instead of the removed `MetadataMatch` copy.

---

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
