# Cấu hình Pipeline

<a id="indexing-validation"></a>

## Bảo vệ tài liệu hiện có khi lập chỉ mục thất bại

Splitter tùy chỉnh hoặc phản hồi embedding bị lỗi không được âm thầm thay thế tài liệu đang tìm kiếm được bằng nội dung thiếu hoặc ghép sai. Pipeline kiểm tra từng tài liệu trước khi bắt đầu lưu, kể cả khi dùng `onDocumentEmbedded`.

Trước embedding, lưu trữ hoặc callback lưu trữ, `RagDocument.Id` là null, chuỗi rỗng hay chỉ có khoảng trắng sẽ gây `ArgumentException`. Đầu ra splitter không hợp lệ gây `InvalidOperationException`: danh sách hoặc đoạn là null, `Content` hoặc `Metadata` là null, ID đoạn rỗng hoặc chỉ có khoảng trắng, hay ID trùng trong cùng tài liệu. Việc phát hiện trùng dùng `StringComparer.Ordinal`, có phân biệt chữ hoa và chữ thường. Giá trị các đoạn và metadata được sao chép trước lần gọi embedding đầu tiên.

ID tùy chỉnh hợp lệ được giữ nguyên. Không có cơ chế tự tạo, cắt khoảng trắng hay sửa ID, và không kiểm tra toàn cục xung đột ID đoạn tùy chỉnh giữa các tài liệu khác nhau. Hãy dùng ID duy nhất trong collection đích, như [ví dụ splitter tùy chỉnh](text-splitters.md). Khóa dành riêng `document_id` chỉ được chuẩn hóa trên bản sao dùng để lưu; metadata gốc không thay đổi.

