# Reranking & Tinh chỉnh tìm kiếm

> 📍 **Pipeline Q&A:** [Viết lại truy vấn](rag-query-rewriting.md) → Lọc → Embedding (khi cần) → [Truy xuất](rag-hybrid-search.md) → **`Reranking`** → Xây dựng context

Giai đoạn câu hỏi `Embedding` phụ thuộc bộ truy xuất; tìm từ khóa không báo giai đoạn này. Bộ tùy chỉnh có thể báo giai đoạn qua `request.ProgressAsync`. Embedding tài liệu không đổi.

## Tại sao cần Reranking?

Tìm kiếm vector trả về ứng viên được sắp xếp theo độ tương đồng embedding, nhưng độ tương đồng embedding là một **xấp xỉ**. Một đoạn đạt điểm 0.82 thực ra có thể liên quan hơn đoạn đạt 0.85 — embedding chỉ không thể phân biệt chúng.

**Reranker** nhận danh sách ứng viên ban đầu và chấm điểm mỗi đoạn so với truy vấn gốc bằng một model mạnh hơn, tạo ra thứ tự liên quan chính xác hơn nhiều. Điều này đặc biệt có giá trị khi:

- Corpus của bạn chứa nhiều đoạn trông giống nhau (ví dụ các mục FAQ)
- Kết quả hàng đầu từ vector search "gần nhưng chưa đúng"
- Bạn cần câu trả lời chính xác cao cho các trường hợp quan trọng

## Các tùy chọn Reranker

### LLM Reranker

Dùng AI service của bạn để chấm điểm kết quả. Hiệu quả nhưng thêm độ trễ:

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

Để câu hỏi và tài liệu của mỗi lần đánh giá không bị trộn với các lần đánh giá trước hoặc cuộc hội thoại của service, `LlmReranker` gửi một yêu cầu độc lập không dùng lịch sử cho mỗi lần đánh giá. Nó không đọc hay thêm nội dung vào lịch sử hội thoại. Cấu hình mặc định của service và mã gọi của bạn không thay đổi. Các lần đánh giá của các reranker dùng chung một AI service được xử lý tuần tự.

### Cohere Reranker

Gọi Cohere Rerank API — nhanh và chính xác:

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM Reranker

Dùng endpoint reranking vLLM được host cục bộ:

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## Tham số truy xuất

Kiểm soát số ứng viên được truy xuất và cách lọc trước khi chọn cuối cùng:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // Số đoạn cuối cùng được trả về
    .WithRetrievalMultiplier(3)    // Truy xuất topK × 3 ứng viên (cho reranking)
    .WithScoreThreshold(0.6)       // Điểm tương đồng tối thiểu
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — số đoạn đưa vào context LLM
- **`RetrievalMultiplier`** — lấy nhiều ứng viên hơn để reranker có nhiều để làm việc. Multiplier 3 nghĩa là 15 ứng viên được lấy, rồi 5 tốt nhất sống sót sau reranking
- **`WithScoreThreshold`** — loại bỏ bất cứ thứ gì dưới ngưỡng tương đồng này, dù ít hơn `TopK` đoạn còn lại

## Chế độ chọn cuối cùng

Khi dùng reranker, chọn cách tính điểm xếp hạng cuối cùng:

```csharp
using Mythosia.AI.Rag;

// Mặc định: chỉ tin vào điểm reranker
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// Pha trộn điểm truy xuất và điểm reranker
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)  // 65% truy xuất, 35% reranker
```

**`RerankerOnly`** là mặc định an toàn — phán đoán của reranker hoàn toàn thay thế điểm truy xuất ban đầu.

**`WeightedBlend`** giữ tín hiệu truy xuất gốc trong khi tích hợp đánh giá của reranker. Hữu ích khi embedding vector của bạn đã có chất lượng cao và bạn muốn reranker đóng vai trò tiebreaker thay vì override hoàn toàn.

Chế độ vector và từ khóa thuần giữ điểm gốc. Hybrid tùy chỉnh dùng RRF có trọng số chuẩn hóa kể cả một nhánh; trọng số vector 0 bỏ embedding câu hỏi. Điểm không phải xác suất. `WeightedBlend` trộn điểm truy xuất và reranker không hiệu chỉnh; nên dùng `RerankerOnly` cho tìm từ khóa chưa hiệu chỉnh điểm.
