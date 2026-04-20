# Agent (ReAct Loop)

## Tại sao cần Agent Loop?

Với function calling thông thường, model thực hiện **một** lần gọi hàm mỗi request, bạn thực thi nó và hội thoại tiếp tục. Nhưng nhiều tác vụ thực tế yêu cầu **nhiều bước** mà model phải tự lên kế hoạch và thực hiện:

- "Nghiên cứu 3 công ty AI hàng đầu và so sánh giá cổ phiếu của họ" — cần nhiều lần tìm kiếm web và tra cứu giá
- "Tìm chính sách liên quan, kiểm tra trạng thái đơn hàng, rồi cho tôi biết tôi có đủ điều kiện hoàn tiền không" — cần nối chuỗi các công cụ khác nhau theo thứ tự logic
- Model có thể cần **thử lại hoặc tinh chỉnh** tìm kiếm nếu kết quả đầu tiên chưa đủ

Tự viết vòng lặp điều phối này rất tẻ nhạt và dễ sai. **Agent loop** (pattern ReAct: Reason → Act → Observe → Repeat) xử lý tự động — model tự quyết định bước tiếp theo cho đến khi đạt được câu trả lời cuối cùng.

## Sử dụng cơ bản

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// Hoặc dùng extension method:
service.WithMaxRounds(15).WithTimeout(60);
```

Policy định sẵn:

```csharp
service.WithFastPolicy();    // Timeout thấp, ít vòng — tác vụ nhanh
service.WithComplexPolicy(); // Timeout cao hơn, nhiều vòng hơn — nghiên cứu sâu
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

Context được truyền qua `AsyncLocal`, do đó các lần chạy agent song song trên cùng một instance service sẽ không gây nhiễu lẫn nhau.

Xem danh sách đầy đủ các thuộc tính trong [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Có sẵn từ Mythosia.AI v6.3.0.

## Cách hoạt động

Mỗi bước:

1. LLM nhận mục tiêu + lịch sử hội thoại + định nghĩa hàm
2. Nếu LLM gọi hàm → thực thi, thêm kết quả vào lịch sử
3. Nếu LLM trả về phản hồi văn bản → kết thúc vòng lặp, trả về phản hồi đó
4. Nếu số bước đạt `maxSteps` → ném `AgentMaxStepsExceededException`
