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

### Sử dụng đồng thời và sửa bản ghi

Khi cập nhật kho dùng chung trong lúc truy vấn, nội dung và chỉ mục từ khóa phải thuộc cùng một phiên bản. `InMemoryVectorStore` đồng bộ hóa ghi, xóa và đọc để mỗi truy vấn vector, văn bản hoặc hybrid thấy một trạng thái nhất quán. Hai nhánh của truy vấn hybrid cũng dùng cùng trạng thái đó.

Kho sao chép các bản ghi đầu vào, gồm cả mảng vector và metadata, đồng thời trả về bản sao độc lập khi lấy bản ghi, tìm kiếm và chẩn đoán. Sửa đối tượng đầu vào hoặc bản ghi trả về không làm đổi dữ liệu đã lưu; hãy gọi lại `UpsertAsync` để lưu thay đổi. Không sửa bản ghi, vector hoặc metadata đầu vào trong khi lời gọi đang sao chép hoặc đọc chúng.

`CancellationToken` được truyền vào có thể hủy lời gọi trong khi chờ thao tác khác nhả khóa kho dữ liệu. Việc hủy chờ tự nó không dừng thao tác đang sử dụng kho. Sau khi bắt đầu cập nhật một bản ghi, việc hủy không ngắt cập nhật giữa phần nội dung và chỉ mục.

Hủy một batch có thể giữ lại các bản ghi đã ghi. `ReplaceByFilterAsync` vẫn thực hiện xóa rồi chèn batch theo thứ tự mà không có transaction: truy vấn khác có thể thấy khoảng trống giữa hai bước, và lỗi hoặc hủy không hoàn tác các lần ghi đã xong.

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

> Cần `Mythosia.VectorDb.Postgres` 10.8.1 trở lên để áp dụng cấu hình vector khi cả hai nhánh tìm kiếm hybrid hoạt động. [Ghi chú bản vá](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081).

Để điều chỉnh phạm vi tìm ứng viên vector trong tìm kiếm hybrid, đặt `HnswIndexOptions.EfSearch` hoặc `IvfFlatIndexOptions.Probes` trong `PostgresOptions.Index`. Các giá trị mặc định này áp dụng cho tìm kiếm vector thông thường và nhánh vector của tìm kiếm hybrid, trong cùng transaction với truy vấn. Tùy chọn runtime theo từng yêu cầu chỉ có trên `SearchAsync`. Tìm kiếm gần đúng và bộ lọc vẫn có thể trả về ít hơn `topK` kết quả.

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
