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

## Streaming Diagnostics

When an SSE connection drops mid-stream or the response ends abnormally, you often need to know exactly where things went wrong. The library exposes diagnostic hooks for this — useful for self-hosted vLLM, internal proxies, and unstable network paths.

### Registering hooks

Register once on the service; all subsequent `StreamAsync` calls pick up the hooks automatically. Same builder pattern as `WithRag`.

```csharp
using Mythosia.AI.Extensions;

service.WithStreamDiagnostics(d => d
    .OnRawLine(line => logger.LogDebug("SSE: {Line}", line))
    .OnComplete(diag => logger.LogInformation("Stream finished: {Diag}", diag)));

// Hooks now apply to every streaming call on this service.
await foreach (var chunk in service.StreamAsync(message))
    Console.Write(chunk.Content);
```

Each `On*` method is independent — call only the ones you need.

```csharp
// Raw line trace only
service.WithStreamDiagnostics(d => d.OnRawLine(line => logger.LogDebug("SSE: {Line}", line)));

// Clear all hooks
service.WithStreamDiagnostics(_ => { });
```

> **Cross-provider switches (`CopyFrom`)**: Registered callbacks are propagated automatically when you copy state to a new service instance. Callbacks that wrap external sinks (`logger`, `metrics`) keep working as expected. Be careful with closures that capture the service instance itself (e.g. `line => Log(service.Provider, line)`) — the copy will still reference the original service. Capturing only external resources is the safe pattern.

### Available callbacks

| Method | Fires | Use for |
|---|---|---|
| `OnRawLine(Action<string>)` | Every SSE line received | Debug-level tracing — see whether the last line before death was truncated or non-standard |
| `OnComplete(Action<StreamDiagnostics>)` | Once on stream exit (success or failure) | Telemetry — line count, accumulated chars, elapsed time |

### Catching diagnostics on failure

When SSE reading throws an `IOException` or transport error, the library wraps it in `StreamReadException`. The `Diagnostics` property exposes the state at the moment of failure — this works regardless of whether `WithStreamDiagnostics` was registered.

```csharp
try
{
    await foreach (var chunk in service.StreamAsync(message))
        Console.Write(chunk.Content);
}
catch (StreamReadException ex)
{
    logger.LogError(ex,
        "Stream died after {Lines} lines, {Chars} chars. Last raw line: {Line}",
        ex.Diagnostics.LinesRead,
        ex.Diagnostics.AccumulatedTextLength,
        ex.Diagnostics.LastRawLine);

    // ex.InnerException carries the original exception (IOException, etc.)
}
```

### `StreamDiagnostics` fields

| Field | Meaning |
|---|---|
| `LinesRead` | Total SSE lines received (including blank/comment lines) |
| `DataLinesProcessed` | Lines accepted as content by the chunk parser |
| `ParseFailures` | Lines that hit a JSON parse error (silently skipped before this feature existed) |
| `AccumulatedTextLength` | Total characters appended to the assistant text buffer |
| `LastRawLine` | Most recent raw SSE line — surfaces the truncated tail when a stream dies mid-line |
| `Elapsed` | Wall-clock time spent reading the stream |

### Diagnosing self-hosted backends

If you see "turn 1 works, turn 2 fails intermittently" against vLLM, ollama, or other self-hosted endpoints:

1. Register `WithStreamDiagnostics(d => d.OnRawLine(...))` at Debug level and reproduce
2. On `StreamReadException`, log `Diagnostics.LastRawLine` and `ex.InnerException.GetType().FullName`
3. Cross-reference the server log (200 OK but truncated response) with the client's last received line

This narrows the issue down to "server finished normally but the client lost the connection mid-line" vs other failure modes very quickly.
