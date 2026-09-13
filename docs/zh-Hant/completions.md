# 文字生成

若要分離每個請求的設定並衍生多個版本，請使用[請求建構器](request-building.md)。先呼叫`CreateRequest(...)`，再串接`With...`。服務屬性與服務上的fluent方法維持原有行為。

<a id="completion-cancellation"></a>

## 取消不再需要的回答

使用者關閉頁面、按下停止，或應用程式等待逾時後，可能不再需要這個回答。傳入 `CancellationToken` 可中斷用戶端的通訊與工作，避免多餘的工具呼叫和後續模型呼叫。需要完整答案時仍可使用 `GetCompletionAsync`；僅需取消時不必建立 Run。

### Before：呼叫端不傳入取消訊號

```csharp
string answer = await service.CreateRequest("摘要這份文件。")
    .GetCompletionAsync();
```

### After：使用者操作或 30 秒後取消

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("摘要這份文件。")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("已取消。");
}
```

呼叫期間保留權杖來源，讓停止按鈕或頁面關閉事件呼叫 `cancellation.Cancel()`。範例也會在 30 秒後要求取消。清理結束後，呼叫端收到 `OperationCanceledException`。使用 `CancellationTokenSource` 設定的期限也屬於呼叫端取消；現有 `FunctionCallingPolicy.TimeoutSeconds` 保留原有逾時錯誤行為。

服務的字串與 `Message` 多載、泛型完成方法、要求建構器和 `MessageChain.SendAsync` / `SendOnceAsync` 均接受權杖。省略權杖的既有呼叫仍可使用。以下是其他呼叫入口：

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "摘要這份文件。", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "以JSON傳回標題和作者。", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("摘要這份文件。")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("翻譯這個句子。")
    .SendOnceAsync(cancellationToken: token);
```

權杖傳遞至要求準備、HTTP 傳送與讀取、配合取消的本機工具以及後續模型回合。偵測到取消後，略過排隊工具和後續回合。清理維持已記錄工具呼叫與結果的配對，因此忽略權杖的已啟動工具可能延遲清理。取消不會復原已完成操作或清空對話歷程。參閱[工具執行約定](function-calling.md#tool-execution-contract)。

不保證供應商伺服器停止生成或計費。OpenAI 說明一般 Responses 要求可透過中斷連線取消；Google 明確說明只取消用戶端，相關用量仍計費。[OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal)。背景工作本身需要明確呼叫 `CancelAsync()`；取消 `WaitForCompletionAsync(cancellationToken: ...)` 只停止等待。一般完成要求不會轉換為背景執行。參閱 [Perplexity](perplexity.md)。

<a id="completion-cancellation-migration"></a>

此功能屬於Mythosia.AI 8.0.0。不傳權杖的呼叫和既有 profile/context 位置參數在原始碼層面仍有效，但使用端需要重新建置。自訂 `IAIService` 實作必須在兩個完成方法簽章末尾新增並傳遞 `CancellationToken cancellationToken = default`。繼承 `AIService` 的自訂供應商保留現有 `GetCompletionAsync(Message)` override，並將受保護的 `RequestCancellationToken` 傳給傳輸層。建構器和 Run 本身不需要這次介面修改。 若子類別覆寫了已修改的字串/profile/context 完成呼叫、影像輔助方法或 `RunAgentAsync` 等 public virtual 多載，也必須新增並傳遞新的 `CancellationToken`；僅接收單一 `Message` 的供應商 override 保留原簽章。直接繫結至已修改簽章的方法群組委派可能需要改成明確傳入或省略權杖的 lambda。

## 單輪對話

最簡單的用法 — 發送訊息，取得回應：

```csharp
var response = await service.GetCompletionAsync("法國的首都在哪裡？");
Console.WriteLine(response); // 巴黎
```

只需要完整答案時，`GetCompletionAsync` 仍然適用。如需逐段顯示、執行中停止或追加指示，請參閱 [Run 使用指南](execution-api-transition.md)。

## 系統提示詞

透過系統提示詞為模型設定角色或指令：

```csharp
service.SystemMessage = "你是一個簡潔的助理，請用一句話回答。";

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
service.ActivateChat.ClearMessages();
```

## 手動建構訊息

使用 `MessageBuilder` 明確建構訊息：

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("請摘要這段文字：...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 多模態（圖像輸入）

支援視覺能力的供應商可以同時接收圖像和文字：

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("這張圖展示了什麼？")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

圖表和截圖分析、本地函式呼叫、快速回答後的深入審查可使用 [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash)。推理預設關閉，透過 `WithDeepSeekReasoning(...)` 或請求級 `WithReasoning(...)` 開啟。

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
    model: AIModels.OpenAI.Gpt4_1
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

Perplexity: [使用 Agent 預設回答 / 來源、影像與結構化答案](perplexity.md).
