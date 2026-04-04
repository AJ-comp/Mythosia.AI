# 後端設定

## 記憶體儲存

最簡單的後端 — 無外部依賴。資料保存在 RAM 中，程序結束後遺失。適合開發、測試和展示。

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**內建混合檢索**：使用 RRF（Reciprocal Rank Fusion）融合餘弦相似度和 BM25 關鍵字得分。

### 診斷

```csharp
// 列出所有儲存的記錄
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"總計：{store.GetTotalRecordCount()}");

// 查看原始相似度分數
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

正式環境級向量資料庫，支援原生混合檢索。透過 Docker 或 Qdrant Cloud 作為獨立服務執行。

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# 在本機啟動 Qdrant
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // gRPC 連接埠
    CollectionName   = "my-docs",
    Dimension        = 1536,           // 必須與嵌入模型相符
    AutoCreateCollection = true        // 首次 upsert 時自動建立集合
});
```

### 全部選項

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Qdrant Cloud 必填

    CollectionName         = "my-collection",  // 必填
    Dimension              = 1536,             // 必填

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // 新增額外的 payload 索引以加速伺服器端過濾
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### 距離策略

| 值 | 說明 |
|----|------|
| `Cosine` | 餘弦相似度 — 適合歸一化嵌入（預設） |
| `Euclidean` | L2 距離 — 距離越小越相似 |
| `DotProduct` | 點積 — 適合單位歸一化向量 |

### 混合融合策略

| 值 | 說明 |
|----|------|
| `Rrf` | Reciprocal Rank Fusion — 穩健的基於排名的融合（預設） |
| `Dbsf` | Distribution-Based Score Fusion — 基於分數分佈的融合 |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "your-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "your-qdrant-cloud-key",
    CollectionName = "production",
    Dimension      = 1536
}
```

### 使用外部 QdrantClient

如果已有配置好的 `QdrantClient`（例如來自 DI 容器），可以直接傳入：

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

儲存器**不會** Dispose 外部提供的客戶端。

> 所有向量儲存器都實作了 `IDisposable`。使用標準建構子建立儲存器時，請呼叫 `Dispose()` 或使用 `using` 來釋放內部資源。

---

## Pinecone

全託管的無伺服器向量資料庫，無需管理基礎設施。

```bash
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "your-api-key"
});
```

### 自動建立索引

如果尚無索引，可讓 SDK 自動建立：

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws"、"gcp" 或 "azure"
    Region          = "us-east-1"
}
```

> 啟用 `AutoCreateIndex` 時，索引使用 `dotproduct` 度量建立 — 這是混合（稀疏 + 稠密）檢索的必要條件。

### 全部選項

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // 必填（或使用 AutoCreateIndex）
    ApiKey                 = "...",           // 必填
    Namespace              = "production",    // 選填：套用於所有操作

    UpsertBatchSize        = 100,             // 每次批次 upsert 的記錄數
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### 使用外部 HttpClient

如果已有配置好的 `HttpClient`（例如來自 `IHttpClientFactory`）：

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

儲存器**不會** Dispose 外部提供的客戶端。

---

## PostgreSQL (pgvector)

使用 [`pgvector`](https://github.com/pgvector/pgvector) 擴充為標準 PostgreSQL 資料庫新增向量相似度搜尋。

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### 前置條件

```sql
-- 在 PostgreSQL 伺服器上執行一次
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- 僅在使用 Trigram 文字搜尋時需要
```

或設定 `EnsureSchema = true` 讓 SDK 自動處理。

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // 自動建立擴充、資料表和索引
});
```

### 索引類型

| 類型 | 類別名稱 | 適用場景 |
|------|----------|----------|
| HNSW | `HnswIndexOptions` | 預設。快速近似搜尋，適合大多數場景。 |
| IVFFlat | `IvfFlatIndexOptions` | 記憶體佔用更低，適合大型靜態資料集。 |
| 無索引 | `NoIndexOptions` | 循序掃描，僅適合極小資料集。 |

```csharp
// HNSW（預設）
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // 每個節點的最大鄰居連線數
        EfConstruction = 64,   // 索引建構時的搜尋範圍（越高品質越好）
        EfSearch       = 40    // 執行時搜尋範圍（越高召回率越高，速度越慢）
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // 倒排列表數量
        Probes = 10    // 查詢時探測的列表數量
    }
}

// 無索引（循序掃描）
new PostgresOptions { Index = new NoIndexOptions() }
```

### 文字搜尋模式

用於混合檢索中的關鍵字部分：

| 模式 | 最適合 |
|------|--------|
| `TsVector` | 標準全文檢索 — 英語、大多數西方語言 |
| `Trigram` | CJK 語言（中文、韓文、日文）、模糊比對 |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // PostgreSQL 文字搜尋組態
}
```

### 距離策略

| 值 | Postgres 運算子 | 說明 |
|----|-----------------|------|
| `Cosine` | `<=>` | 1 − 餘弦相似度（預設） |
| `Euclidean` | `<->` | L2 距離 |
| `InnerProduct` | `<#>` | 負點積 — 適合單位歸一化向量 |

### 執行時搜尋設定

在查詢時微調召回率與延遲的平衡：

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // 直接覆蓋 HNSW ef_search
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### 全部選項

```csharp
new PostgresOptions
{
    ConnectionString  = "...",
    Dimension         = 1536,

    SchemaName        = "public",
    TableName         = "vectors",

    EnsureSchema      = false,
    DistanceStrategy  = DistanceStrategy.Cosine,
    Index             = new HnswIndexOptions(),

    TextSearchConfig  = "simple",
    TextSearchMode    = TextSearchMode.TsVector,

    FailFastOnIndexCreationFailure = true
}
```
