# Tạo văn bản

## Một lượt

Cách dùng đơn giản nhất — gửi tin nhắn, nhận kết quả:

```csharp
var response = await service.GetCompletionAsync("Thủ đô của Pháp là gì?");
Console.WriteLine(response); // Paris
```

## System Prompt

Đặt system prompt để định hướng vai trò hoặc hành vi của model:

```csharp
service.SystemPrompt = "Bạn là trợ lý súc tích. Trả lời trong một câu.";

var response = await service.GetCompletionAsync("Giải thích đệ quy.");
```

## Hội thoại nhiều lượt

Tin nhắn được tích lũy tự động. Mỗi lần gọi `GetCompletionAsync` sẽ thêm vào lịch sử hội thoại:

```csharp
await service.GetCompletionAsync("Tôi tên là Alice.");
var response = await service.GetCompletionAsync("Tên tôi là gì?");
// → "Tên bạn là Alice."
```

Để xóa lịch sử hội thoại:

```csharp
service.ClearMessages();
```

## Xây dựng tin nhắn thủ công

Dùng `MessageBuilder` để tạo tin nhắn theo cách tường minh:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("Tóm tắt đoạn văn này: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Đầu vào hình ảnh)

Các provider hỗ trợ vision có thể nhận hình ảnh kèm văn bản:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("Sơ đồ này mô tả gì?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Quick Ask (API tĩnh)

Dành cho truy vấn nhanh không cần tạo service instance, dùng `QuickAskAsync`. Provider được tự động nhận diện từ tên model:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "Thủ đô của Pháp là gì?",
    model: AIModels.OpenAI.Gpt4oMini  // mặc định
);
```

Phiên bản có hình ảnh:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Mô tả hình ảnh này",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4Vision
);
```

## Phương thức tiện ích cho hình ảnh

Phân tích hình ảnh mà không cần `MessageBuilder` — service tự đọc file và xác định MIME type:

```csharp
// Từ đường dẫn file
var response = await service.GetCompletionWithImageAsync(
    "Sơ đồ này mô tả gì?", "diagram.png");

// Từ URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Mô tả ảnh này", "https://example.com/photo.jpg");
```

## Thử lại tin nhắn cuối

Xóa phản hồi cuối của assistant và gửi lại tin nhắn cuối của user:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Hữu ích khi phản hồi trước chưa như ý và bạn muốn model thử lại.

## Đếm token

Ước tính lượng token trước khi gửi request. Khả dụng trên **tất cả provider**:

```csharp
// Đếm token cho lịch sử hội thoại hiện tại
uint conversationTokens = await service.GetInputTokenCountAsync();

// Đếm token cho một prompt cụ thể
uint promptTokens = await service.GetInputTokenCountAsync("Prompt của bạn");
```

OpenAI và hầu hết provider dùng ước tính cục bộ dựa trên TikToken. Anthropic và Google gọi API đếm token gốc để có kết quả chính xác.

## Fluent Message Chain

`BeginMessage()` cung cấp API fluent để xây dựng và gửi tin nhắn trong một chuỗi — bao gồm text, hình ảnh, streaming và cấu hình policy:

```csharp
// Text + hình ảnh đơn giản → gửi
string response = await service.BeginMessage()
    .AddText("Sơ đồ này mô tả gì?")
    .AddImage("diagram.png")
    .SendAsync();

// Truy vấn một lần (không lưu lịch sử)
string answer = await service.BeginMessage()
    .AddText("Dịch sang tiếng Hàn")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("Viết một bài thơ về mùa xuân")
    .StreamAsync(chunk => Console.Write(chunk));

// Với timeout và policy tùy chỉnh
string result = await service.BeginMessage()
    .AddText("Phân tích hình ảnh này")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` cũng hỗ trợ `IAsyncEnumerable`:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("Kể cho tôi một câu chuyện").StreamAsync())
    Console.Write(chunk);
```

## Kiểm soát độ dài output và nhiệt độ

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // thấp hơn = xác định hơn
```
