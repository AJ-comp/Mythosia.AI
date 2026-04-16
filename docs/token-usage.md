# Token Usage

Token usage tells you how much of a model request was spent on input, output, cache, and reasoning. In Mythosia.AI it is exposed through `TokenUsage` on streaming events.

This matters most when a conversation can take more than one LLM round. A normal answer usually has one round. An agent or function-calling run may call the model, execute a tool, then call the model again with the tool result. In that case there are two useful numbers:

- `RoundUsage` shows the usage for one LLM round.
- `Completion.Usage` shows the cumulative usage for the whole streaming run.

## Why It Matters

Token usage is useful for three different jobs.

For a UI token meter, you usually want the latest `RoundUsage.Usage.TotalTokens`. That value is closest to "how large the next model input would be if this conversation continued."

For diagnostics, logs, and cost analysis, use `Completion.Usage.TotalTokens`. It keeps the total for the whole run, including all rounds in a function-calling or agent flow.

For performance tuning, the cache and reasoning fields help you see whether the provider reused cached input or spent extra tokens on reasoning.

## Event Model

| Event | Meaning | Best use |
|---|---|---|
| `StreamingContentType.RoundUsage` | Usage for the LLM round that just finished | UI context meter, per-round debugging |
| `StreamingContentType.Completion` | Final stream event with cumulative usage | Logging, diagnostics, cost reports |

`RoundUsage.Usage` is not cumulative. If round 1 uses 10,100 tokens and round 2 uses 14,000 tokens, the final `Completion.Usage.TotalTokens` may be 24,100 while the last `RoundUsage.Usage.TotalTokens` remains 14,000.

`RoundUsage` also includes:

| Property | Meaning |
|---|---|
| `RoundIndex` | One-based LLM round number |
| `IsFinalRound` | `true` when this is the last LLM round in the stream |

Token usage is emitted when the provider returns usage data. You do not need `IncludeMetadata = true` to receive usage events.

## Final Cumulative Usage

Use `Completion.Usage` when you want the total usage for the whole streamed request:

```csharp
await foreach (var chunk in service.StreamAsync("Explain quantum computing", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

For a single LLM round, this value is usually close to the round usage. For an agent run, it is the sum of every LLM round.

## UI Token Meter

Use the latest `RoundUsage` event for a context-size meter:

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

This is the right value for chat UIs because the last model round sees the latest conversation state, including tool results that may have been added during the run.

## Function Calling and Agents

In function-calling flows, the model may run multiple times. Read every `RoundUsage` event and keep the last one for the UI. Read `Completion.Usage` at the end for cumulative diagnostics.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function result: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.TotalTokens}");
Console.WriteLine($"Run total: {cumulative?.TotalTokens}");
```

## Cache and Reasoning Fields

`TokenUsage` includes extra fields when the provider supplies them:

```csharp
if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
{
    var usage = chunk.Usage;

    Console.WriteLine($"Cached input: {usage.CachedInputTokens}");
    Console.WriteLine($"Cache created: {usage.CacheCreationTokens}");
    Console.WriteLine($"Reasoning:     {usage.ReasoningTokens}");
    Console.WriteLine($"Visible output:{usage.VisibleOutputTokens}");
}
```

| Property | Meaning |
|---|---|
| `InputTokens` | Tokens in the prompt/input |
| `OutputTokens` | Tokens generated by the model |
| `TotalTokens` | Input + output for the event scope |
| `CachedInputTokens` | Input tokens served from cache |
| `CacheCreationTokens` | Tokens written into cache |
| `ReasoningTokens` | Tokens spent on hidden reasoning |
| `VisibleOutputTokens` | Output tokens excluding reasoning |

## Provider Notes

Different providers attach usage data to different stream chunks. Mythosia.AI normalizes that into `RoundUsage` and final `Completion` events.

Gemini is the most important edge case: usage can arrive on text or status chunks, and sometimes after a function-call chunk. The library keeps reading the stream long enough to capture that usage before moving to the next round.

As a consumer, prefer the normalized `RoundUsage` and `Completion.Usage` events instead of parsing provider-specific metadata yourself.
