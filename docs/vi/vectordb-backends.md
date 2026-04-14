# Cấu hình Backend

## In-Memory

Backend đơn giản nhất — không cần dependency bên ngoài. Dữ liệu được giữ trong RAM và mất khi process kết thúc. Phù hợp cho phát triển, kiểm thử và demo.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Hybrid search tích hợp**: RRF (Reciprocal Rank Fusion) hợp nhất điểm cosine similarity và BM25 keyword.

### Diagnostics

```csharp
// Liệt kê tất cả record đã lưu
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Tổng số: {store.GetTotalRecordCount()}");

// Kiểm tra điểm tương đồng thô
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Vector database cấp production với hybrid search gốc. Chạy như standalone service qua Docker hoặc Qdrant Cloud.

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# Khởi động Qdrant cục bộ
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // cổng gRPC
    CollectionName   = "my-docs",
    Dimension        = 1536,           // Phải khớp với model embedding của bạn
    AutoCreateCollection = true        // Tạo collection khi upsert lần đầu
});
```

### Tất cả tùy chọn

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Bắt buộc cho Qdrant Cloud

    CollectionName         = "my-collection",  // Bắt buộc
    Dimension              = 1536,             // Bắt buộc

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Thêm payload index để lọc server-side nhanh hơn
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Chiến lược khoảng cách

| Giá trị | Mô tả |
|-------|-------------|
| `Cosine` | Cosine similarity — tốt nhất cho embedding đã chuẩn hóa (mặc định) |
| `Euclidean` | Khoảng cách L2 — khoảng cách thấp hơn = giống nhau hơn |
| `DotProduct` | Tích vô hướng — dùng với vector unit-normalized |

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

---

## Pinecone

Vector database serverless được quản lý hoàn toàn. Không cần quản lý infrastructure.

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "your-api-key"
});
```

### Tự động tạo Index

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp", hoặc "azure"
    Region          = "us-east-1"
}
```

> Khi `AutoCreateIndex` được bật, index được tạo với metric `dotproduct` — bắt buộc cho hybrid (sparse + dense) search.

---

## PostgreSQL (pgvector)

Dùng extension [`pgvector`](https://github.com/pgvector/pgvector) để thêm vector similarity search vào PostgreSQL tiêu chuẩn.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Điều kiện tiên quyết

```sql
-- Chạy một lần trên PostgreSQL server của bạn
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Chỉ khi dùng Trigram text search
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // Tự động tạo extension, table và index
});
```

### Loại Index

| Loại | Class | Khi nào dùng |
|------|-------|-------------|
| HNSW | `HnswIndexOptions` | Mặc định. Tìm kiếm gần đúng nhanh. Tốt nhất cho hầu hết trường hợp. |
| IVFFlat | `IvfFlatIndexOptions` | Bộ nhớ thấp hơn. Tốt cho tập dữ liệu tĩnh lớn. |
| None | `NoIndexOptions` | Quét tuần tự. Chỉ dùng cho tập dữ liệu rất nhỏ. |

### Chế độ Text Search

Dùng cho phần keyword của hybrid search:

| Chế độ | Phù hợp nhất |
|------|----------|
| `TsVector` | Full-text search tiêu chuẩn — tiếng Anh, hầu hết ngôn ngữ phương Tây |
| `Trigram` | Ngôn ngữ CJK (Hàn, Trung, Nhật), khớp mờ |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"
}
```
