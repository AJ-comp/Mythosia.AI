# Agent (ReAct Loop)

## Tại sao cần Agent Loop?

Function calling thông thường có thể thực thi **nhiều hàm từ một phản hồi của model dưới dạng batch có thứ tự** và tiếp tục qua các vòng dùng công cụ. API Agent đóng gói cơ chế đó thành vòng lặp ReAct hướng mục tiêu với **giới hạn số bước** rõ ràng, gửi kết quả của từng batch lại cho model cho đến khi tạo ra câu trả lời cuối cùng:

- "Nghiên cứu 3 công ty AI hàng đầu và so sánh giá cổ phiếu của họ" — cần nhiều lần tìm kiếm web và tra cứu giá
- "Tìm chính sách liên quan, kiểm tra trạng thái đơn hàng, rồi cho tôi biết tôi có đủ điều kiện hoàn tiền không" — cần nối chuỗi các công cụ khác nhau theo thứ tự logic
- Model có thể cần **thử lại hoặc tinh chỉnh** tìm kiếm nếu kết quả đầu tiên chưa đủ

`GetCompletionAsync` và `StartRunAsync` đã thực thi vòng lặp chung giữa mô hình và công cụ. Các hàm agent cũ chỉ thêm giới hạn vòng cho từng lần gọi và chuyển đổi lỗi riêng, không tạo bộ lập kế hoạch hay bộ thực thi độc lập.

## Hiển thị tiến độ tác vụ công cụ bằng Run

Để người dùng theo dõi tiến độ của tác vụ dùng nhiều công cụ hoặc dừng giữa chừng, hãy dùng `StartRunAsync`. Xem hỗ trợ chỉ dẫn bổ sung và cách lấy kết quả trong [hướng dẫn Run](execution-api-transition.md).

```csharp
// Đăng ký các hàm trên service trước khi bắt đầu tác vụ.
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Tìm chính sách, kiểm tra đơn hàng và giải thích kết quả.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

## Ví dụ tương thích với API cũ

`RunAgentAsync` và `RunAgentStreamAsync` bên dưới là API tương thích có cảnh báo `[Obsolete]`. Với mã mới, dùng Run ở trên và ghi rõ `WithMaxRounds(10)` để giữ giới hạn 10 vòng trước đây. Nếu phụ thuộc cách xử lý ngoại lệ riêng của API cũ, hãy đọc hướng dẫn chuyển đổi trước.

Đăng ký hàm, rồi gọi `RunAgentAsync` với mục tiêu:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Tìm kiếm thông tin trên web",
        ("query", "Từ khóa tìm kiếm", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Lấy giá cổ phiếu hiện tại",
        ("ticker", "Mã cổ phiếu", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "Giá cổ phiếu hiện tại của 3 công ty AI hàng đầu là bao nhiêu?",
    maxSteps: 10
);

Console.WriteLine(result);
```

Model sẽ gọi hàm khi cần, quan sát kết quả và quyết định bước tiếp theo — cho đến khi trả về câu trả lời văn bản cuối cùng.

## maxSteps

`maxSteps` giới hạn số vòng LLM→gọi hàm. Nếu agent chưa hoàn thành trong giới hạn, `AgentMaxStepsExceededException` được ném:

```csharp
try
{
    string result = await service.RunAgentAsync("Nghiên cứu và tóm tắt...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponse chứa những gì model đã tạo ra đến thời điểm đó
    Console.WriteLine($"Dừng sớm: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Kiểm soát hành vi của mỗi vòng trong agent loop:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// RunAgentAsync dùng DefaultPolicy và tham số maxSteps được chỉ định.
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "Nghiên cứu và tóm tắt...", maxSteps: 15);
```

Policy định sẵn:

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Timeout thấp, ít vòng — tác vụ nhanh
var fastResult = await service.RunAgentAsync(
    "Nghiên cứu và tóm tắt...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Timeout cao hơn, nhiều vòng hơn — nghiên cứu sâu
var complexResult = await service.RunAgentAsync(
    "Nghiên cứu và tóm tắt...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## Ngữ cảnh yêu cầu theo từng lệnh gọi

`RunAgentAsync` và `RunAgentStreamAsync` nhận một `AIRequestContext` tùy chọn để bạn chèn prefix/suffix động cho system message, tài liệu tham chiếu, hoặc thay thế hoàn toàn thông điệp mục tiêu — **giới hạn trong một lần chạy agent**, không làm thay đổi system message của service hay lịch sử hội thoại.

```csharp
string result = await service.RunAgentAsync(
    goal: "Tìm chính sách hoàn tiền và kiểm tra xem đơn hàng #1234 có đủ điều kiện không.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Ngày hôm nay là {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nLuôn trích dẫn mục chính sách bạn đã dùng."
    });
```

Phiên bản streaming nhận tham số tương tự:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Nghiên cứu giá cổ phiếu của 3 công ty AI hàng đầu.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Múi giờ người dùng: {userTz}\n"
    }))
{
    // xử lý nội dung
}
```

`AIRequestContext` được truyền qua `AsyncLocal`, nhưng lịch sử hội thoại và chính sách của dịch vụ không vì thế mà an toàn cho thao tác đồng thời. Hãy dùng các phiên bản dịch vụ riêng cho các tác vụ đồng thời độc lập.

Xem danh sách đầy đủ các thuộc tính trong [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Có sẵn từ Mythosia.AI v6.3.0.

## Cách hoạt động

Mỗi bước:

1. LLM nhận mục tiêu + lịch sử hội thoại + định nghĩa hàm
2. Nếu LLM gọi hàm → thực thi, thêm kết quả vào lịch sử
3. Nếu LLM trả về phản hồi văn bản → kết thúc vòng lặp, trả về phản hồi đó
4. Nếu số bước đạt `maxSteps` → ném `AgentMaxStepsExceededException`
