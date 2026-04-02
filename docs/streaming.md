# Streaming

## Basic Streaming

Use `StreamAsync` to receive tokens as they are generated:

```csharp
await foreach (var token in service.StreamAsync("Tell me a story"))
{
    Console.Write(token);
}
```

## Streaming with Content Type

`StreamAsync` can return `StreamingContent` objects that carry both the text and its type:

```csharp
await foreach (var content in service.StreamAsync("Explain quantum computing"))
{
    Console.Write(content.Content);
}
```

## Reasoning Streaming

All reasoning-capable providers (OpenAI, Claude, Gemini, Grok, DeepSeek) share the same pattern. Pass `StreamOptions` with reasoning enabled:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Solve: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Thinking] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

`StreamingContentType.Reasoning` carries the model's internal chain-of-thought, while `StreamingContentType.Text` carries the final answer.

## Streaming with Structured Output

Stream text in real-time and get a deserialized object when done:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Stream tokens to the UI as they arrive
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Get the fully parsed result after streaming completes
MyDto result = await run.Result;
```

## Token Usage

When streaming completes, the final `Completion` event carries a `TokenUsage` object with detailed usage metrics:

```csharp
await foreach (var content in service.StreamAsync("Explain quantum computing"))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nInput tokens:  {content.Usage.InputTokens}");
        Console.WriteLine($"Output tokens: {content.Usage.OutputTokens}");
        Console.WriteLine($"Total tokens:  {content.Usage.TotalTokens}");
    }
}
```

### TokenUsage Properties

| Property | Description |
|---|---|
| `InputTokens` | Tokens in the input/prompt |
| `OutputTokens` | Tokens in the output/completion |
| `TotalTokens` | Input + Output |
| `CachedInputTokens` | Tokens served from cache (reduced cost) |
| `CacheCreationTokens` | Tokens written to cache (Anthropic) |
| `ReasoningTokens` | Tokens used for internal reasoning |
| `CacheHitRatio` | Cache hit ratio (0.0–1.0) |
| `VisibleOutputTokens` | Output tokens excluding reasoning |

### Checking Cache Efficiency

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"Cache hit ratio: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Non-cached input: {content.Usage.NonCachedInputTokens}");
}
```

## StreamOptions Presets

`StreamOptions` provides presets and a fluent builder for controlling what the stream yields:

```csharp
// Full featured — metadata, function calls, reasoning
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Minimal overhead — text only, no metadata
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// Function calling scenarios
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* handle Text, FunctionCall, FunctionResult, Completion */ }
```

Fluent builder for custom combinations:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // include chain-of-thought
    .WithMetadata()        // include model info in Completion
    .WithFunctionCalls();  // enable function calling during stream
```

## Stateless Streaming (StreamOnceAsync)

Stream a response without affecting the conversation history — the streaming equivalent of `AskOnceAsync`:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Translate this to French"))
    Console.Write(chunk);
```

Also accepts a `Message` for multimodal input:

```csharp
var message = MessageBuilder.Create().AddText("Describe this").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## Conversation Summary Before Streaming

The automatic summary policy does not trigger during streaming. Call `ApplySummaryPolicyIfNeededAsync` explicitly before `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continue our conversation..."))
    Console.Write(chunk.Content);
```
