# Mythosia.AI.Rag.Abstractions - Release Notes

## v3.1.0

### Added

- `IQueryRewriter` interface for rewriting follow-up queries into standalone queries using conversation history.
  - `RewriteAsync(query, conversationHistory, cancellationToken)` — returns a standalone query suitable for vector search.
- `ConversationTurn` lightweight DTO representing a single conversation turn (`Role`, `Content`) for use with `IQueryRewriter`.
- `RagProcessedQuery.RewrittenQuery` nullable property — contains the rewritten query when query rewriting occurred, or `null` if no rewriting was needed.

### Compatibility

- Fully backward compatible with v3.0.0. No breaking changes.

---

## v3.0.0

### Breaking Changes

- `RagProcessedQuery` now uses a diagnostics-aware constructor and no longer exposes legacy constructor overloads without diagnostics.

### Added

- `IRagDiagnosticsStore` optional interface for vector-store level diagnostics.
  - `ListAllRecordsAsync(string?, CancellationToken)`
  - `ScoredListAsync(float[], string?, CancellationToken)`
- Enables RAG diagnostics to use stable contract-based capabilities instead of runtime reflection.
- `RagQueryOptions` for per-request query overrides (`TopK`, `MinScore`, `Namespace`).
- `RagQueryDiagnostics` and `RagProcessedQuery.Diagnostics` for applied retrieval settings (`AppliedNamespace`, `AppliedTopK`, `AppliedMinScore`) and `ElapsedMs`.

---

## v2.0.0

### Breaking Changes

- `IVectorStore`, `VectorRecord`, `VectorFilter`, `VectorSearchResult` moved to `Mythosia.VectorDb.Abstractions` package (namespace `Mythosia.VectorDb`).
- Consumers must add `using Mythosia.VectorDb;` to resolve these types.
- Added project dependency on `Mythosia.VectorDb.Abstractions`.

### Added

- `RagProcessedQuery.HasReferences` computed property — returns `true` when the query matched at least one vector store reference.

---

## v1.0.0

### Initial Release

- `IRagPipeline`, `IContextBuilder`, `ITextSplitter`, `IEmbeddingProvider` interfaces.
- `RagPipelineOptions`, `RagProcessedQuery` shared models.
- `IVectorStore`, `VectorRecord`, `VectorFilter`, `VectorSearchResult` contracts.
