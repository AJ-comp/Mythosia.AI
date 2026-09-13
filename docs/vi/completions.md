# Tạo văn bản

Để có cấu hình độc lập và tái sử dụng biến thể, dùng [builder yêu cầu](request-building.md). Gọi `CreateRequest(...)` trước `With...`. Thuộc tính và phương thức fluent trên dịch vụ giữ nguyên hành vi.

<a id="completion-cancellation"></a>

## Hủy câu trả lời không còn cần thiết

Khi người dùng đóng màn hình, nhấn Dừng hoặc hết thời gian chờ của ứng dụng, câu trả lời có thể không còn hữu ích. Truyền `CancellationToken` để dừng giao tiếp và công việc phía máy khách, tránh gọi công cụ và mô hình ở các vòng tiếp theo. `GetCompletionAsync` vẫn phù hợp để nhận câu trả lời hoàn chỉnh; chỉ hủy thì không cần Run.

### Before: bên gọi không truyền tín hiệu hủy

```csharp
string answer = await service.CreateRequest("Tóm tắt tài liệu này.")
    .GetCompletionAsync();
```

### After: hủy theo người dùng hoặc sau 30 giây

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Tóm tắt tài liệu này.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Đã hủy.");
}
```

Giữ nguồn token trong lúc gọi và nối nút Dừng hoặc sự kiện đóng màn hình với `cancellation.Cancel()`. Ví dụ cũng đặt lịch hủy sau 30 giây. Bên gọi nhận `OperationCanceledException` sau khi dọn dẹp. Thời hạn đặt bằng `CancellationTokenSource` cũng là hủy từ bên gọi; `FunctionCallingPolicy.TimeoutSeconds` giữ cách báo lỗi hết thời gian hiện có.

Các overload dịch vụ nhận chuỗi và `Message`, kết quả có kiểu, request builder và `MessageChain.SendAsync` / `SendOnceAsync` đều nhận token. Lệnh gọi cũ bỏ qua token vẫn hoạt động. Các điểm gọi khác:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Tóm tắt tài liệu này.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Trả về tiêu đề và tác giả dưới dạng JSON.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Tóm tắt tài liệu này.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Dịch câu này.")
    .SendOnceAsync(cancellationToken: token);
```

Token đi tới khâu chuẩn bị, gửi và đọc HTTP, công cụ cục bộ có hỗ trợ hủy và các vòng mô hình tiếp theo. Khi phát hiện hủy, bỏ qua công cụ đang chờ và các vòng sau. Việc dọn dẹp giữ từng lệnh gọi đã ghi khớp với kết quả; công cụ đã chạy nhưng bỏ qua token có thể làm chậm bước này. Không hoàn tác hành động đã xong hay xóa lịch sử. Xem [quy tắc công cụ](function-calling.md#tool-execution-contract).

Không bảo đảm nhà cung cấp dừng suy luận hoặc tính phí. OpenAI hướng dẫn ngắt kết nối cho Responses thông thường; Google nêu rõ chỉ hủy phía máy khách và vẫn tính phí phần sử dụng áp dụng. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). Tác vụ nền cần gọi rõ `CancelAsync()`; hủy `WaitForCompletionAsync(cancellationToken: ...)` chỉ dừng chờ. Yêu cầu thông thường không được chuyển thành chạy nền. Xem [Perplexity](perplexity.md).

<a id="completion-cancellation-migration"></a>

Phần bổ sung này thuộc Mythosia.AI 8.0.0. Lệnh gọi bỏ qua token và đối số profile/context theo vị trí vẫn tương thích ở mã nguồn, nhưng bên sử dụng cần biên dịch lại. Triển khai `IAIService` riêng phải thêm `CancellationToken cancellationToken = default` ở cuối cả hai chữ ký và truyền tiếp. Nhà cung cấp kế thừa `AIService` giữ override `GetCompletionAsync(Message)` hiện có và truyền `RequestCancellationToken` được bảo vệ vào tầng giao tiếp. Riêng builder và Run không cần thay đổi giao diện này. Lớp con ghi đè các overload public virtual đã đổi cho phản hồi string/profile/context, hàm hỗ trợ ảnh hoặc `RunAgentAsync` cũng phải thêm và truyền `CancellationToken` mới; chỉ override nhà cung cấp nhận một `Message` giữ chữ ký cũ. Delegate liên kết trực tiếp với chữ ký đã đổi có thể cần lambda tường minh để truyền hoặc bỏ qua token.

## Một lượt

Cách dùng đơn giản nhất — gửi tin nhắn, nhận kết quả:

```csharp
var response = await service.GetCompletionAsync("Thủ đô của Pháp là gì?");
Console.WriteLine(response); // Paris
```

`GetCompletionAsync` vẫn phù hợp khi chỉ cần câu trả lời hoàn chỉnh. Nếu cần hiển thị từng phần, dừng giữa chừng hoặc bổ sung chỉ dẫn, hãy xem [hướng dẫn Run](execution-api-transition.md).

## System Prompt

Đặt system prompt để định hướng vai trò hoặc hành vi của model:

```csharp
service.SystemMessage = "Bạn là trợ lý súc tích. Trả lời trong một câu.";

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
service.ActivateChat.ClearMessages();
```

## Xây dựng tin nhắn thủ công

Dùng `MessageBuilder` để tạo tin nhắn theo cách tường minh:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Tóm tắt đoạn văn này: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Đầu vào hình ảnh)

Các provider hỗ trợ vision có thể nhận hình ảnh kèm văn bản:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("Sơ đồ này mô tả gì?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

Để phân tích biểu đồ, ảnh chụp, gọi hàm cục bộ hoặc rà soát kỹ câu trả lời, dùng [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). Suy luận mặc định tắt; bật bằng `WithDeepSeekReasoning(...)` hoặc `WithReasoning(...)` cho từng yêu cầu.

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
    model: AIModels.OpenAI.Gpt4_1
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

Perplexity: [Trả lời bằng preset Agent / Nguồn, ảnh và câu trả lời có cấu trúc](perplexity.md).
