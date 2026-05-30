# Mythosia.AI.Abstractions - Release Notes

## v2.3.0

### Added

- **Model id constants — 2026 refresh** (`AIModels`):
  - OpenAI: `Gpt5_5`, `Gpt5_5_260423`, `Gpt5_5Pro`, `Gpt5_5Pro_260423`, `Gpt5Pro`.
  - Anthropic: `ClaudeOpus4_8`, `ClaudeOpus4_7`.
  - Google: `Gemini3_1ProPreview`, `Gemini3_5Flash`, `Gemini3_1FlashLite`.
  - xAI: `Grok4_3`, `Grok4_20Reasoning`, `Grok4_20NonReasoning`, `GrokBuild0_1`.
  - Perplexity: `SonarReasoningPro`.
- **`Gpt5_5Reasoning`** enum — reasoning effort for GPT-5.5 (None/Low/Medium/High/XHigh).

### Removed

- Constants for retired or non-callable models (the underlying models no longer function at runtime):
  - xAI: `Grok4` (grok-4-0709), `Grok4_1Fast` (grok-4-1-fast), `Grok3` (grok-3) — retired 2026-05-15; `Grok4_20MultiAgent` (grok-4.20-multi-agent-0309) — multi-agent API, not chat completions.
  - Anthropic: `ClaudeSonnet4_250514` (claude-sonnet-4-20250514) — retires 2026-06-15.
  - Perplexity: `SonarReasoning` (sonar-reasoning) — removed from the API 2025-12-15.
  - Google: `Gemini3ProPreview` (gemini-3-pro-preview) — shut down 2026-03-09.
  - OpenAI: `Gpt5_3CodexSpark` (gpt-5.3-codex-spark) — not generally accessible via the standard API.

### Compatibility

- Minor release. The removed constants reference models that are already retired/non-functional; code that references them by name requires a one-line update to a current model id.

---

## v2.2.0

### Added

