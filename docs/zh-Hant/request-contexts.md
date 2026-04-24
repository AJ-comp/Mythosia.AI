# AIRequestContext

## 概述

`AIRequestContext` 可以修改**模型在單次請求中看到的內容** — 注入額外指令、加入參考文件或完全替換使用者訊息 — 而不會永久改變服務的系統訊息或對話歷史。

## 它解決了什麼問題

```csharp
// ❌ 沒有 AIRequestContext — 汙染系統訊息
var originalSystem = service.SystemMessage;
service.SystemMessage = originalSystem +
    $"\n\n請根據以下資訊回答：\n{retrievedDocs}";
var answer = await service.GetCompletionAsync(userQuestion);
service.SystemMessage = originalSystem;
```

**有了** `AIRequestContext`：

```csharp
// ✅ 有 AIRequestContext — 簡潔、作用域隔離、無副作用
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\n請根據以下資訊回答：\n{retrievedDocs}"
    });
```

## 可用屬性

### SystemMessagePrefix

僅在本次請求中向系統訊息前部追加文字：

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "今天的日期是 2026-03-31。\n"
};
```

### SystemMessageSuffix

僅在本次請求中向系統訊息尾部追加文字：

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\n請始終用中文回答。"
};
```

### AdditionalMessages

僅在本次請求中插入額外訊息：

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("參考資料：退款政策允許 30 天內退貨。").Build()
    }
};
```

### RequestMessageOverride

完全替換本次請求的使用者訊息：

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"根據以下上下文回答問題。\n\n上下文：{docs}\n\n問題：{userQuery}")
        .Build()
};
```

> **💡 提示：** 使用 `.WithRag()` 時，RAG 管線會自動利用此屬性。詳見[管線自訂](rag-pipeline.md#how-it-works-internally)。

## 與 AIRequestProfile 組合

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n上下文：\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("範例：...").Build()
        }
    }
);
```

詳見 [AIRequestProfile](request-profiles.md)。

## 使用 `SystemMessageProvider` 自動注入

### 此功能解決的問題

典型的聊天應用有多個需要相同基準（今日日期、活動資料夾、工作階段資訊等）的 LLM 進入點。**不使用** `SystemMessageProvider` 時，每個呼叫點都需要記得建構並傳遞該上下文：

```csharp
// ❌ 不使用 SystemMessageProvider — 每個進入點都必須記得注入
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. 主聊天回應
var answer = await service.GetCompletionAsync(userMessage,
    new AIRequestContext { SystemMessageSuffix = today });

// 2. 標題生成器（後來新增）
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 3. 摘要器（更晚新增）
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 4. Agent 呼叫 — 容易忘記！ 編譯器不會警告你
var agentResult = await service.RunAgentAsync(goal);  // ← 日期遺失，靜默 bug
```

此方式的問題：

- 相同的上下文建構片段在每個呼叫點**重複**
- 新進入點（上面的 `RunAgentAsync`）**容易遺漏** — 沒有編譯時檢查
- 每個新增 LLM 呼叫的新功能都必須記住此慣例
- 測試也必須在每個呼叫點複製上下文設定

使用 `SystemMessageProvider`，基準**只需註冊一次**，所有外發呼叫自動接收：

```csharp
// ✅ 使用 SystemMessageProvider — 註冊一次，隨處生效
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// 以下所有呼叫都自動接收基準 — 無需每次呼叫的樣板程式碼
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← 也接收基準

// 串流進入點也一樣 — 相同基準，不需每次呼叫的樣板程式碼
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### 運作方式

透過 `WithSystemMessageProvider` fluent 輔助方法註冊一次回呼。每個外發呼叫（`GetCompletionAsync`、`StreamAsync`、`RunAgentAsync`、`RunAgentStreamAsync`）都會自動呼叫它以建構基準上下文：

