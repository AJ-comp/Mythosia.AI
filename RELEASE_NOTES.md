# Mythosia.AI workspace release notes

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
