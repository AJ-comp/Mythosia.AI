# Mythosia.AI.Rag - Release Notes

## v4.0.0

### Breaking Changes

- **`RagPipeline.SetContextBuilder()` removed** — context builder is now resolved automatically from `RagPipelineOptions.PromptTemplate` at query time with internal caching.
- **`RagStore.UpdateQuerySettings()` removed** — replaced by `RagStore.UpdateOptions(Action<RagPipelineOptions>)`.
- **`RagStore.UpdateRetrievalMultiplier()` removed** — use `UpdateOptions` instead.

### Migration Guide

```csharp
// Before (v3.x)
store.UpdateQuerySettings(topK: 8, minScore: 0.4, promptTemplate: "...");
store.UpdateRetrievalMultiplier(3);

// After (v4.0)
store.UpdateOptions(opt =>
{
    opt.TopK = 8;
    opt.MinScore = 0.4;
    opt.PromptTemplate = "...";
    opt.RetrievalMultiplier = 3;
});
```

### Added

- **Auto-multiplier for re-ranking** — when a reranker is configured, the retrieval stage automatically fetches `TopK × RetrievalMultiplier` candidates, then the reranker selects the best `TopK` from that wider pool. No API changes needed; single `TopK` keeps the API simple.
- **`RagStore.UpdateOptions(Action<RagPipelineOptions>)`** — single method to update all pipeline options at runtime. New options added to `RagPipelineOptions` are automatically available without modifying `RagStore`.
- **`PromptTemplate` in `RagPipelineOptions`** — `RagPipeline` lazily resolves `ContextBuilder` from `Options.PromptTemplate` with caching, replacing the explicit `SetContextBuilder()` pattern.

### Changed

- `RagBuilder.WithPromptTemplate()` now sets `RagPipelineOptions.PromptTemplate` instead of creating a `TemplateContextBuilder` at build time.

---

## v3.2.0

### Added

- **Hybrid Search** — `UseHybridSearch()` fluent API combines BM25 keyword search with vector similarity search via **Reciprocal Rank Fusion (RRF)**.
  - `UseHybridSearch(float vectorWeight = 0.5f)` — adjustable balance between vector and keyword relevance.
  - `UseVectorSearch()` — explicit pure vector mode (same as default behavior).
  - Automatically selects the optimal strategy based on the store:
    - Stores with native `IVectorStore.HybridSearchAsync` support (Postgres, Qdrant) → native hybrid query delegation.
    - Non-hybrid stores (InMemory) → application-level BM25 index + vector search + RRF merge.
- **Re-ranking** — `WithReranker(IReranker)` fluent API re-orders search results after retrieval.
  - `CohereReranker` — Cohere Rerank API v2 (`rerank-v3.5` default model).
  - `LlmReranker` — uses any `AIService` to score and reorder results via LLM.
- **Retrieval Strategy abstraction** — `VectorRetrievalStrategy` and `HybridRetrievalStrategy` implement `IRetrievalStrategy` for pluggable retrieval logic.
- `RagPipeline` now accepts optional `IRetrievalStrategy` and `IReranker` via constructor injection.

### Compatibility

- Fully backward compatible with v3.1.0. No breaking changes.
- Existing code without `UseHybridSearch()` or `WithReranker()` behaves identically to v3.1.0 (pure vector search, no re-ranking).

---

## v3.1.0

### Added

- `WithQueryRewriter()` fluent API for multi-turn RAG conversations.
  - Automatically rewrites follow-up queries (e.g., "Tell me more about that") into standalone queries using conversation history before vector search.
  - Uses the inner `AIService` as the LLM for rewriting by default.
  - Supports custom `IQueryRewriter` implementations via `WithQueryRewriter(IQueryRewriter)`.
- `LlmQueryRewriter` — default `IQueryRewriter` implementation that uses an `AIService` in `StatelessMode` for rewriting without polluting conversation history.
- `RagProcessedQuery.RewrittenQuery` property for inspecting/debugging rewritten queries.

