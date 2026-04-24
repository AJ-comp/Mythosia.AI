# Mythosia.AI.Abstractions

Core contracts and shared models for the **Mythosia.AI** ecosystem.
Defines `IAIService`, all model types, and streaming primitives.
Consumed by `Mythosia.AI.Rag` and any library that needs the AI service contract without pulling in heavy provider implementations.

## Installation

```bash
dotnet add package Mythosia.AI.Abstractions
```

Install this package directly only when writing a **library that depends on the AI service contract** (e.g., RAG orchestration, custom middleware).
Applications normally take a transitive dependency through `Mythosia.AI`.

---

## Core Interface

### `IAIService`

The central abstraction for AI completion and streaming.

```csharp
public interface IAIService
{
    string Model { get; }
    string Provider { get; }
    string SystemMessage { get; set; }
    bool StatelessMode { get; set; }
    ChatBlock ActivateChat { get; }

    // Completion
    Task<string> GetCompletionAsync(string prompt);
    Task<string> GetCompletionAsync(Message message);
    Task<string> GetCompletionAsync(Message message, AIRequestContext context);
    // ... additional overloads with AIRequestProfile

    // Streaming
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default);
    IAsyncEnumerable<StreamingContent> StreamAsync(Message message, StreamOptions options, CancellationToken ct = default);
    // ... additional overloads
}
```

All concrete providers (`OpenAIService`, `AnthropicService`, `GoogleAIService`, etc.) in `Mythosia.AI` implement this interface.

---

## Models

| Type | Description |
| --- | --- |
| `Message` | A conversation message with role, content, and optional multimodal content |
| `MessageContent` | Base class for multimodal content (`TextContent`, `ImageContent`, `AudioContent`) |
| `ChatBlock` | Conversation container holding system message and message history |
| `ActorRole` | Message role enum (`System`, `User`, `Assistant`, `Function`) |
| `AIRequestContext` | Per-request context overrides (system message prefix/suffix, message override) |
| `AIRequestProfile` | Per-request parameter overrides (temperature, max tokens, stateless mode) |
| `AIModels` | Model identifier constants for all supported providers |
| `AIProvider` | Provider enum (`OpenAI`, `Anthropic`, `Google`, `xAI`, `DeepSeek`, `Perplexity`) |

## Streaming

| Type | Description |
| --- | --- |
| `StreamingContent` | Streaming chunk with content, type, metadata, token usage, and round information |
| `StreamingContentType` | Chunk type enum (`Text`, `Reasoning`, `FunctionCall`, `FunctionResult`, `Status`, `Error`, `Completion`, `RoundUsage`) |
| `StreamOptions` | Streaming behavior options (metadata, function calls, reasoning) |
| `TokenUsage` | Token count data (input, output, cached, reasoning) |
| `StreamDiagnostics` | SSE round observability snapshot — lines read, accumulated chars, last raw line, elapsed time |
| `StreamDiagnosticsBuilder` | Fluent configurator for service-level streaming diagnostics; consumed by `Mythosia.AI`'s `WithStreamDiagnostics(d => d.OnRawLine(...).OnComplete(...))` |

## Functions

| Type | Description |
| --- | --- |
| `FunctionDefinition` | Function schema for LLM function calling |
| `FunctionCallingPolicy` | Controls function calling behavior and iteration limits |
| `AiFunctionAttribute` | Marks a method as an AI-callable function |
| `AiParameterAttribute` | Describes a function parameter for the AI |

## Exceptions

| Type | Description |
| --- | --- |
| `AIServiceException` | Base exception for AI service errors |
| `AgentMaxStepsExceededException` | Thrown when agent exceeds maximum iteration steps |
| `StreamReadException` | Thrown when an SSE read fails (transport error, premature stream end, etc.). Wraps the underlying exception in `InnerException` and attaches a `StreamDiagnostics` snapshot via the `Diagnostics` property |

---

## Relationship to Microsoft.Extensions.AI

`Microsoft.Extensions.AI` (`IChatClient`) and `IAIService` solve different problems at different layers.

| | `IAIService` (Mythosia.AI) | `IChatClient` (MS.Extensions.AI) |
|---|---|---|
| **State** | Stateful — `ChatBlock` accumulates conversation history automatically | Stateless — caller passes the full message list on every call |
| **Session management** | Multiple `ChatBlock` sessions per service instance, switchable at runtime | None; caller manages message lists |
| **System message** | First-class property; supports per-request prefix/suffix injection via `AIRequestContext` | Passed as a `ChatMessage` with `Role = system` |
| **Request parameters** | Strongly-typed `AIRequestProfile` (temperature, max tokens, stateless, disable functions) | `ChatOptions` dictionary |
| **Function calling** | Automatic ReAct loop with configurable `FunctionCallingPolicy` (max rounds, timeout, concurrency) | Single-round; caller implements the loop |
| **Streaming chunks** | Typed `StreamingContent` — `Text`, `Reasoning`, `FunctionCall`, `FunctionResult`, `Completion`, `Error` | Text content updates only |
| **Conversation summarization** | Built-in `SummaryConversationPolicy` — auto-summarizes when token/message thresholds are exceeded | Not provided |
| **Multimodal** | `Message` serializes to each provider's wire format automatically | Caller constructs provider-specific content objects |
| **Token usage** | Tracks input, output, cached, cache-creation, and reasoning tokens | Input and output tokens only |

`IAIService` sits at a higher abstraction level than `IChatClient`. Implementing `IChatClient` on top of `IAIService` would discard stateful session management, typed streaming, and the function-calling loop. The two interfaces are not interchangeable.

If interoperability with the MS ecosystem is required, the recommended direction is to accept an `IChatClient` as a constructor dependency inside a Mythosia.AI provider — not to replace `IAIService` with `IChatClient`.

---

## Why This Package?

```
Mythosia.AI.Rag  →  Mythosia.AI.Abstractions  (zero heavy dependencies)
                     instead of
                     Mythosia.AI  (Azure.AI.OpenAI, NJsonSchema, TiktokenSharp, ...)
```

By depending on abstractions rather than the full implementation package, libraries like `Mythosia.AI.Rag` avoid pulling in provider-specific dependencies. The concrete provider is chosen by the final application.

---

## Links

- [Mythosia.AI (implementation)](https://www.nuget.org/packages/Mythosia.AI)
- [GitHub](https://github.com/AJ-comp/Mythosia.AI)
- [Wiki](https://github.com/AJ-comp/Mythosia.AI/wiki)
