# Mythosia.AI.Abstractions - Release Notes

## v3.0.0

> This is the abstractions release paired with Mythosia.AI v7. Follow the [v7 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v7-migration.md) before upgrading.

### Added

- **Claude adaptive-thinking controls** — `ClaudeReasoningEffort` represents `Auto`, `Low`, `Medium`, `High`, `XHigh`, and `Max`; `ClaudeThinkingDisplay` represents omitted or summarized reasoning output.

- **GPT-5.6 model contract** — `AIModels.OpenAI` now includes `Gpt5_6` (the Sol alias), `Gpt5_6Sol`, `Gpt5_6Terra`, and `Gpt5_6Luna`. `Gpt5_6Reasoning` adds the model family's `Max` effort, and `Gpt5_6ReasoningMode` represents standard or pro reasoning execution without inventing a `gpt-5.6-pro` model ID.
- **Claude 5 model IDs** — `AIModels.Anthropic.ClaudeOpus5` and `ClaudeSonnet5` expose the generally available `claude-opus-5` and `claude-sonnet-5` API models.
- **Claude Mythos 5 model ID** — `AIModels.Anthropic.ClaudeMythos5` exposes `claude-mythos-5` for approved Project Glasswing customers. Mythos 5 is limited availability, not generally available.

- **`IImageGenerationService`** is an optional, provider-neutral contract for services that generate or edit images. Its `DefaultImageModel` is deliberately independent from `IAIService.Model`, so selecting a chat model cannot accidentally select an image model.
- **Image request and result models** — `ImageGenerationRequest`, `ImageEditRequest`, `ImageInput`, `GeneratedImage`, and `ImageGenerationResult` describe multi-image generation, ordered reference images, optional masks, output controls, provider/model provenance, request IDs, and token usage without adding a provider dependency.
- **Current OpenAI image model IDs** — `AIModels.OpenAI.GptImage2` exposes the `gpt-image-2` alias and `GptImage2_260421` exposes its current `2026-04-21` snapshot without adding either image model to chat-model selectors.
- **Current Gemini model IDs** — `AIModels.Google` adds Gemini 3.6 Flash and Gemini 3.5 Flash-Lite, plus an image-specific catalogue for Gemini 3.1 Flash Image, Gemini 3.1 Flash-Lite Image, and Gemini 3 Pro Image.
- **Current xAI model IDs** — `AIModels.xAI` adds Grok 4.5, its `grok-4.5-latest` alias, `grok-build-latest`, and the current Grok 4.3 aliases `grok-4.3-latest` and `grok-latest`.
- **Current Grok reasoning contract** — `GrokReasoning` now represents `Auto`, `None`, `Low`, `Medium`, and `High`. `Auto` omits the provider parameter, Grok 4.3 supports `None` through `High`, and Grok 4.5 supports `Low` through `High` and cannot disable reasoning.
- **Gemini safety controls** — `GeminiSafetyThreshold` represents provider-default, disabled, and the supported blocking thresholds without coupling the common image or chat contracts to Google request types.
- **Typed function-call batches** — `FunctionCallBatch`, `FunctionCallResult`, and `FunctionCallResultBatch` preserve every call from one assistant response, its provider correlation data, and the matching ordered results. `Message` and `StreamingContent` expose typed function call/result fields instead of requiring consumers to reconstruct them from metadata.
- **Function execution modes** — `FunctionExecutionMode` and `FunctionCallingPolicy.ExecutionMode` select sequential or bounded-parallel handler execution. Sequential remains the default; `MaxConcurrency` limits parallel handlers while result ordering remains stable.

### Changed

- **Nullable annotations match the actual contracts** — optional attribute overrides, function handlers, schema descriptions/defaults/enums/items, and other provider-populated values are explicitly nullable. Required names and type identifiers retain non-null defaults, eliminating false consumer warnings without changing the CLR signatures.

### Removed

- **`ChatBlock.RemoveFunctionMessages()`** — removing function calls while retaining their results creates an invalid conversation. Clear or rebuild the conversation explicitly when a model switch requires a new history.
- **Retired or deprecated OpenAI model IDs** — `Gpt4Vision`, `Gpt4oLatest`, `Gpt5ChatLatest`, `Gpt5_2Codex`, `Gpt5_250807`, `Gpt5Mini_250807`, `Gpt5Nano_250807`, and `Gpt4_1Nano` were removed instead of being retained as obsolete aliases.
- **Retired Claude Opus snapshots** — `AIModels.Anthropic.ClaudeOpus4_250514` (`claude-opus-4-20250514`) and `ClaudeOpus4_1_250805` (`claude-opus-4-1-20250805`) were removed. Opus 4 retired on June 15, 2026, and Opus 4.1 retired on August 5, 2026. Use `ClaudeOpus4_8` for the official direct replacement.
- **`GrokReasoning.Off`** — use `Auto` to preserve the provider default without serializing `reasoning_effort`, or `None` for an explicit non-reasoning Grok 4.3 request. `None` is invalid for always-reasoning Grok 4.5.

### Compatibility

- Breaking release for the removed `ChatBlock` member, retired or deprecated model constants, and `GrokReasoning.Off`. Consumers of the new image contract and typed function batches require `Mythosia.AI.Abstractions` v3.0.0 or later.

---

## v2.5.0

### Added

- **`ContextLengthExceededException`** — the provider rejected the request because the prompt exceeded the model's context window. Carries the window and the rejected prompt's size when the provider reported them, the HTTP status, how many compaction attempts were made, and `RecoverySkipReason` — why recovery could not save it. `StreamAsync` never throws it; there the overflow arrives as an error chunk instead. The legacy callback API `StreamCompletionAsync`, which has no round loop, does throw it.
- **`AIHttpErrorFactory`** — translates a provider's HTTP failure into that exception, or a plain `AIServiceException` when the body says something else, and builds the metadata for streaming error chunks. Every provider words its errors differently, so detection belongs here rather than in the core or the consuming app: the app should only ever learn *that* the context overflowed, never how a particular vendor phrases it. Recognises OpenAI's `context_length_exceeded` code, the OpenAI/vLLM `maximum context length is N tokens` wording, Anthropic's `prompt is too long` and combined `input length and \`max_tokens\` exceed context limit` forms, and Google's `input token count (N) exceeds …`.
  - Detection is deliberately narrow and gated to HTTP 400/413. A false positive is worse than a miss: it makes the core compact the conversation, which deletes messages irreversibly, in response to an unrelated failure. Rate limits and server errors are never considered.
  - `BuildErrorMetadata` only adds keys — `error` and `status_code` are always present, so existing readers are unaffected.

### Deprecated

- **`ChatBlock.RemoveFunctionMessages()`** now names its removal version (**v7.0**) instead of "future versions". Dropping function messages leaves function results without their originating call, which the chat/completions wire format rejects.

### Compatibility

- Additive minor release; no breaking changes.

---

## v2.4.0

### Added

- **`AIModels.Anthropic.ClaudeFable5`** — `claude-fable-5`, Anthropic's new top model tier above Opus (1M context window, 128K max output).

### Compatibility

- Additive minor release; no breaking changes.

---

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
