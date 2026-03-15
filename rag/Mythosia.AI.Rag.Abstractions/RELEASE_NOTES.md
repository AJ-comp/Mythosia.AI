# Mythosia.AI.Rag.Abstractions - Release Notes

## v4.0.0

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
