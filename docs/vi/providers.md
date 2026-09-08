# Tính năng đặc thù của từng Provider

## OpenAI (OpenAIService)

> GPT-6 Astra và tính năng gọi công cụ bất đồng bộ được hỗ trợ từ `Mythosia.AI` 7.1.0, với các kiểu dùng chung trong `Mythosia.AI.Abstractions` 3.1.0.

Trong khi chờ truy vấn thời tiết chậm, mô hình vẫn có thể giới thiệu đồ dùng du lịch thông thường không phụ thuộc kết quả thời tiết. Gọi công cụ bất đồng bộ ở cấp mô hình giúp tiếp tục công việc độc lập trong thời gian chờ; quyết định phụ thuộc kết quả vẫn phải đợi kết quả trả về.

Dùng `FunctionDefinition.AllowAsync = true` hoặc `FunctionBuilder.WithAsync()` để cho phép gọi công cụ bất đồng bộ với GPT-6 Astra qua Responses. Mặc định là `false`; mô hình chưa hỗ trợ vẫn chờ kết quả từ cùng handler. Xem ví dụ và vòng đời yêu cầu trong [hướng dẫn gọi hàm](function-calling.md).

Xem [hướng dẫn suy luận và tìm kiếm](reasoning-and-search.md) để đặt mức suy luận giữa các nhà cung cấp và dùng thông tin mới hoặc tài liệu đã lập chỉ mục. Hướng dẫn nêu rõ mô hình hỗ trợ, cách giữ bộ nhớ đệm và giới hạn kết hợp.

### Mức độ suy luận

GPT-6 Astra / GPT-5.x và dòng o3 hỗ trợ kiểm soát mức độ suy luận. Đặt mức để cân bằng giữa tốc độ và độ sâu:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol là mô hình hàng đầu; Terra và Luna là các lựa chọn tiết kiệm hơn.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// Dòng GPT-5.4
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// Dòng GPT-5.2
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

GPT-6 Astra mặc định dùng Responses API; việc gọi hàm cũng yêu cầu API này. `Auto` tương ứng với giá trị mặc định của thư viện, `Medium`; không có `None` hoặc `Minimal`. `AIRequestProfile.DisableReasoning = true` dùng mức `Low` trong chế độ `Standard` và bỏ phần tóm tắt suy luận. Chọn `Gpt6ReasoningMode.Pro` để dùng chế độ Pro với cùng mã mô hình `gpt-6-astra`.

### Text-to-Speech

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Xin chào, thế giới!",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Speech-to-Text (Phiên âm)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "vi"  // tùy chọn, ISO-639-1
);
```

### Tạo hình ảnh

```csharp
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "Thành phố tương lai về đêm",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### Đếm token (API gốc)

`GetInputTokenCountAsync` có trên tất cả provider (xem [Tạo văn bản](completions.md#đếm-token)). Phiên bản Anthropic gọi endpoint `messages/count_tokens` chính thức, trả về **số token chính xác** thay vì ước tính cục bộ:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Prompt của bạn");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Mức độ suy nghĩ

Kiểm soát mức độ suy luận nội bộ của Gemini:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Tùy chọn: Disabled, Low, Medium, High
```

Mức cao hơn tạo ra phản hồi kỹ lưỡng hơn nhưng tăng độ trễ và lượng token.

---

## xAI (XAIService)

### Chế độ suy luận

```csharp
using Mythosia.AI.Models;

service.ReasoningEffort = GrokReasoning.High;
// Tùy chọn: Auto, None, Low, Medium, High (tùy mô hình)
```

---

## Perplexity (PerplexityService)

### Tìm kiếm web kèm trích dẫn

Các model Sonar có thể tìm kiếm web và trả về trích dẫn nguồn cùng với phản hồi:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "Những tiến bộ mới nhất trong năng lượng tổng hợp hạt nhân là gì?",
    domainFilter: new[] { "nature.com", "science.org" },  // tùy chọn
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"Nguồn: {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

Cài package riêng:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

Model có sẵn: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` và các biến thể.

Chọn endpoint tương thích bằng `EndpointPlatform` khi tạo service:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
