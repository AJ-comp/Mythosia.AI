# Function Calling Fallback: FC ON → FC OFF

## Core Problem

When FC ON conversation history is sent through the FC OFF (non-function) API path, two issues cause `400 Bad Request` errors on all providers:

1. **`Role = Function` is invalid in FC OFF** — Claude, OpenAI, and Gemini all reject the `"function"` role when function calling is not enabled. Only `User` and `Assistant` roles are accepted.

2. **`Assistant` content is empty** — During FC ON, when the AI calls a function, the assistant message has empty content (the actual call info is in metadata). In FC OFF, empty assistant content triggers validation errors (especially on Claude).

## Solution

When FC OFF, convert these messages before sending:

| Message | Problem | Fix |
|---------|---------|-----|
| `Function` role (result) | `"function"` role rejected | Change role to `User`, write function result as content |
| `Assistant` (function call) | Content is empty | Fill content with function call description |

This is done in `GetLatestMessagesWithFunctionFallback()` — original messages in ChatBlock are never modified.

### Conversion Example

```text
[FC ON history in ChatBlock]
  User: "What's the weather in Seoul?"
  Assistant: (empty content, metadata: function_call=get_weather)   ← problem: empty content
  Function: "Seoul: 15°C, Clear"                                   ← problem: invalid role
  Assistant: "The weather in Seoul is 15°C and clear."

[After fallback conversion for FC OFF]
  User: "What's the weather in Seoul?"
  Assistant: "[Called get_weather({"city":"Seoul"})]"                ← filled with call info
  User: "[Function get_weather returned: Seoul: 15°C, Clear]"      ← role changed to User
  Assistant: "The weather in Seoul is 15°C and clear."
```

## Implementation

```csharp
// AIService.cs
internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
{
    foreach (var message in GetLatestMessages())
    {
        // Assistant with empty content (function call) → fill with call description
        if (message.Role == ActorRole.Assistant &&
            message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)
                ?.ToString() == "function_call")
        {
            var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
            var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
            yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
            continue;
        }

        // Function role → change to User role, keep result as content
        if (message.Role == ActorRole.Function)
        {
            var funcName = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "function";
            yield return new Message(ActorRole.User, $"[Function {funcName} returned: {message.Content}]");
            continue;
        }

        yield return message;
    }
}
```

Applied in each service's non-function `BuildRequestBody()`:
- `ClaudeService.Parsing.cs`
- `ChatGptService.Parsing.cs` (`BuildNewApiBody()`, `BuildLegacyApiBody()`)
- `GeminiService.Parsing.cs`

## Related

Works together with **MaxTokens auto-capping** (`GetEffectiveMaxTokens()`) — see `RELEASE_NOTES.md` v4.0.1.
