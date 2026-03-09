# Mythosia.VectorDb.Abstractions - Release Notes

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
