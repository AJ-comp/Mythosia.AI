# Agentic RAG

## Tại sao cần Agentic RAG?

Trong RAG tiêu chuẩn, mỗi tin nhắn của user kích hoạt đúng **một** lần truy xuất. Hệ thống tìm kiếm, xây dựng context và tạo phản hồi — bất kể là gì. Điều này hoạt động tốt cho các câu hỏi đơn giản, nhưng không đủ khi:

- Câu hỏi cần **nhiều lần tìm kiếm** qua các chủ đề khác nhau (ví dụ "So sánh chính sách hoàn tiền cho sản phẩm phần cứng và phần mềm")
- Kết quả tìm kiếm đầu tiên **không đủ** và hệ thống nên tinh chỉnh và thử lại
- Một số câu hỏi **không cần truy xuất** (ví dụ "Tóm tắt cuộc trò chuyện của chúng ta")
- Câu trả lời phụ thuộc vào việc kết hợp **truy xuất tài liệu với dữ liệu trực tiếp** từ API

Agentic RAG giải quyết tất cả những điều này. Thay vì pipeline retrieve-then-answer cố định, **agent tự quyết định** — khi nào tìm kiếm, tìm gì, có nên tìm lại không, và khi nào gọi các công cụ khác — tất cả trong một ReAct loop.

## Bắt đầu nhanh

Đăng ký `RagStore` như một công cụ với `WithAgenticRag`, rồi giao cho `RunAgentAsync`:

```csharp
// Xây dựng index một lần
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// Đăng ký RAG như một công cụ và chạy agent
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("Tóm tắt chính sách hoàn tiền.");
```

Agent tự động gọi `search_documents` khi cần context tài liệu, rồi tổng hợp câu trả lời cuối cùng từ các đoạn đã truy xuất.

## Kết hợp với các công cụ khác

Agentic RAG tỏa sáng khi kết hợp với các công cụ bổ sung — agent chọn công cụ phù hợp cho từng tác vụ con:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Tra cứu trạng thái đơn hàng theo ID.",
           ("order_id", "ID đơn hàng cần tra cứu.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// Agent tìm kiếm tài liệu về chính sách VÀ gọi API để lấy dữ liệu đơn hàng trực tiếp
var answer = await service.RunAgentAsync(
    "Đơn hàng #12345 — tôi có đủ điều kiện hoàn tiền theo chính sách hiện tại không?");
```

Trong ví dụ này, agent tự chủ:

1. Tìm kiếm tài liệu về chính sách hoàn tiền
2. Gọi API đơn hàng để lấy trạng thái đơn #12345
3. Kết hợp cả hai thông tin để đưa ra câu trả lời cuối cùng

## Mô tả công cụ tùy chỉnh

Mô tả công cụ kiểm soát khi nào agent quyết định gọi RAG. Điều chỉnh nó cho domain của bạn để chọn công cụ chính xác hơn:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "Tìm kiếm chính sách nhân sự nội bộ, hướng dẫn sản phẩm và tài liệu tuân thủ. " +
        "Gọi công cụ này khi cần thông tin chính sách hoặc sản phẩm cụ thể của công ty.");
```

Mô tả mơ hồ như "Tìm kiếm tài liệu" có thể khiến agent gọi RAG quá nhiều hoặc quá ít. Hãy cụ thể về **loại thông tin** mà tài liệu chứa.

## Sự khác biệt với RAG tiêu chuẩn

| | RAG tiêu chuẩn | Agentic RAG |
| --- | --- | --- |
| Thời điểm tìm kiếm | Mỗi tin nhắn | Agent quyết định |
| Đặt câu truy vấn | QueryRewriter | Agent tự đặt |
| Số lần tìm kiếm | Một lần mỗi lượt | Một hoặc nhiều lần tùy nhu cầu |
| Kết hợp công cụ | Không áp dụng | Bất kỳ công cụ nào đã đăng ký |
| Thiết lập | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **Lưu ý:** `QueryRewriter` cố tình bị bỏ qua trong Agentic RAG. Agent tự đặt truy vấn tìm kiếm tự chứa, nên bước viết lại riêng biệt sẽ thừa và có thể làm sai lệch ý định của agent.

## Khi nào chọn cái nào

- **RAG tiêu chuẩn** — mỗi câu hỏi đều dựa trên tài liệu, một chủ đề, và bạn muốn độ trễ tối thiểu
- **Agentic RAG** — câu hỏi trải qua nhiều chủ đề, cần kết hợp tài liệu + dữ liệu trực tiếp, hoặc cần truy xuất lặp lại
