# Cấu hình Pipeline

## Tại sao cần tùy chỉnh?

Pipeline RAG mặc định hoạt động tốt ngay từ đầu, nhưng các dự án thực tế thường cần kiểm soát nhiều hơn:

- **Debug** — giai đoạn nào chậm? Bộ viết lại có thay đổi truy vấn theo cách không mong muốn không?
- **Kỹ thuật prompt** — template prompt mặc định có thể không phù hợp với giọng điệu hoặc ràng buộc của domain bạn
- **Kiến trúc** — nhiều service chia sẻ một index tiết kiệm bộ nhớ và giữ embedding nhất quán
- **Kiểm tra** — đôi khi bạn cần xem kết quả truy xuất *trước* khi gửi đến LLM

## Theo dõi tiến độ

Theo dõi giai đoạn RAG nào đang thực thi qua callback async theo từng query:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Các giai đoạn: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Câu hỏi của bạn", options);
```

Vô cùng hữu ích để đo độ trễ — bạn có thể đo thời gian giữa các giai đoạn để tìm điểm nghẽn cổ chai.

## Template prompt tùy chỉnh

Kiểm soát cách context được truy xuất được inject vào prompt dùng placeholder `{context}` và `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Chỉ dùng thông tin sau để trả lời câu hỏi.
        Nếu câu trả lời không có trong context, nói "Tôi không biết."

        Context:
        {context}

        Câu hỏi: {question}
        """)
    .AddDocument("faq.txt")
)
```

Template được thiết kế tốt có thể giảm đáng kể ảo giác bằng cách hướng dẫn model bám vào context được cung cấp.

## Chia sẻ RagStore

Xây dựng index một lần và dùng lại cho nhiều service instance — hữu ích khi bạn muốn so sánh các provider hoặc chạy A/B test:

```csharp
// Xây dựng một lần
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDocuments("docs/")
    .BuildAsync();

// Dùng lại cho nhiều service
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

Cả hai service chia sẻ cùng embedding và vector index — không trùng lặp lưu trữ hay tính toán.

## Truy vấn RagStore trực tiếp

Truy vấn store độc lập với bất kỳ AI service nào để kiểm tra những gì sẽ được truy xuất:

```csharp
RagProcessedQuery result = await store.QueryAsync("Chính sách hoàn trả là gì?");

Console.WriteLine($"Truy vấn đã viết lại: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` chứa prompt được lắp ráp hoàn chỉnh sẽ được gửi đến LLM. Cực kỳ hữu ích để debug chất lượng truy xuất mà không tốn token LLM.

## Cách hoạt động nội bộ

Khi bạn gọi `.WithRag()`, một wrapper `RagEnabledService` được tạo xung quanh AIService của bạn. Cơ chế chính đằng sau là [AIRequestContext](request-contexts.md).

### Toàn bộ flow

```
ragService.GetCompletionAsync("Chính sách hoàn trả là gì?")
    ↓
① RagEnabledService thực thi RAG pipeline
   Viết lại truy vấn → Embedding → Truy xuất → Lắp ráp context
    ↓
② TemplateContextBuilder thay thế {context} và {question}
   → "Trả lời theo thông tin sau.\n[1] Hoàn trả trong 30 ngày...\nCâu hỏi: Chính sách hoàn trả là gì?"
    ↓
③ RagEnabledService tạo AIRequestContext
   RequestMessageOverride = prompt đã lắp ráp
    ↓
④ _innerService.GetCompletionAsync(tin nhắn gốc, context) được gọi
   → AIService lưu context trong AsyncLocal
   → Câu hỏi gốc được thêm vào lịch sử hội thoại
    ↓
⑤ AIService.GetLatestMessages() thay thế tin nhắn cuối
   Lịch sử: "Chính sách hoàn trả là gì?" (giữ nguyên gốc)
   Model thấy: prompt đã lắp ráp (RequestMessageOverride)
```

### Tại sao thiết kế này?

Điểm mấu chốt là **tách biệt lịch sử hội thoại khỏi input của model**:

- **Lịch sử hội thoại giữ câu hỏi gốc** — để các câu hỏi tiếp theo như "còn điều đó thì sao?" có context đúng
- **Model nhận prompt đã lắp ráp** — prompt đầy đủ với tài liệu đã truy xuất + câu hỏi
- **State của AIService không bao giờ bị thay đổi** — `AsyncLocal<T>` cung cấp cách ly theo request
