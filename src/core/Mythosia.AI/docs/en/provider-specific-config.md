# Provider-Specific Configuration Architecture

> GPT-6 Astra, `AllowAsync`, `StartRunAsync`, and the common reasoning/search API are available from `Mythosia.AI` 7.1.0, with shared types in `Mythosia.AI.Abstractions` 3.1.0.

## Principle

Applications can now express task-level effort and hosted retrieval through the [common reasoning and search API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/reasoning-and-search.md). `AIRequestFeatures` is copied for one logical request; provider adapters validate and translate it, while provider-specific defaults remain on the service. Cache-preserving changes retain protocol state in the tracked conversation. `AICitation` keeps hosted source references independently of stream observation. Custom services opt in through `IAIRequestFeatureService`, without adding mandatory `IAIService` members.

| Config Type | Location | Examples |
|-------------|----------|----------|
| **Common** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty, etc. |
| **Provider-specific** | Each service class | ThinkingBudget (Gemini), ReasoningEffort (GPT), etc. |
| **Per-function permission** | `FunctionDefinition` | `AllowAsync` (default `false`) |

`AllowAsync` is a caller-controlled permission; the service determines model/API support internally. `FunctionBuilder.WithAsync()` and `[AiFunction("lookup", "Look up data", AllowAsync = true)]` enable the same permission. GPT-6 Astra uses it through Responses, while unsupported models omit the API option and wait for the same handler's result without changing the permission.

## Current Implementation: Service Level

Provider-specific settings are managed as properties of each service class.

```csharp
// Common settings → ChatBlock
geminiService.ActivateChat.Temperature = 0.7f;
geminiService.ActivateChat.MaxTokens = 4096;

// Provider-specific settings → Service
geminiService.ThinkingBudget = 1024;
```

### Pros
- ChatBlock remains completely provider-agnostic (clean separation)
- Follows OOP principles (each service manages its own config)
- One setting per service instance → simple structure

### Cons
- All ChatBlocks within one service share the same provider-specific settings

## When to Migrate to ChatBlock Level

If a requirement arises where **each ChatBlock needs independent provider-specific settings**, migrate by adding a lazy-initialized config class inside ChatBlock.

```csharp
// Example (not currently implemented)
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// Usage
chatBlock.Gemini.ThinkingBudget = 1024;
```

### Scenarios Requiring This
- ChatBlock A and B within a single service instance need different ThinkingBudgets
- In practice, this case is extremely rare, so service-level is maintained for now

## Decision Log

- **2026-02-12**: Initially implemented as ChatBlock-level (Option B), then rolled back to service-level. Provider-specific settings belong in their respective service classes.

## Choosing execution controls

An application may need to show progress or let a user revise a long task while it runs. `StartRunAsync` returns an `AIRun` for that task; the provider determines whether mid-turn steering is supported. Keep model settings on the service, configure them before startup, and check `run.CanSteer` before sending an instruction. See [why and when to use a run](../../../../../docs/execution-api-transition.md) for examples, cancellation, and compatibility.
