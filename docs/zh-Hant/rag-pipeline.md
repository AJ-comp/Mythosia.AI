# RAG 管線自訂

## 為什麼需要自訂管線？

預設 RAG 管線開箱即用效果良好，但實際專案往往需要更多控制 — 除錯、提示詞工程、架構設計和檢查。

## 進度追蹤

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // 階段：QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("你的問題", options);
```

## 自訂提示詞範本

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        僅根據以下資訊回答問題。
        如果答案不在上下文中，請回答「我不知道。」

        上下文：
        {context}

        問題：{question}
        """)
    .AddDocument("faq.txt")
)
```

## 共享 RagStore

```csharp
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDocuments("docs/")
    .BuildAsync();

var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

## RagStore 直接查詢

```csharp
RagProcessedQuery result = await store.QueryAsync("退款政策是什麼？");

Console.WriteLine($"改寫後的查詢：{result.RewrittenQuery}");

foreach (var ref_ in result.References)
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
```

## 內部運作原理

呼叫 `.WithRag()` 時，會在你的 AIService 外層建立一個 `RagEnabledService` 包裝器。其關鍵機制是 [AIRequestContext](request-contexts.md)。

### 完整流程

```
ragService.GetCompletionAsync("退款政策是什麼？")
    ↓
① RagEnabledService 執行 RAG 管線
   查詢改寫 → 嵌入 → 檢索 → 上下文組裝
    ↓
② TemplateContextBuilder 替換 {context} 和 {question}
    ↓
③ RagEnabledService 建立 AIRequestContext
   RequestMessageOverride = 組裝後的提示詞
    ↓
④ 呼叫 _innerService.GetCompletionAsync(原始訊息, context)
    ↓
⑤ AIService.GetLatestMessages() 替換最後一則訊息
   對話歷史：「退款政策是什麼？」（保留原文）
   模型看到的：組裝後的提示詞（RequestMessageOverride）
```

### 為什麼這樣設計？

- **對話歷史保留原始問題** — 後續追問「那個怎麼樣？」才有正確的上下文
- **模型接收組裝後的提示詞** — 包含檢索到的文件和問題
- **AIService 狀態不會被修改** — `AsyncLocal<T>` 提供每個請求的隔離

### 程式碼實作

```csharp
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),
    context: BuildRequestContext(processed));

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)
    };
}
```
