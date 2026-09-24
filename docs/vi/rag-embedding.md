# Embedding

> 📍 **Pipeline Q&A:** [Viết lại truy vấn](rag-query-rewriting.md) → [Lọc](rag-filtering.md) → **`Embedding (khi cần)`** → [Truy xuất](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → [Xây dựng context](rag-context-build.md)

Giai đoạn câu hỏi `Embedding` phụ thuộc bộ truy xuất; tìm từ khóa không báo giai đoạn này. Bộ tùy chỉnh có thể báo giai đoạn qua `request.ProgressAsync`. Embedding tài liệu không đổi.

## Embedding là gì?

Embedding là quá trình chuyển đổi văn bản thành vector số (mảng các con số) nắm bắt ý nghĩa. Các vector này tồn tại trong không gian nhiều chiều, nơi **các văn bản có ý nghĩa tương tự nằm gần nhau**.

Hãy tưởng tượng như vẽ các thành phố lên bản đồ. Các thành phố gần nhau về mặt địa lý xuất hiện gần nhau trên bản đồ. Tương tự, các câu "Làm thế nào để hủy đăng ký của tôi?" và "Tôi muốn kết thúc tư cách thành viên" tạo ra các vector gần nhau — dù dùng từ hoàn toàn khác nhau.

Trong RAG pipeline, embedding xảy ra tại hai điểm:

1. **Lập index tài liệu** — mỗi đoạn được embed và lưu vào vector store
2. **Thời điểm truy vấn** — câu hỏi của user được embed để so sánh với các đoạn đã lưu

Trang này tập trung vào embedding thời điểm truy vấn (bước 2), chuyển đổi câu hỏi của user thành vector để tìm kiếm độ tương đồng.

## Provider embedding tích hợp

Chọn nhà cung cấp embedding theo ngôn ngữ tài liệu, môi trường triển khai và nhu cầu truy xuất.

### Perplexity

Embedding tiêu chuẩn xử lý đoạn độc lập và triển khai `IEmbeddingProvider` cho bộ dựng RAG hiện có. Embedding ngữ cảnh giữ thứ tự đoạn và nhóm tài liệu. API riêng ngăn gộp phẳng các tài liệu không liên quan.

[Perplexity Agent API, tìm kiếm và embedding](perplexity.md).

### OpenAI Embedding

Lựa chọn cloud phổ biến nhất. Chất lượng cao, cần API key:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",   // mặc định
    dimensions: 1536                    // mặc định
);
```

Cũng có thể dùng fluent builder:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

<a id="openai-dimensions"></a>

`text-embedding-ada-002` có kích thước cố định **1536 chiều**. Nhà cung cấp bỏ trường `dimensions` mà mô hình này không hỗ trợ trong cả yêu cầu đơn lẻ và theo lô; cấu hình kích thước khác sẽ gây ra `ArgumentOutOfRangeException` trước khi gọi API. Yêu cầu cho `text-embedding-3-small` và `text-embedding-3-large` vẫn gửi giá trị `dimensions` đã cấu hình.

### Ollama (Cục bộ)

Chạy embedding cục bộ mà không gửi dữ liệu lên cloud. Cần [Ollama](https://ollama.com/) chạy trên máy của bạn:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",       // mặc định
    dimensions: 1024,                    // mặc định
    baseUrl: "http://localhost:11434"    // mặc định
);
```

<a id="ollama-dimensions"></a>

Vector tài liệu và câu hỏi phải dùng cùng mô hình và số chiều. `OllamaEmbeddingProvider` gửi `dimensions` đã cấu hình đến `/api/embed` và kiểm tra độ dài của từng vector trả về. Mặc định của provider vẫn là `qwen3-embedding:4b` với **1024 chiều được yêu cầu**; đầu ra gốc của mô hình có 2560 chiều. Máy chủ Ollama và mô hình được chọn phải hỗ trợ số chiều yêu cầu. Yêu cầu không được hỗ trợ hoặc phản hồi bỏ qua thiết lập sẽ gây lỗi, thay vì âm thầm thay đổi `Dimensions` hay chỉnh độ dài vector tại máy khách.

Nếu thay mô hình hoặc số chiều, hãy tạo lại embedding tài liệu bằng cùng thiết lập dùng cho câu hỏi và cấu hình kho vector tương ứng. Vector hiện có không được tự động chuyển đổi.

### vLLM (Tự host)

Dành cho nhóm chạy embedding server riêng với [vLLM](https://docs.vllm.ai/):

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B", // mặc định
    dimensions: 1024,                     // mặc định
    baseUrl: "http://localhost:8002"      // mặc định
);
```

### Cục bộ (Không cần API)

Provider nhẹ không cần cấu hình, dựa trên feature hashing. Không cần API key, không cần dịch vụ bên ngoài — nhưng chất lượng embedding thấp hơn nhiều so với model neural, nên **không khuyến nghị cho production**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **Mẹo:** Dùng `OpenAIEmbeddingProvider` với model `text-embedding-3-small`. Giá cực rẻ — gần như miễn phí — và cho kết quả tốt hơn nhiều.

## Xử lý theo lô

Khi lập index tài liệu, pipeline embed các đoạn theo lô để tránh gửi hàng ngàn văn bản trong một API call. Kích thước lô có thể cấu hình:

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // mặc định: 100 đoạn mỗi API call
pipeline.Options = options;
```

