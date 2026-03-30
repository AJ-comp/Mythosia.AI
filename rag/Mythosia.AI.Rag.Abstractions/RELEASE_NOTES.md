# Mythosia.AI.Rag.Abstractions - Release Notes

## v6.0.0

### Breaking Changes

`VectorFilter` construction API changed (see `Mythosia.VectorDb.Abstractions` v3.0.0). Any code that assigns `RagQueryOptions.StoreFilter` using the old API must be updated:

```csharp
// Before — compile error in v6.0.0
options.StoreFilter = VectorFilter.ByMetadata("storage_id", id);
options.StoreFilter = new VectorFilter { MetadataMatch = new Dictionary<string, string> { ["storage_id"] = id, ["folder"] = "/docs" } };

// After
options.StoreFilter = new VectorFilter().Where("storage_id", id);
options.StoreFilter = new VectorFilter().Where("storage_id", id).Where("folder", "/docs");
```

No structural changes to this package's own types (`RagQueryOptions`, `IRagPipeline`, etc.).

---

## v5.1.0

### Dependency Changes

- **Removed `Mythosia.AI.Loaders.Abstractions` dependency** — `RagDocument` is now self-contained in this package (`Mythosia.AI.Rag` namespace). Consumers that relied on the transitive Loaders dependency must add an explicit reference to `Mythosia.Documents.Abstractions` if needed.

### Added

- **`RagDocument`** — self-contained document model (`Id`, `Content`, `Source`, `Metadata`) defined directly in `Mythosia.AI.Rag` namespace. Identical shape to the former `Mythosia.AI.Loaders.RagDocument`.
- **`RagQueryOptions.StoreFilter`** (`VectorFilter?`) — optional metadata filter passed directly to `IVectorStore.SearchAsync` / `HybridSearchAsync` on every retrieval call. Enables per-query tenant isolation, permission-based filtering, and category scoping. When `null` the retrieval is unfiltered (backward compatible). When `Namespace` is also set, both constraints are applied together. Multiple metadata conditions are expressed via `VectorFilter.MetadataMatch` (AND logic).

---

## v5.0.0

### Breaking Changes

- **`IReranker.RerankAsync` removed `topK` parameter** — the reranker now returns all results re-scored and reordered. TopK trimming is the pipeline's responsibility, enabling weighted-blend final selection.
- **`IRetrievalStrategy.RetrieveAsync` `query` parameter now nullable (`string?`)** — when `null`, keyword search is skipped and only dense vector search is performed.

### Added

- **`RagFinalSelectionOptions`** — configures how the pipeline selects final references after optional re-ranking (`RerankerOnly` or `WeightedBlend` mode).
- **`RagFinalSelectionMode` enum** — `RerankerOnly` (default, backward compatible) and `WeightedBlend` (blends retrieval + reranker scores).
- **`RagQueryOptions.FinalSelection`** — per-request final selection policy override.
- **`QueryRewriteResult.Keywords`** — optional retrieval-oriented search terms extracted for text/keyword search leg of hybrid search.
- **`QueryRewriteResult.Search(string query, IReadOnlyList<string>? keywords)`** — new factory method with keyword support.
- **`RagProcessedQuery.SearchKeywords`** — retrieval-oriented keywords extracted by the query rewriter for hybrid search.
- **`RagProcessedQuery.RerankedCandidates`** — all results after re-ranking but before final selection (topK + minScore). Null when no reranker is configured.

---

## v4.0.1

### Breaking Changes

- `IQueryRewriter.RewriteAsync` now returns `Task<QueryRewriteResult>` instead of `Task<string>`. All implementations must update their return type.
- `RagProcessedQuery.AugmentedPrompt` renamed to `RequestMessageContent` to clarify that the value is transient request-only content not meant for conversation history persistence.
- `RagProcessedQuery` constructor now requires an additional `IReadOnlyList<VectorSearchResult> retrievalCandidates` parameter.
- `RagPipelineOptions.TopK`, `MinScore`, `DefaultNamespace`, `RetrievalMultiplier` removed. Replaced by `DefaultQuery` property of type `RagQueryOptions`.
- `RagQueryOptions` restructured — `int? TopK`, `double? MinScore`, `string? Namespace` replaced by `RagFilter FinalFilter`, `RagRetrievalDerivation RetrievalDerivation`, `string Namespace`.
- `RagQueryDiagnostics.AppliedTopK` renamed to `FinalTopK`; `RetrievalK` renamed to `RetrievalTopK`; `AppliedMinScore` renamed to `AppliedFinalMinScore`.

