# 串流輸出

> Grok 4.7 是尚未發布的新增功能；請參閱[模型選擇、推理與處理速度](providers.md#grok-47)。

需要同時取得完整答案、用量與來源時，使用 `await run.Result` 傳回的 `AIRunResult`；字串位於 `result.Text`，不必讀取串流。這是Mythosia.AI 8.0.0 的 API 變更；`GetCompletionAsync` 與 `StructuredStreamRun<T>.Result` 的傳回型別保持不變。 [Run 結果與移轉](execution-api-transition.md#run-result).


若要分離每個請求的設定並衍生多個版本，請使用[請求建構器](request-building.md)。先呼叫`CreateRequest(...)`，再串接`With...`。服務屬性與服務上的fluent方法維持原有行為。

逐段顯示收到的文字，讓使用者不必等長答案全部產生後才能閱讀。如果還需要停止按鈕和工具狀態，可透過 `StartRunAsync` 啟動工作並讀取 `run.StreamAsync()`。[Run 使用指南](execution-api-transition.md)提供回呼和取消的範例。

```csharp
await using var run = await service.StartRunAsync(
    "摘要文件。",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

接收輸入的服務與 RAG StreamAsync 在 v8 仍公開。新的執行控制使用 StartRunAsync；run.StreamAsync() 只觀察已啟動的 run。

## 基本串流輸出

使用 `StreamAsync` 可在生成過程中逐步接收文字。

```csharp
await foreach (var token in service.StreamAsync("講個故事吧"))
{
    Console.Write(token);
}
```

## 帶內容類型的串流輸出

`StreamAsync` 可以回傳 `StreamingContent` 物件，攜帶文字及其類型：

```csharp
await foreach (var content in service.StreamAsync("解釋一下量子運算", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## 推理過程串流輸出

OpenAI、Claude、Gemini、Grok 和 DeepSeek Flash 透過相同串流模式回傳供應商推理。先在服務或請求中開啟推理，再用 `StreamOptions.WithReasoning()` 觀察：

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

Gemini 3.7/3.8 Flash 沿用現有串流和 Run 事件。`StreamingContentType.Reasoning` 包含供應商傳回的摘要或進度，不保證公開完整的內部推理。`StreamOptions.WithReasoning()` 選擇此輸出，服務的 `WithReasoning(ReasoningLevel...)` 則控制推理強度。

Grok 4.6 也透過這些事件傳遞供應商選擇性提供的推理摘要。串流選項選擇可見輸出，`WithReasoning(ReasoningLevel...)` 則選擇一個任務的推理強度。沒有摘要不代表推理已關閉。參閱 [Grok 設定](providers.md#xai-xaiservice)。

DeepSeek Flash 開啟推理後透過相同事件回傳 `reasoning_content`。`StreamOptions.WithReasoning()` 控制觀察；`WithDeepSeekReasoning(...)` 或服務級 `WithReasoning(...)` 控制推理。參閱 [DeepSeek 設定](providers.md#deepseek-deepseekservice)。

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
await foreach (var content in service.StreamAsync("解釋一下量子運算", StreamOptions.Default))
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

在 `run.Result` 成功之前，應將已顯示的片段視為暫定輸出。共用的 OpenAI 相容串流處理路徑和 DeepSeek 串流處理路徑會拒絕明確結束後的新文字、推理或工具資料，以及發生變化的結束原因：`run.Result` 會擲出例外，失敗回合不會儲存到對話歷史，也不會執行該回合的工具。這種失敗處理不會撤銷先前的回合或已在外部執行的操作。 允許最後一個增量與首次結束事件一起到達，也允許隨後僅包含用量資訊的事件。

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

await foreach (var chunk in service.StreamAsync("繼續我們的對話...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [讓長時間工作持續執行 / 引用可能指向網頁結果或其他提供者來源。位移屬於單一提供者回應的內容部分，而不是 Run 累積結果。保留 URL 與標題供顯示和核對；傳回來源本身並不證明每項產生的主張都正確。](perplexity.md).
