# RAG（檢索增強生成）

RAG 透過在查詢時檢索相關文字片段，讓模型基於你自己的文件來回答問題。

## 安裝

```bash
dotnet add package Mythosia.AI.Rag
```

## 快速上手

在任何 `IAIService` 上使用 `.WithRag()` 即可透過流式 API 啟用 RAG：

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("退款政策是什麼？");
```

文件會被自動分割、嵌入並儲存。查詢時，最相關的文字片段會被檢索並注入到提示詞中。

## 新增文件

支援多種來源類型：

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // 本機檔案
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("也可以直接加入文字內容。")            // 原始字串
)
```

## 自訂嵌入供應商

預設情況下，RAG 使用內建的本機嵌入供應商。如需使用專用嵌入模型：

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## 自訂向量儲存

預設使用記憶體儲存。正式環境請接入持久化向量儲存：

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## 查詢選項

按查詢微調檢索行為：

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};

var response = await service.GetCompletionAsync("你的問題", options: options);
```

## 後續步驟

- [混合搜尋](rag-hybrid-search.md) — 語義搜尋與關鍵字搜尋結合
- [查詢重寫](rag-query-rewriting.md) — 基於對話上下文優化查詢
- [重新排序](rag-reranking.md) — 進一步提升搜尋結果準確度
- [管線自訂](rag-pipeline.md) — 精細控制 RAG 流程
- [智能體 RAG](rag-agentic.md) — AI 自行判斷何時搜尋什麼
- [向量儲存](vectordb-overview.md) — 持久化儲存配置
- [文字分割器](text-splitters.md) — 自訂文件分割方式
