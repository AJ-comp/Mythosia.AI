# Viết lại truy vấn

> 📍 **Pipeline Q&A:** **`Viết lại truy vấn`** → Lọc → Embedding (khi cần) → [Truy xuất](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → Xây dựng context

Giai đoạn câu hỏi `Embedding` phụ thuộc bộ truy xuất; tìm từ khóa không báo giai đoạn này. Bộ tùy chỉnh có thể báo giai đoạn qua `request.ProgressAsync`. Embedding tài liệu không đổi.

## Tại sao cần viết lại truy vấn?

Trong hội thoại nhiều lượt, người dùng tự nhiên dùng đại từ và tham chiếu ngắn:

> User: "Cho tôi biết về chính sách hoàn tiền."
> User: "Còn các ngoại lệ của **nó** thì sao?"

Nếu "Còn các ngoại lệ của nó thì sao?" được gửi trực tiếp đến vector store, embedding không biết "nó" là gì. Kết quả tìm kiếm không liên quan và câu trả lời kém chất lượng.

**Viết lại truy vấn** giải quyết các tham chiếu này trước khi truy xuất, mở rộng "nó" → "các ngoại lệ của chính sách hoàn tiền" để embedding nắm bắt được đầy đủ ý định. Nó cũng triển khai **search gate** — nếu truy vấn không cần truy xuất (ví dụ "Cảm ơn!"), bỏ qua vector search hoàn toàn, tiết kiệm độ trễ và chi phí.

## Cấu hình

`LlmQueryRewriter` dùng chính AI service để viết lại truy vấn trước khi embedding:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter(250)          // Dùng cùng AI service
    .AddDocument("docs.txt")
)
```

Bộ viết lại kiểm tra context hội thoại và tạo ra một truy vấn tìm kiếm tự chứa mà vector store có thể hiểu mà không cần lịch sử.

## RAG nhiều lượt

Khi truy vấn `RagStore` trực tiếp, truyền lịch sử hội thoại để bộ viết lại có thể giải quyết tham chiếu:

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("Chính sách hoàn tiền là gì?", "Bạn có thể trả hàng trong vòng 30 ngày."),
    new ConversationTurn("Còn sản phẩm kỹ thuật số thì sao?", "Sản phẩm kỹ thuật số không được hoàn tiền.")
};

var result = await store.QueryAsync(
    query: "Có ngoại lệ nào không?",
    conversationHistory: history
);
```

Bộ viết lại xem toàn bộ lịch sử và viết lại "Có ngoại lệ nào không?" thành "ngoại lệ của chính sách không hoàn tiền sản phẩm kỹ thuật số", cho kết quả truy xuất tốt hơn nhiều.

<a id="runtime-query-rewriter"></a>

## Đổi cấu hình viết lại khi đang xử lý truy vấn

Để tạm tắt tính năng viết lại hoặc đổi cách triển khai mà không xây dựng lại chỉ mục, hãy dùng `store.SetQueryRewriter(null)` hoặc `store.SetQueryRewriter(rewriter)`. Khi gọi trực tiếp overload của `RagStore.QueryAsync` nhận `conversationHistory`, truy vấn giữ bộ viết lại được chọn lúc bắt đầu. Dù cấu hình bị tắt hoặc thay đổi trong khi chờ thông báo tiến độ hay bước viết lại, truy vấn đó vẫn dùng cùng một thể hiện; các truy vấn sau dùng cấu hình mới. Hành vi này áp dụng cho truy vấn trực tiếp vào kho và không cập nhật bộ viết lại mà wrapper `RagEnabledService` đã giữ trước đó.

## Cách search gate hoạt động

Không phải mọi tin nhắn của user đều cần tìm kiếm tài liệu. Bộ viết lại phân loại truy vấn và trả về viết lại rỗng cho các tin nhắn như:

- "Cảm ơn!"
- "Tôi hiểu rồi, thông tin rất hữu ích."
- "Bạn có thể tóm tắt những gì vừa nói không?"

Khi gate kích hoạt, toàn bộ pipeline truy xuất bị bỏ qua — không embedding, không vector search, không reranking — và LLM trả lời trực tiếp từ context hội thoại.
