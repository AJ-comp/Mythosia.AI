# Mythosia.AI.Rag - Release Notes

## v7.0.0

### Breaking Changes

`VectorFilter` construction API changed (see `Mythosia.VectorDb.Abstractions` v3.0.0). Any code that assigns `RagQueryOptions.StoreFilter` using the old API must be updated:

```csharp
// Before — compile error in v7.0.0
options.StoreFilter = VectorFilter.ByMetadata("storage_id", id);
options.StoreFilter = new VectorFilter { MetadataMatch = new Dictionary<string, string> { ["storage_id"] = id, ["folder"] = "/docs" } };

// After
options.StoreFilter = new VectorFilter().Where("storage_id", id);
options.StoreFilter = new VectorFilter().Where("storage_id", id).Where("folder", "/docs");
```

Requires `Mythosia.AI.Rag.Abstractions` v6.0.0, `Mythosia.VectorDb.Abstractions` v3.0.0, `Mythosia.VectorDb.InMemory` v3.0.0.

### Changed

- **`MergeStoreFilter`** (internal) — rewrote filter merge logic to use `AppendConditionsFrom` on the new `VectorFilter` condition tree instead of merging `MetadataMatch` dictionaries. `storeFilter` conditions are appended first (permission constraints), followed by per-query `filter` conditions. `Scope` is taken from `storeFilter` when set, falling back to the query filter.
- **`DeleteDocumentAsync`** — uses `new VectorFilter().Where("document_id", documentId)` instead of the removed `VectorFilter.ByMetadata()`.
- **`HybridRetrievalStrategy.WithoutMinScore`** — updated to copy the condition tree via `AppendConditionsFrom` instead of copying the removed `MetadataMatch` property.

---

## v6.2.0

### Dependency Changes

- **`Mythosia.AI` → `Mythosia.AI.Abstractions`** — the Rag package now depends on the lightweight abstractions package instead of the full AI implementation. All public API surface accepts `IAIService` (widened from `AIService` — existing callers remain source-compatible). `WithoutRag()` now returns `IAIService`.
- **`Mythosia.AI.Loaders.Office/Pdf` → `Mythosia.Documents.Office/Pdf`** — follows the package rename.

### Added

- **`DoclingDocumentConverter`** — converts `DoclingDocument` (from `Mythosia.Documents`) to `RagDocument` (from `Mythosia.AI.Rag`). Used internally by `RagBuilder` for all loader integrations.
- **`RagQueryOptions.StoreFilter` passthrough** — `VectorFilter?` property on `RagQueryOptions` that is passed directly to `IVectorStore.SearchAsync` / `IVectorStore.HybridSearchAsync` on every retrieval call.
  - Enables per-query **tenant isolation**, **permission-based filtering**, **category scoping**, and **time-range filtering** without wrapping the store in a custom decorator.
  - When `StoreFilter` is `null` the pipeline behaves exactly as before (no breaking change).
  - When `Namespace` is also set, both constraints are applied together: namespace sets `VectorFilter.Namespace`; `StoreFilter` contributes `MetadataMatch` and `Scope`.
  - If both an explicit `VectorFilter` parameter and `StoreFilter` are present, their `MetadataMatch` dictionaries are merged (`StoreFilter` wins on key conflicts). `Scope` is taken from `StoreFilter` when set.
  - Multiple metadata conditions are expressed via `VectorFilter.MetadataMatch` (any number of key-value pairs, all combined with AND logic).
- **`MergeStoreFilter`** (internal) — merges explicit `VectorFilter` with per-query `StoreFilter`.

### Usage

```csharp
// Single metadata condition
var options = new RagQueryOptions();
options.FinalFilter.TopK = 5;
options.StoreFilter = VectorFilter.ByMetadata("storage_id", storageId);
var result = await ragStore.QueryAsync("질문", options, cancellationToken);

// Multiple conditions (AND) — storage_id AND folder_path
options.StoreFilter = new VectorFilter
{
    MetadataMatch = new Dictionary<string, string>
    {
        ["storage_id"] = storageId,
        ["folder_path"] = "/docs/private"
    }
};

// Namespace + metadata simultaneously
options.Namespace = "tenant-A";
options.StoreFilter = VectorFilter.ByMetadata("user_id", currentUserId);
```

### Compatibility

- Requires `Mythosia.AI.Rag.Abstractions` v5.1.0.
- `StoreFilter = null` (default) preserves existing behavior.

---

## v6.1.0

### Added

- **`onDocumentEmbedded` callback parameter on `BuildAsync`** — optional `Func<IReadOnlyList<VectorRecord>, Task>?` callback invoked after each document's embedding is complete.
  - When omitted (`null`), the default behavior is unchanged — records are saved to the configured store via `UpsertBatchAsync` as before.
  - When provided, the callback **replaces** the default `UpsertBatchAsync` call, giving full control over how records are persisted.
  - Enables atomic file replacement by combining with `IVectorStore.ReplaceByFilterAsync` (Abstractions v2.3.0).

