# Thao tác Vector Store

## Upsert

Chèn hoặc cập nhật một record. Nếu record có cùng `Id` đã tồn tại, nó sẽ được thay thế.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("Hoàn tiền được chấp nhận trong vòng 30 ngày."),
    Content = "Hoàn tiền được chấp nhận trong vòng 30 ngày.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "vi",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## Batch Upsert

Upsert nhiều record trong một lần gọi. Hiệu quả hơn là gọi `UpsertAsync` trong vòng lặp — các backend dùng batch API nội bộ khi có.

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

## Tìm kiếm

Trả về top-K record giống nhau nhất với query vector. Tùy chọn lọc theo metadata trước khi chấm điểm.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("Chính sách hoàn tiền là gì?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Nguồn: {r.Record.Metadata["source"]}");
}
```

### Tìm kiếm có lọc

Kết hợp vector similarity với lọc metadata:

```csharp
var filter = new VectorFilter()
    .Where("language", "vi")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

Xem [VectorFilter](vector-filter.md) để biết API lọc đầy đủ.

## Hybrid Search

Hợp nhất dense vector similarity với keyword (BM25) search. Recall tốt hơn cho các query có thuật ngữ, tên hoặc code cụ thể.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("trạng thái đơn hàng #12345");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "trạng thái đơn hàng #12345",   // Văn bản thô dùng cho BM25
    topK: 5
);
```

Cách hybrid search hoạt động theo backend:

| Backend | Cơ chế |
|---------|-----------|
| **InMemory** | RRF hợp nhất cosine similarity + Lucene BM25 scores |
| **Qdrant** | Server-side: dense + sparse vectors fused với RRF hoặc DBSF |
| **Pinecone** | Sparse + dense vectors được hợp nhất server-side |
| **Postgres** | Vector similarity + `tsvector`/`trigram` scores hợp nhất trong SQL |

## Lấy theo ID

Truy xuất một record cụ thể theo ID:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("Không tìm thấy");
```

## Xóa theo ID

Xóa một record:

```csharp
await store.DeleteAsync("record-id-123");
```

## Xóa theo Filter

Xóa tất cả record khớp với filter. Dùng cẩn thận — đây là xóa hàng loạt.

```csharp
// Xóa tất cả record từ một tài liệu cụ thể
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## Thay thế theo Filter

Xóa nguyên tử tất cả record khớp filter và chèn tập mới. Hữu ích để re-index tài liệu mà không để lại chunk lỗi thời.

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

> Trên Postgres thao tác này chạy trong transaction, đảm bảo tính nguyên tử hoàn toàn.

## Đếm

Đếm record đã lưu, tùy chọn theo phạm vi filter:

```csharp
long total   = await store.CountAsync();
long viet    = await store.CountAsync(new VectorFilter().Where("language", "vi"));

Console.WriteLine($"Tổng: {total}, Tiếng Việt: {viet}");
```

## Kiểm tra kết nối

Kiểm tra backend có thể truy cập được không. Hữu ích trong health check hoặc kiểm tra khi khởi động:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Kết nối vector store OK");
}
catch (Exception ex)
{
    Console.WriteLine($"Kết nối thất bại: {ex.Message}");
}
```

## Dùng với RAG

Truyền `IVectorStore` vào `RagBuilder` để dùng bất kỳ backend nào làm RAG retrieval store:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "knowledge-base",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey, http)
        .AddDirectory("docs/", ".txt", ".md")
    );

var answer = await ragService.GetCompletionAsync("Chính sách hoàn trả là gì?");
```