ID không hợp lệ, lỗi chia đoạn và batch embedding không hợp lệ giữ nguyên bản ghi cũ của tài liệu đó và không gọi callback lưu trữ. Mọi batch của tài liệu phải vượt qua [kiểm tra embedding](rag-embedding.md#embedding-validation) trước khi lưu. Cơ chế này không hoàn tác các tài liệu đã xử lý xong trước đó trong cùng thao tác; rollback sau khi bắt đầu lưu phụ thuộc vào kho lưu trữ hoặc callback.

Các kiểm tra và sửa thứ tự phản hồi này không tự khôi phục nội dung đã bị ghi đè hoặc các vector đã lưu nhưng ghép sai đoạn; hãy lập lại chỉ mục cho tài liệu bị ảnh hưởng từ nguồn gốc.

<a id="custom-persistence"></a>

## Thay toàn bộ tài liệu trong callback lưu trữ

Khi tài liệu ngắn đi, chỉ upsert các đoạn mới sẽ để phần cuối cũ tiếp tục xuất hiện trong tìm kiếm. `onDocumentEmbedded` thay hoàn toàn cơ chế lưu mặc định: dùng `document_id` đã chuẩn hóa trong các bản ghi để thay toàn bộ tài liệu. Mỗi lần callback nhận một tài liệu không rỗng đã được kiểm tra:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

Chia thành công nhưng có 0 đoạn sẽ không gọi callback hoặc truy cập kho mặc định. Hãy xóa rõ ràng ID đã biết trong kho riêng; chỉ dùng `DeleteDocumentAsync` khi nhắm đến kho của pipeline. Tính nguyên tử và rollback phụ thuộc kho hoặc callback.

<a id="url-documents"></a>

## Đọc tài liệu URL an toàn

Máy chủ có thể nén tài liệu văn bản để truyền đi. `AddUrl` giải nén `gzip`, `deflate` và Brotli (`br`) trước khi đọc văn bản, đồng thời kiểm tra luồng nén đã hoàn chỉnh. Truyền HTTP thành công chưa đủ: dữ liệu nén bị cắt ngắn, lỗi giải nén hoặc lỗi kiểm tra checksum có trong định dạng sẽ dừng việc tải trước khi embedding hay lưu trữ, giữ nguyên bản ghi cũ của tài liệu. Giá trị `Content-Encoding` không được hỗ trợ hoặc có nhiều lớp nén cũng bị từ chối trước khi embedding hay lưu trữ.

Để dừng chờ tài liệu URL tải chậm, truyền `cancellationToken` cho `RagStore.BuildAsync`. Token được chuyển đến yêu cầu HTTP, quá trình đọc thân phản hồi và giải nén. Hủy mang tính hợp tác và không hoàn tác tài liệu đã lưu trước đó.

<a id="custom-retriever"></a>

## Kết nối bộ truy xuất không bắt buộc embedding

Mã sản phẩm phù hợp tìm từ khóa, còn câu hỏi diễn đạt khác tài liệu cần tìm ngữ nghĩa. Bộ truy xuất được chọn chỉ chuẩn bị biểu diễn cần thiết; tìm từ khóa không phải tạo embedding câu hỏi trước.

- Trước: mọi chiến lược nhận embedding câu hỏi.
- Sau: bộ truy xuất chỉ chuẩn bị biểu diễn cần thiết.

Triển khai `IRagRetriever` cho chỉ mục ngoài hoặc biểu diễn khác. `RagRetrievalRequest` truyền `Query` (câu hỏi ngữ nghĩa đầy đủ), `TextQuery` nullable (ghi đè từ khóa), `TopK`, `Filter` và `ProgressAsync`. Bộ dựng sẵn dùng `Query` khi `TextQuery` là null; chuỗi rỗng bỏ nhánh văn bản. Bộ tùy chỉnh phải chuẩn bị truy vấn, áp dụng lọc, giới hạn kết quả và hủy.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

Đăng ký bằng `UseRetriever(...)` hoặc `RagPipeline.SetRetriever(...)`. `IRetrievalStrategy` và `SetRetrievalStrategy(...)` vẫn dùng được qua adapter tạo embedding câu hỏi. Kết quả phải có nội dung và metadata cho xếp hạng lại và ngữ cảnh.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` bỏ qua embedding câu hỏi. Nhập tài liệu vẫn chia đoạn và tạo embedding cho kho vector hiện có; đây không phải lập chỉ mục chỉ có văn bản. Khởi tạo trì hoãn vẫn có thể gọi embedding tài liệu ở câu hỏi đầu tiên.

Giai đoạn câu hỏi `Embedding` phụ thuộc bộ truy xuất; tìm từ khóa không báo giai đoạn này. Bộ tùy chỉnh có thể báo giai đoạn qua `request.ProgressAsync`. Embedding tài liệu không đổi.

## Tại sao cần tùy chỉnh?

Pipeline RAG mặc định hoạt động tốt ngay từ đầu, nhưng các dự án thực tế thường cần kiểm soát nhiều hơn:

- **Debug** — giai đoạn nào chậm? Bộ viết lại có thay đổi truy vấn theo cách không mong muốn không?
- **Kỹ thuật prompt** — template prompt mặc định có thể không phù hợp với giọng điệu hoặc ràng buộc của domain bạn
- **Kiến trúc** — nhiều service chia sẻ một index tiết kiệm bộ nhớ và giữ embedding nhất quán
- **Kiểm tra** — đôi khi bạn cần xem kết quả truy xuất *trước* khi gửi đến LLM

Ngoài tiến độ truy xuất, nếu cần điều khiển hiển thị và dừng trong lúc tạo câu trả lời, hãy dùng Run trả về từ `RagEnabledService.StartRunAsync`. Chỉ dẫn bổ sung không tự chạy lại truy xuất RAG. Xem [hướng dẫn Run](execution-api-transition.md).

## Theo dõi tiến độ

Theo dõi giai đoạn RAG nào đang thực thi qua callback async theo từng query:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Các giai đoạn: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
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
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

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
   Viết lại truy vấn → Lọc → Embedding (khi cần) → Truy xuất → Lắp ráp context
    ↓
② TemplateContextBuilder thay thế {context} và {question}
   → "Trả lời theo thông tin sau.\n[1] Hoàn trả trong 30 ngày...\nCâu hỏi: Chính sách hoàn trả là gì?"
    ↓
③ RagEnabledService tạo AIRequestContext
   RequestMessageOverride = prompt đã lắp ráp
    ↓
④ _innerService.GetCompletionAsync(tin nhắn gốc, context: context) được gọi
   → AIService lưu context trong AsyncLocal
   → Câu hỏi gốc được thêm vào lịch sử hội thoại
    ↓
⑤ AIService.GetLatestMessages() thay thế đầu vào ban đầu của yêu cầu hiện tại
   Lịch sử: "Chính sách hoàn trả là gì?" (giữ nguyên gốc)
   Model thấy: prompt đã lắp ráp (RequestMessageOverride)
```

### Tại sao thiết kế này?

Điểm mấu chốt là **tách biệt lịch sử hội thoại khỏi input của model**:

- **Lịch sử hội thoại giữ câu hỏi gốc** — để các câu hỏi tiếp theo như "còn điều đó thì sao?" có context đúng
- **Model nhận prompt đã lắp ráp** — prompt đầy đủ với tài liệu đã truy xuất + câu hỏi
- **State của AIService không bao giờ bị thay đổi** — `AsyncLocal<T>` cung cấp cách ly theo request

`AIService` lưu ngữ cảnh trong `AsyncLocal`. `GetLatestMessages()` áp dụng `RequestMessageOverride` cho đầu vào ban đầu của yêu cầu logic hiện tại, giữ nguyên các lệnh gọi công cụ của trợ lý và kết quả tiếp theo. Nhờ đó, tài liệu truy xuất và kết quả công cụ cùng được gửi trong các yêu cầu tiếp theo đến mô hình. Khi hoàn tất, ngữ cảnh trước đó được khôi phục.
