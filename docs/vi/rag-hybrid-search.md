# Hybrid Search

Mã sản phẩm phù hợp tìm từ khóa, còn câu hỏi diễn đạt khác tài liệu cần tìm ngữ nghĩa. Bộ truy xuất được chọn chỉ chuẩn bị biểu diễn cần thiết; tìm từ khóa không phải tạo embedding câu hỏi trước.

## Chế độ dựng sẵn

```csharp
// Tìm ngữ nghĩa (mặc định)
.UseVectorSearch()

// Tìm từ khóa không có embedding câu hỏi
.UseKeywordSearch()

// Tìm hybrid có trọng số
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` bỏ qua embedding câu hỏi. Nhập tài liệu vẫn chia đoạn và tạo embedding cho kho vector hiện có; đây không phải lập chỉ mục chỉ có văn bản. Khởi tạo trì hoãn vẫn có thể gọi embedding tài liệu ở câu hỏi đầu tiên.

## Kết hợp kết quả từ khóa và ngữ nghĩa

`VectorWeight` là trọng số vector (0–1), trọng số từ khóa là `1 - VectorWeight`. `CandidateMultiplier` điều khiển ứng viên mỗi nhánh, `RrfK` điều khiển làm mượt thứ hạng trong Reciprocal Rank Fusion có trọng số. Chúng khác hệ số ứng viên reranker RAG. Hãy đánh giá với tài liệu và câu hỏi thực tế.

Chế độ vector và từ khóa thuần giữ điểm gốc. Hybrid tùy chỉnh dùng RRF có trọng số chuẩn hóa kể cả một nhánh; trọng số vector 0 bỏ embedding câu hỏi. Điểm không phải xác suất. `WeightedBlend` trộn điểm truy xuất và reranker không hiệu chỉnh; nên dùng `RerankerOnly` cho tìm từ khóa chưa hiệu chỉnh điểm.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## Hỗ trợ kho và tương thích

InMemory, PostgreSQL và Qdrant hỗ trợ đường từ khóa mới và RRF có trọng số tùy chỉnh. Điểm văn bản khác nhau: BM25 ở InMemory, toàn văn hoặc trigram đã cấu hình ở PostgreSQL, chỉ mục thưa ở Qdrant. Điểm của các công cụ không tương đương.

Pinecone giữ hybrid gốc qua `UseHybridSearch()` mặc định trên chỉ mục `dotproduct` tương thích. Adapter không hỗ trợ chế độ từ khóa hay RRF có trọng số tùy chỉnh cả hai nhánh. Kho khác cần interface tùy chọn tương ứng. Chế độ hoặc tùy chọn không hỗ trợ sẽ báo lỗi rõ ràng, không tự chuyển sang tìm vector hay bỏ qua trọng số.

Các bộ tiếp hợp InMemory, PostgreSQL và Qdrant hiện có không cài mô hình nơ-ron hay chuyển chỉ mục. Phân biệt `C#` và `C++` phụ thuộc bộ phân tích. Tùy chọn PIXIE bên dưới cũng cần đánh giá khả năng khớp chính xác định danh.

Xem [chế độ và kho hỗ trợ](rag.md#retrieval-modes) và [bộ truy xuất tùy chỉnh](rag-pipeline.md#custom-retriever).

<a id="pixie-search"></a>

## So sánh tìm kiếm nơ-ron cục bộ bằng PIXIE

Khi câu hỏi và tài liệu dùng cách diễn đạt khác nhau, tìm kiếm thưa đã học có thể bổ sung từ vựng liên quan. Gói tùy chọn `Mythosia.AI.Rag.Search.Pixie` mã hóa cả tài liệu lẫn truy vấn bằng PIXIE cục bộ và kết hợp với embedding đặc hiện có. PIXIE không cần máy chủ Python hay khóa API; nhà cung cấp embedding đặc hoặc câu trả lời đã chọn vẫn có thể dùng API từ xa.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

Với kho này, `UseKeywordSearch()` chọn tìm kiếm thưa bằng nơ-ron: bỏ qua nhà cung cấp embedding đặc cho truy vấn nhưng vẫn chạy PIXIE trên câu hỏi. Việc nhập tài liệu RAG vẫn tạo embedding đặc. `UseHybridSearch(...)` hợp nhất thứ hạng tích vô hướng thưa và độ tương đồng cosin đặc bằng RRF có trọng số đã cấu hình.

Bản xem trước cung cấp `PixieInMemoryStore`, chỉ mục trong bộ nhớ. Nó không kết nối PIXIE với PostgreSQL, Qdrant hay Pinecone. Hãy lập lại chỉ mục sau khi khởi động lại hoặc đổi mô hình/cấu hình. Giữ bộ mã hóa hoạt động trong mọi thao tác rồi giải phóng khi hoàn tất. Tìm kiếm hiện tại vẫn là mặc định: hãy so sánh cùng tài liệu và câu hỏi có đánh giá độ liên quan trước khi chuyển đổi. PIXIE không đảm bảo phân biệt chính xác `C#`/`C++` hoặc điều kiện loại trừ.

[Hướng dẫn PIXIE và so sánh (tiếng Anh)](../rag-pixie-search.md).
