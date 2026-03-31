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

## Conversation Summary Before Streaming

The automatic summary policy does not trigger during streaming. Call `ApplySummaryPolicyIfNeededAsync` explicitly before `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continue our conversation..."))
    Console.Write(chunk.Content);
```
