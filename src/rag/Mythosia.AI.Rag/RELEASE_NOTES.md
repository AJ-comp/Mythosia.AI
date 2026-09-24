# Mythosia.AI.Rag - Release Notes

## Unreleased

> This section describes unreleased source changes. The next release version has not been assigned; versioned entries below retain their original release history.

### Added

- `RagEnabledService.WithSpeed(InferenceSpeed)` configures provider processing for the next generated answer after retrieval. `LastProcessing` and Run `Processing` report that answer without mixing in internal query rewriting. The supporting provider decides allowed modes; Fast can incur premium charges and unsupported modes fail explicitly. Existing retrieval and embedding modes are unchanged. See [processing speed](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md#inference-speed).
- Optional `Mythosia.AI.Rag.Search.Pixie` 0.1.0-preview can connect through `.UseStore(new PixieInMemoryStore(encoder))` with the current dense embedding provider, keyword/vector modes and configurable hybrid retrieval. Its learned sparse index is local and memory-only; existing RAG defaults are unchanged. See the [PIXIE guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-pixie-search.md).
- Request-based retrieval via `IRagRetriever`, builder `UseRetriever`, pipeline `SetRetriever`, and store `UpdateRetriever`; custom retrievers receive query text, top-K, filters, progress and cancellation without a mandatory embedding.
- `UseKeywordSearch` and `HybridSearchOptions` overloads select text retrieval or explicitly configured weighted RRF. Runtime `RagStore.UseKeywordSearch()` and `UpdateRetrievalStrategy(HybridSearchOptions)` are available.

### Changed

- Query preparation belongs to the retriever; document ingestion continues to create dense embeddings. Standard and Agentic RAG share retrieval configuration, and a missing lexical override uses the full query in built-in text/hybrid retrieval.
- Unsupported search modes/settings now fail explicitly. Existing `IRetrievalStrategy` retains dense-input behavior through an adapter; default Pinecone native hybrid remains supported. New configurable hybrid results consistently use normalized weighted RRF, including single active legs. No analyzer or existing-index migration is included.


### Fixed

- **Respect Ada embedding dimensions:** `OpenAIEmbeddingProvider` omits the unsupported `dimensions` field for `text-embedding-ada-002` in single/batch requests and rejects configured dimensions other than its fixed 1536 with `ArgumentOutOfRangeException` before any API call. `text-embedding-3-small` and `text-embedding-3-large` continue sending their configured dimensions. Public signatures, defaults and model availability are unchanged.
- **Preserve tool results in RAG completion:** The core request-message override stays attached to the initial input of a logical request instead of replacing the newest message after each tool round. Ordinary RAG `GetCompletionAsync` requests keep retrieved context, assistant tool calls and tool outputs together, including image-bearing inputs. Original history and public APIs are unchanged.
- **Preserve RAG message attachments:** `RagEnabledService.GetCompletionAsync(Message)` now retains non-text attachments in the augmented request, matching `StartRunAsync(Message)`. Retrieval continues to use message text, and retrieved context does not overwrite the original message or the user text stored in conversation history. Media support remains provider/model-specific.
- **Keep query rewriting stable during runtime changes:** The direct `RagStore.QueryAsync` overload accepting `conversationHistory` captures the selected rewriter before awaiting progress or rewriting. Disabling or replacing it through `SetQueryRewriter` no longer causes an in-flight query to dereference a cleared rewriter or switch implementations; subsequent queries use the new setting. Public signatures are unchanged.

- **Validate compressed URL documents before replacing content:** `AddUrl` decodes gzip, zlib-wrapped deflate and Brotli, checks compression completion and available format checksums, and rejects unsupported or nested content encodings before embedding or persistence. A successful HTTP transfer with a truncated compression stream no longer replaces the document with partial text. Retains download/decode cancellation and charset/BOM handling. Uses SharpZipLib 1.4.2 for managed DEFLATE decoding and checksums, plus the standard Brotli decoder; no process-wide compression settings change. Reindex any previously corrupted URL documents from their original source.
- **Request Ollama embedding dimensions:** `/api/embed` requests now include the configured `dimensions`, so the default `qwen3-embedding:4b` provider requests 1024 dimensions instead of expecting 1024 from an unconfigured 2560-dimensional response. Constructor defaults and signatures are unchanged. The model/server must support the selected dimension; HTTP errors and mismatched responses remain failures, with no silent resizing or fallback. Rebuild existing indexes when changing embedding models or dimensions.

- **Stable Office/PDF document identity:** Word, Excel, PowerPoint and PDF file loaders use normalized absolute paths as `Source`, matching TXT loading. Relative and absolute registrations derive the same automatic ID. Existing relative-path records need explicit deletion by their old document ID before reindexing, or indexing the complete source set into a new empty collection and switching after validation; reindexing only the new ID in the existing collection does not remove old records. Unrelated records and explicit IDs must be preserved.
- **Validate and capture query vectors:** Built-in dense query retrieval and the legacy strategy adapter validate positive dimensions, exact length and finite values, then immediately copy the returned vector before progress callbacks or search can await. Invalid provider output fails before search; custom retrievers retain responsibility for their own query preparation.
- **Validate Ollama embedding responses:** Direct single/batch calls reject malformed response shape, vector counts, dimensions and non-finite values with `InvalidOperationException`. Supplied `HttpClient` ownership remains with the caller.
- **Cancel URL document loading:** `AddUrl` forwards indexing/build cancellation into the HTTP request and response-body reading rather than waiting for the download to finish. Cancellation does not roll back previously completed document writes.
- **Document-scoped callback examples:** Official persistence callbacks replace records by normalized `document_id` instead of only upserting new chunks, so shorter updates remove old tails. Empty split results still skip the callback and require explicit deletion of the known document ID in custom storage. Atomicity and rollback remain storage-specific.

- **Validate indexing before persistence:** Missing or whitespace-only document IDs fail with `ArgumentException`; malformed splitter output, missing or duplicate chunk IDs within a document fail with `InvalidOperationException` before embeddings, storage or the persistence callback. Valid custom IDs are preserved, and chunk values/metadata are copied before the first embedding request. Global custom-ID collisions across documents are not automatically detected.
- **Validate embedding batches:** Require a positive dimension, exactly one non-null vector per input, matching lengths and finite values before persistence; copy each batch's vectors before requesting the next batch. Validation failures preserve the affected document's previous records and skip custom persistence. OpenAI response indices are required, checked and reordered; vLLM retains fully index-free response compatibility while rejecting partially missing or invalid indices. These checks and response-order corrections do not automatically recover previously overwritten content or incorrectly paired stored vectors; reindex affected documents from their original sources.
- **Custom splitter examples:** All 13 documentation languages now assign unique document-based chunk IDs and inherit document metadata, preserving filters and preventing silent overwrites caused by omitted IDs.
- **Recursive separator allocation:** Avoid repeatedly copying an unchanged segment when distinct separators only match its beginning. Existing chunk output is preserved while large separator configurations allocate substantially less memory.

- **Bounded Markdown context:** Repeated headings, table headers and prose labels count toward a per-document output budget of `max(65536, 32 × document.Content.Length)` UTF-16 code units. Excessive expansion fails with `InvalidOperationException` before constructing it, without silently truncating content or returning partial chunks. Default RAG indexing preserves existing records when splitting fails.
- **Paragraph-aware labels:** Bold text on a soft-wrapped line inside an existing paragraph is no longer promoted to a label, repeated across chunks or separated by an inserted blank line. Labels require a paragraph or structural boundary.
- **Recursive separator settings:** Duplicate separators are applied once in first-occurrence order, and an explicit work stack replaces nested recursive calls so long settings do not multiply repeated passes or consume the call stack.

- **Reserved document identity:** Stored records and custom persistence callbacks receive copied metadata with `document_id` normalized to the actual `RagDocument.Id`. Conflicting caller metadata cannot redirect the document replacement filter; input document and splitter dictionaries remain untouched. Previously mis-tagged records are not repaired automatically: rebuild from trusted sources or perform scoped cleanup before reindexing.
- **Stable embedding batches:** Document indexing validates that `EmbeddingBatchSize` is positive and captures it for the call before embedding or replacement. Invalid sizes cannot loop over empty batches, and changes during an awaited operation do not skip chunks.
- **Markdown semantic isolation:** Only standalone bold prose labels repeat within their text block; tables, fences, headings and subsequent labels end that scope. Bold table cells are not repeated as conditions for other rows or later prose. Opening-fence indentation is preserved with the code, and opening-fence metadata is parsed once per block, removing repeated scans for pathological long fences.

- **TXT/Markdown chunk correctness:** Character, Recursive and Token splitters reject invalid sizes and negative overlap rather than hanging or skipping text. Recursive merging respects the size budget and zero-overlap setting; Character/Token no longer append an overlap-only tail. Character, Recursive and Markdown avoid cutting UTF-16 surrogate pairs (a pair may exceed a size of one).
- **Markdown content and structure:** Disabling heading breadcrumbs preserves original headings, heading-only content is retained, fence closure respects the opening character and length, and GFM table recognition supports optional outer pipes. A parent-heading change closes the preceding child section so its old breadcrumb is not attached to new content, including when `MinSplitHeadingLevel` skips that parent level. Ordinary content uses the requested budget without an implicit minimum; complete fenced blocks and a table header plus one row may exceed it. Repeated heading breadcrumbs remain outside the content budget.
- **Splitter guidance:** Corrected Markdown constructor examples across all 13 documentation languages, clarified that `TokenTextSplitter` counts whitespace-separated units rather than model tokens, and documented validation, overlap and atomic-block limits. Reindex affected documents and refresh evaluation inputs/caches after chunk-boundary changes; existing stored chunks are not automatically updated. No public method signatures or default splitter selection change.

- **Isolate LLM reranking requests:** `LlmReranker` now applies request-scoped stateless execution so a shared AI service does not include earlier document assessments, existing conversation history, or stored summaries of earlier conversations in later scoring requests. Evaluation prompts and responses are not added to history; service defaults and caller APIs are unchanged. Evaluations by rerankers sharing the same AI service are processed sequentially.

- **Separate same-named files across directories:** `PlainTextDocumentLoader` and `DirectoryDocumentLoader` use normalized absolute file paths as `Source` and automatic document IDs. Equivalent relative/absolute paths reuse an ID; distinct directory roots no longer overwrite each other. Explicit `AddText` IDs, `RagDocument.Id`, custom loader rules and caller APIs are unchanged. Default directory `filename`/`relative_path` metadata remains available for display. Existing relative-path IDs are not automatically migrated or deleted, and default citations may now show absolute paths. See [document identity and index migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#document-identity).

- **Clear stale content after an empty update:** When splitting succeeds with zero chunks, default persistence replaces records for that `document_id` with an empty set. It skips embeddings, preserves other document IDs and allows later nonempty updates under the same ID. Exceptions before storage and cancellation observed before the storage call preserve that document's records; rollback after storage starts remains store-specific. Empty loader results are not deletion instructions, and `onDocumentEmbedded` keeps its existing zero-chunk behavior (no callback or default-store access). Public APIs are unchanged. See [empty document updates](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#empty-document-updates).

## v8.0.0

> This coordinated major release changes public contracts. See the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) before upgrading the package family.

### Added

- Forwards the major `AIRun.Result` change to `Task<AIRunResult>` through the existing RAG Run wrapper. Callers read `.Text` for the answer or inspect the inner execution's reported usage, citations, model, rounds, and finish details without a stream reader. Retrieval/embedding usage is not added to model token usage. RAG completion remains `Task<string>` and the package remains independent of the full core implementation. See [migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result).
- **Ordinary completion cancellation:** RAG completion overloads propagate the caller token through retrieval, query rewriting and the inner completion call. Cancellation skips the subsequent model request when retrieval is cancelled. The wrapper implements the updated `IAIService` signatures without depending on the full core implementation. Cooperative retrieval and tool cleanup retain the common cancellation limits. See [the common contract](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation).

- `WithAgenticRag` forwards the execution cancellation token into `RagStore.QueryAsync`. Search failures remain available in diagnostic traces and propagate to the common executor as failed tool results; they no longer return successful error strings. Cancellation remains cooperative in retrieval components.

- Perplexity standard 0.6B/4B embedding providers for ordinary RAG retrieval, with a `UsePerplexityEmbedding` builder extension. Signed int8 vectors are decoded and normalized for the existing float-vector interface.
- Separate contextualized 0.6B/4B embedding APIs retain each document's ordered chunks instead of flattening unrelated documents. Packed binary embeddings use an explicit result type and Hamming distance; they are not silently converted into float embeddings. See the [Perplexity guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/perplexity.md).

### Internal

- Rebuilt against `Mythosia.AI.Abstractions` v4.0.0 for the coordinated major release, including updated completion cancellation and rich Run-result contracts. RAG request-feature scopes let a supporting inner service retain captured provider options while retrieval and query rewriting run.

### Compatibility

- Existing retrieval APIs remain available; the Perplexity embedding APIs are additions. Contextualized embeddings do not implement the flat `IEmbeddingProvider` contract. Requires `Mythosia.AI.Abstractions` v4.0.0+ and `Mythosia.AI.Rag.Abstractions` v6.2.0+.
- RAG still depends on the lightweight contracts and does not acquire a dependency on the full core implementation.

---

## v7.6.0

### Added

- **Control a RAG answer with Run:** `RagEnabledService.StartRunAsync` retrieves documents once, forwards the augmented request to the inner service, and returns the same `AIRun` for text callbacks, output events, the collected result, cancellation, and supported steering. Message content and request context are preserved. Steering does not repeat the initial retrieval.
- **Reasoning and hosted search:** `WithReasoning`, `WithWebSearch`, and `WithFileSearch` configure the final answer, including completion, structured-output repair, and Run paths. Provider sources are available through `LastCitations` and the returned run; RAG retrieval references remain on `RagProcessedQuery`.
- **Agentic RAG with Run:** existing `WithAgenticRag` tools work with `StartRunAsync` and the configured function-round policy when additional model-directed searches are needed.

### Changed

- Request features are captured before retrieval and consumed for one logical request. Internal query rewriting does not inherit final-answer search settings; retrieval and validation failures do not leak pending options to the next request.
- NuGet packages now include these full release notes and symbol packages, with repository and license metadata.

### Fixed

- **Duplicate-source filtering:** when every input document path has already been processed, `RagBuilder` now returns an empty list. Overlapping file and directory registration no longer re-embeds those documents or changes their splitter selection through a later registration.

### Compatibility

- Requires `Mythosia.AI.Abstractions` v3.1.0+ and `Mythosia.AI.Rag.Abstractions` v6.2.0+. The package continues to depend on the lightweight AI contracts, not the full `Mythosia.AI` implementation.
- Existing completion and streaming APIs remain callable. Run requires an inner service implementing optional `IAIRunService`; common request features require `IAIRequestFeatureService`. Unsupported capabilities fail explicitly.
- See the [Run guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md) and [reasoning/search guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/reasoning-and-search.md) for provider support and migration examples.

---

## v7.5.0

### Fixed

- **`RagPipeline.QueryAsync(query, topK, filter, ct)` silently dropped `ProgressAsync` and `StoreFilter`** — the convenience overload manually rebuilt `RagQueryOptions` from `Options.DefaultQuery` but only copied `FinalFilter`, `RetrievalDerivation`, and `FinalSelection`. Any tenant/permission scope set on `DefaultQuery.StoreFilter` and any progress callback on `DefaultQuery.ProgressAsync` were lost whenever a caller used the `topK`-only overload. The overload now uses `RagQueryOptions.Clone()` (introduced in `Mythosia.AI.Rag.Abstractions` v6.2.0) and overrides only `FinalFilter.TopK`, preserving every other configured field.
- **Race condition on `PromptTemplate` cache** — `RagPipeline` cached the resolved `IContextBuilder` against `Options.PromptTemplate` to skip per-query allocation. With `RagStore.UpdateOptions` allowing runtime template changes while queries are in flight, two correlated fields (`_cachedPromptTemplate`, `_resolvedContextBuilder`) could tear, briefly returning the previous builder against the new template. The cache has been removed entirely — `TemplateContextBuilder` construction is a single reference assignment, dwarfed by the embedding/search I/O each query already incurs, so the cache existed for negligible benefit at the cost of a thread-safety hazard.

- **`RagStore.UpdateOptions` snapshot safety** - runtime option updates now configure a cloned `RagPipelineOptions` instance and atomically swap the completed snapshot into the pipeline, so in-flight queries do not observe partially-mutated option objects.

### Compatibility

- Requires `Mythosia.AI.Rag.Abstractions` v6.2.0+.
- No public API changes in `Mythosia.AI.Rag`. Existing callers see strictly more correct behavior — the previously-lost fields are now honored, and `PromptTemplate` updates take effect deterministically on the next query.

---

## v7.4.0

### Added

- `WithAgenticRag(..., queryOptions: ...)` now supports per-tool-call `RagQueryOptions`, enabling Agentic RAG permission filters such as `StoreFilter`.
- `WithAgenticRagTracing(...)` + `AgenticRagSearchTrace` provide structured step-level access to each Agentic RAG search query, references, candidates, diagnostics, and failures.
- `AgenticRagQueryContext` gives `queryOptions` access to the current tool name and self-contained search query for dynamic per-step filtering or retrieval policy selection.

---

## v7.3.2

### Changed

- Recompiled for the `Mythosia.AI` v6.1.0 release line.
- No changes to `Mythosia.AI.Rag` source code, public API, or runtime behavior.

---

## v7.3.1

### MarkdownTextSplitter Improvements

- **Bold label propagation** — `**label**` context is now tracked and prepended to all subsequent chunks within a section, regardless of where the split occurs (`MergeBlocksIntoChunks` or `SplitOversizedBlock`). Propagation logic moved to `ChunkSections` for universal coverage.
- **Cascading split** — oversized text blocks are split in stages: paragraph (`\n\n`) → line (`\n`) → word boundary (space), minimizing mid-word breaks.
- **50-char buffer margin** — chunk budget reserves 50 characters for breadcrumb and label overhead, preventing chunk size overflow.
- **Method renames** — `SplitContentBlocks` → `MergeBlocksIntoChunks`, `SplitLargeText` → `SplitOversizedBlock` for clarity.

### Dependency Updates

- Recompiled against `Mythosia.Documents.Office` 1.0.1, `Mythosia.Documents.Pdf` 1.1.1 (both updated for `Mythosia.Documents.Abstractions` 1.1.0).

---

## v7.3.0

### Breaking Changes

- **`RagBuilder.WithNamespace()` removed.**
- **`HealthCheckResult.Namespace` removed** — constructor changed from `(string @namespace, int totalChunks, items)` to `(int totalChunks, items)`.

### Changed

- **Internal namespace/scope handling migrated to Metadata** — follows `Mythosia.VectorDb.Abstractions` v4.0.0.
  - `RagPipeline.BuildAsync` now writes namespace/scope to `Metadata["namespace"]` / `Metadata["scope"]` instead of `VectorRecord.Namespace` / `VectorRecord.Scope`.
  - `RagPipeline.QueryAsync` now applies namespace filter via `VectorFilter.Where("namespace", ns)` instead of `VectorFilter.Namespace`.
  - `RagPipeline.DeleteDocumentAsync` updated similarly.
  - `HybridRetrievalStrategy.WithoutMinScore` no longer copies removed `Namespace`/`Scope` properties — conditions are preserved via `AppendConditionsFrom`.
  - `RagDiagnostics.DiagnoseQueryAsync` fallback filter updated.
  - `RagDiagnosticSession` error message: "namespace/metadata" → "metadata".
  - `HealthCheckResult.ToReport()`: `Namespace: "default" (N chunks)` → `N chunks indexed`.
- **Default indexing now uses `ReplaceByFilterAsync`** — when no `onDocumentEmbedded` callback is provided, `IndexSingleDocumentAsync` now calls `ReplaceByFilterAsync(Where("document_id", docId), records)` instead of `UpsertBatchAsync(records)`.
  - Fixes stale chunk problem: re-indexing a file that produces fewer chunks no longer leaves orphan chunks from the previous version.
  - The operation is atomic (transactional in stores that support it) — on failure, existing data remains intact.
  - `onDocumentEmbedded` callback behavior is unchanged — when provided, it still replaces the default persistence logic entirely.

### Compatibility

- Requires `Mythosia.AI.Rag.Abstractions` v6.1.0, `Mythosia.VectorDb.Abstractions` v4.0.1.

---

## v7.1.0

### Added

- **`WithAgenticRag<TService>(RagStore, string?, string?)`** — new extension method on `AgenticRagExtensions` that registers the `RagStore` as a callable search tool on any AI service implementing both `IAIService` and `IFunctionRegisterable`.
  - Registers a `search_documents` function (name configurable via `toolName`) in the agent's function list.
  - Inside the tool handler, `RagStore.QueryAsync(query)` is called directly — `QueryRewriter` is intentionally bypassed. The agent formulates its own self-contained search query as part of its ReAct reasoning.
  - Returns all retrieved excerpts with source metadata as a formatted string for the agent to reason over.
  - When no results are found, returns a descriptive fallback message so the agent can decide to retry with a different query.
  - Tool description is customizable via `toolDescription` parameter; defaults to a domain-agnostic description that instructs the agent to use self-contained queries.
  - Fully compatible with combining other tools via `WithFunction` / `WithFunctionAsync`.

### Compatibility

- Requires `Mythosia.AI.Abstractions` v1.1.0.

### Usage

```csharp
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .UseOpenAIEmbedding(apiKey));

// Basic: RAG as the only tool
var service = new ClaudeService(apiKey, http);
service.WithAgenticRag(ragStore);
var answer = await service.RunAgentAsync("Summarise the refund policy.");

// Combined with other tools
service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Look up an order by ID.",
           ("order_id", "The order ID.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// Custom tool description for better domain-specific selection
service.WithAgenticRag(ragStore,
    toolDescription: "Search HR policies and product manuals.");
```

### Design Notes

- `QueryRewriter` set on the `RagStore` is intentionally not invoked. The agent's own ReAct reasoning replaces the rewriter's role — it produces a clean, standalone query before calling the tool.
- Existing `WithRag()` / `RagEnabledService` flows are completely unaffected.
- Requires `Mythosia.AI.Abstractions` v1.1.0 and `Mythosia.AI` v5.3.0 (both implement `IFunctionRegisterable`).

### Compatibility

- No breaking changes to any existing API.
- Requires `Mythosia.AI.Abstractions` v1.1.0 for `IFunctionRegisterable`.

---

## v7.0.1

### Changed

- **Mythosia.Documents.Pdf** dependency updated to v1.1.0 — structured extraction improvements including font-size based heading detection, bullet/numbered list recognition, and spatial paragraph grouping.

---

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
