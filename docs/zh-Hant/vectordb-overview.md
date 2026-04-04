# 向量資料庫概述

Mythosia.AI 提供統一的 `IVectorStore` 抽象，可跨多個向量資料庫後端使用。你只需針對介面編寫一次程式碼，即可在不修改任何檢索邏輯的情況下切換後端。

## 核心介面：`IVectorStore`

```csharp
// 插入/更新
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// 搜尋
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// 按 ID 取得
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// 刪除
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// 工具方法
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## 資料模型

### VectorRecord

每條儲存的記錄都是一個 `VectorRecord`：

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // 唯一識別碼
    public float[] Vector { get; set; }                      // 嵌入向量
    public string Content { get; set; }                      // 原始文字內容
    public Dictionary<string, string> Metadata { get; set; } // 自訂鍵值元資料
}
```

使用 `Metadata` 字典儲存任意自訂欄位 — 來源檔案、語言、日期、分類等：

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("一些文字"),
    Content = "一些文字",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "zh-Hant",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

搜尋結果將記錄與相似度分數配對：

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0（越高越相似）
}
```

## 可用後端

| 後端 | 套件 | 適用場景 |
|------|------|----------|
| **記憶體** | `Mythosia.VectorDb.InMemory` | 開發、測試、展示 |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | 正式環境、原生混合檢索 |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | 無伺服器託管服務 |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | 既有 Postgres 部署、ACID 交易 |

所有後端實作相同的 `IVectorStore` 介面。各後端設定詳見[後端設定](vectordb-backends.md)。

## 依賴注入

將任意後端註冊為 `IVectorStore`：

```csharp
// 記憶體
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

## 各後端過濾器執行方式

`VectorFilter` 條件會盡可能下推到後端執行：

| 運算子 | 記憶體 | Qdrant | Pinecone | Postgres |
|--------|--------|--------|----------|----------|
| Eq / Ne | 客戶端 | **伺服器端** | **伺服器端** | **SQL** |
| In / NotIn | 客戶端 | **伺服器端** | **伺服器端** | **SQL** |
| Gt / Gte / Lt / Lte | 客戶端 | 客戶端 | 客戶端 | **SQL** |
| Like | 客戶端 | 客戶端 | 客戶端 | **SQL** |
| Exists / NotExists | 客戶端 | 客戶端 | 客戶端 | **SQL** |

Postgres 對所有運算子實作了完整的 SQL 下推。Qdrant 和 Pinecone 原生下推等值和集合成員判斷。
