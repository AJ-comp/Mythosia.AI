# Tổng quan Vector Database

Mythosia.AI cung cấp abstraction `IVectorStore` thống nhất hoạt động trên nhiều backend vector database. Bạn viết ứng dụng dựa trên interface một lần và hoán đổi backend mà không cần thay đổi logic truy xuất.

## Interface cốt lõi: `IVectorStore`

```csharp
// Upsert
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Tìm kiếm
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Lấy theo ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Xóa
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Tiện ích
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Mô hình dữ liệu

### VectorRecord

Mỗi entry được lưu là một `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Định danh duy nhất
    public float[] Vector { get; set; }                      // Vector embedding
    public string Content { get; set; }                      // Nội dung văn bản gốc
    public Dictionary<string, string> Metadata { get; set; } // Metadata tùy chỉnh key-value
}
```

Dùng dictionary `Metadata` cho bất kỳ trường tùy chỉnh nào — file nguồn, ngôn ngữ, ngày, danh mục, v.v.:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("Một đoạn văn bản"),
    Content = "Một đoạn văn bản",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "vi",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

Kết quả tìm kiếm ghép một record với điểm tương đồng của nó:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (cao hơn = giống nhau hơn)
}
```

## Backend có sẵn

| Backend | Package | Trường hợp sử dụng |
|---------|---------|----------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Phát triển, kiểm thử, demo |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Production, hybrid search gốc |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Dịch vụ managed serverless |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Triển khai Postgres hiện có, ACID |

Tất cả backend đều triển khai cùng interface `IVectorStore`. Xem [Thiết lập Backend](vectordb-backends.md) để biết cấu hình từng backend.

## Dependency Injection

Đăng ký bất kỳ backend nào như `IVectorStore`:

```csharp
// In-Memory
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

## Hỗ trợ Filter theo Backend

Điều kiện `VectorFilter` được đẩy xuống backend khi có thể:

| Toán tử | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Client | **Server** | **Server** | **SQL** |
| In / NotIn | Client | **Server** | **Server** | **SQL** |
| Gt / Gte / Lt / Lte | Client | Client | **Server** | **SQL** |
| Like | Client | Client | Client | **SQL** |
| Exists / NotExists | Client | Client | Client | **SQL** |

Postgres có SQL pushdown đầy đủ cho tất cả toán tử. Qdrant và Pinecone đẩy xuống server cho toán tử equality, set membership và comparison.

> **Lưu ý:** Qdrant âm thầm bỏ qua các toán tử filter không được hỗ trợ (`Like`, `Exists`, `NotExists`) — chúng không được áp dụng phía client. Nếu cần các toán tử này với Qdrant, áp dụng lọc thêm trên kết quả được trả về.
