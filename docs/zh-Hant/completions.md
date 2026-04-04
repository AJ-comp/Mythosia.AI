# 文字生成

## 單輪對話

最簡單的用法 — 發送訊息，取得回應：

```csharp
var response = await service.GetCompletionAsync("法國的首都在哪裡？");
Console.WriteLine(response); // 巴黎
```

## 系統提示詞

透過系統提示詞為模型設定角色或指令：

```csharp
service.SystemPrompt = "你是一個簡潔的助理，請用一句話回答。";

var response = await service.GetCompletionAsync("解釋一下遞迴。");
```

## 多輪對話

訊息會自動累積。每次呼叫 `GetCompletionAsync` 都會追加到對話歷史中：

```csharp
await service.GetCompletionAsync("我叫小明。");
var response = await service.GetCompletionAsync("我叫什麼名字？");
// → "你叫小明。"
```

清除對話歷史：

```csharp
service.ClearMessages();
```

## 手動建構訊息

使用 `MessageBuilder` 明確建構訊息：

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("請摘要這段文字：...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 多模態（圖像輸入）

支援視覺能力的供應商可以同時接收圖像和文字：

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("這張圖展示了什麼？")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 快速提問（靜態 API）

無需建立服務實體的一次性查詢，使用靜態方法 `QuickAskAsync`。供應商會根據模型名稱自動識別：

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "法國的首都在哪裡？",
    model: AIModels.OpenAI.Gpt4oMini  // 預設值
);
```

帶圖像的版本：

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "描述這張圖片",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4Vision
);
```

## 圖像快捷方法

無需 `MessageBuilder` 即可分析圖像 — 服務會自動讀取檔案並識別 MIME 類型：

```csharp
// 從檔案路徑
var response = await service.GetCompletionWithImageAsync(
    "這張圖展示了什麼？", "diagram.png");

// 從 URL
var response = await service.GetCompletionWithImageUrlAsync(
    "描述這張照片", "https://example.com/photo.jpg");
```

## 重試上一則訊息

移除上一則助理回應，重新發送最後一則使用者訊息：

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

當上一則回應不理想時，可用此方法讓模型重新生成。

## Token 計數

在發送請求前估算 Token 用量。所有供應商均支援：

```csharp
// 統計目前對話歷史的 Token 數
uint conversationTokens = await service.GetInputTokenCountAsync();

// 統計特定提示詞的 Token 數
uint promptTokens = await service.GetInputTokenCountAsync("你的提示詞");
```

OpenAI 及大多數供應商使用本地 TikToken 估算。Anthropic 和 Google 會呼叫原生 Token 計數 API 以取得精確結果。

## 流式訊息鏈

`BeginMessage()` 提供流式 API，可在一條鏈中建構並發送訊息 — 包括文字、圖像、串流輸出及策略設定：

```csharp
// 文字 + 圖像 → 發送
string response = await service.BeginMessage()
    .AddText("這張圖展示了什麼？")
    .AddImage("diagram.png")
    .SendAsync();

// 一次性查詢（不保留對話歷史）
string answer = await service.BeginMessage()
    .AddText("把這段翻譯成英文")
    .SendOnceAsync();

// 串流輸出
await service.BeginMessage()
    .AddText("寫一首關於春天的詩")
    .StreamAsync(chunk => Console.Write(chunk));

// 自訂逾時和策略
string result = await service.BeginMessage()
    .AddText("分析這張圖片")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` 也支援 `IAsyncEnumerable`：

```csharp
await foreach (var chunk in service.BeginMessage().AddText("講個故事吧").StreamAsync())
    Console.Write(chunk);
```

## 控制輸出長度和溫度

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // 越低越確定
```
