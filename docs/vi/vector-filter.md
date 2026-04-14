# VectorFilter

`VectorFilter` là API fluent để lọc kết quả trong vector store theo metadata. Áp dụng cho `IVectorStore.SearchAsync`, `HybridSearchAsync`, và các truy vấn RAG.

## So sánh bằng cơ bản

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "vi");
```

## Toán tử so sánh

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| Phương thức | Tương đương SQL |
|--------|---------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## Thuộc tập hợp

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## Kiểm tra sự tồn tại của key

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // Key phải tồn tại
    .WhereNotExists("deprecated");   // Key phải vắng mặt
```

## Nhóm điều kiện logic (AND / OR)

Các điều kiện cùng cấp được kết hợp bằng AND theo mặc định. Dùng `.Or()` để tạo nhóm OR:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

AND lồng nhau:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "en").Where("region", "us"))
        .And(a => a.Where("lang", "vi").Where("region", "vn"))
    );
// (lang = "en" AND region = "us") OR (lang = "vi" AND region = "vn")
```

## Ngưỡng điểm số

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Dùng với Vector Store

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contract")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## Dùng với RAG

Truyền qua `StoreFilter` trong `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "product-manual.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("Làm thế nào để reset thiết bị?", options);
```

## Kết hợp filter

Dùng `AppendConditionsFrom` để kết hợp hai filter (ví dụ: kết hợp filter cấp pipeline với filter cấp truy vấn):

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "vi");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter giờ có cả hai điều kiện
```
