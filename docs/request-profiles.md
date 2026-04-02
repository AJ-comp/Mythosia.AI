# AIRequestProfile

## What Is It?

`AIRequestProfile` lets you override generation parameters — temperature, max tokens, stateless mode, function calling — **for a single request only**. The service's global settings remain untouched.

## The Problem It Solves

Imagine you have a chatbot configured for creative conversation:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("You are a creative writing assistant.");
```

Now your RAG pipeline needs to rewrite the user's query with low temperature and no history. **Without** `AIRequestProfile`, you'd have to do this:

```csharp
// ❌ Without AIRequestProfile — manual state management
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("Rewrite this query: ...");

// Restore everything — easy to forget, not thread-safe
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

This is verbose, error-prone, and **breaks in multi-threaded scenarios** (e.g. a web server handling concurrent users). If an exception is thrown before the restore, the service is left in a corrupted state.

**With** `AIRequestProfile`, it's one line:

```csharp
// ✅ With AIRequestProfile — clean and safe
var rewritten = await service.GetCompletionAsync("Rewrite this query: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

The service's global settings are never touched. No cleanup needed. Thread-safe.

## Available Properties

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Override temperature
    MaxTokens = 256,          // Override max output tokens
    Stateless = true,         // Don't add this exchange to conversation history
    DisableFunctions = true,  // Skip function calling for this request
    DisableReasoning = true   // Skip reasoning/chain-of-thought for this request
};

var response = await service.GetCompletionAsync("Your prompt", profile);
```

All properties are optional — only set what you need to override. Anything you leave unset uses the service's current value.

## Predefined Profiles

For common scenarios, built-in profiles are provided so you don't have to configure properties manually:

```csharp
// Query rewriting: low temperature, small token budget, stateless
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Summarization: slightly higher temperature, moderate tokens
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Real-World Examples

### Internal query rewriting in a RAG pipeline

```csharp
// Main service configured for user-facing conversation
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// Rewrite query with different settings — service stays unchanged
var betterQuery = await service.GetCompletionAsync(
    $"Rewrite this for search: {userQuery}",
    RequestProfiles.QueryRewrite);

// Continue normal conversation — still Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### Disabling functions for a specific step

```csharp
// Service has functions registered
service.WithFunction("search_web", "Search the web", ...);

// For this one call, skip function calling — just answer directly
var directAnswer = await service.GetCompletionAsync(
    "What is 2 + 2?",
    new AIRequestProfile { DisableFunctions = true });
```

## Combining with AIRequestContext

Both can be passed together for maximum control:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nBe concise." }
);
```

See [AIRequestContext](request-contexts.md) for details on injecting content into requests.
