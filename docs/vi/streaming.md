# Streaming

## Streaming cơ bản

Dùng `StreamAsync` để nhận token khi chúng được sinh ra:

```csharp
await foreach (var token in service.StreamAsync("Kể cho tôi một câu chuyện"))
{
    Console.Write(token);
}
```

## Streaming kèm loại nội dung

`StreamAsync` có thể trả về đối tượng `StreamingContent` chứa cả text lẫn loại nội dung:

```csharp
await foreach (var content in service.StreamAsync("Giải thích điện toán lượng tử"))
{
    Console.Write(content.Content);
}
```

## Streaming suy luận

Tất cả provider hỗ trợ suy luận (OpenAI, Claude, Gemini, Grok, DeepSeek) dùng cùng một pattern. Truyền `StreamOptions` với reasoning được bật:

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

`StreamingContentType.Reasoning` mang chuỗi suy luận nội bộ của model, còn `StreamingContentType.Text` mang câu trả lời cuối cùng.

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
await foreach (var content in service.StreamAsync("Giải thích điện toán lượng tử"))
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

await foreach (var chunk in service.StreamAsync("Tiếp tục câu chuyện của chúng ta..."))
    Console.Write(chunk.Content);
```
