# 生成參數

## 通用屬性

所有 AI 服務實體都暴露以下屬性：

```csharp
service.Temperature = 0.7f;        // 隨機性 [0, 2]，越低越確定
service.TopP = 1.0f;               // 核取樣閾值
service.MaxTokens = 1024;          // 最大輸出 Token 數
service.FrequencyPenalty = 0.0f;   // 對重複 Token 的懲罰
service.PresencePenalty = 0.0f;    // 對已出現 Token 的懲罰
service.MaxMessageCount = 20;      // 對話視窗大小（已棄用 — 將於 v7.0 移除）
```

> **已棄用：** `MaxMessageCount`（訊息數量滑動視窗）已過時，將於 v7.0 移除 —— 上下文管理將改為僅透過 `ConversationPolicy` 以 Token 為基礎進行。在移除之前，此視窗保證絕不會捨棄最近一則使用者訊息，因此 agentic 工具執行不會遺失其正在處理的查詢。

## 流式擴充方法

回傳 `this` 以支援鏈式呼叫：

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("你是一個有用的助理。")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| 方法 | 說明 |
|------|------|
| `.WithSystemMessage(string)` | 設定系統提示詞 |
| `.WithTemperature(float)` | 限制在 [0, 2] 範圍內 |
| `.WithMaxTokens(uint)` | 最大輸出 Token 數 |
| `.WithStatelessMode(bool)` | 停用對話歷史累積 |

## 無狀態模式

啟用後，每次請求獨立 — 不發送也不儲存對話歷史：

```csharp
service.StatelessMode = true;

// 等價寫法：
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

適用於不需要歷史上下文的一次性查詢。

## 一次性查詢

以下擴充方法執行單次查詢，不影響也不使用對話歷史：

```csharp
// 文字提示
string response = await service.AskOnceAsync("2+2 等於多少？");

// 訊息（多模態）
string response = await service.AskOnceAsync(message);

// 從檔案路徑載入圖像
string response = await service.AskOnceWithImageAsync("描述一下這張圖", "photo.jpg");
```

## 切換模型

在保留對話歷史的前提下切換模型：

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// 或使用擴充方法 — 清除歷史並重新開始：
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## 管理多個對話

單一服務實體可以管理多個獨立的對話執行緒：

```csharp
// 建立新的對話區塊
var chat1 = service.AddNewChat();

// 切換到另一個對話區塊
service.SetActivateChat(chat2Id);

// 存取所有對話區塊
var allChats = service.ChatRequests;
```

## 檢視對話狀態

取得最後一則助理回應或目前會話的快速摘要：

```csharp
// 取得最後一則助理訊息（若沒有則回傳 null）
string? lastReply = service.GetLastAssistantResponse();

// 取得目前服務狀態的文字摘要
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: 你是一個有用的助理。
```

## 複製服務設定

從另一個服務實體複製所有設定（不包括對話歷史）：

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
