# Provider-Specific Configuration Architecture

## Principle

| Config Type | Location | Examples |
|-------------|----------|----------|
| **Common** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty, etc. |
| **Provider-specific** | Each service class | ThinkingBudget (Gemini), ReasoningEffort (GPT), etc. |

## Current Implementation: Service Level

Provider-specific settings are managed as properties of each service class.

```csharp
// Common settings ¡æ ChatBlock
geminiService.ActivateChat.Temperature = 0.7f;
geminiService.ActivateChat.MaxTokens = 4096;

// Provider-specific settings ¡æ Service
geminiService.ThinkingBudget = 1024;
```

### Pros
- ChatBlock remains completely provider-agnostic (clean separation)
- Follows OOP principles (each service manages its own config)
- One setting per service instance ¡æ simple structure

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
