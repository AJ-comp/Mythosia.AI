# Token 用量

Token 用量表示一次模型請求在輸入、輸出、快取與推理上消耗了多少 token。在 Mythosia.AI 中，這些資料會透過串流事件上的 `TokenUsage` 提供。

當一次回應不只包含一個 LLM 呼叫時，這點特別重要。一般回答通常只有一個 round；但 agent 或 function calling 流程可能先呼叫模型、執行工具，再把工具結果帶回去呼叫模型。因此有兩個值需要分清楚。

- `RoundUsage` 表示單一 LLM round 的用量。
- `Completion.Usage` 表示整個 stream run 的累計用量。

## 什麼是 round？

「Round」是與模型一次完整的來回：你的應用程式發送一個 prompt，模型回覆，這次交換就結束了。一則普通的聊天訊息正好是一個 round。

Function calling 與 agent 會自動引入更多 round。以下是一個具體範例——使用者問道：*「現在台北的天氣如何？」*

**Round 1 — 決定使用哪個工具**

你的 app 把使用者訊息傳送給模型。模型不知道目前天氣，因此它不直接回答，而是回傳一個函式呼叫請求：*「請呼叫 `GetWeather("Taipei")`」*。模型這一輪的回應就此結束。

**兩個 round 之間**

你的 app 執行 `GetWeather("Taipei")` 並取得結果：`「15°C，多雲」`。

**Round 2 — 最終回答**

你的 app 將函式結果作為新訊息傳回給模型。此時模型具備了所需的全部資訊，寫出最終回答：*「台北目前 15°C，多雲。」*

使用者一則訊息觸發了兩個 LLM round。若模型還需要呼叫另一個工具，就會有第三個 round，以此類推。

`RoundUsage` 在每個 round 結束後觸發，只包含該 round 的 token 數量。`Completion.Usage` 在所有內容完成後觸發一次，包含所有 round 的彙總數量。

## 為什麼需要它

如果你在做聊天 UI 的 context token meter，通常應該使用最新的 `RoundUsage.Usage.TotalTokens`。它最接近「如果現在繼續對話，下一次模型輸入會有多大」這個值。

如果是記錄 log、診斷或成本分析，請使用 `Completion.Usage.TotalTokens`。它會保留整個 run 的累計用量，包括 function calling 或 agent 造成的多個 round。

如果是效能調校，快取與推理相關欄位可以幫你看出 provider 是否重用了快取輸入，以及模型在內部推理上額外花了多少 token。

## 事件模型

| 事件 | 含義 | 適合用途 |
|---|---|---|
| `StreamingContentType.RoundUsage` | 剛結束的 LLM round 用量 | UI context meter、逐 round debug |
| `StreamingContentType.Completion` | 最終事件，包含累計用量 | Log、診斷、成本報表 |

`RoundUsage.Usage` 不是累計值。假設 round 1 使用 10,100 token，round 2 使用 14,000 token，最後的 `Completion.Usage.TotalTokens` 可能是 24,100，但最後一個 `RoundUsage.Usage.TotalTokens` 仍然是 14,000。

| 屬性 | 含義 |
|---|---|
| `RoundIndex` | 從 1 開始的 LLM round 編號 |
| `IsFinalRound` | 如果這是 stream 中最後一個 LLM round，則為 `true` |

只要 provider 回傳 usage 資料，就會 emit token usage。接收 usage 事件不需要開啟 `IncludeMetadata = true`。

## 最終累計用量

如果你需要整個串流請求的總用量，請讀取 `Completion.Usage`。

```csharp
await foreach (var chunk in service.StreamAsync("解釋一下量子運算", StreamOptions.FullOptions))
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

如果只有一個 LLM round，這個值通常接近 `RoundUsage`。如果是 agent，它就是所有 LLM round 的總和。

## UI Token Meter

Context 大小的 meter 應使用最新的 `RoundUsage`。

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

最後一個模型 round 看到的是最新的對話狀態，包括 run 過程中加入的工具結果。因此對聊天 UI 來說，最後一個 `RoundUsage.TotalTokens` 最能代表回應後的 context 大小。

## Function Calling 與 Agent

在 function calling 流程中，模型可能會執行多次。讀取每一個 `RoundUsage`，把最後一個保留給 UI；最後再用 `Completion.Usage` 做累計診斷。

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

## 快取與推理欄位

如果 provider 提供這些資料，`TokenUsage` 也會包含快取與推理相關欄位。

| 屬性 | 含義 |
|---|---|
| `InputTokens` | prompt/input 中的 token |
| `OutputTokens` | 模型產生的 token |
| `TotalTokens` | 該事件範圍內的輸入 + 輸出 |
| `CachedInputTokens` | 從快取命中的輸入 token |
| `CacheCreationTokens` | 寫入快取的 token |
| `ReasoningTokens` | 隱藏內部推理消耗的 token |
| `VisibleOutputTokens` | 不含推理的可見輸出 token |

## Provider 注意事項

不同 provider 會把 usage 資料放在不同的 stream chunk 上。Mythosia.AI 會把它們統一整理成 `RoundUsage` 和最終的 `Completion.Usage`。

Gemini 是最需要留意的情況：usage 可能出現在 text 或 status chunk 上，有時甚至會在 function-call chunk 之後才到達。函式庫會繼續讀取 stream，確保在進入下一 round 前捕捉這些 usage。

作為 consumer，建議優先使用已標準化的 `RoundUsage` 和 `Completion.Usage`，不要自己解析 provider 專屬 metadata。
