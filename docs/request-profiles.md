# Request Profiles & Contexts

These allow you to override service settings for a single request without changing the service's global state.

## AIRequestProfile

A per-request parameter override bag. Pass it to `GetCompletionAsync` or `StreamAsync`:

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,
    MaxTokens = 256,
    Stateless = true,        // Don't add this request to history
    DisableFunctions = true, // Skip function calling for this request
    DisableReasoning = true  // Skip reasoning for this request
};

var response = await service.GetCompletionAsync("Summarize this.", profile);
```

### Predefined Profiles

Two built-in profiles for common use cases:

```csharp
// Low temperature, small token budget, stateless — for query rewriting
var response = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Slightly higher temperature, moderate tokens — for summarization
var response = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## AIRequestContext

Injects additional content into a single request without touching the service's system message or history:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "Today's date is 2026-03-31.\n",
    SystemMessageSuffix = "\nAlways respond in Korean.",
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("Reference doc: ...").Build()
    }
};

var response = await service.GetCompletionAsync("Answer the question.", context);
```

### RequestMessageOverride

Replace the request message entirely for this one call:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User("Reformulated prompt based on retrieved context...")
        .Build()
};

await service.GetCompletionAsync(originalPrompt, context);
```

## Combining Profile and Context

Both can be passed together:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nBe concise." }
);
```