- **Streaming diagnostics primitives** (consumed by `Mythosia.AI`'s new `WithStreamDiagnostics` extension and `ReadSseLinesAsync` helper).
  - `StreamDiagnostics` — observability snapshot for a single SSE round. Fields: `LinesRead`, `DataLinesProcessed`, `ParseFailures`, `AccumulatedTextLength`, `LastRawLine`, `Elapsed`. Lets callers tell "stream silently ended" apart from "transport error after N chunks".
  - `StreamReadException` — wraps the underlying read-time exception (`IOException`, `NotSupportedException`, `ObjectDisposedException`, etc.) and attaches a `StreamDiagnostics` snapshot taken at the moment of failure. `InnerException` preserves the original.
  - `StreamDiagnosticsBuilder` — fluent configurator with independent `OnRawLine(Action<string>)` and `OnComplete(Action<StreamDiagnostics>)` hooks. Consumed by `service.WithStreamDiagnostics(d => d.OnRawLine(...).OnComplete(...))` in `Mythosia.AI`.

### Compatibility

- Additive public API update.
- No existing types or members were removed, renamed, or changed.

---

## v2.1.0

### Added

- **Round-scoped streaming usage**
  - Added `StreamingContentType.RoundUsage`.
  - Added `StreamingContent.RoundIndex` and `StreamingContent.IsFinalRound`.
  - `StreamingContent.Usage` can now carry one-round usage on `RoundUsage` events while `Completion` keeps cumulative run usage.

### Compatibility

- Additive public API update.
- No existing members were removed or renamed.

---

## v2.0.0

### Changed

- **`IAIService.GetCompletionAsync` — 8 overloads → 2** (breaking change)
  - `GetCompletionAsync(string prompt, AIRequestProfile? profile = null, AIRequestContext? context = null)`
  - `GetCompletionAsync(Message message, AIRequestProfile? profile = null, AIRequestContext? context = null)`
  - Migration: callers using `GetCompletionAsync(message, context)` must switch to `GetCompletionAsync(message, context: context)`.

- **`IAIService.StreamAsync` — 6 overloads → 4** (breaking change)
  - `StreamAsync(string prompt, CancellationToken cancellationToken = default)`
  - `StreamAsync(Message message, AIRequestContext? context = null, CancellationToken cancellationToken = default)`
  - `StreamAsync(string prompt, StreamOptions options, CancellationToken cancellationToken = default)`
  - `StreamAsync(Message message, StreamOptions options, AIRequestContext? context = null, CancellationToken cancellationToken = default)`
  - Migration: callers using `StreamAsync(message, context, ct)` must switch to `StreamAsync(message, context: context, ct)`.

---

## v1.1.0

### Added

- **`IFunctionRegisterable`** — new interface that marks an AI service as capable of accepting function (tool) registrations at runtime.

### Documentation

- Added `Relationship to Microsoft.Extensions.AI` section to README — a factual comparison table between `IAIService` and `IChatClient` covering state management, session handling, request parameters, function calling, streaming, conversation summarization, multimodal handling, and token usage tracking.
  - Exposes a single `void AddFunction(FunctionDefinition function)` method.
  - Allows extension packages (e.g., `Mythosia.AI.Rag`) to register tools on `AIService` without taking a direct dependency on the full `Mythosia.AI` core package.
  - `AIService` in `Mythosia.AI` v5.3.0 implements this interface via explicit interface implementation (`IFunctionRegisterable.AddFunction` → `Functions.Add`).

### Purpose

Enables `Mythosia.AI.Rag.WithAgenticRag<TService>()` to constrain its generic parameter to `where TService : IAIService, IFunctionRegisterable`, keeping the RAG package dependency-free from the AI core while still being able to register the RAG search tool.

---

## v1.0.0

### Initial Release

Extracted core abstractions and shared models from `Mythosia.AI` into a standalone zero-dependency package.

### Added

- **`IAIService`** — core interface defining the AI completion and streaming contract.
  - Completion: `GetCompletionAsync` overloads with `string`, `Message`, `AIRequestProfile`, `AIRequestContext`.
  - Streaming: `StreamAsync` overloads returning `IAsyncEnumerable<string>` or `IAsyncEnumerable<StreamingContent>`.
  - Properties: `Model`, `Provider`, `SystemMessage`, `StatelessMode`, `ActivateChat`.

- **Models** — all shared model types previously in `Mythosia.AI`:
  - `Message`, `MessageContent` (`TextContent`, `ImageContent`, `AudioContent`)
  - `ChatBlock` — conversation container with system message and message history
  - `ActorRole` — role enum (`System`, `User`, `Assistant`, `Function`)
  - `AIRequestContext` — per-request context overrides
  - `AIRequestProfile`, `AIRequestPurpose`, `RequestProfiles` — per-request parameter profiles
  - `AIModels` — model identifier constants for all providers
  - `AIProvider` — provider enum
  - `SummaryConversationPolicy` — conversation summarization configuration
  - `StructuredOutputPolicy` — structured output behavior configuration
  - `RoundResult` — function calling round result

- **Streaming types**:
  - `StreamingContent`, `StreamingContentType` — streaming chunk with type discrimination
  - `StreamOptions` — streaming behavior configuration
  - `TokenUsage` — token count data (input, output, cached, reasoning)

- **Function calling types**:
  - `FunctionDefinition` — function schema for LLM tool use
  - `FunctionCallingPolicy`, `FunctionCallMode` — function calling behavior controls

- **Attributes**:
  - `AiFunctionAttribute` — marks methods as AI-callable
  - `AiParameterAttribute` — describes function parameters

- **Enums**: `ReasoningEffort`, `ReasoningSummary`, `GeminiThinkingLevel`, `Verbosity`

- **Exceptions**: `AIServiceException`, `AgentMaxStepsExceededException`

### Purpose

Enables downstream libraries (e.g., `Mythosia.AI.Rag`) to depend on the lightweight contract package instead of the full `Mythosia.AI` implementation, avoiding transitive dependencies on `Azure.AI.OpenAI`, `NJsonSchema`, `TiktokenSharp`, etc.
