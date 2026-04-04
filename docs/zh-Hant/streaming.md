# 串流輸出

## 基本串流輸出

使用 `StreamAsync` 在 Token 生成時逐個接收：

```csharp
await foreach (var token in service.StreamAsync("講個故事吧"))
{
    Console.Write(token);
}
```

## 帶內容類型的串流輸出

`StreamAsync` 可以回傳 `StreamingContent` 物件，攜帶文字及其類型：

```csharp
await foreach (var content in service.StreamAsync("解釋一下量子運算"))
{
    Console.Write(content.Content);
}
```

## 推理過程串流輸出

所有支援推理的供應商（OpenAI、Claude、Gemini、Grok、DeepSeek）共用同一模式。傳入啟用推理的 `StreamOptions`：

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

`StreamingContentType.Reasoning` 攜帶模型內部的思維鏈，`StreamingContentType.Text` 攜帶最終回答。

## 串流輸出 + 結構化輸出

即時串流傳輸文字，完成後取得反序列化的物件：

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// 即時將 Token 輸出到介面
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// 串流輸出完成後取得完整解析結果
MyDto result = await run.Result;
```

## Token 使用量

串流輸出完成時，最後的 `Completion` 事件攜帶 `TokenUsage` 物件，包含詳細的使用指標：

```csharp
await foreach (var content in service.StreamAsync("解釋一下量子運算"))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\n輸入 Token：{content.Usage.InputTokens}");
        Console.WriteLine($"輸出 Token：{content.Usage.OutputTokens}");
        Console.WriteLine($"總計 Token：{content.Usage.TotalTokens}");
    }
}
```

### TokenUsage 屬性

| 屬性 | 說明 |
|------|------|
| `InputTokens` | 輸入/提示詞的 Token 數 |
| `OutputTokens` | 輸出/生成的 Token 數 |
| `TotalTokens` | 輸入 + 輸出 |
| `CachedInputTokens` | 從快取中取得的 Token 數（降低成本） |
| `CacheCreationTokens` | 寫入快取的 Token 數（Anthropic） |
| `ReasoningTokens` | 用於內部推理的 Token 數 |
| `CacheHitRatio` | 快取命中率（0.0–1.0） |
| `VisibleOutputTokens` | 排除推理後的輸出 Token 數 |

### 檢查快取效率

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"快取命中率：{content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"未快取輸入：{content.Usage.NonCachedInputTokens}");
}
```

## StreamOptions 預設

`StreamOptions` 提供預設和流式建構器，用於控制串流輸出包含的內容：

```csharp
// 全功能 — 中繼資料、函式呼叫、推理
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// 最小開銷 — 僅文字，無中繼資料
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// 函式呼叫情境
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* 處理 Text、FunctionCall、FunctionResult、Completion */ }
```

自訂組合的流式建構器：

```csharp
var options = new StreamOptions()
    .WithReasoning()       // 包含思維鏈
    .WithMetadata()        // 在 Completion 中包含模型資訊
    .WithFunctionCalls();  // 在串流輸出中啟用函式呼叫
```

## 無狀態串流輸出（StreamOnceAsync）

在不影響對話歷史的情況下進行串流輸出 — 相當於 `AskOnceAsync` 的串流版本：

```csharp
await foreach (var chunk in service.StreamOnceAsync("把這段翻譯成法文"))
    Console.Write(chunk);
```

也接受 `Message` 以支援多模態輸入：

```csharp
var message = MessageBuilder.Create().AddText("描述一下").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## 串流輸出前的對話摘要

自動摘要策略不會在串流輸出期間觸發。請在 `StreamAsync` 之前明確呼叫 `ApplySummaryPolicyIfNeededAsync`：

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("繼續我們的對話..."))
    Console.Write(chunk.Content);
```
