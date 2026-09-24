# RAG (Retrieval-Augmented Generation)

Chỉ cần kết quả cuối cùng và nút Dừng thì truyền `cancellationToken` vào `GetCompletionAsync`. Dùng Run cho sự kiện tiến độ hoặc chỉ dẫn bổ sung được hỗ trợ. Xem [hủy câu trả lời](completions.md#completion-cancellation).

Với câu trả lời có ngữ cảnh truy xuất, cũng truyền `cancellationToken` vào `RagEnabledService.GetCompletionAsync`. Token đi qua truy xuất, `LlmQueryRewriter`, `LlmReranker` và lệnh gọi nội bộ; hủy lúc truy xuất sẽ chặn lần gọi mô hình sau đó. `RagPipeline.QueryAndGenerateAsync` cũng truyền token. Các thành phần phải hỗ trợ hủy; không hoàn tác truy xuất hoặc hành động công cụ đã xong.

RAG cho phép model trả lời câu hỏi dựa trên tài liệu của riêng bạn bằng cách truy xuất các đoạn liên quan tại thời điểm truy vấn.

Để hiển thị dần câu trả lời dựa trên tài liệu truy xuất và cho phép dừng tạo nội dung, có thể dùng `RagEnabledService.StartRunAsync`. Truy xuất diễn ra trước Run; chỉ dẫn bổ sung không tự kích hoạt truy xuất lại. Xem ví dụ và phạm vi trong [hướng dẫn Run](execution-api-transition.md).


Khi giữ tham chiếu `IAIService`, dùng `GetLastProcessing()` trong `Mythosia.AI.Extensions`. Nó đọc `IAIProcessingInfoService` tùy chọn và trả danh sách rỗng nếu không có chẩn đoán. `IAIService` không thêm thành viên bắt buộc. Với RAG, `RagEnabledService.WithSpeed(...)` cấu hình câu trả lời kế tiếp sau truy xuất; `LastProcessing` mô tả câu trả lời đó. Viết lại truy vấn nội bộ được tách riêng và Run trả cùng các bản ghi `Processing`. [WithSpeed](request-building.md#inference-speed)

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

Bản xem trước tùy chọn `Mythosia.AI.Rag.Search.Pixie` cho phép so sánh tìm kiếm thưa bằng nơ-ron cục bộ với cách tìm hiện tại. Nó giữ nhà cung cấp embedding đặc và dùng chỉ mục PIXIE trong bộ nhớ, không chuyển kho bền vững hay thay tìm kiếm mặc định. [Hướng dẫn PIXIE và so sánh (tiếng Anh)](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## Trả lời về tệp đính kèm dựa trên tài liệu của bạn

Để giải thích ảnh sản phẩm dựa trên hướng dẫn sử dụng, hãy truyền một `Message` chứa câu hỏi và ảnh vào `RagEnabledService.GetCompletionAsync(Message)` hoặc `StartRunAsync(Message)`. Cả hai đều giữ nội dung đính kèm không phải văn bản trong yêu cầu gửi đến dịch vụ AI bên trong. Việc truy xuất dùng phần văn bản của tin nhắn; tệp đính kèm không tự động được lập chỉ mục hay chuyển thành embedding. Nhà cung cấp và mô hình đã chọn phải hỗ trợ loại tệp đính kèm đó. Ngữ cảnh tìm được chỉ được thêm vào yêu cầu gửi đi: nó không ghi đè `Message` gốc hay thay thế văn bản của người dùng trong lịch sử hội thoại.

Khi câu trả lời cần cả tài liệu hướng dẫn lẫn số lượng hàng tồn kho hiện tại, hãy kết hợp RAG với các công cụ đã đăng ký. Trong các lượt gọi công cụ của `GetCompletionAsync`, ngữ cảnh truy xuất được giữ trên đầu vào ban đầu và từng kết quả công cụ tiếp theo được gửi nguyên vẹn đến mô hình. Lịch sử vẫn giữ đầu vào gốc của người dùng.

<a id="retrieval-modes"></a>

## Chọn cách tìm tài liệu

Mã sản phẩm phù hợp tìm từ khóa, còn câu hỏi diễn đạt khác tài liệu cần tìm ngữ nghĩa. Bộ truy xuất được chọn chỉ chuẩn bị biểu diễn cần thiết; tìm từ khóa không phải tạo embedding câu hỏi trước.

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` bỏ qua embedding câu hỏi. Nhập tài liệu vẫn chia đoạn và tạo embedding cho kho vector hiện có; đây không phải lập chỉ mục chỉ có văn bản. Khởi tạo trì hoãn vẫn có thể gọi embedding tài liệu ở câu hỏi đầu tiên.

Xem [chế độ và kho hỗ trợ](rag-hybrid-search.md) và [bộ truy xuất tùy chỉnh](rag-pipeline.md#custom-retriever).

## Thêm tài liệu

Nhiều loại nguồn được hỗ trợ:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // file cục bộ
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Nội dung nội tuyến có thể đặt ở đây.")  // chuỗi trực tiếp
)
```

`AddUrl` kiểm tra và giải nén các định dạng HTTP được hỗ trợ trước khi đọc văn bản, từ chối nội dung nén không đầy đủ, không được hỗ trợ hoặc có nhiều lớp. Xem [giải nén URL và hủy](rag-pipeline.md#url-documents).

<a id="document-identity"></a>

### Phân biệt các tệp trùng tên

Hai công ty có thể cùng cung cấp một tệp `docs/faq.txt`. Cả hai tài liệu phải được giữ trong chỉ mục, còn khi đăng ký lại cùng một tệp thì cần dùng lại định danh của tệp đó:

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

Trong luồng lưu trữ RAG mặc định, ID tài liệu được tạo trước khi gửi các bản ghi đến kho vector, sau đó các bản ghi có cùng `document_id` được thay thế. Kho PostgreSQL (pgvector) của thư viện dùng ID này và không tự kiểm tra đường dẫn tệp gốc. Trước đây, khi đăng ký thư mục, cả `company-a/docs/faq.txt` và `company-b/docs/faq.txt` đều được gửi với ID `faq.txt`, nên tài liệu thứ hai thay thế tài liệu thứ nhất. Bản sửa giữ lại đường dẫn đầy đủ khi tạo ID; lược đồ PostgreSQL không thay đổi. Bộ lọc `full_path` trong ví dụ lưu trữ dùng siêu dữ liệu do bên gọi cung cấp; nó không tự động tạo ID duy nhất cho tài liệu hoặc bản ghi.

`PlainTextDocumentLoader` và `DirectoryDocumentLoader` tích hợp dùng đường dẫn tuyệt đối của tệp, chuẩn hóa bằng `Path.GetFullPath`, làm `Source` và ID tài liệu tự động. Vì vậy, các tệp ở thư mục khác nhau có ID khác nhau. Đường dẫn tương đối, tuyệt đối và có `./` dùng lại cùng ID nếu chúng được phân giải thành cùng đường dẫn tuyệt đối, kể cả chữ hoa và chữ thường. Hãy giữ thư mục làm việc nhất quán khi dùng đường dẫn tương đối. Việc di chuyển tệp, dùng liên kết tượng trưng hoặc liên kết cứng, hay thay đổi chữ hoa/chữ thường không bảo đảm giữ nguyên ID.

`AddText(..., id: ...)`, `RagDocument.Id` được gán rõ ràng và quy tắc `Source` của bộ nạp tùy chỉnh không thay đổi. Không cần đổi API gọi. Do `Source` của các bộ nạp tích hợp này nay là đường dẫn tuyệt đối, trích dẫn mặc định cũng có thể hiển thị đường dẫn tuyệt đối. Để hiển thị, hãy dùng `filename` hoặc siêu dữ liệu `relative_path` từ bộ nạp thư mục mặc định. Overload đăng ký thư mục có cấu hình không tự động thêm `relative_path`.

**Chỉ mục hiện có:** ID theo đường dẫn tương đối cũ không tự động bị xóa hoặc chuyển đổi. Nên lập lại chỉ mục toàn bộ tài liệu trong một collection mới, kiểm tra rồi chuyển ứng dụng sang đó. Nếu dùng lại collection cũ, chỉ xóa các ID cũ đã xác nhận thuộc tài liệu nào, rồi lập lại chỉ mục các tệp nguồn tương ứng. Không xóa hàng loạt theo tên tệp vì thư mục khác có thể chứa tài liệu trùng tên.

Để cập nhật và xóa chỉ tác động đúng tài liệu, `document_id` là khóa dành riêng cho pipeline. Trước khi lưu, mỗi bản ghi nhận `RagDocument.Id` thực tế, dù metadata đầu vào cung cấp giá trị khác. Từ điển metadata của tài liệu đầu vào và splitter không bị sửa; callback lưu trữ tùy chỉnh cũng nhận bản ghi đã chuẩn hóa. Hãy dùng khóa khác cho mã định danh riêng của ứng dụng.

Điều này không tự sửa các bản ghi đã lưu với `document_id` sai. Hãy tạo collection mới từ nguồn đáng tin cậy, hoặc xác định tài liệu sở hữu và chỉ dọn các bản ghi bị ảnh hưởng trước khi lập chỉ mục lại. Chỉ đăng ký lại với ID đúng không thể tìm chắc chắn các bản ghi cũ đang nằm dưới ID khác.

Cùng một tệp được đăng ký bằng đường dẫn tương đối và tuyệt đối phải cập nhật cùng tài liệu; các tệp trùng tên ở thư mục khác nhau phải tách biệt. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` và `PdfDocumentLoader` nay đặt `DoclingDocument.Source` thành đường dẫn tuyệt đối đã chuẩn hóa như các loader TXT tích hợp. RAG tạo ID tự động từ giá trị này; ID chỉ định rõ vẫn do bên gọi quản lý. Trích dẫn mặc định có thể hiển thị đường dẫn tuyệt đối.

[Giữ định danh tệp ổn định khi đăng ký](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### Làm rỗng tài liệu mà không giữ lại kết quả tìm kiếm cũ

Khi bạn xóa nội dung của chính sách hoàn tiền đã ngừng áp dụng rồi lập chỉ mục lại cùng tài liệu, nội dung cũ không được tiếp tục xuất hiện trong câu trả lời. Với cơ chế lưu trữ RAG mặc định, nếu việc chia đoạn hoàn tất thành công và tạo ra 0 đoạn, các bản ghi khớp `document_id` đó được thay bằng tập rỗng. Không yêu cầu embedding và dữ liệu của các ID khác được giữ nguyên. Điều này áp dụng cho tài liệu rỗng hoặc chỉ có khoảng trắng khi bộ chia trả về 0 đoạn, cũng như bộ chia tùy chỉnh trả về 0 đoạn thành công.

Với một `RagPipeline` tên `pipeline` đã được cấu hình, hãy dùng lại ID của tài liệu đang lưu:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

Sau đó bạn có thể lập chỉ mục nội dung không rỗng với cùng ID. Bộ tải không trả về tài liệu nào, hoặc tài liệu vắng mặt trong danh sách tệp sau này, không phải là lệnh xóa: không có ID tài liệu nào được cung cấp để thay thế.

Ngoại lệ khi tải, phân tích hoặc chia đoạn, và việc hủy được phát hiện trước khi gọi kho lưu trữ, sẽ giữ nguyên các bản ghi của tài liệu đó. Bộ tải và bộ phân tích phải báo lỗi bằng ngoại lệ; kết quả thành công với 0 đoạn không thể phân biệt với việc chủ động làm rỗng. Sau khi bắt đầu lưu, khả năng hoàn tác khi lỗi hoặc hủy phụ thuộc vào kho lưu trữ; PostgreSQL thay thế trong một giao dịch. Xử lý theo lô thực hiện từng tài liệu và không hoàn tác các tài liệu đã hoàn tất trước đó.

**Lưu trữ tùy chỉnh:** khi cung cấp `onDocumentEmbedded`, callback này vẫn chịu trách nhiệm lưu trữ. Với 0 đoạn, callback không được gọi và kho mặc định không được truy cập. Ứng dụng phải chủ động xóa ID đã biết trong kho riêng, hoặc dùng `DeleteDocumentAsync` cho kho của pipeline.

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
