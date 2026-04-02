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
