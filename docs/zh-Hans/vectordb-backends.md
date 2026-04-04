# 后端配置

## 内存存储

最简单的后端 — 无外部依赖。数据保存在 RAM 中，进程退出后丢失。适合开发、测试和演示。

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**内置混合检索**：使用 RRF（Reciprocal Rank Fusion）融合余弦相似度和 BM25 关键词得分。

### 诊断

```csharp
// 列出所有存储的记录
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"总计：{store.GetTotalRecordCount()}");

// 查看原始相似度分数
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

生产级向量数据库，支持原生混合检索。通过 Docker 或 Qdrant Cloud 作为独立服务运行。

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# 本地启动 Qdrant
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // gRPC 端口
    CollectionName   = "my-docs",
    Dimension        = 1536,           // 必须与嵌入模型匹配
    AutoCreateCollection = true        // 首次 upsert 时自动创建集合
});
```

### 全部选项

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

    // 添加额外的 payload 索引以加速服务端过滤
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### 距离策略

| 值 | 说明 |
|----|------|
| `Cosine` | 余弦相似度 — 适合归一化嵌入（默认） |
| `Euclidean` | L2 距离 — 距离越小越相似 |
| `DotProduct` | 点积 — 适合单位归一化向量 |

### 混合融合策略

| 值 | 说明 |
|----|------|
| `Rrf` | Reciprocal Rank Fusion — 稳健的基于排名的融合（默认） |
| `Dbsf` | Distribution-Based Score Fusion — 基于分数分布的融合 |

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

如果已有配置好的 `QdrantClient`（例如来自 DI 容器），可以直接传入：

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

存储器**不会** Dispose 外部提供的客户端。

> 所有向量存储器都实现了 `IDisposable`。使用标准构造函数创建存储器时，请调用 `Dispose()` 或使用 `using` 来释放内部资源。

---

## Pinecone

全托管的无服务器向量数据库，无需管理基础设施。

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

### 自动创建索引

如果尚无索引，可让 SDK 自动创建：

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

> 启用 `AutoCreateIndex` 时，索引使用 `dotproduct` 度量创建 — 这是混合（稀疏 + 稠密）检索的必要条件。

### 全部选项

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // 必填（或使用 AutoCreateIndex）
    ApiKey                 = "...",           // 必填
    Namespace              = "production",    // 可选：应用于所有操作

    UpsertBatchSize        = 100,             // 每次批量 upsert 的记录数
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

如果已有配置好的 `HttpClient`（例如来自 `IHttpClientFactory`）：

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

存储器**不会** Dispose 外部提供的客户端。

---

## PostgreSQL (pgvector)

使用 [`pgvector`](https://github.com/pgvector/pgvector) 扩展为标准 PostgreSQL 数据库添加向量相似度搜索。

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### 前置条件

```sql
-- 在 PostgreSQL 服务器上执行一次
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- 仅在使用 Trigram 文本搜索时需要
```

或设置 `EnsureSchema = true` 让 SDK 自动处理。

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // 自动创建扩展、表和索引
});
```

### 索引类型

| 类型 | 类名 | 适用场景 |
|------|------|----------|
| HNSW | `HnswIndexOptions` | 默认。快速近似搜索，适合大多数场景。 |
| IVFFlat | `IvfFlatIndexOptions` | 内存占用更低，适合大型静态数据集。 |
| 无索引 | `NoIndexOptions` | 顺序扫描，仅适合极小数据集。 |

```csharp
// HNSW（默认）
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // 每个节点的最大邻居连接数
        EfConstruction = 64,   // 索引构建时的搜索范围（越高质量越好）
        EfSearch       = 40    // 运行时搜索范围（越高召回率越高，速度越慢）
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // 倒排列表数量
        Probes = 10    // 查询时探测的列表数量
    }
}

// 无索引（顺序扫描）
new PostgresOptions { Index = new NoIndexOptions() }
```

### 文本搜索模式

用于混合检索中的关键词部分：

| 模式 | 最适合 |
|------|--------|
| `TsVector` | 标准全文检索 — 英语、大多数西方语言 |
| `Trigram` | CJK 语言（中文、韩文、日文）、模糊匹配 |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // PostgreSQL 文本搜索配置
}
```

### 距离策略

| 值 | Postgres 操作符 | 说明 |
|----|-----------------|------|
| `Cosine` | `<=>` | 1 − 余弦相似度（默认） |
| `Euclidean` | `<->` | L2 距离 |
| `InnerProduct` | `<#>` | 负点积 — 适合单位归一化向量 |

### 运行时搜索配置

在查询时微调召回率与延迟的平衡：

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // 直接覆盖 HNSW ef_search
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### 全部选项

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
