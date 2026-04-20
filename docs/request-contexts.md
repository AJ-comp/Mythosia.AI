# AIRequestContext

## What Is It?

`AIRequestContext` lets you modify **what the model sees** for a single request — inject extra instructions, add reference documents, or completely replace the user's message — without permanently changing the service's system message or conversation history.

## The Problem It Solves

Consider a RAG pipeline that retrieves relevant documents and needs to include them in the prompt. **Without** `AIRequestContext`, you'd have to modify the system message directly:

```csharp
// ❌ Without AIRequestContext — polluting the system message
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\nUse the following context to answer:\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// Restore — but this context is now stuck in conversation history too
service.SystemMessage = originalSystem;
```

Problems with this approach:

- The retrieved context **leaks into conversation history** — future requests still see it
- Restoring the system message doesn't undo the history pollution
- In a multi-user web app, mutating shared state causes race conditions

**With** `AIRequestContext`, the injection is scoped to exactly one request:

```csharp
// ✅ With AIRequestContext — clean, scoped, no side effects
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nUse the following context to answer:\n{retrievedDocs}"
    });
```

The system message is only modified for this one call. The next request sees the original system message. No cleanup required.

## Available Properties

### SystemMessagePrefix

Prepends text to the system message for this request only:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "Today's date is 2026-03-31.\n"
};

var response = await service.GetCompletionAsync("What day is it?", context);
```

**When to use:** Injecting dynamic metadata (date, user timezone, session info) that changes per request.

### SystemMessageSuffix

Appends text to the system message for this request only:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nAlways respond in Korean."
};

var response = await service.GetCompletionAsync("Hello!", context);
```

**When to use:** Adding per-request behavioral instructions, RAG context, or language preferences.

### AdditionalMessages

Inserts extra messages into the conversation for this request only — useful for injecting reference documents or few-shot examples:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("Reference doc: The refund policy allows returns within 30 days.").Build()
    }
};

var response = await service.GetCompletionAsync("Am I eligible for a refund?", context);
```

**When to use:** Providing reference material, few-shot examples, or auxiliary context that shouldn't persist in conversation history.

### RequestMessageOverride

Completely replaces the user's message for this request. The original prompt is ignored:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"Based on the following context, answer the question.\n\nContext: {docs}\n\nQuestion: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context);
```

**When to use:** When a middleware layer (RAG, query rewriting) needs to reformulate the prompt entirely before sending it to the model, while keeping the original user input in the conversation history.

> **💡 Note:** When you use `.WithRag()`, the RAG pipeline leverages this property automatically. See [Pipeline Customization — How It Works Internally](rag-pipeline.md#how-it-works-internally) for the full flow.

## Before vs. After Comparison

### Scenario: RAG with date injection and retrieved context

**Without AIRequestContext:**

```csharp
// ❌ Messy, stateful, error-prone
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nToday: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nContext:\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // remove the few-shot example
```

**With AIRequestContext:**

```csharp
// ✅ Clean, stateless, no side effects
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"Today: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nContext:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
        }
    });
```

## Combining with AIRequestProfile

Both can be passed together for maximum control over a single request:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nContext:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("Example: ...").Build()
        }
    }
);
```

See [AIRequestProfile](request-profiles.md) for details on overriding generation parameters.

## Automatic Injection with `SystemMessageProvider`

### The Problem It Solves

A typical chat app has several LLM entry points that all need the same dynamic baseline — today's date, the active folder, session info. Without `SystemMessageProvider`, every single call site has to remember to build and pass that context:

```csharp
// ❌ Without SystemMessageProvider — every entry point must remember to inject
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Main chat answer
var answer = await service.GetCompletionAsync(userMessage,
    new AIRequestContext { SystemMessageSuffix = today });

// 2. Title generator (added later)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 3. Summarizer (added later still)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 4. Agent call — easy to forget! The compiler won't warn you.
var agentResult = await service.RunAgentAsync(goal);  // ← no date, silent bug
```

Problems with this approach:

- The same context-building snippet is duplicated across every call site
- New entry points (`RunAgentAsync` above) are easy to miss — there is no compile-time check that the baseline was applied
- Every new feature that adds another LLM call has to remember the convention
- Tests have to replicate the context setup at every call site

With `SystemMessageProvider`, you register the baseline **once** and every outbound call picks it up automatically:

```csharp
// ✅ With SystemMessageProvider — register once, applied everywhere
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// All of these automatically receive the baseline — no per-call boilerplate
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← also receives the baseline

// Streaming entry points too — same baseline, no per-call boilerplate
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### How It Works

Register the callback once via the `WithSystemMessageProvider` fluent helper. Every outbound call (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) automatically invokes it to build a baseline context:

```csharp
// Typically at service construction / DI setup
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### Async overload for IO-backed providers

When the baseline context comes from a database, cache, or HTTP call, use the async overload so the provider does not have to block on `.Result` / `.GetAwaiter().GetResult()`. Overload resolution picks the right one by lambda arity — no arg for sync, one `CancellationToken` for async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

Non-streaming paths (`GetCompletionAsync`, `RunAgentAsync`) do not support cancellation by design — their signatures do not accept a `CancellationToken`, and `CancellationToken.None` is always passed to the provider. If your provider needs cancellation (e.g. a long-running DB query), use the streaming paths (`StreamAsync`, `RunAgentStreamAsync`) which forward the caller's token through to the provider callback.

### Merging with an explicit per-call context

When a call has a registered provider **and** also passes an explicit `AIRequestContext`, the two are merged field-by-field:

| Field | Merge rule |
|---|---|
| `SystemMessagePrefix` | explicit wins if non-null, else provider |
| `SystemMessageSuffix` | explicit wins if non-null, else provider |
| `RequestMessageOverride` | explicit wins if non-null, else provider |
| `AdditionalMessages` | concatenated (provider first, then explicit) |

Rationale: the common case is "provider supplies a baseline, a specific call wants to replace one scalar field or add extra messages" — field-level override keeps the semantics predictable without surprising concatenation.

### Per-call invocation

The provider is invoked **once per request**, so return values can reflect up-to-the-moment state (timestamp, session, etc.). Returning `null` is a no-op — identical to leaving `SystemMessageProvider` unset for that call.

> Available in Mythosia.AI v6.3.0+.
