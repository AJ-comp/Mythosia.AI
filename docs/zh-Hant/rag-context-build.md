# 上下文構建

> 📍 **問答檢索管線：** [查詢改寫](rag-query-rewriting.md) → [嵌入](rag-embedding.md) → [過濾](rag-filtering.md) → [檢索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → **`上下文構建`**

## 什麼是上下文構建？

上下文構建是 RAG 管線的**最後一個階段**。在檢索和排序最相關的文字區塊之後，這個階段將它們**組裝成一個提示詞（prompt）**，供 LLM 生成回答。

精心構建的提示詞能減少幻覺，幫助模型基於提供的上下文進行回答。

## 預設 Context Builder

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "Answer the question based on the following context:",
    QueryPrefix = "Question:",
    IncludeScores = false,
    IncludeSource = true
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

## 提示詞範本

使用 `{context}` 和 `{question}` 作為佔位符：

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        你是一個客服助手。
        請僅根據以下文件內容回答問題。
        如果文件中沒有答案，請回答「我沒有相關資訊。」

        文件：
        {context}

        客戶問題：{question}
        """)
    .AddDocument("support-kb.txt")
)
```

### 何時使用範本

- **限制行為** — 「如果上下文中沒有答案，請說『我不知道』」
- **設定語氣** — 「請以專業簡潔的方式回答」
- **指定角色** — 「你是一位醫療顧問」
- **控制語言** — 「始終用繁體中文回答」

## 自訂 Context Builder

實作 `IContextBuilder` 介面以獲得完全控制：

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### 相關資訊 ###");

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "未知";
            sb.AppendLine($"📄 來源：{source}（相關度：{result.Score:P0}）");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"請根據以上資訊回答：{query}");
        return sb.ToString();
    }
}
```

## 內部機制

```
搜尋結果 + 查詢 → ContextBuilder.BuildContext() → 提示詞 → LLM
```

選擇順序：

1. **自訂 `IContextBuilder`** — 透過 `.WithContextBuilder()`
2. **`TemplateContextBuilder`** — 透過 `.WithPromptTemplate()`
3. **`DefaultContextBuilder`** — 預設

## 後續步驟

- [管線自訂](rag-pipeline.md) — 精細調整 RAG 行為
- [重排序](rag-reranking.md) — 在構建前提升文字區塊品質
- [RAG 基礎](rag.md) — 回顧完整流程
