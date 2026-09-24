# Streaming

> Grok 4.7 là phần bổ sung chưa phát hành; xem [chọn mô hình, suy luận và tốc độ xử lý](providers.md#grok-47).

Để nhận câu trả lời, mức sử dụng và nguồn cùng lúc, dùng bản chụp `AIRunResult` do `await run.Result` trả về. Chuỗi ở `result.Text`; không cần đọc luồng. Đây là thay đổi của Mythosia.AI 8.0.0; kiểu trả về của `GetCompletionAsync` và `StructuredStreamRun<T>.Result` giữ nguyên. [Kết quả Run và chuyển đổi](execution-api-transition.md#run-result).


Để có cấu hình độc lập và tái sử dụng biến thể, dùng [builder yêu cầu](request-building.md). Gọi `CreateRequest(...)` trước `With...`. Thuộc tính và phương thức fluent trên dịch vụ giữ nguyên hành vi.

Hiển thị từng phần văn bản ngay khi nhận được giúp người dùng không phải đợi câu trả lời dài hoàn thành mới đọc. Nếu cần cả nút Dừng và trạng thái công cụ, hãy bắt đầu bằng `StartRunAsync` rồi đọc `run.StreamAsync()`. [Hướng dẫn Run](execution-api-transition.md) có ví dụ về callback và hủy.

```csharp
await using var run = await service.StartRunAsync(
    "Tóm tắt tài liệu.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

StreamAsync nhận đầu vào của dịch vụ/RAG vẫn công khai trong v8. Dùng StartRunAsync cho điều khiển mới; run.StreamAsync() chỉ quan sát run đã tồn tại.

## Streaming cơ bản

Dùng `StreamAsync` để nhận văn bản khi được tạo ra.

```csharp
await foreach (var token in service.StreamAsync("Kể cho tôi một câu chuyện"))
{
    Console.Write(token);
}
```

## Streaming kèm loại nội dung

`StreamAsync` có thể trả về đối tượng `StreamingContent` chứa cả text lẫn loại nội dung:

```csharp
await foreach (var content in service.StreamAsync("Giải thích điện toán lượng tử", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Streaming suy luận

OpenAI, Claude, Gemini, Grok và DeepSeek Flash trả suy luận của nhà cung cấp qua cùng mẫu streaming. Bật suy luận ở dịch vụ hoặc yêu cầu rồi quan sát bằng `StreamOptions.WithReasoning()`:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Giải: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Đang suy nghĩ] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

Gemini 3.7/3.8 Flash dùng các sự kiện streaming và Run hiện có. `StreamingContentType.Reasoning` chứa bản tóm tắt hoặc tiến độ mà nhà cung cấp trả về, không bảo đảm cung cấp toàn bộ suy luận nội bộ. `StreamOptions.WithReasoning()` chọn đầu ra này, còn `WithReasoning(ReasoningLevel...)` của dịch vụ điều chỉnh mức suy luận.

Grok 4.6 cũng có thể cung cấp tóm tắt suy luận tùy chọn qua các sự kiện này. Tùy chọn luồng chọn nội dung hiển thị; `WithReasoning(ReasoningLevel...)` chọn mức suy luận của một tác vụ. Không có tóm tắt không có nghĩa là suy luận đã tắt. Xem [cấu hình Grok](providers.md#xai-xaiservice).

DeepSeek Flash trả `reasoning_content` qua cùng các sự kiện sau khi bật suy luận. `StreamOptions.WithReasoning()` điều khiển quan sát; `WithDeepSeekReasoning(...)` hoặc `WithReasoning(...)` của dịch vụ điều khiển suy luận. Xem [DeepSeek](providers.md#deepseek-deepseekservice).

## Streaming kết hợp Structured Output

Stream text theo thời gian thực và nhận đối tượng đã deserialize khi xong:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Stream token lên UI khi đến
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Lấy kết quả đã parse sau khi streaming hoàn tất
MyDto result = await run.Result;
```

## Thống kê token

Khi streaming hoàn tất, sự kiện `Completion` cuối cùng mang đối tượng `TokenUsage` với thông tin sử dụng chi tiết:

```csharp
await foreach (var content in service.StreamAsync("Giải thích điện toán lượng tử", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nToken đầu vào:  {content.Usage.InputTokens}");
        Console.WriteLine($"Token đầu ra: {content.Usage.OutputTokens}");
        Console.WriteLine($"Tổng token:   {content.Usage.TotalTokens}");
    }
}
```

### Thuộc tính TokenUsage

| Thuộc tính | Mô tả |
|---|---|
| `InputTokens` | Token trong input/prompt |
| `OutputTokens` | Token trong output/completion |
| `TotalTokens` | Input + Output |
| `CachedInputTokens` | Token được phục vụ từ cache (giảm chi phí) |
| `CacheCreationTokens` | Token được ghi vào cache (Anthropic) |
| `ReasoningTokens` | Token dùng cho suy luận nội bộ |
| `CacheHitRatio` | Tỷ lệ cache hit (0.0–1.0) |
| `VisibleOutputTokens` | Token output không kể reasoning |

### Kiểm tra hiệu quả cache

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"Tỷ lệ cache hit: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Input không từ cache: {content.Usage.NonCachedInputTokens}");
}
```

## Preset StreamOptions

`StreamOptions` cung cấp các preset và fluent builder để kiểm soát nội dung stream:

```csharp
// Đầy đủ tính năng — metadata, function call, reasoning
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Tối giản — chỉ text, không metadata
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// Kịch bản function calling
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* xử lý Text, FunctionCall, FunctionResult, Completion */ }
```

Fluent builder để tùy chỉnh:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // thêm chuỗi suy luận
    .WithMetadata()        // thêm thông tin model vào Completion
    .WithFunctionCalls();  // bật function calling trong stream
```

Hãy coi các đoạn đã hiển thị là đầu ra tạm thời cho đến khi `run.Result` thành công. Luồng xử lý streaming dùng chung tương thích với OpenAI và luồng xử lý streaming của DeepSeek từ chối văn bản, suy luận hoặc dữ liệu công cụ mới sau tín hiệu kết thúc rõ ràng, cũng như việc thay đổi lý do kết thúc: `run.Result` ném ngoại lệ, lượt bị lỗi không được lưu vào lịch sử và các công cụ của lượt đó không được thực thi. Cách xử lý lỗi này không hoàn tác các lượt trước hoặc những hành động đã được thực thi bên ngoài. Delta cuối cùng có thể đi kèm sự kiện kết thúc đầu tiên; sự kiện tiếp theo chỉ chứa dữ liệu sử dụng vẫn được chấp nhận.

## Stateless Streaming (StreamOnceAsync)

Stream response mà không ảnh hưởng lịch sử hội thoại — tương đương streaming của `AskOnceAsync`:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Dịch sang tiếng Pháp"))
    Console.Write(chunk);
```

Cũng nhận `Message` cho đầu vào multimodal:

```csharp
var message = MessageBuilder.Create().AddText("Mô tả ảnh này").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## Tóm tắt hội thoại trước khi streaming

Policy tóm tắt tự động không kích hoạt trong lúc streaming. Gọi `ApplySummaryPolicyIfNeededAsync` trước `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Tiếp tục câu chuyện của chúng ta...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [Giữ tác vụ dài tiếp tục chạy / Trích dẫn có thể chỉ đến web hoặc nguồn khác của nhà cung cấp. Vị trí thuộc từng phản hồi/phần nội dung, không phải kết quả Run đã nối. Giữ URL và tiêu đề để hiển thị, kiểm chứng; nguồn trả về không tự xác thực mọi khẳng định.](perplexity.md).
