# Lọc kết quả

> 📍 **Pipeline Q&A:** [Viết lại truy vấn](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → **`Lọc`** → [Truy xuất](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → [Xây dựng context](rag-context-build.md)

## Lọc là gì?

Lọc thu hẹp **những đoạn nào được xem xét** trước khi tìm kiếm độ tương đồng chạy. Thay vì tìm kiếm toàn bộ vector store, bạn có thể giới hạn tìm kiếm trong các tập con cụ thể dựa trên metadata hoặc ngưỡng điểm.

Hãy nghĩ như tìm kiếm trong thư viện. Không có lọc, bạn tìm kiếm mọi cuốn sách trong toàn tòa nhà. Với lọc, bạn đi thẳng đến khu vực đúng (ví dụ "Y tế" hoặc "Pháp lý") rồi chỉ tìm kiếm trong các kệ đó. Tìm kiếm nhanh hơn và kết quả liên quan hơn.

RAG pipeline áp dụng hai loại lọc:

1. **Lọc metadata** — bao gồm hoặc loại trừ các đoạn dựa trên metadata của chúng (ví dụ danh mục, tenant, ngày)
2. **Lọc điểm** — đặt ngưỡng điểm tương đồng tối thiểu để loại bỏ kết quả kém chất lượng

## Lọc metadata

Mỗi đoạn lưu trong vector store có thể mang metadata — các cặp key-value được đính kèm khi lập index. Lọc cho phép bạn chỉ truy vấn các đoạn khớp với điều kiện cụ thể.

### Lọc theo từng query

Truyền `VectorFilter` khi truy vấn để thu hẹp phạm vi tìm kiếm:

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("Làm thế nào để được hoàn tiền?", filter: filter);
```

### API lọc fluent

`VectorFilter` hỗ trợ nhiều toán tử:

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")         // khớp chính xác
    .WhereNot("status", "archived")             // không bằng
    .WhereIn("region", "us-east", "eu-west")    // giá trị trong tập
    .WhereGreaterThan("year", "2023")           // so sánh phạm vi
    .WhereLike("title", "%kubernetes%");        // khớp pattern
```

Các toán tử có sẵn:

| Phương thức | Tương đương SQL | Mô tả |
| --- | --- | --- |
| `Where` | `=` | Khớp chính xác |
| `WhereNot` | `!=` | Không bằng |
| `WhereIn` | `IN (...)` | Giá trị trong tập |
| `WhereNotIn` | `NOT IN (...)` | Giá trị không trong tập |
| `WhereGreaterThan` | `>` | Lớn hơn |
| `WhereGreaterThanOrEqual` | `>=` | Lớn hơn hoặc bằng |
| `WhereLessThan` | `<` | Nhỏ hơn |
| `WhereLessThanOrEqual` | `<=` | Nhỏ hơn hoặc bằng |
| `WhereLike` | `LIKE` | Khớp pattern (`%` = bất kỳ, `_` = một ký tự) |
| `WhereExists` | `IS NOT NULL` | Khóa metadata tồn tại |
| `WhereNotExists` | `IS NULL` | Khóa metadata không tồn tại |

### Nhóm logic

Kết hợp điều kiện với logic AND/OR:

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
// Khớp: tenant = "acme" AND (category = "billing" OR category = "refund")
```

## Lọc store ở cấp pipeline

Với các điều kiện **luôn áp dụng** (như phân tách tenant), đặt `StoreFilter` trong `RagQueryOptions`. Filter này tự động được hợp nhất với bất kỳ filter theo query nào:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};

var response = await ragService.GetCompletionAsync("câu hỏi", ragOptions: options);
```

### Cách filter hợp nhất

Khi có cả `StoreFilter` cấp pipeline và filter theo query, chúng được AND lại:

```
Filter cuối = điều kiện StoreFilter AND điều kiện filter theo query
```

## Lọc điểm

Ngưỡng `MinScore` loại bỏ các đoạn có điểm tương đồng thấp hơn một mức nhất định:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7   // loại bỏ bất cứ thứ gì dưới 0.7 độ tương đồng
    }
};
```

## Trường hợp sử dụng phổ biến

### Phân tách multi-tenant

Đảm bảo mỗi tenant chỉ thấy tài liệu của họ:

```csharp
// Khi lập index — đính kèm metadata tenant
var doc = new RagDocument
{
    Id = "doc-1",
    Content = "...",
    Metadata = { ["tenant_id"] = "tenant-abc" }
};

// Khi truy vấn — lọc theo tenant
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### Tìm kiếm theo danh mục

Tìm kiếm chỉ trong một danh mục tài liệu cụ thể:

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("lỗi 404", filter: filter);
```

### Lọc theo thời gian

Giới hạn kết quả trong tài liệu gần đây:

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## Bước tiếp theo

- [Truy xuất (Hybrid Search)](rag-hybrid-search.md) — kết hợp vector và tìm kiếm từ khóa
- [Tham chiếu VectorFilter](vector-filter.md) — tài liệu đầy đủ về API lọc
- [Reranking](rag-reranking.md) — tinh chỉnh kết quả sau khi truy xuất
