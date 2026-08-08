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
service.ActivateChat.ClearMessages();
```

## Summary Policy

### Why Automatic Summarization?

Every message in the conversation history is sent to the model on each request. As conversations grow, this creates two problems:

1. **Cost** — longer histories mean more input tokens billed per request
2. **Context overflow** — once the history exceeds the model's context window (e.g. 128K tokens for GPT-4o), requests fail entirely

You could manually truncate old messages, but that loses context the model might need. **`SummaryConversationPolicy`** solves this by automatically condensing older messages into a compact summary while keeping recent messages verbatim — the model retains the gist of the full conversation without the token cost.

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
