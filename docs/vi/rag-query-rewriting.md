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

> Cần `Mythosia.AI.Rag` 8.1.1 trở lên để thay đổi bộ viết lại trong lúc chạy có hiệu lực với các wrapper `WithRag(store)` đã kết nối. [Ghi chú bản vá](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v811).

Để thêm hoặc thay bộ viết lại mà không xây dựng lại chỉ mục, dùng `store.SetQueryRewriter(rewriter)`; dùng `store.SetQueryRewriter(null)` để tắt việc viết lại và trích xuất từ khóa tìm kiếm. Cả overload của `RagStore.QueryAsync` nhận `conversationHistory` lẫn các wrapper đã kết nối qua `service.WithRag(store)` đều chọn bộ viết lại hiện tại của kho cho từng yêu cầu. Yêu cầu giữ nguyên thể hiện đã chọn trong lúc chờ thông báo tiến độ hoặc bước viết lại. Việc thêm, thay hoặc tắt chỉ tác động đến các yêu cầu sau, bao gồm truy xuất, tạo câu trả lời, streaming và Run qua các wrapper đó.

```csharp
var rag = service.WithRag(store);
store.SetQueryRewriter(rewriter);
var rewritten = await rag.RetrieveAsync("Chính sách hoàn tiền có những ngoại lệ nào?");

store.SetQueryRewriter(null);
var original = await rag.RetrieveAsync("Chính sách hoàn tiền có những ngoại lệ nào?");
```

`LlmQueryRewriter` mặc định được bật bằng `WithQueryRewriter()` chỉ được tạo một lần khi khởi tạo trì hoãn; sau khi tắt, nó không tự tạo lại ở yêu cầu tiếp theo. Các overload của kho không nhận `conversationHistory` vẫn bỏ qua bước viết lại, tương tự Agentic RAG, nơi agent tự tạo truy vấn tìm kiếm.

## Cách search gate hoạt động

Không phải mọi tin nhắn của user đều cần tìm kiếm tài liệu. Bộ viết lại phân loại truy vấn và trả về viết lại rỗng cho các tin nhắn như:

- "Cảm ơn!"
- "Tôi hiểu rồi, thông tin rất hữu ích."
- "Bạn có thể tóm tắt những gì vừa nói không?"

Khi gate kích hoạt, toàn bộ pipeline truy xuất bị bỏ qua — không embedding, không vector search, không reranking — và LLM trả lời trực tiếp từ context hội thoại.
