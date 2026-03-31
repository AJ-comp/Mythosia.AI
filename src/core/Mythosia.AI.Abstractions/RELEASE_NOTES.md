# Mythosia.AI.Abstractions - Release Notes

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
