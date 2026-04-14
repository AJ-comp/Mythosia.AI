# Hybrid Search

> 📍 **Pipeline Q&A:** [Viết lại truy vấn](rag-query-rewriting.md) → Embedding → Lọc → **`Truy xuất`** → [Reranking](rag-reranking.md) → Xây dựng context

## Tại sao cần Hybrid Search?

Tìm kiếm vector thuần túy giỏi nắm bắt ý nghĩa ngữ nghĩa — "hủy đăng ký của tôi" khớp với "kết thúc tư cách thành viên" dù không có từ nào giống nhau. Tuy nhiên, nó có thể bỏ sót **các thuật ngữ chính xác** như tên sản phẩm, mã lỗi hoặc định danh chính sách mà user nhập nguyên văn.

BM25 keyword search xử lý những trường hợp này hoàn hảo nhưng lại kém về hiểu ngữ nghĩa. **Hybrid search kết hợp cả hai**, cho bạn điều tốt nhất của cả hai thế giới: hiểu ngữ nghĩa cùng với khớp từ khóa chính xác.

## Cấu hình

Pha trộn dense vector search với BM25 keyword search bằng một lệnh gọi:

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% vector, 40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight` từ 0.0 (thuần BM25) đến 1.0 (thuần vector). Giá trị khoảng **0.5–0.7** hoạt động tốt trong hầu hết trường hợp.

## Khi nào dùng gì

| Kịch bản | Trọng số khuyến nghị |
| --- | --- |
| Q&A tổng quát với ngôn ngữ tự nhiên | 0.7–0.8 (thiên về vector) |
| Tài liệu kỹ thuật với thuật ngữ cụ thể | 0.4–0.5 (cân bằng) |
| Tra cứu code hoặc mã lỗi | 0.2–0.3 (thiên về BM25) |

## Ví dụ

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("product-catalog.txt")
        .AddDocument("error-codes.txt")
    );

// "ERR-4012" được khớp bởi BM25; context ngữ nghĩa được khớp bởi vector
var answer = await service.GetCompletionAsync("Làm thế nào để sửa lỗi ERR-4012?");
```
