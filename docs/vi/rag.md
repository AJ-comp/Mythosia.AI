# RAG (Retrieval-Augmented Generation)

Chỉ cần kết quả cuối cùng và nút Dừng thì truyền `cancellationToken` vào `GetCompletionAsync`. Dùng Run cho sự kiện tiến độ hoặc chỉ dẫn bổ sung được hỗ trợ. Xem [hủy câu trả lời](completions.md#completion-cancellation).

Với câu trả lời có ngữ cảnh truy xuất, cũng truyền `cancellationToken` vào `RagEnabledService.GetCompletionAsync`. Token đi qua truy xuất, `LlmQueryRewriter`, `LlmReranker` và lệnh gọi nội bộ; hủy lúc truy xuất sẽ chặn lần gọi mô hình sau đó. `RagPipeline.QueryAndGenerateAsync` cũng truyền token. Các thành phần phải hỗ trợ hủy; không hoàn tác truy xuất hoặc hành động công cụ đã xong.

RAG cho phép model trả lời câu hỏi dựa trên tài liệu của riêng bạn bằng cách truy xuất các đoạn liên quan tại thời điểm truy vấn.

Để hiển thị dần câu trả lời dựa trên tài liệu truy xuất và cho phép dừng tạo nội dung, có thể dùng `RagEnabledService.StartRunAsync`. Truy xuất diễn ra trước Run; chỉ dẫn bổ sung không tự kích hoạt truy xuất lại. Xem ví dụ và phạm vi trong [hướng dẫn Run](execution-api-transition.md).

## Cài đặt

```bash
dotnet add package Mythosia.AI.Rag
```

## Bắt đầu nhanh

Dùng `.WithRag()` trên bất kỳ `IAIService` nào để bật RAG với fluent API:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("Chính sách hoàn tiền là gì?");
```

Tài liệu được tách, embed và lưu trữ tự động. Tại thời điểm truy vấn, các đoạn liên quan nhất được truy xuất và inject vào prompt.

Nếu nhà cung cấp đã quản lý chỉ mục tài liệu, hãy so sánh [tìm kiếm tệp được lưu trữ và RAG](reasoning-and-search.md). Tham chiếu truy xuất RAG được lưu riêng với nguồn do nhà cung cấp trả về.

## Thêm tài liệu

Nhiều loại nguồn được hỗ trợ:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // file cục bộ
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Nội dung nội tuyến có thể đặt ở đây.")  // chuỗi trực tiếp
)
```

## Embedding provider tùy chỉnh

Mặc định, RAG dùng local embedding provider tích hợp sẵn. Để dùng model embedding chuyên dụng:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## Vector store tùy chỉnh

Mặc định dùng store in-memory. Cho production, kết nối vector store bền vững:

```csharp
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## Tùy chọn truy vấn

Tinh chỉnh hành vi truy xuất theo từng truy vấn:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,            // số đoạn cần truy xuất
        MinScore = 0.7       // ngưỡng độ tương đồng tối thiểu
    }
};

var response = await service.GetCompletionAsync("Câu hỏi của bạn", options: options);
```

## Bước tiếp theo

- [Hybrid Search](rag-hybrid-search.md) — kết hợp tìm kiếm ngữ nghĩa và từ khóa
- [Viết lại truy vấn](rag-query-rewriting.md) — tối ưu hóa query với context hội thoại
- [Reranking](rag-reranking.md) — tinh chỉnh thêm độ chính xác kết quả tìm kiếm
- [Tùy chỉnh Pipeline](rag-pipeline.md) — kiểm soát chi tiết quá trình RAG
- [Agentic RAG](rag-agentic.md) — AI tự quyết định khi nào và tìm kiếm gì
- [Vector Store](vectordb-overview.md) — thiết lập lưu trữ bền vững
- [Text Splitter](text-splitters.md) — tùy chỉnh cách chia nhỏ tài liệu

Perplexity: [Dùng vector trong chỉ mục riêng / Tìm kiếm mà không tạo câu trả lời](perplexity.md).
