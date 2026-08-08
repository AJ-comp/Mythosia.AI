# 對話管理

## 對話歷史的運作方式

每次呼叫 `GetCompletionAsync` 或 `StreamAsync` 都會追加到服務的內部訊息清單。模型擁有所有前序輪次的上下文。

```csharp
await service.GetCompletionAsync("我最喜歡的顏色是藍色。");
var reply = await service.GetCompletionAsync("我最喜歡的顏色是什麼？");
// → "你最喜歡的顏色是藍色。"
```

重新開始：

```csharp
service.ActivateChat.ClearMessages();
```

## 摘要策略

### 為什麼需要自動摘要？

每條對話歷史訊息都會在每次請求時發送給模型。隨著對話增長，會產生兩個問題：

1. **成本** — 更長的歷史意味著每次請求計費更多輸入 Token
2. **上下文溢出** — 一旦歷史超過模型的上下文視窗（如 GPT-4o 的 128K Token），請求將直接失敗

**`SummaryConversationPolicy`** 自動將舊訊息壓縮成簡要摘要，同時保留最近訊息的原文。

### 按訊息數觸發

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // 歷史超過 20 則訊息時觸發摘要
    keepRecentCount: 5  // 保留最近 5 則訊息原文
);
```

### 按 Token 數觸發

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);
```

### 同時按兩者觸發（OR 條件）

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,
    keepRecentCount: 7
);
```

設定後，摘要在 `GetCompletionAsync` 時自動觸發。

### 運作原理

1. 每次生成前，策略檢查對話是否超過設定的閾值。
2. 若觸發，舊訊息透過無狀態 LLM 呼叫被壓縮為簡要文字。
3. 摘要作為系統訊息前綴注入 — 模型將其視為先前的上下文。
4. 最近的訊息保持原樣。

### 串流輸出

摘要不會在 `StreamAsync` 期間自動觸發。請先明確呼叫：

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("繼續我們的對話..."))
    Console.Write(chunk.Content);
```

## 儲存與還原摘要

持久化摘要以便在工作階段重啟後保留上下文：

```csharp
string saved = service.ConversationPolicy.CurrentSummary;

service.ConversationPolicy.LoadSummary(saved);
```
