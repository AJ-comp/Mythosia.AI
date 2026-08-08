# 向量存储操作

## 插入/更新（Upsert）

插入或更新单条记录。如果已存在相同 `Id` 的记录，则会被替换。

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("30 天内可以退款。"),
    Content = "30 天内可以退款。",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "zh",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## 批量插入/更新

单次调用中插入多条记录。比循环调用 `UpsertAsync` 更高效 — 后端会在可用时使用批量 API。

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## 搜索

返回与查询向量最相似的 Top-K 条记录。可选在评分前按元数据过滤。

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("退款政策是什么？");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  来源：{r.Record.Metadata["source"]}");
}
```

### 带过滤器的搜索

将向量相似度与元数据过滤结合使用：

```csharp
var filter = new VectorFilter()
    .Where("language", "zh")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

完整的过滤 API 详见 [VectorFilter](vector-filter.md)。

## 混合检索

融合稠密向量相似度与关键词（BM25）搜索。对包含特定术语、名称或代码的查询有更好的召回率。

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("订单 #12345 状态");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "订单 #12345 状态",   // 用于 BM25 的原始文本
    topK: 5
);
```

各后端的混合检索实现方式：

| 后端 | 机制 |
|------|------|
| **内存** | RRF 融合余弦相似度 + Lucene BM25 分数 |
| **Qdrant** | 服务端：稠密 + 稀疏向量通过 RRF 或 DBSF 融合 |
| **Pinecone** | 稀疏 + 稠密向量在服务端融合 |
| **Postgres** | 向量相似度 + `tsvector`/`trigram` 分数在 SQL 中融合 |

## 按 ID 获取

按 ID 获取特定记录：

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("未找到");
```

应用过滤器限定范围（如使用多租户命名空间时）：

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## 批量获取

单次调用获取多条记录：

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## 按 ID 删除

删除单条记录：

```csharp
await store.DeleteAsync("record-id-123");
```

## 按过滤器删除

删除所有匹配过滤器的记录。谨慎使用 — 这是批量删除操作。

```csharp
// 删除特定文档的所有记录
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## 按过滤器替换

原子性地删除所有匹配过滤器的记录并插入新记录。适合重新索引文档而不留下过期片段。

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> 在 Postgres 上此操作在事务内执行，完全原子化。

## 计数

统计存储的记录数，可选按过滤器限定范围：

```csharp
long total   = await store.CountAsync();
long chinese = await store.CountAsync(new VectorFilter().Where("language", "zh"));

Console.WriteLine($"总计：{total}，中文：{chinese}");
```

## 验证连接

检查后端是否可达。适用于健康检查或启动验证：

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("向量存储连接正常");
}
catch (Exception ex)
{
    Console.WriteLine($"连接失败：{ex.Message}");
}
```

## 在 RAG 中使用

将 `IVectorStore` 传给 `RagBuilder`，即可使用任意后端作为 RAG 检索存储：

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "knowledge-base",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey)
        .AddDocuments("docs/")
    );

var answer = await ragService.GetCompletionAsync("退款政策是什么？");
```

或独立构建 `RagStore`，跨多个 AI 服务共享：

```csharp
RagStore ragStore = await RagStore.BuildAsync(rag => rag
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey)
    .AddDocument("knowledge-base.pdf"));

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