Kích thước lô lớn hơn nghĩa là ít API call hơn nhưng dùng nhiều bộ nhớ hơn mỗi call. Nếu gặp rate limit hoặc vấn đề bộ nhớ, thử giảm giá trị này.

`EmbeddingBatchSize` phải dương. Pipeline kiểm tra và chốt giá trị ở đầu mỗi lần gọi lập chỉ mục tài liệu, trước khi embedding hoặc thay thế bản ghi. Điều này tránh lặp lô rỗng và bỏ qua chunk khi cấu hình thay đổi trong lúc chờ bất đồng bộ. Các lần gọi sau có thể dùng giá trị mới.

<a id="embedding-validation"></a>

## Giữ mỗi vector gắn với đúng đoạn

Phản hồi HTTP thành công vẫn có thể thiếu vector hoặc sai thứ tự, khiến văn bản được gắn với ý nghĩa của đoạn khác. `IEmbeddingProvider` tùy chỉnh phải trả về đúng một `float[]` khác null cho mỗi đầu vào, theo thứ tự đầu vào, và cung cấp `Dimensions` dương. Mỗi vector phải có độ dài đúng bằng số chiều đó và mọi giá trị phải hữu hạn, không có `NaN` hay vô cực.

Khi lập chỉ mục tài liệu, pipeline từ chối số chiều, số lượng phản hồi hoặc vector không hợp lệ bằng `InvalidOperationException` trước khi lưu hay gọi `onDocumentEmbedded`. Mỗi vector hợp lệ được sao chép trước khi yêu cầu batch tiếp theo, nên việc provider tái sử dụng bộ đệm ở batch sau không làm thay đổi các đoạn trước. Dữ liệu trả về phải ổn định trong lúc bên gọi đọc; không hỗ trợ sửa đồng thời khi kiểm tra hoặc sao chép. Nếu kiểm tra thất bại, bản ghi hiện có của tài liệu được giữ nguyên.

`OpenAIEmbeddingProvider` yêu cầu `index` hợp lệ và duy nhất ở mọi phần tử phản hồi rồi khôi phục thứ tự đầu vào. `VllmEmbeddingProvider` áp dụng cùng quy tắc khi có chỉ số; để tương thích, nó cũng chấp nhận phản hồi mà tất cả phần tử đều bỏ `index`, theo thứ tự phản hồi. Chỉ số thiếu một phần, trùng lặp hoặc ngoài phạm vi đều bị từ chối. Provider tùy chỉnh hoặc phản hồi không có chỉ số vẫn phải bảo đảm đúng thứ tự; kiểm tra cấu trúc không xác minh được ý nghĩa của vector.

<a id="query-embedding-validation"></a>

## Bảo vệ vector câu hỏi trước khi tìm kiếm

Việc tái sử dụng bộ đệm không được làm đổi câu hỏi trong lúc chờ thông báo tiến độ hoặc tìm kiếm. Truy xuất dense tích hợp, gồm adapter `IRetrievalStrategy`, yêu cầu `Dimensions` dương, vector khác null có đúng độ dài đó và các giá trị hữu hạn. Kết quả sai gây `InvalidOperationException` trước tìm kiếm. Vector hợp lệ được sao chép ngay sau khi trả về, trước thông báo hoặc tìm kiếm tiếp theo. Provider phải giữ dữ liệu ổn định trong lúc đọc; `IRagRetriever` tùy chỉnh tự đảm nhiệm việc chuẩn bị và kiểm tra câu hỏi.

`OllamaEmbeddingProvider` cũng kiểm tra cấu trúc, số vector chính xác, số chiều và giá trị hữu hạn khi gọi trực tiếp đơn lẻ hoặc theo batch. JSON hoặc vector lỗi gây `InvalidOperationException` thay vì trả kết quả thiếu. `HttpClient` được truyền vào vẫn thuộc bên gọi; giải phóng từng yêu cầu/phản hồi HTTP không giải phóng client đó.

## Số chiều (Dimensions)

Thuộc tính `Dimensions` kiểm soát kích thước của mỗi embedding vector. Điều này quan trọng vì:

- **Vector store phải khớp** — nếu embedding của bạn có 1536 chiều, cột trong vector store cũng phải là 1536
- **Chiều cao hơn = chi tiết hơn** — nhưng cũng tốn nhiều lưu trữ và tìm kiếm chậm hơn
- **Chiều thấp hơn = nhanh hơn** — nhưng có thể mất đi sự khác biệt ý nghĩa tinh tế

Kích thước chiều phổ biến:

| Provider | Model | Chiều mặc định |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | Yêu cầu 1024 (gốc: 2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Cục bộ | (feature hashing) | 1024 |

## Provider embedding tùy chỉnh

Nếu dùng dịch vụ embedding khác, triển khai `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Gọi embedding API của bạn ở đây
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Batch embedding call
    }
}
```

Đăng ký với builder:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```

## Bước tiếp theo

- [Lọc](rag-filtering.md) — thu hẹp các đoạn nào được tìm kiếm
- [Truy xuất (Hybrid Search)](rag-hybrid-search.md) — kết hợp vector và tìm kiếm từ khóa
- [Tùy chỉnh Pipeline](rag-pipeline.md) — chia sẻ embedding provider giữa các service