### Added

- `QueryRewriteResult` — result of a query rewrite operation including a search gate decision (`NeedsSearch`). Factory methods `Pass()` and `Search()` for convenience.
- `RagFilter` — final selection policy (`TopK`, `MinScore`).
- `RagRetrievalDerivation` — controls how retrieval candidates are derived from `RagFilter` (`TopKMultiplier`, `MinScoreDivider`).
- `RagRetrievalFilter` — immutable computed retrieval filter (`TopK`, `MinScore`).
- `RagProgressStage` enum — pipeline stages for progress reporting (`QueryRewrite`, `Embedding`, `Filtering`, `Retrieval`, `Reranking`, `ContextBuild`).
- `RagQueryOptions.ProgressAsync` — optional async callback invoked when the pipeline enters each stage.
- `RagQueryOptions.GetRetrievalFilter(bool hasReranker)` — computes retrieval-stage TopK/MinScore from `FinalFilter` and `RetrievalDerivation`.
- `RagProcessedQuery.RetrievalCandidates` — raw retrieval candidates before re-ranking.
- `RagProcessedQuery.SearchSkipped` — indicates the search gate bypassed the RAG pipeline entirely.
- `RagProcessedQuery.RewriteResult` — raw `QueryRewriteResult` from the query rewriter.
- `RagQueryDiagnostics.AppliedRetrievalMinScore` — retrieval-stage score threshold.
- `RagQueryDiagnostics.RewriteElapsedMs` — time spent on query rewriting.

### Migration Guide

| v3.x | v4.0.0 |
| --- | --- |
| `IQueryRewriter.RewriteAsync` → `Task<string>` | → `Task<QueryRewriteResult>` |
| `RagProcessedQuery.AugmentedPrompt` | → `RequestMessageContent` |
| `RagPipelineOptions.TopK` | → `DefaultQuery.FinalFilter.TopK` |
| `RagPipelineOptions.MinScore` | → `DefaultQuery.FinalFilter.MinScore` |
| `RagPipelineOptions.DefaultNamespace` | → `DefaultQuery.Namespace` |
| `RagPipelineOptions.RetrievalMultiplier` | → `DefaultQuery.RetrievalDerivation.TopKMultiplier` |
| `RagQueryOptions.TopK` | → `FinalFilter.TopK` |
| `RagQueryOptions.MinScore` | → `FinalFilter.MinScore` |
| `RagQueryDiagnostics.AppliedTopK` | → `FinalTopK` |
| `RagQueryDiagnostics.RetrievalK` | → `RetrievalTopK` |
| `RagQueryDiagnostics.AppliedMinScore` | → `AppliedFinalMinScore` |

---

## v3.2.0

### Added

- `IRetrievalStrategy` interface — abstracts retrieval logic for pluggable search strategies (pure vector or hybrid).
  - `RetrieveAsync(IVectorStore, float[], string, int, VectorFilter?, CancellationToken)` — returns ranked `VectorSearchResult` list.
- `IReranker` interface — re-ranks search results post-retrieval for improved relevance.
  - `RerankAsync(string query, IReadOnlyList<VectorSearchResult>, int topN, CancellationToken)` — returns reordered results.
- `RagPipelineOptions.RetrievalMultiplier` (default `3`) — multiplier applied to `TopK` when a reranker is configured. The retrieval stage fetches `TopK × RetrievalMultiplier` candidates, then the reranker selects the best `TopK` from that wider pool.
- `RagPipelineOptions.PromptTemplate` — optional prompt template with `{context}` and `{question}` placeholders. When set, overrides the default context builder at query time.
- `RagQueryDiagnostics.RetrievalK` — the number of candidates actually fetched from the vector store. When a reranker is configured this is `TopK × RetrievalMultiplier`; otherwise it equals `AppliedTopK`.

### Compatibility

- Fully backward compatible with v3.1.0. No breaking changes — new interfaces and properties are additive only.

---

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
