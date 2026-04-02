# Conversation Management

## How Conversation History Works

Every call to `GetCompletionAsync` or `StreamAsync` appends to the service's internal message list. This means the model has context from all previous turns.

```csharp
await service.GetCompletionAsync("My favorite color is blue.");
var reply = await service.GetCompletionAsync("What is my favorite color?");
// → "Your favorite color is blue."
```

To start fresh:

```csharp
service.ClearMessages();
```

## Summary Policy

Long conversations consume tokens and eventually exceed the model's context limit. `SummaryConversationPolicy` automatically summarizes old messages when a threshold is reached.

### Trigger by Message Count

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // summarize when history exceeds 20 messages
    keepRecentCount: 5  // keep the 5 most recent messages verbatim
);
```

### Trigger by Token Count

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // summarize when token usage exceeds 3000
    keepRecentTokens: 1000  // keep recent messages up to 1000 tokens
);
```

### Trigger by Both (OR Condition)

Trigger summarization when **either** the token limit or message count is exceeded:

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // optional, defaults to triggerTokens / 3
    keepRecentCount: 7       // optional, defaults to triggerCount / 4
);
```

Once set, summarization happens automatically on `GetCompletionAsync`. No other changes needed.

### How It Works

1. Before each completion, the policy checks if the conversation exceeds the configured threshold.
2. If triggered, older messages are summarized into a concise text using a stateless LLM call.
3. The summary is injected as a system message prefix — the model sees it as prior context.
4. Recent messages (controlled by `KeepRecentCount` or `KeepRecentTokens`) are preserved verbatim.

When using token-based triggers, the policy automatically uses the **actual input token count** reported by the API (from the last streaming response) instead of local estimation, ensuring accurate trigger decisions.

### Streaming

Summarization does not trigger automatically during `StreamAsync`. Call it explicitly first:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continue our conversation..."))
    Console.Write(chunk.Content);
```

## Saving and Restoring Summary

Persist the summary across sessions so the model retains context after a restart:

```csharp
// Save
string saved = service.ConversationPolicy.CurrentSummary;
// → store in database, file, etc.

// Restore in a new session
service.ConversationPolicy.LoadSummary(saved);
```
