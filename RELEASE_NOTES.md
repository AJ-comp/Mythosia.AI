# Mythosia.AI workspace release notes

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
