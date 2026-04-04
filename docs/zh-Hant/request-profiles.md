# AIRequestProfile

## 概述

`AIRequestProfile` 可以**僅對單次請求**覆寫生成參數 — 溫度、最大 Token 數、無狀態模式、函式呼叫等。服務的全域設定不受影響。

## 它解決了什麼問題

```csharp
// ❌ 沒有 AIRequestProfile — 手動管理狀態
var savedTemp = service.Temperature;
service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("改寫這個查詢：...");

// 還原 — 容易遺忘，非執行緒安全
service.Temperature = savedTemp;
```

**有了** `AIRequestProfile`，一行搞定：

```csharp
// ✅ 有 AIRequestProfile — 簡潔且安全
var rewritten = await service.GetCompletionAsync("改寫這個查詢：...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

## 可用屬性

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,
    MaxTokens = 256,
    Stateless = true,
    DisableFunctions = true,
    DisableReasoning = true
};

var response = await service.GetCompletionAsync("你的提示詞", profile);
```

所有屬性均為可選 — 只設定需要覆寫的項。

## 預定義設定

```csharp
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## 實際用例

### RAG 管線中的內部查詢改寫

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

var betterQuery = await service.GetCompletionAsync(
    $"改寫為搜尋查詢：{userQuery}",
    RequestProfiles.QueryRewrite);

var answer = await service.GetCompletionAsync(userQuery);
```

### 對特定步驟停用函式

```csharp
var directAnswer = await service.GetCompletionAsync(
    "2 + 2 等於多少？",
    new AIRequestProfile { DisableFunctions = true });
```

## 與 AIRequestContext 組合

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\n請簡潔作答。" }
);
```

詳見 [AIRequestContext](request-contexts.md)。
