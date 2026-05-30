# Token Usage

Token usage tells you how much of a model request was spent on input, output, cache, and reasoning. In Mythosia.AI it is exposed through `TokenUsage` on streaming events.

This matters most when a conversation can take more than one LLM round. A normal answer usually has one round. An agent or function-calling run may call the model, execute a tool, then call the model again with the tool result. In that case there are two useful numbers:

- `RoundUsage` shows the usage for one LLM round.
- `Completion.Usage` shows the cumulative usage for the whole streaming run.

> [!NOTE]
> This page assumes you already know what an **LLM round** is. In short: one round = one call-and-reply between your app and the model, and function-calling flows can produce multiple rounds per user message. For a step-by-step walkthrough, see [Core Concepts — What Is a Round?](core-concepts.md#what-is-a-round).

## Why It Matters

Token usage is useful for three different jobs.

For a UI context-size meter, you usually want the latest `RoundUsage.Usage.InputTokens`. That value is the size of the prompt/input sent into the latest model round.

For diagnostics, logs, and cost analysis, use `Completion.Usage.TotalTokens`. It keeps the total for the whole run, including all rounds in a function-calling or agent flow.

For performance tuning, the cache and reasoning fields help you see whether the provider reused cached input or spent extra tokens on reasoning.

## Event Model

| Event | Meaning | Best use |
|---|---|---|
| `StreamingContentType.RoundUsage` | Usage for the LLM round that just finished | UI context meter, per-round debugging |
| `StreamingContentType.Completion` | Final stream event with cumulative usage | Logging, diagnostics, cost reports |

`RoundUsage.Usage` is not cumulative. If round 1 uses 10,100 tokens and round 2 uses 14,000 tokens, the final `Completion.Usage.TotalTokens` may be 24,100 while the last `RoundUsage.Usage.TotalTokens` remains 14,000. For a context-size meter, use the last round's `InputTokens`, not `TotalTokens`.

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
        UpdateContextTokenMeter(chunk.Usage.InputTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

This is the right value for chat UIs because the last model round sees the latest conversation state, including tool results that may have been added during the run. `InputTokens` tracks the context window pressure; `TotalTokens` also includes the text the model generated in that round.

<a id="how-context-size-changes"></a>

## How Context Size Changes

Think of context size as the input size of the latest model call, not a running total. A later round already includes the conversation items that survived from earlier rounds, so adding round inputs together double-counts the same prompt, tool definitions, and history.

For example:

| Step | What is added before this model call? | Approximate input tokens | UI context meter |
|---|---|---:|---:|
| Round 1 | System prompt, tools, history, user message | 20,000 | 20,000 |
| Between rounds | Tool call output is 100 tokens; tool result is 5,000 tokens | no LLM call | unchanged |
| Round 2 | Round 1 input + tool-call message + tool result | 25,100 + overhead | 25,100 + overhead |
| Round 2 output | Model generates 3,000 tokens and another round is needed | no LLM call | unchanged |
| Round 3 | Round 2 input + round 2 output, plus any new tool result | 28,100 + overhead | 28,100 + overhead |
| Round 3 output | Model generates a 2,000-token final answer | no LLM call | unchanged |
| Next user message | Previous final answer + the new user message are now part of the next input | about 30,100 + new message + overhead | replaced by the new round's `InputTokens` |

So if round 3 is the final round, the context meter should show roughly **28,100 + overhead**, not 30,100 and not the sum of all rounds. The 2,000-token final answer affects the next model call because it becomes conversation history.

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
        Console.WriteLine($"Round {chunk.RoundIndex}: input={latestRound.InputTokens}, total={latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.InputTokens}");
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

## Why use the normalized events

Different providers attach usage data to different stream chunks. Gemini is the trickiest case — usage can arrive on text or status chunks, and sometimes after a function-call chunk — so Mythosia.AI keeps reading the stream long enough to capture that usage before moving to the next round. The library absorbs these provider-specific differences and normalizes them into `RoundUsage` and final `Completion.Usage` events, so instead of parsing provider-specific metadata yourself, read the normalized `RoundUsage` and `Completion.Usage`.