### Usage

```csharp
// Default: works exactly as before (no callback, saves to store automatically)
var store = await RagStore.BuildAsync(builder =>
{
    builder.AddDocuments("./docs/")
           .UseOpenAIEmbedding(apiKey)
           .UseStore(vectorStore);
}, ct);

// Atomic file replacement via callback
var store = await RagStore.BuildAsync(builder =>
{
    builder.AddDocuments(loader, file.LocalPath)
           .UseEmbedding(embeddingProvider)
           .UseStore(vectorStore);
},
onDocumentEmbedded: records =>
    vectorStore.ReplaceByFilterAsync(
        VectorFilter.ByMetadata("full_path", file.FullPath), records, ct),
ct);
```

### Compatibility

- Fully backward compatible with v6.0.1. No breaking changes — omitting the callback preserves existing behavior.

---

## v6.0.1

### Mythosia.AI v5.0.1 Compatibility

- Compatible with `Mythosia.AI` v5.0.1 — inherits streaming Template Method refactor and `Stream` flag restoration fix during conversation summary.
- No functional changes to RAG pipeline.

---

## v6.0.0

### Breaking Changes (requires Abstractions v5.0.0)

- **`IReranker.RerankAsync` removed `topK` parameter** — all reranker implementations (`CohereReranker`, `LlmReranker`, `VllmReranker`) now return all results re-scored and reordered. TopK trimming is handled by the pipeline after final selection.
- **`IRetrievalStrategy.RetrieveAsync` `query` parameter now nullable** — `HybridRetrievalStrategy` falls back to dense-only search when the lexical query is null/empty.
- **`OllamaEmbeddingProvider` / `VllmEmbeddingProvider` strict dimension validation** — `dimensions` is now `readonly` with constructor validation (`> 0`). Dimension mismatch with server response throws `InvalidOperationException` instead of silently auto-correcting.

### Added

- **Weighted-blend final selection** — `RagBuilder.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight)` blends retrieval and reranker scores for final ranking instead of relying on reranker scores alone.
- **Retrieval keyword extraction in `LlmQueryRewriter`** — when `extractKeywords: true` (default), the rewriter outputs a `KEYWORDS:` line with shaped search terms for the text/keyword leg of hybrid search. Helps lexical retrieval handle language-particle and formatting mismatches.
- **`LlmQueryRewriter` configurable `maxTokens`** — control the LLM response token limit for query rewriting (default 250).
- **`RagBuilder.WithQueryRewriter(uint maxTokens)`** — new overload to configure max tokens without providing a custom rewriter.
- **`RagPipeline` reranked candidates tracking** — `RagProcessedQuery.RerankedCandidates` exposes all results after re-ranking but before final selection.
- **`RagStore` / `RagEnabledService` keyword-derived text search** — when the rewriter produces keywords, they are joined and passed as the lexical query for hybrid search, separate from the semantic query used for embedding.
- **`VllmEmbeddingProvider` sends `dimensions` parameter** in the request body to the server.

### Changed

- `LlmQueryRewriter` now builds an inline `AIRequestProfile` with explicit `Temperature`, `MaxTokens`, and `DisableReasoning` settings instead of using `RequestProfiles.QueryRewrite`.
- `CohereReranker` / `VllmReranker` `top_n` now set to `results.Count` (returns all results to the pipeline for final selection).
- `LlmReranker` no longer applies `.Take(topK)` after scoring.
- `HybridRetrievalStrategy` skips BM25 entirely and falls back to dense vector search when lexical query is null or empty.

### Migration Guide

```csharp
// Before (v5.x) — custom IReranker implementation
public Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
    string query, IReadOnlyList<VectorSearchResult> results,
    int topK, CancellationToken ct = default)

// After (v6.0) — remove topK parameter, return all results
public Task<IReadOnlyList<VectorSearchResult>> RerankAsync(
    string query, IReadOnlyList<VectorSearchResult> results,
    CancellationToken ct = default)
```

```csharp
// Before (v5.x) — custom IRetrievalStrategy implementation
public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
    float[] denseVector, string query, int topK, ...)

// After (v6.0) — query is now nullable
public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
    float[] denseVector, string? query, int topK, ...)
```

```csharp
// New: Weighted-blend final selection
.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithReranker(new CohereReranker(apiKey))
    .WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)
)
```

---

## v5.0.1

### Breaking Changes (requires Abstractions v4.0.0)

