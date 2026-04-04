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
