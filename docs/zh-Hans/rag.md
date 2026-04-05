# RAG（检索增强生成）

RAG 通过在查询时检索相关文本片段，让模型基于你自己的文档来回答问题。

## 安装

```bash
dotnet add package Mythosia.AI.Rag
```

## 快速上手

在任何 `IAIService` 上使用 `.WithRag()` 即可通过流式 API 启用 RAG：

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("退款政策是什么？");
```

文档会被自动分割、嵌入并存储。查询时，最相关的文本片段会被检索并注入到提示词中。

## 添加文档

支持多种来源类型：

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // 本地文件
    .AddDocument("https://example.com/doc.txt")   // URL
    .AddText("也可以直接添加文本内容。")            // 原始字符串
)
```

## 自定义嵌入提供商

默认情况下，RAG 使用服务自身的提供商生成嵌入。如需使用专用嵌入模型：

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbeddingProvider(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## 自定义向量存储

默认使用内存存储。生产环境请接入持久化向量存储：

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(connectionString, embedDimension: 1536);

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseVectorStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## 查询选项

按查询微调检索行为：

```csharp
var options = new RagQueryOptions
{
    TopK = 5,              // 检索的文本片段数量
    ScoreThreshold = 0.7f  // 最低相似度分数
};

var response = await service.GetCompletionAsync("你的问题", ragOptions: options);
```

## 后续步骤

- [混合搜索](rag-hybrid-search.md) — 语义搜索与关键词搜索结合
- [查询重写](rag-query-rewriting.md) — 基于对话上下文优化查询
- [重新排序](rag-reranking.md) — 进一步提升搜索结果准确度
- [管线自定义](rag-pipeline.md) — 精细控制 RAG 流程
- [智能体 RAG](rag-agentic.md) — AI 自行判断何时搜索什么
- [向量存储](vectordb-overview.md) — 持久化存储配置
- [文本分割器](text-splitters.md) — 自定义文档分割方式