```csharp
// 通常在服務建構 / DI 設定時註冊
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### 用於 IO 支援的 provider 的非同步多載

當基準上下文來自資料庫、快取或 HTTP 呼叫時，請使用非同步多載，以便 provider 無需透過 `.Result` / `.GetAwaiter().GetResult()` 阻塞。根據 lambda arity 自動進行多載解析 — 無參數為 sync，一個 `CancellationToken` 為 async：

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

非串流路徑（`GetCompletionAsync`、`RunAgentAsync`）在設計上不支援取消 — 其簽章不接受 `CancellationToken`，始終向 provider 傳遞 `CancellationToken.None`。如果您的 provider 需要取消（例如長時間執行的 DB 查詢），請使用串流路徑（`StreamAsync`、`RunAgentStreamAsync`），它們會將呼叫者的 token 傳遞到 provider 回呼。

### 與顯式 per-call 上下文合併

當呼叫同時擁有已註冊的 provider **且**傳遞顯式的 `AIRequestContext` 時，兩者按欄位合併：

| 欄位 | 合併規則 |
|---|---|
| `SystemMessagePrefix` | 顯式值非 null 時優先，否則使用 provider |
| `SystemMessageSuffix` | 顯式值非 null 時優先，否則使用 provider |
| `RequestMessageOverride` | 顯式值非 null 時優先，否則使用 provider |
| `AdditionalMessages` | 串接（provider 在前，然後是顯式） |

原因：常見情境是「provider 提供基準，特定呼叫想替換一個純量欄位或加入額外訊息」— 欄位級覆寫使語意可預測，避免意外的串接。

### 每次呼叫的 invocation

Provider **每個請求呼叫一次**，因此回傳值可以反映最新狀態（時間戳、工作階段等）。回傳 `null` 是 no-op — 相當於該呼叫未設定 `SystemMessageProvider`。

### 小結：何時選擇此工具 — 三條件的交集

從上述用例與合併規則退一步看，`SystemMessageProvider` 是以下 **三個條件同時成立** 時的專用工具：

1. **所有 LLM 呼叫都需要共通** 的基準 — 不想在每個入口點記得手動注入
2. **值必須在呼叫時動態計算** — 目前時間、作用中資料夾、登入使用者等在啟動時無法固定的值
3. **不能汙染永久狀態（`SystemMessage`、對話歷史）** — 該值不能洩漏到後續呼叫

三個條件中缺一，較簡單的工具便是正解：

| 情境 | 正解 | 原因 |
|---|---|---|
| 基準在整個工作階段中 **固定（不變）** | `service.SystemMessage = "..."` | 一次設定即可，不需要 provider |
| **僅一次呼叫** 需要特殊處理 | 於呼叫時顯式傳入 `AIRequestContext` | 非共用基準，而是一次性注入 |
| 共用 + 動態 + 不汙染 **（三條件全部）** | **`SystemMessageProvider`** | 此三者交集的專用工具 |

#### 為何不與 `AIRequestContext` 的「一次性」原則衝突

`AIRequestContext` 的本質不是「只用一次」，而是 **「絕不汙染永久狀態」**。`SystemMessageProvider` 是一個在每次請求時 **重新執行回呼** 以 **產生該請求專用的全新 `AIRequestContext`** 的工廠。產生的上下文仍是 per-request 範圍，值不會洩漏到對話歷史，下一次呼叫時回呼再次執行以反映 **當下的** 值。所以 provider 並未違反 `AIRequestContext` 的設計原則，而是 **將其自動化**。

具體而言，下面的註冊並 **不會** 修改 `service.SystemMessage` 與 `service.Messages`：

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- 跨過午夜後，下一次呼叫的 provider 重新執行會自動反映 **新日期**（並非靜態）
- 一週後開啟對話歷史，也不會看到過去的請求中嵌入「Today is ...」
- 在多使用者環境中使用共用服務，每次呼叫都會產生獨立的上下文

> 於 Mythosia.AI v6.3.0+ 提供。
