# Embedding

> 📍 **Pipeline Q&A:** [Viết lại truy vấn](rag-query-rewriting.md) → **`Embedding`** → [Lọc](rag-filtering.md) → [Truy xuất](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → [Xây dựng context](rag-context-build.md)

## Embedding là gì?

Embedding là quá trình chuyển đổi văn bản thành vector số (mảng các con số) nắm bắt ý nghĩa. Các vector này tồn tại trong không gian nhiều chiều, nơi **các văn bản có ý nghĩa tương tự nằm gần nhau**.

Hãy tưởng tượng như vẽ các thành phố lên bản đồ. Các thành phố gần nhau về mặt địa lý xuất hiện gần nhau trên bản đồ. Tương tự, các câu "Làm thế nào để hủy đăng ký của tôi?" và "Tôi muốn kết thúc tư cách thành viên" tạo ra các vector gần nhau — dù dùng từ hoàn toàn khác nhau.

Trong RAG pipeline, embedding xảy ra tại hai điểm:

1. **Lập index tài liệu** — mỗi đoạn được embed và lưu vào vector store
2. **Thời điểm truy vấn** — câu hỏi của user được embed để so sánh với các đoạn đã lưu

Trang này tập trung vào embedding thời điểm truy vấn (bước 2), chuyển đổi câu hỏi của user thành vector để tìm kiếm độ tương đồng.

## Provider embedding tích hợp

Mythosia.AI.Rag đi kèm bốn embedding provider. Chọn dựa trên nhu cầu của bạn:

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
var options = new RagPipelineOptions
{
    EmbeddingBatchSize = 100   // mặc định: 100 đoạn mỗi API call
};
```

Kích thước lô lớn hơn nghĩa là ít API call hơn nhưng dùng nhiều bộ nhớ hơn mỗi call. Nếu gặp rate limit hoặc vấn đề bộ nhớ, thử giảm giá trị này.

## Số chiều (Dimensions)

Thuộc tính `Dimensions` kiểm soát kích thước của mỗi embedding vector. Điều này quan trọng vì:

- **Vector store phải khớp** — nếu embedding của bạn có 1536 chiều, cột trong vector store cũng phải là 1536
- **Chiều cao hơn = chi tiết hơn** — nhưng cũng tốn nhiều lưu trữ và tìm kiếm chậm hơn
- **Chiều thấp hơn = nhanh hơn** — nhưng có thể mất đi sự khác biệt ý nghĩa tinh tế

Kích thước chiều phổ biến:

| Provider | Model | Chiều mặc định |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
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