### Compatibility

- Fully backward compatible with v3.0.0. No breaking changes.

---

## v3.0.0

### Breaking Changes

- `RagProcessedQuery` construction is now diagnostics-first; call sites must provide `RagQueryDiagnostics` when creating instances directly.

### Changed

- `Mythosia.AI.Rag` directly references `Mythosia.VectorDb.InMemory` for out-of-the-box defaults.
- Default store resolution in `RagBuilder.BuildAsync` uses in-memory store creation when no custom store is configured.
- RAG diagnostics now use `IRagDiagnosticsStore` (from `Mythosia.AI.Rag.Abstractions`) for full chunk-level analysis capabilities.
- Removed reflection-based in-memory diagnostics probing and switched to interface-based capability detection.
- Added per-request retrieval overrides via `RagQueryOptions` (`TopK`, `MinScore`, `Namespace`) across `IRagPipeline`, `RagStore`, and `RagEnabledService`.
- `RagProcessedQuery` now includes `Diagnostics` (`RagQueryDiagnostics`) with applied retrieval settings (`AppliedNamespace`, `AppliedTopK`, `AppliedMinScore`) and `ElapsedMs` for request-level observability.

---

## v2.0.0

### Breaking Changes

- Vector DB abstraction types (`IVectorStore`, `VectorRecord`, `VectorFilter`, `VectorSearchResult`) moved to `Mythosia.VectorDb` namespace.
- `InMemoryVectorStore` moved to `Mythosia.VectorDb.InMemory` package (namespace `Mythosia.VectorDb.InMemory`).
- Consumers must replace `using Mythosia.AI.VectorDB;` with `using Mythosia.VectorDb.InMemory;`.
- Consumers must add `using Mythosia.VectorDb;` for vector DB contract types.

### Changed

- Improved `MarkdownTextSplitter` behavior for large markdown tables:
  - Large table blocks are now split by row within chunk budget.
  - Table header/separator rows are preserved at the start of each split chunk.
  - Code fence blocks remain unsplit.
- `ProcessAsync` now returns the original query as-is when no references are found, instead of an empty context template that confuses the LLM.

---

## v1.2.0

### Changed

- Integrated `IDocumentParser`-based loaders for Office and PDF sources.
- Removed semantic splitter from `RagBuilder`/`RagPipeline`.

### Added

- `DocumentSourceBuilder` for per-extension routing with per-source loader/text splitter configuration.
- `MarkdownTextSplitter` — splits on markdown headers.
- `RecursiveTextSplitter` — recursive splitting with ordered separators.
- Convenience document helpers: `AddWord`, `AddExcel`, `AddPowerPoint`.
- Per-source routing: single-file sources prioritized over directory sources; deduplicated by normalized full path.

### Fixed

- `CharacterTextSplitter` overlap now aligns to separator boundaries.

---

## v1.1.0

### Added

- Convenience document helpers for Office files: AddWord, AddExcel, AddPowerPoint.
- DocumentSourceBuilder for per-extension routing with per-source loader/text splitter configuration.
- MarkdownTextSplitter (splits on markdown headers).
- RecursiveTextSplitter (recursive, ordered separators).
- Per-source routing updates: single-file sources take priority over directory sources and documents are deduplicated by normalized full path.

### Fixed

- CharacterTextSplitter overlap now aligns to separator boundaries to avoid awkward mid-paragraph splits.

### Compatibility

- Backward compatible with v1.0.0 (existing ITextSplitter usage unchanged).

### Documentation

- RAG README expanded with per-extension routing examples.

---

## v1.0.0

### Initial Release

- RagPipeline + RagBuilder orchestration for indexing and querying.
- DefaultContextBuilder for query context construction.
- CharacterTextSplitter and TokenTextSplitter.
- OpenAIEmbeddingProvider and LocalEmbeddingProvider.
- PlainTextDocumentLoader integration for RAG sources.
