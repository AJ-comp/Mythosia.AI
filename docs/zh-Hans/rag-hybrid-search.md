# 混合检索

> 📍 **问答检索管道：** [查询改写](rag-query-rewriting.md) → 嵌入 → 过滤 → **`检索`** → [重排序](rag-reranking.md) → 上下文构建

## 为什么需要混合检索？

纯向量检索擅长捕捉语义含义 — "取消我的订阅"能匹配"终止会员资格"，即使两者没有共同词汇。但它可能遗漏用户原样输入的**精确术语**，如产品名称、错误代码或政策标识符。

BM25 关键词检索能完美处理这些场景，但无法理解语义。**混合检索结合了两者的优势**：语义理解加上精确关键词匹配。

## 配置

通过单个方法调用将稠密向量检索与 BM25 关键词检索融合：

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% 向量，40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight` 范围从 0.0（纯 BM25）到 1.0（纯向量）。大多数场景下 **0.5–0.7** 效果较好。

## 选择建议

| 场景 | 建议权重 |
| --- | --- |
| 自然语言通用问答 | 0.7–0.8（偏向量） |
| 含特定术语的技术文档 | 0.4–0.5（均衡） |
| 代码或错误码查找 | 0.2–0.3（偏 BM25） |

## 示例

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("product-catalog.txt")
        .AddDocument("error-codes.txt")
    );

// "ERR-4012" 由 BM25 匹配；语义上下文由向量匹配
var answer = await service.GetCompletionAsync("如何修复 ERR-4012？");
```
