# 向量数据库概述

Mythosia.AI 提供统一的 `IVectorStore` 抽象，可跨多个向量数据库后端使用。你只需针对接口编写一次代码，即可在不修改任何检索逻辑的情况下切换后端。

## 核心接口：`IVectorStore`

```csharp
// 插入/更新
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// 搜索
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// 按 ID 获取
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// 删除
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// 工具方法
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## 数据模型

### VectorRecord

每条存储的记录都是一个 `VectorRecord`：

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // 唯一标识符
    public float[] Vector { get; set; }                      // 嵌入向量
    public string Content { get; set; }                      // 原始文本内容
    public Dictionary<string, string> Metadata { get; set; } // 自定义键值元数据
}
```

使用 `Metadata` 字典存储任意自定义字段 — 来源文件、语言、日期、分类等：

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("一些文本"),
    Content = "一些文本",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "zh",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

搜索结果将记录与相似度分数配对：

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0（越高越相似）
}
```

## 可用后端

| 后端 | 包名 | 适用场景 |
|------|------|----------|
| **内存** | `Mythosia.VectorDb.InMemory` | 开发、测试、演示 |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | 生产环境、原生混合检索 |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | 无服务器托管服务 |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | 已有 Postgres 部署、ACID 事务 |

所有后端实现相同的 `IVectorStore` 接口。各后端配置详见[后端配置](vectordb-backends.md)。

## 依赖注入

将任意后端注册为 `IVectorStore`：

```csharp
// 内存
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "my-collection",
    Dimension = 1536
}));

// PostgreSQL
services.AddSingleton<IVectorStore>(new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Database=vectors;",
    Dimension = 1536,
    EnsureSchema = true
}));
```

## 各后端过滤器执行方式

`VectorFilter` 条件会尽可能下推到后端执行：

| 操作符 | 内存 | Qdrant | Pinecone | Postgres |
|--------|------|--------|----------|----------|
| Eq / Ne | 客户端 | **服务端** | **服务端** | **SQL** |
| In / NotIn | 客户端 | **服务端** | **服务端** | **SQL** |
| Gt / Gte / Lt / Lte | 客户端 | 客户端 | **服务端** | **SQL** |
| Like | 客户端 | 客户端 | 客户端 | **SQL** |
| Exists / NotExists | 客户端 | 客户端 | 客户端 | **SQL** |

Postgres 对所有操作符实现了完整的 SQL 下推。Qdrant 和 Pinecone 原生下推等值、集合成员判断和比较运算符。

> **注意：** Qdrant 会静默忽略不支持的过滤运算符（`Like`、`Exists`、`NotExists`）—— 这些运算符也不会在客户端应用。如果在使用 Qdrant 时需要这些运算符，请对返回结果进行额外过滤。
