# 流式输出

## 基本流式输出

使用 `StreamAsync` 在 Token 生成时逐个接收：

```csharp
await foreach (var token in service.StreamAsync("讲个故事吧"))
{
    Console.Write(token);
}
```

## 带内容类型的流式输出

`StreamAsync` 可以返回 `StreamingContent` 对象，携带文本及其类型：

```csharp
await foreach (var content in service.StreamAsync("解释一下量子计算"))
{
    Console.Write(content.Content);
}
```

## 推理过程流式输出

所有支持推理的提供商（OpenAI、Claude、Gemini、Grok、DeepSeek）共用同一模式。传入启用了推理的 `StreamOptions`：

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("求解：2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[思考中] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

`StreamingContentType.Reasoning` 携带模型内部的思维链，`StreamingContentType.Text` 携带最终回答。

## 流式输出 + 结构化输出

实时流式传输文本，完成后获取反序列化的对象：

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// 实时将 Token 输出到界面
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// 流式输出完成后获取完整解析结果
MyDto result = await run.Result;
```

## Token 使用量

流式输出完成时，最后的 `Completion` 事件携带 `TokenUsage` 对象，包含详细的使用指标：

```csharp
await foreach (var content in service.StreamAsync("解释一下量子计算"))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\n输入 Token：{content.Usage.InputTokens}");
        Console.WriteLine($"输出 Token：{content.Usage.OutputTokens}");
        Console.WriteLine($"总计 Token：{content.Usage.TotalTokens}");
    }
}
```

### TokenUsage 属性

| 属性 | 说明 |
|------|------|
| `InputTokens` | 输入/提示词的 Token 数 |
| `OutputTokens` | 输出/生成的 Token 数 |
| `TotalTokens` | 输入 + 输出 |
| `CachedInputTokens` | 从缓存中获取的 Token 数（降低成本） |
| `CacheCreationTokens` | 写入缓存的 Token 数（Anthropic） |
| `ReasoningTokens` | 用于内部推理的 Token 数 |
| `CacheHitRatio` | 缓存命中率（0.0–1.0） |
| `VisibleOutputTokens` | 排除推理后的输出 Token 数 |

### 检查缓存效率

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"缓存命中率：{content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"未缓存输入：{content.Usage.NonCachedInputTokens}");
}
```

## StreamOptions 预设

`StreamOptions` 提供预设和流式构建器，用于控制流式输出包含的内容：

```csharp
// 全功能 — 元数据、函数调用、推理
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// 最小开销 — 仅文本，无元数据
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// 函数调用场景
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* 处理 Text、FunctionCall、FunctionResult、Completion */ }
```

自定义组合的流式构建器：

```csharp
var options = new StreamOptions()
    .WithReasoning()       // 包含思维链
    .WithMetadata()        // 在 Completion 中包含模型信息
    .WithFunctionCalls();  // 在流式输出中启用函数调用
```

## 无状态流式输出（StreamOnceAsync）

在不影响对话历史的情况下进行流式输出 — 相当于 `AskOnceAsync` 的流式版本：

```csharp
await foreach (var chunk in service.StreamOnceAsync("把这段翻译成法语"))
    Console.Write(chunk);
```

也接受 `Message` 以支持多模态输入：

```csharp
var message = MessageBuilder.Create().AddText("描述一下").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## 流式输出前的对话摘要

自动摘要策略不会在流式输出期间触发。请在 `StreamAsync` 之前显式调用 `ApplySummaryPolicyIfNeededAsync`：

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("继续我们的对话..."))
    Console.Write(chunk.Content);
```
