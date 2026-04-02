# Mythosia.AI.Abstractions - Release Notes

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
