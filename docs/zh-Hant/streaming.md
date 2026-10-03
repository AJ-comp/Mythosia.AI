# 串流輸出

預設回呼串流介接器在提前結束時取消並等待內部產生工作。Run 取消、逾時、觀察回呼失敗和 `DisposeAsync` 都等待供應商清理結束後才完成 `Result` 並釋放執行鎖；不回應取消的工作可能延遲結束。觀察與清理例外一併保留。`ContextRecoveryMaxRetries` 使用請求擷取的值。僅停止 `run.StreamAsync()` 觀察仍不會取消 Run。 成功 SSE 回應的本文串流取得另有[取消限制](#sse-acquisition-cancellation-limitation)。

> Claude Sonnet 5.5: 需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0。[設定與移轉](providers.md#claude-sonnet-55)

Adaptive 模式可用 `ClaudeThinkingDisplay.Updates` 取得工具進度，或用 `Summarized` 取得推理摘要。讀取 `StreamingContentType.Reasoning`，一般完成後讀取 `LastThinkingContent`。Adaptive 輔助方法的預設 display 引數為 `Summarized`，與未設定時不同。`between_tools` 自動回傳工具進度；不保證固定通知間隔。

> Grok 4.7: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型選擇、推理與處理速度](providers.md#grok-47)

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

**Claude 錯誤回應：** 在串流請求或 Run 中，即使 HTTP 錯誤本文的讀取停滯，取消和請求原則逾時仍會生效。Run 清理完成後，可在同一服務上啟動下一個 Run。呼叫端取消會拋出 `OperationCanceledException`，原則逾時會拋出 `AIServiceException`。取消控制本機傳輸和配合取消的清理程序，不保證供應商停止處理或計費。

**Claude 回應清理：** Claude 串流請求和 Run 會等待已取得的 HTTP 回應本文完成非同步清理，包括需要非同步釋放的自訂串流。之後釋放回應或內容時發生的例外不會覆蓋成功完成、原始讀取錯誤或取消的結果；仍會嘗試釋放原始回應和內容。

**HTTP 逾時：** 對於使用共用串流回合處理路徑的文字、內容或回呼串流請求及 Run，在呼叫端取消和請求原則逾時皆未觸發時，可識別的 `HttpClient.Timeout`（內含 `TimeoutException` 的 `TaskCanceledException`）會轉換為 `AIServiceException`。`InnerException` 保留原始傳輸例外，因此 `run.Result` 會以保留逾時原因的失敗狀態完成。呼叫端取消、原則逾時及其他傳輸取消的既有行為保持不變。

<a id="sse-acquisition-cancellation-limitation"></a>

## 已知限制：成功 SSE 回應的本文串流取得

對於 HTTP 200 SSE，如果自訂處理常式使用緩衝本文的 `HttpContent` 包裝器，`ReadAsStreamAsync` 可能在取得本文串流和開始清理之前停滯。呼叫端取消和請求原則逾時後，`run.Result` 仍可能維持未完成、回應未釋放、服務的執行鎖未解除，直到取得完成；下一個 Run 會因已有執行中的 Run 而被拒絕。此問題尚未修復，與清理緩慢不同。預設 `SocketsHttpHandler` 通過了已測試的情境；相同包裝器下的 HTTP 錯誤本文取消也通過了驗證。請使用一般串流內容，避免使用緩衝本文的包裝器。Claude 原生網頁搜尋另有[接續限制](providers.md#claude-native-continuation-limitation)。

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
