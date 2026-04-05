# 上下文构建

> 📍 **问答检索管道：** [查询改写](rag-query-rewriting.md) → [嵌入](rag-embedding.md) → [过滤](rag-filtering.md) → [检索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → **`上下文构建`**

## 什么是上下文构建？

上下文构建是 RAG 管道的**最后一个阶段**。在检索和排序最相关的文本块之后，这个阶段将它们**组装成一个提示词（prompt）**，供 LLM 生成回答。

精心构建的提示词能减少幻觉，帮助模型基于提供的上下文进行回答。

## 默认 Context Builder

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

## 提示词模板

使用 `{context}` 和 `{question}` 作为占位符：

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        你是一个客服助手。
        请仅根据以下文档内容回答问题。
        如果文档中没有答案，请回答"我没有相关信息。"

        文档：
        {context}

        客户问题：{question}
        """)
    .AddDocument("support-kb.txt")
)
```

### 何时使用模板

- **限制行为** — "如果上下文中没有答案，请说'我不知道'"
- **设定语气** — "请以专业简洁的方式回答"
- **指定角色** — "你是一位医疗顾问"
- **控制语言** — "始终用中文回答"

## 自定义 Context Builder

实现 `IContextBuilder` 接口以获得完全控制：

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### 相关信息 ###");

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "未知";
            sb.AppendLine($"📄 来源：{source}（相关度：{result.Score:P0}）");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"请根据以上信息回答：{query}");
        return sb.ToString();
    }
}
```

## 内部机制

```
搜索结果 + 查询 → ContextBuilder.BuildContext() → 提示词 → LLM
```

选择顺序：

1. **自定义 `IContextBuilder`** — 通过 `.WithContextBuilder()`
2. **`TemplateContextBuilder`** — 通过 `.WithPromptTemplate()`
3. **`DefaultContextBuilder`** — 默认

## 后续步骤

- [管道自定义](rag-pipeline.md) — 精细调整 RAG 行为
- [重排序](rag-reranking.md) — 在构建前提升文本块质量
- [RAG 基础](rag.md) — 回顾完整流程
