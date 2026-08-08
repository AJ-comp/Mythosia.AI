# 向量儲存操作

## 插入/更新（Upsert）

插入或更新單筆記錄。如果已存在相同 `Id` 的記錄，則會被取代。

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("30 天內可以退款。"),
    Content = "30 天內可以退款。",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "zh-Hant",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## 批次插入/更新

單次呼叫中插入多筆記錄。比迴圈呼叫 `UpsertAsync` 更高效 — 後端會在可用時使用批次 API。

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

## 搜尋

傳回與查詢向量最相似的 Top-K 筆記錄。可選在評分前按元資料過濾。

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("退款政策是什麼？");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  來源：{r.Record.Metadata["source"]}");
}
```

### 帶過濾器的搜尋

將向量相似度與元資料過濾結合使用：

```csharp
var filter = new VectorFilter()
    .Where("language", "zh-Hant")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

完整的過濾 API 詳見 [VectorFilter](vector-filter.md)。

## 混合檢索

融合稠密向量相似度與關鍵字（BM25）搜尋。對包含特定術語、名稱或代碼的查詢有更好的召回率。

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("訂單 #12345 狀態");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "訂單 #12345 狀態",   // 用於 BM25 的原始文字
    topK: 5
);
```

各後端的混合檢索實作方式：

| 後端 | 機制 |
|------|------|
| **記憶體** | RRF 融合餘弦相似度 + Lucene BM25 分數 |
| **Qdrant** | 伺服器端：稠密 + 稀疏向量透過 RRF 或 DBSF 融合 |
| **Pinecone** | 稀疏 + 稠密向量在伺服器端融合 |
| **Postgres** | 向量相似度 + `tsvector`/`trigram` 分數在 SQL 中融合 |

## 按 ID 取得

按 ID 取得特定記錄：

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("未找到");
```

套用過濾器限定範圍（如使用多租戶命名空間時）：

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## 批次取得

單次呼叫取得多筆記錄：

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## 按 ID 刪除

刪除單筆記錄：

```csharp
await store.DeleteAsync("record-id-123");
```

## 按過濾器刪除

刪除所有符合過濾器的記錄。請謹慎使用 — 這是批次刪除操作。

```csharp
// 刪除特定文件的所有記錄
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## 按過濾器取代

原子性地刪除所有符合過濾器的記錄並插入新記錄。適合重新索引文件而不留下過期片段。

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

> 在 Postgres 上此操作在交易內執行，完全原子化。

## 計數

統計儲存的記錄數，可選按過濾器限定範圍：

```csharp
long total      = await store.CountAsync();
long traditional = await store.CountAsync(new VectorFilter().Where("language", "zh-Hant"));

Console.WriteLine($"總計：{total}，繁體中文：{traditional}");
```

## 驗證連線

檢查後端是否可達。適用於健康檢查或啟動驗證：

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("向量儲存連線正常");
}
catch (Exception ex)
{
    Console.WriteLine($"連線失敗：{ex.Message}");
}
```

## 在 RAG 中使用

將 `IVectorStore` 傳給 `RagBuilder`，即可使用任意後端作為 RAG 檢索儲存：

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

var answer = await ragService.GetCompletionAsync("退款政策是什麼？");
```

或獨立建構 `RagStore`，跨多個 AI 服務共享：

```csharp
RagStore ragStore = await RagStore.BuildAsync(rag => rag
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey)
    .AddDocument("knowledge-base.pdf"));

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
