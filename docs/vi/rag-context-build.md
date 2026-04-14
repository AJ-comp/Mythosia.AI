# Xây dựng ngữ cảnh

> 📍 **Pipeline Q&A:** [Viết lại truy vấn](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → [Lọc](rag-filtering.md) → [Truy xuất](rag-hybrid-search.md) → [Reranking](rag-reranking.md) → **`Xây dựng ngữ cảnh`**

## Xây dựng ngữ cảnh là gì?

Xây dựng ngữ cảnh là giai đoạn cuối cùng của RAG pipeline. Sau khi truy xuất và xếp hạng các đoạn liên quan nhất, giai đoạn này **tập hợp chúng thành một prompt** mà LLM có thể hiểu và dùng để tạo câu trả lời.

Hãy nghĩ như viết một tài liệu tóm tắt cho ai đó trước cuộc họp. Bạn đã thu thập thông tin liên quan (truy xuất) và sắp xếp theo tầm quan trọng (reranking). Giờ bạn cần **tổ chức rõ ràng** và đặt câu hỏi để người đọc biết chính xác phải làm gì với thông tin đó.

Chất lượng giai đoạn này ảnh hưởng trực tiếp đến chất lượng phản hồi của LLM. Prompt được cấu trúc tốt giảm thiểu ảo giác và giúp model bám sát vào context được cung cấp.

## Context Builder mặc định

Khi không có cấu hình tùy chỉnh, pipeline dùng `DefaultContextBuilder`, tạo ra định dạng này:

```
Trả lời câu hỏi dựa trên context sau:

[1] (Nguồn: manual.txt)
Hoàn tiền có thể thực hiện trong vòng 30 ngày kể từ khi mua...

[2] (Nguồn: policy.txt)
Sản phẩm kỹ thuật số không được hoàn tiền...

Câu hỏi: Chính sách hoàn tiền là gì?
```

Builder mặc định có các thuộc tính có thể cấu hình:

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "Trả lời câu hỏi dựa trên context sau:",
    QueryPrefix = "Câu hỏi:",
    IncludeScores = false,    // hiển thị điểm tương đồng?
    IncludeSource = true      // hiển thị metadata nguồn?
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

### Bao gồm điểm số

Khi `IncludeScores = true`, mỗi đoạn hiển thị điểm tương đồng:

```
[1] (Nguồn: manual.txt) [Điểm: 0.892]
Hoàn tiền có thể thực hiện trong vòng 30 ngày...
```

Hữu ích cho debug và hiểu tại sao các đoạn nhất định được chọn.

## Template prompt

Để kiểm soát tốt hơn prompt cuối cùng, dùng **template prompt** với placeholder `{context}` và `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Bạn là trợ lý hỗ trợ khách hàng. Chỉ dùng các tài liệu sau
        để trả lời câu hỏi. Nếu câu trả lời không có trong tài liệu, nói
        "Tôi không có thông tin đó."

        Tài liệu:
        {context}

        Câu hỏi của khách hàng: {question}
        """)
    .AddDocument("support-kb.txt")
)
```

Pipeline thay thế `{context}` bằng danh sách đoạn được đánh số và `{question}` bằng truy vấn của user.

### Khi nào dùng template

Template đặc biệt mạnh khi bạn cần:

- **Hạn chế hành vi** — "Nếu câu trả lời không có trong context, nói 'Tôi không biết'"
- **Đặt giọng điệu** — "Trả lời theo cách chuyên nghiệp, súc tích"
- **Thêm vai trò** — "Bạn là trợ lý y tế" hoặc "Bạn là cố vấn pháp lý"
- **Kiểm soát ngôn ngữ** — "Luôn trả lời bằng tiếng Việt"

### Mẹo thiết kế template

| Mẹo | Ví dụ |
| --- | --- |
| Yêu cầu model bám vào context | "Chỉ dựa trên tài liệu đã cung cấp để trả lời" |
| Xử lý thông tin còn thiếu | "Nếu không tìm thấy câu trả lời, nói 'Tôi không có thông tin đó'" |
| Chỉ định định dạng output | "Trả lời dạng danh sách gạch đầu dòng" |
| Đặt ràng buộc ngôn ngữ | "Luôn trả lời cùng ngôn ngữ với câu hỏi" |

## Context Builder tùy chỉnh

Để kiểm soát hoàn toàn, triển khai `IContextBuilder`:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### Thông tin liên quan ###");
        sb.AppendLine();

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "không rõ";
            sb.AppendLine($"📄 Từ: {source} (độ liên quan: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine();
        sb.AppendLine($"Dựa trên thông tin trên, hãy trả lời: {query}");

        return sb.ToString();
    }
}
```

Đăng ký với builder:

```csharp
.WithRag(rag => rag
    .WithContextBuilder(new MyContextBuilder())
    .AddDocument("docs.txt")
)
```

## Bước tiếp theo

- [Tùy chỉnh Pipeline](rag-pipeline.md) — tinh chỉnh hành vi RAG tổng thể
- [Reranking](rag-reranking.md) — cải thiện chất lượng đoạn trước khi xây dựng ngữ cảnh
- [Tổng quan RAG](rag.md) — xem lại toàn bộ flow RAG