- **`RagPipelineOptions.TopK`, `MinScore`, `DefaultNamespace`, `RetrievalMultiplier` removed** — replaced by `DefaultQuery` property of type `RagQueryOptions`, which contains `FinalFilter`, `RetrievalDerivation`, and `Namespace`.
- **`RagProcessedQuery.AugmentedPrompt` renamed to `RequestMessageContent`** — clarifies the value is transient request-only content.
- **`RagProcessedQuery` constructor** now requires an additional `IReadOnlyList<VectorSearchResult> retrievalCandidates` parameter.
- **`RagQueryDiagnostics` property renames** — `AppliedTopK` → `FinalTopK`, `RetrievalK` → `RetrievalTopK`, `AppliedMinScore` → `AppliedFinalMinScore`.
- **`LlmQueryRewriter.RewriteAsync` returns `QueryRewriteResult`** instead of `Task<string>` — includes search gate decision (`NeedsSearch`).
- **`RagQueryOptions` restructured** — `int? TopK`, `double? MinScore`, `string? Namespace` replaced by `RagFilter FinalFilter`, `RagRetrievalDerivation RetrievalDerivation`, `string Namespace`.
- **`RagQueryResult` constructor** now requires `retrievalCandidates` parameter (internal but affects custom pipeline implementations).

### Added

- **`VllmEmbeddingProvider`** — vLLM-compatible OpenAI-style embedding provider (`/v1/embeddings`). Configurable model, dimensions, and base URL.
- **`VllmReranker`** — vLLM-compatible reranker (`/v1/rerank`). Supports Qwen3-Reranker and other vLLM-served models.
- **`RagStore.QueryAsync` with conversation history** — new overloads accepting `IReadOnlyList<ConversationTurn>?` for integrated query rewriting + search gate in a single call.
- **`RagStore.SetQueryRewriter(IQueryRewriter?)`** — set or clear the query rewriter at runtime without rebuilding.
- **Search gate in `LlmQueryRewriter`** — returns `[PASS]` for greetings/chitchat/non-search queries, skipping the RAG pipeline entirely (`RagProcessedQuery.SearchSkipped = true`).
- **Progress reporting** — `RagQueryOptions.ProgressAsync` callback invoked when the pipeline enters each `RagProgressStage` (`QueryRewrite`, `Embedding`, `Filtering`, `Retrieval`, `Reranking`, `ContextBuild`).
- **Final MinScore filtering** — after re-ranking, results below `FinalFilter.MinScore` are discarded before context building.
- **`RagBuilder.WithRetrievalMultiplier(int)`** — configure retrieval candidate multiplier at build time.
- **`RagBuilder.WithRetrievalMinScore(double)`** — configure retrieval-stage score threshold at build time.
- **`RagProcessedQuery.RetrievalCandidates`** — raw retrieval candidates before re-ranking.
- **`RagProcessedQuery.SearchSkipped`** — indicates the search gate bypassed the RAG pipeline.
- **`RagProcessedQuery.RewriteResult`** — raw `QueryRewriteResult` from the query rewriter.
- **`RagQueryDiagnostics.AppliedRetrievalMinScore`** — retrieval-stage score threshold.
- **`RagQueryDiagnostics.RewriteElapsedMs`** — time spent on query rewriting.

### Changed

- `RagStore` constructor simplified — `queryRewriterEnabled` parameter removed; rewriter is now managed via `SetQueryRewriter()`.
- `RagBuilder` now builds `RagQueryOptions` with `FinalFilter`/`RetrievalDerivation` structure instead of flat properties.
- `MarkdownTextSplitter` — removed unused `IsAtomicBlock` private method.

### Migration Guide

```csharp
// Before (v4.0)
store.UpdateOptions(opt =>
{
    opt.TopK = 8;
    opt.MinScore = 0.4;
    opt.RetrievalMultiplier = 3;
    opt.PromptTemplate = "...";
});

// After (v5.0)
store.UpdateOptions(opt =>
{
    opt.DefaultQuery.FinalFilter.TopK = 8;
    opt.DefaultQuery.FinalFilter.MinScore = 0.4;
    opt.DefaultQuery.RetrievalDerivation.TopKMultiplier = 3;
    opt.PromptTemplate = "...";
});
```

```csharp
// Before (v4.0)
var result = await ragStore.QueryAsync("query", new RagQueryOptions { TopK = 15, MinScore = 0.2 });
Console.WriteLine(result.AugmentedPrompt);
Console.WriteLine(result.Diagnostics.AppliedTopK);

// After (v5.0)
var result = await ragStore.QueryAsync("query",
    new RagQueryOptions { FinalFilter = new RagFilter { TopK = 15, MinScore = 0.2 } });
Console.WriteLine(result.RequestMessageContent);
Console.WriteLine(result.Diagnostics.FinalTopK);
```

---

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
