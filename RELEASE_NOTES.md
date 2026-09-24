# Mythosia.AI workspace release notes

## Unreleased

> This section describes unreleased source changes. The next release version has not been assigned; versioned entries below retain their original release history.

### Added

- **GPT-6 Sol and Luna:** `AIModels.OpenAI.Gpt6Sol` / `Gpt6Luna` select the new models through completion, streaming, structured output, image input, local tools and Run. Sol/Luna support `None` through `Max` except `Minimal`, default to `Medium`, and send sampling only with `None`; Astra keeps mandatory reasoning. Native async tools, WebSocket steering, Standard single-agent cache-preserving updates, paid Fast processing, capability inspection and Chat UI selection are connected. Service defaults and published package versions are unchanged. See [model selection and requirements](docs/providers.md#gpt-6-sol-luna).

- **DeepSeek Responses, Files and V4 Pro:** select text-only `AIModels.DeepSeek.V4Pro`, or retain the default vision-capable Flash. Opt-in `UseResponsesApi` reuses completion, streaming, Run, typed output and local tools with captured full-history replay. Image upload, metadata, listing and deletion plus immutable `DeepSeekImageFileContent` enable Flash image reuse through either transport. Chat UI and RAG rewriting use the current model catalogue and migrate the former `DeepSeekChat` UI label without rewriting arbitrary model IDs. Chat Completions remains the default; background storage, hosted search, native asynchronous tools, steering and image generation are outside this addition.

- **Grok 4.7:** explicit `AIModels.xAI.Grok4_7` selection through existing completion, streaming, Run, local tools, structured output and image input. Low/Medium/High/XHigh reasoning, model capabilities, Chat UI and paid priority processing are connected; Auto omits effort and uses the provider High default. Grok 4.5 remains the service default, and all earlier supported models remain available. This does not expose the Cursor/Grok Build-only Fast variant or Responses-only features. See [usage and scope](docs/providers.md#grok-47).

- **Per-request processing speed:** immutable `WithSpeed(InferenceSpeed.ProviderDefault/Standard/Fast)` builder branches and next-request service extensions select provider processing without changing model or reasoning effort. Supported Anthropic, OpenAI, xAI and Gemini Developer API combinations map to native fast/priority modes; capability inspection retains Supported/Unsupported/Unknown and does not prove account access. Explicit unknown or unsupported modes fail without a client fallback. Fast can cost more and the provider may report a lower tier. `AIService.LastProcessing` and `AIRunResult.Processing` retain immutable per-inference-attempt `AIProcessingInfo` (including server continuations), with requested/applied modes, raw mode and response ID; absent reporting stays unknown. Internal auxiliary calls remain isolated. See [speed selection and observed results](docs/request-building.md#inference-speed).
- **Claude Opus 5.5:** `AIModels.Anthropic.ClaudeOpus5_5` selects `claude-opus-5-5` through existing completion, streaming, Run, tools and common reasoning paths. Untouched settings use medium adaptive effort with readable thinking omitted; low/medium/high/xhigh/max, explicit summarized/progress display, per-message effort, turn instructions and binding diagnostics use existing contracts. Preserve signed thinking, including empty blocks, across turns and tool results. Direct edits to stored Opus 5.5 assistant response content fail before HTTP; send corrections as new user input or start a fresh conversation. None/Minimal, forced tools and assistant prefills are unsupported; sampling is omitted. The service default model is unchanged. Native computer toolsets, task budgets, inline tool changes, compaction and automatic server fallback are outside this addition. See [usage and migration](docs/providers.md#claude-opus-55).
- **Shared retrieval evaluation infrastructure:** versioned document/query datasets, graded relevance and no-answer metrics, pluggable search adapters, reusable embedding cache, unique JSON/CSV reports and compatible-run regression gates. Deterministic tests and an offline golden baseline run in CI; a manual workflow runs PIXIE or paid OpenAI comparisons. Existing PIXIE benchmark entry points delegate to the shared runner.
- **Optional local PIXIE search preview:** `Mythosia.AI.Rag.Search.Pixie` 0.1.0-preview adds ONNX document/query sparse encoding and `PixieInMemoryStore`, using a bundled, pinned 8-bit quantized model. Compare sparse or weighted-hybrid retrieval with the existing search while retaining the dense embedding provider. The index is memory-only; no persistent-store migration or default search replacement is included. See the [PIXIE guide](docs/rag-pixie-search.md).
- **Request-based RAG retrieval:** `IRagRetriever` / `RagRetrievalRequest`, `UseRetriever`, `SetRetriever` and runtime `UpdateRetriever` let search providers own query preparation. `UseKeywordSearch` skips query embeddings while document ingestion keeps its existing vector workflow.
- **Explicit hybrid settings:** `HybridSearchOptions`, `ITextSearchStore` and `IConfigurableHybridSearchStore` expose keyword search and normalized weighted RRF across InMemory, PostgreSQL and Qdrant. Weights, candidate multipliers, filters and cancellation flow to the selected backend; unsupported combinations fail explicitly.

### Changed

- Standard and Agentic RAG use the same selected retriever. Built-in keyword/hybrid search uses the full query when no lexical override is supplied; existing `IRetrievalStrategy` remains supported through its dense-input adapter. Pinecone retains its default legacy native hybrid route; custom mixed fusion settings are unsupported. Existing backend adapters keep their analyzers and indexes; PIXIE is a separate opt-in package.
- PostgreSQL keyword query construction handles punctuation-only terms without malformed operators while retaining OR semantics; this does not add symbol-sensitive identifiers. Qdrant filters correctly address flat metadata keys, and PostgreSQL empty `NotIn` excludes missing keys.


### Fixed

- **xAI context-overflow detection:** recognize the provider's `[input_too_large]` context-window wording and retain requested/maximum token counts through the existing typed exception and streaming metadata. Real API verification and negative regression cases cover the fix; unrelated size limits and quota failures are not treated as recoverable context overflow.

- **Respect Ada embedding dimensions:** `OpenAIEmbeddingProvider` omits `dimensions` for `text-embedding-ada-002` in single and batch requests, and rejects any configured dimension other than its fixed 1536 before HTTP. `text-embedding-3-small` and `text-embedding-3-large` continue sending configured dimensions. The model remains selectable; no resizing, fallback or default-model change is introduced. See [OpenAI embedding configuration](docs/rag-embedding.md#openai-dimensions).

- **Google image options by model:** Capability lists and generation/editing validation now agree: Flash Image accepts 512/1K/2K/4K and 14 ratios, Flash-Lite Image conservatively accepts 1K and 14 ratios, and Pro Image accepts 1K/2K/4K and 10 standard ratios. All retain `Auto`, which omits the corresponding selector. Unsupported explicit choices throw `NotSupportedException` before HTTP without resizing or fallback. Unknown custom image models retain `Unknown` capabilities and provider-wide option validation. The Flash-Lite 512 documentation discrepancy remains unverified; see the [model matrix and source note](docs/providers.md#google-image-options).
- **InMemory store consistency:** Synchronize records and the BM25 index across concurrent writes, deletes and reads, including both legs of hybrid search. Copy stored and returned records, vectors and metadata so edits require `UpsertAsync` and cannot bypass indexing. Waiting for the store lock is cancellable without itself aborting the operation holding it. Cancellation does not split an already-started record update and can retain completed batch writes; default `ReplaceByFilterAsync` remains sequential and non-transactional. Public signatures and package versions are unchanged.
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

- **RAG indexing boundaries:** Normalize the reserved `document_id` on copied record metadata to the actual `RagDocument.Id`, so replacement and deletion target the same document; caller and splitter metadata remain unchanged. Validate and capture a positive embedding batch size before indexing to prevent empty-batch loops or skipped chunks after setting changes. Existing incorrectly tagged records require scoped cleanup or a rebuilt collection; they are not automatically repaired.
- **Markdown context and code:** Repeat only standalone prose labels within their text block; bold table cells no longer become conditions for other rows or following paragraphs. Preserve opening-fence indentation with the code, and analyze each opening fence once to avoid repeated long-fence scans. Reindex affected documents to update stored chunks.

- **TXT/Markdown chunking:** Reject invalid sizes and negative overlaps; honor Recursive size/zero-overlap settings; preserve Unicode surrogate pairs and original Markdown headings; remove overlap-only trailing chunks. Markdown handles matching fence lengths and GFM tables with optional outer pipes, and resets child-section context when a parent heading changes to prevent stale breadcrumbs. Its content budget excludes repeated heading breadcrumbs and permits whole fenced blocks or a table header plus one row to exceed it. Corrected examples and token-count guidance in all 13 documentation languages. Reindex affected documents and refresh retrieval baselines after boundary changes; see [splitter behavior and limits](docs/text-splitters.md).

- **RAG reranker history isolation:** `LlmReranker` now evaluates each request without reading or appending conversation history, including stored summaries of earlier conversations, preventing previous assessments from entering later requests when an AI service is shared. Service defaults and caller APIs are unchanged. Evaluations by rerankers sharing the same AI service are processed sequentially.

- **RAG document identity:** Built-in text and directory loaders now use normalized absolute file paths as `Source` and automatic document IDs, preventing same-named files in different directories from overwriting each other. Explicit IDs and caller APIs are unchanged. Existing relative-path IDs are not automatically migrated or removed; see [document identity and index migration](docs/rag.md#document-identity).

- **RAG empty document updates:** Successful splitting with zero chunks now clears existing records for the same `document_id` in the default storage flow, without requesting embeddings or modifying other documents. Loading/parsing/splitting failures and cancellation observed before the storage call preserve existing records. Custom persistence callbacks retain their prior zero-chunk behavior; see [empty document updates](docs/rag.md#empty-document-updates) for explicit deletion and storage rollback limits.

### Internal

- **Cross-platform retrieval evaluation:** use explicit LF JSON formatting for reports and compatibility fingerprints so identical Windows/Linux corpora, vectors and settings compare against the same baseline. Refresh the smoke fingerprint after verifying identical results and add a regression test against the committed baseline; retrieval scores and strict mismatch checks are unchanged.
- **Package dependency validation:** include `Mythosia.VectorDb.Abstractions`, `Mythosia.AI.Rag.Abstractions` and `Mythosia.VectorDb.InMemory` in the explicit, dependency-ordered package set alongside the existing six packages. Isolated consumers resolve the new RAG contracts from the same build instead of incompatible published dependencies. Add README/release-note/symbol packaging and provenance metadata to these dependencies. Version numbers remain unchanged pending release preparation; existing-version and source-commit publication guards remain enforced. PIXIE and the other document/vector providers are outside this publication set.

- **Claude failure diagnostics:** preserve context-test failure steps, provider details and original exception stacks; record synthetic context exchanges without authentication headers or thinking blocks, and retain Anthropic speed error details. Add four context-history/refusal regression cases. A focused live recheck passed the original context scenario and confirmed zero Fast quota on both completion and Run; this does not establish a fix for the initial intermittent refusal. See the [follow-up record](tests/Mythosia.AI.Test/validation/2026-09-24-claude-errors.md).

## v8.0.0

This release brings independent request configuration, typed image options, cancellable tool and completion workflows, richer Run results, and local model-capability inspection into one coordinated package update. Start with the [v8 migration guide](docs/v8-migration.md) for before/after examples and required caller changes.

| Package | Previous published version | Release version | Full notes |
| --- | --- | --- | --- |
| Mythosia.AI.Abstractions | 3.1.0 | **4.0.0** | [Contracts](src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| Mythosia.AI | 7.1.0 | **8.0.0** | [Core](src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| Mythosia.AI.Providers.Alibaba | 2.0.1 | **3.0.0** | [Alibaba](src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| Mythosia.AI.Rag | 7.6.0 | **8.0.0** | [RAG](src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| Mythosia.AI.Mcp | 0.0.1-preview | **0.1.0-preview** | [MCP](src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| Mythosia.AI.Serving.Vllm | 1.0.0-preview | **1.0.0** | [vLLM management](src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

The first four packages advance to a new major version because their public or inherited contracts change. MCP remains a prerelease and updates with the same core contract. Serving.Vllm moves from 1.0.0-preview to stable 1.0.0 while retaining its independent control-plane API. RAG.Abstractions, VectorDb and document loaders keep their existing versions; their APIs and dependency direction do not require a coordinated update.

### Added

- **Independent requests:** `CreateRequest` returns an immutable `AIRequestBuilder`. Branch temperature, functions, reasoning, profiles and hosted search without rewriting the service defaults. Conversation ownership and one-active-run limits remain unchanged.
- **Typed image options:** quality, background and output format use enums. `ImageSize.Pixels(...)` and `ImageSize.Preset(...)` distinguish exact dimensions from a resolution class and aspect ratio. Provider/model validation rejects unsupported combinations before sending.
- **Async local tools and cancellation:** synchronous objects, `Task<T>` and `ValueTask<T>` use one result contract. Tools can receive a schema-hidden `CancellationToken`; completion, typed results, builders, message chains and RAG propagate caller cancellation through client work and cooperative tools.
- **Complete Run metadata:** `AIRun.Result` returns `AIRunResult`; read `.Text`, reported usage, citations, requested/actual model, rounds and finish details without observing the stream.
- **Local capabilities:** service defaults and captured request builders expose Supported, Unsupported and Unknown definitions for model controls. Image capabilities are separate; inspection does not call providers or consume next-call settings.
- **Provider updates:** Claude Fable 5.1 and limited-access Mythos 5.1; Gemini 3.7/3.8 Flash; Grok 4.6 effort controls; DeepSeek Flash text, vision and tools; Perplexity Agent, Search and embedding APIs; Grok Imagine Image 2.0; and explicit GPT Image 2.5 Sunburst/Flare generation and editing. See [provider support](docs/providers.md) for implemented combinations and defaults.

### Changed

- **Stable vLLM management package:** `Mythosia.AI.Serving.Vllm` advances from `1.0.0-preview` to `1.0.0`. The existing public API and runtime behavior remain unchanged.
- Request snapshots preserve JSON ownership, standard collection types, array shape, key comparers, cycles and shared references. Invalid recursive tool schemas fail normally during capture.
- Explicit provider token totals are retained; overflowing accumulated counters fail instead of returning wrapped values. Final aggregation failures still settle Run results and clean up resources.
- The shared OpenAI-compatible and DeepSeek stream paths reject data after a terminal response while accepting its final delta and later usage-only events. Failed rounds do not become successful history or tool executions; earlier external actions are not undone.
- Image uploads capture caller-owned bytes, and Google image results reject incomplete candidates or malformed inline image declarations. Structured-output retry limits cannot overflow or leave next-call options for an unrelated request.
- MCP preserves pending calls when skipping malformed responses, rejects new work after disposal or reader termination, and coordinates concurrent cleanup even when cancellation callbacks throw.
- Perplexity moves from legacy Sonar-specific APIs to Agent contracts. Active model choices omit retired/retiring aliases; retained DeepSeek and GPT-5/o3 constants carry warning-only obsolete annotations and keep their wire values. Audio transcription uses `gpt-transcribe`.
- NuGet descriptions, release-note metadata, README links and the dependency-ordered publication set cover all six release packages, including the MCP preview and stable Serving.Vllm.

### Compatibility

- Read `(await run.Result).Text` where a Run previously returned a string. `GetCompletionAsync` and the existing public streaming entry points remain available; this release does not remove them.
- Rebuild compiled consumers. Custom `IAIService` implementations and overrides of changed completion signatures must append and forward `CancellationToken`. Cancellation stops client work; it does not guarantee remote inference or billing cancellation.
- Replace image option strings with enums and the separate `AspectRatio` property with an `ImageSize` preset. The output default is `ImageOutputFormat.Auto`; choose saved file extensions from `GeneratedImage.MediaType`. The adapters do not transcode images.
- Replace `async void` tools with `Task` or `ValueTask`. Handle `McpException` for direct MCP tool errors. Preserve provider-reported token totals even when an incomplete component breakdown does not sum to them.
- Follow the [Perplexity migration](docs/perplexity.md) for removed Sonar helpers and the changed `Perplexity.Sonar` wire value. Provider-native steering, asynchronous tools and background jobs remain distinct capabilities.

### Internal

- The final architecture checks passed **2,462 AI unit + 203 RAG + 38 MCP = 2,703 tests**, with no failures or skips. Three adversarial rounds cover snapshots, tool returns, cancellation/cleanup, terminal streams, Run usage, images and capabilities; the last round added 43 regression cases.
- Release builds, actual-module UI regressions and 13-language documentation/DocFX checks passed. The [test guide](tests/Mythosia.AI.Test/README.md) retains detailed red/green evidence and earlier targeted live API records. GPT Image 2.5 and account-resource-specific Perplexity tests remain prepared for later execution; local validation is not a claim that every remote feature was exercised.

---

## v7.1.0

This entry covers the coordinated update below. Package versions and release notes are prepared; this entry does not itself indicate NuGet publication.

| Package | Previous version | New version | Full notes |
| --- | --- | --- | --- |
| Mythosia.AI.Abstractions | 3.0.0 | 3.1.0 | [Contracts](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v310) |
| Mythosia.AI | 7.0.0 | 7.1.0 | [Core](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v710) |
| Mythosia.AI.Rag | 7.5.0 | 7.6.0 | [RAG](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v760) |
| Mythosia.AI.Providers.Alibaba | 2.0.0 | 2.0.1 | [Alibaba](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v201) |

### Added

- **GPT-6 Astra:** model registration, Responses routing, reasoning effort, Standard/Pro mode, verbosity, reasoning summaries, request validation, and Chat UI integration.
- **Native asynchronous tools:** opt-in function declarations and supported Astra execution allow the model to continue while a handler is pending. Other models retain the existing wait-for-result path.
- **Run control:** `StartRunAsync` returns an `AIRun` with `Result`, text callbacks, event streaming, cancellation, and supported `SteerAsync` input. Registered functions follow the existing round policy, including Agentic RAG and MCP tools.
- **Common reasoning and hosted search:** `WithReasoning`, `WithWebSearch`, and `WithFileSearch` translate supported requests to OpenAI, Anthropic, and Google APIs. Provider citations remain available with completed answers and Run.
- **Cache-preserving reasoning changes:** supported Astra Standard single-agent and Claude models can change effort between responses while retaining an eligible conversation prefix. Unsupported combinations fail explicitly.
- **RAG integration:** Run retrieves once before generation; common request features apply to the final answer without leaking into query rewriting or later requests. Agentic RAG tools can perform further searches during the run.

### Changed

- `RunAgentAsync` and `RunAgentStreamAsync` retain their behavior and now emit non-error `[Obsolete]` warnings recommending `StartRunAsync` with the desired round policy.
- Provider continuations preserve native reasoning, tool output, and sources. Failed or cancelled unaccepted cache-preserving changes roll back; accepted changes persist.
- Alibaba's completion override now participates in the common request-feature scope and requires the updated core.
- English and all 12 translations explain when Run, reasoning, and search help, followed by examples, provider support, and compatibility details. Package and related feature guides link to the new manuals.

### Fixed

- **RAG duplicate documents:** a source batch containing only already-processed paths now filters to an empty list. Overlapping file and directory registration no longer re-embeds those documents or changes the selected splitter through a later registration.
- **Keyless vLLM embeddings in Chat UI:** the Run button requires an OpenAI key only when OpenAI is the selected embedding provider, matching the server behavior.
- **Translated sample commands:** all 12 translated README files now use `apps/Mythosia.AI.Samples.ChatUi`; the documentation checker detects the retired `samples/` path.

### Internal

- Strict actual-API runners cover native asynchronous tools, Run/Steer, reasoning changes, web search, and hosted file search. File-search fixtures upload generated test documents and clean up their own remote resources.
- Final functional verification recorded on 2026-09-08: 861 core unit tests and 109 RAG tests passed; 27 actual-API cases passed with none skipped (16 reasoning/search, 5 Run/Steer, 6 asynchronous-tool cases).
- All 78 new reasoning/search C# examples compiled. Documentation validation covers all 13 languages; DocFX is checked with warnings treated as errors.
- Release preparation includes RAG in the explicit package set and verifies packaged release notes, source metadata, symbol packages, and dependency minimum versions. Existing package history is preserved.

### Compatibility

- `GetCompletionAsync`, including typed and RAG overloads, remains public and supported. No existing completion or streaming entry point is removed in this update. Input-taking service/RAG streaming is documented for public withdrawal in the next major release; `run.StreamAsync()` observes an existing run.
- New Run and request-feature interfaces are optional for custom `IAIService` implementations. Provider/model capabilities and option combinations are checked before sending a request.
- Native file search uses existing provider-owned stores; it does not upload application files. OpenAI stores and Google stores are not interchangeable. Anthropic has no native file-store adapter, and Google web/file search cannot be combined.
- Cache preservation does not guarantee a cache hit, lower latency, or lower cost. `SteerAsync` supplies an additional instruction to an active supported run; it is separate from changing reasoning between responses.
- RAG continues to depend on `Mythosia.AI.Abstractions`, without a dependency on the full core implementation. MCP has documentation updates only and does not require a new package version for use with the updated core.

See the [Run guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md) and [reasoning/search guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/reasoning-and-search.md) for usage and detailed limits.
