# Token 用量

Token 用量表示一次模型请求在输入、输出、缓存和推理上消耗了多少 token。在 Mythosia.AI 中，这些数据会通过流式事件上的 `TokenUsage` 提供。

当一次响应不止一个 LLM 调用时，这一点尤其重要。普通回答通常只有一个 round；而 agent 或 function calling 流程可能先调用模型，再执行工具，然后把工具结果带回去再次调用模型。因此这里有两个需要区分的值。

- `RoundUsage` 表示单个 LLM round 的用量。
- `Completion.Usage` 表示整个 stream run 的累计用量。

## 为什么需要它

如果你在做聊天 UI 的上下文 token 计量器，通常应该使用最新的 `RoundUsage.Usage.TotalTokens`。它最接近“如果现在继续对话，下一次模型输入会有多大”这个值。

如果你在做日志、诊断或成本分析，应该使用 `Completion.Usage.TotalTokens`。它会保留整个 run 的累计用量，包括 function calling 或 agent 产生的多个 round。

如果你在调优性能，缓存和推理相关字段可以帮助你判断 provider 是否复用了缓存输入，以及模型在内部推理上额外消耗了多少 token。

## 事件模型

| 事件 | 含义 | 适合用途 |
|---|---|---|
| `StreamingContentType.RoundUsage` | 刚结束的 LLM round 的用量 | UI 上下文计量器、按 round 调试 |
| `StreamingContentType.Completion` | 最终事件，包含累计用量 | 日志、诊断、成本报表 |

`RoundUsage.Usage` 不是累计值。比如 round 1 使用 10,100 token，round 2 使用 14,000 token，最终的 `Completion.Usage.TotalTokens` 可能是 24,100，但最后一个 `RoundUsage.Usage.TotalTokens` 仍然是 14,000。

| 属性 | 含义 |
|---|---|
| `RoundIndex` | 从 1 开始的 LLM round 编号 |
| `IsFinalRound` | 如果这是 stream 中最后一个 LLM round，则为 `true` |

只要 provider 返回 usage 数据，就会 emit token usage。接收 usage 事件不需要开启 `IncludeMetadata = true`。

## 最终累计用量

如果你需要整个流式请求的总用量，请读取 `Completion.Usage`。

```csharp
await foreach (var chunk in service.StreamAsync("解释一下量子计算", StreamOptions.FullOptions))
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

如果只有一个 LLM round，这个值通常接近 `RoundUsage`。如果是 agent，它就是所有 LLM round 的总和。

## UI Token 计量器

上下文大小计量器应使用最新的 `RoundUsage`。

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

最后一个模型 round 看到的是最新的对话状态，包括 run 过程中加入的工具结果。因此对聊天 UI 来说，最后一个 `RoundUsage.TotalTokens` 最能代表响应后的上下文大小。

## Function Calling 和 Agent

在 function calling 流程中，模型可能会运行多次。读取每一个 `RoundUsage`，把最后一个保留给 UI；最后再用 `Completion.Usage` 做累计诊断。

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}
```

## 缓存和推理字段

如果 provider 提供这些数据，`TokenUsage` 还会包含缓存和推理相关字段。

| 属性 | 含义 |
|---|---|
| `InputTokens` | prompt/input 中的 token |
| `OutputTokens` | 模型生成的 token |
| `TotalTokens` | 该事件范围内的输入 + 输出 |
| `CachedInputTokens` | 从缓存命中的输入 token |
| `CacheCreationTokens` | 写入缓存的 token |
| `ReasoningTokens` | 隐藏内部推理消耗的 token |
| `VisibleOutputTokens` | 不含推理的可见输出 token |

## Provider 注意事项

不同 provider 会把 usage 数据放在不同的 stream chunk 上。Mythosia.AI 会把它们统一整理成 `RoundUsage` 和最终的 `Completion.Usage`。

Gemini 是最需要留意的情况：usage 可能出现在 text 或 status chunk 上，有时甚至会在 function-call chunk 之后才到达。库会继续读取 stream，确保在进入下一 round 之前捕获这些 usage。

作为消费者，建议优先使用已经标准化的 `RoundUsage` 和 `Completion.Usage`，不要自己解析 provider 专属 metadata。
