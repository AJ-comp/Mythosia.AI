# Hướng dẫn nhanh

## Cài đặt

Cài package cốt lõi:

```bash
dotnet add package Mythosia.AI
```

Nếu bạn dùng streaming với các LINQ operator (ví dụ `ToListAsync`), cài thêm:

```bash
dotnet add package System.Linq.Async
```

## Completion đầu tiên

Chọn provider và tạo instance service với API key cùng `HttpClient`:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

Sau đó gọi `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("Xin chào!");
Console.WriteLine(response);
```

## Chọn model

Mỗi service mặc định dùng một model phù hợp, nhưng bạn có thể chỉ định rõ:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

Xem [API Reference](../api/Mythosia.AI.Models.AIModels.yml) để biết toàn bộ hằng số model.

## Bước tiếp theo

- [Completions cơ bản](completions.md) — system prompt, lịch sử hội thoại, multimodal
- [Streaming](streaming.md) — nhận output từng token và streaming suy luận
- [Function Calling](function-calling.md) — để model gọi code của bạn
- [Structured Output](structured-output.md) — deserialize response thành kiểu C#
