# Tính năng đặc thù của từng Provider

> Ví dụ `CreateRequest` cần Mythosia.AI 8.0.0 / Abstractions 4.0.0. Bản 7.1 trước đây giới thiệu Run và tùy chọn chung chưa có builder. Gói cũ có thể tiếp tục dùng các overload của dịch vụ.

<a id="image-options-migration"></a>
Chế độ và siêu dữ liệu trả về phụ thuộc nhà cung cấp, mô hình và API. Xem [tùy chọn tốc độ chung](request-building.md#inference-speed) và capabilities; yêu cầu Fast không chứng minh đã được xử lý Fast.

## Chuyển sang tùy chọn ảnh có kiểu

Chọn chất lượng và định dạng qua enum và tự động hoàn thành, đồng thời phân biệt pixel chính xác với mức độ phân giải. Cách này tránh lỗi gõ chuỗi và chuyển ngầm kích thước sang mức khác.

Thay đổi không tương thích cho Mythosia.AI 8.0.0: `Quality`, `Background`, `OutputFormat` là enum; `Size` là `ImageSize`; thuộc tính `AspectRatio` riêng của yêu cầu bị bỏ. Mặc định `OutputFormat` nay là `ImageOutputFormat.Auto`. Các phương thức tạo và chỉnh sửa không đổi.

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)` yêu cầu kích thước chính xác. `Preset(resolution, aspectRatio)` chỉ định mức độ phân giải và tỷ lệ; nhà cung cấp quyết định pixel thực. Dùng `ImageSize.Auto` khi không cần ràng buộc kích thước. Chỉ đổi yêu cầu pixel sang preset nếu ứng dụng chấp nhận kích thước gần đúng.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Enum chưa định nghĩa và tổ hợp không được mô hình hỗ trợ bị từ chối trước HTTP. Không phải mô hình nào cũng hỗ trợ mọi giá trị. Google chỉ hỗ trợ `ImageQuality.Auto`; xAI hỗ trợ `Auto`, `Low`, `Medium`.

Bạn có thể tái sử dụng bộ đệm đầu vào sau khi `EditImagesAsync` trả về `Task`. Yêu cầu đã bắt đầu giữ dữ liệu ảnh riêng, bao gồm các byte mặt nạ của OpenAI; thay đổi các mảng `ImageInput.Data` gốc sau đó không làm thay đổi nội dung tải lên.

Để tránh lưu đầu ra bị gián đoạn như ảnh hoàn chỉnh, việc tạo và chỉnh sửa ảnh của Google yêu cầu mọi kết quả ứng viên trả về đều kết thúc bằng `finishReason: STOP`. Nếu bất kỳ ứng viên nào bị chặn, chưa hoàn tất hoặc thiếu trạng thái kết thúc này, toàn bộ lời gọi sẽ ném `AIServiceException`. Dữ liệu base64 nội tuyến hoặc thông tin MIME ảnh bị thiếu hay không hợp lệ cũng khiến toàn bộ lời gọi thất bại; thư viện không mặc định đoán là PNG. Các kiểm tra này không xác minh rằng byte của tệp khớp với định dạng ảnh đã khai báo.

## OpenAI (OpenAIService)

> GPT-6 Astra và tính năng gọi công cụ bất đồng bộ được hỗ trợ từ `Mythosia.AI` 7.1.0, với các kiểu dùng chung trong `Mythosia.AI.Abstractions` 3.1.0.

Trong khi chờ truy vấn thời tiết chậm, mô hình vẫn có thể giới thiệu đồ dùng du lịch thông thường không phụ thuộc kết quả thời tiết. Gọi công cụ bất đồng bộ ở cấp mô hình giúp tiếp tục công việc độc lập trong thời gian chờ; quyết định phụ thuộc kết quả vẫn phải đợi kết quả trả về.

Dùng `FunctionDefinition.AllowAsync = true` hoặc `FunctionBuilder.WithAsync()` để cho phép gọi công cụ bất đồng bộ với GPT-6 Astra / Sol / Luna qua Responses. Mặc định là `false`; mô hình chưa hỗ trợ vẫn chờ kết quả từ cùng handler. Xem ví dụ và vòng đời yêu cầu trong [hướng dẫn gọi hàm](function-calling.md).

Xem [hướng dẫn suy luận và tìm kiếm](reasoning-and-search.md) để đặt mức suy luận giữa các nhà cung cấp và dùng thông tin mới hoặc tài liệu đã lập chỉ mục. Hướng dẫn nêu rõ mô hình hỗ trợ, cách giữ bộ nhớ đệm và giới hạn kết hợp.

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna (chưa phát hành)

Chọn GPT-6 Sol cho lập trình phức tạp, sử dụng công cụ và tác vụ tác nhân; chọn Luna khi cần xử lý lượng lớn văn bản hoặc ảnh với chi phí thấp. Cả hai dùng API trả lời đầy đủ, streaming và Run hiện có nên đổi mô hình không làm thay đổi luồng gọi của ứng dụng.

> Đây là phần bổ sung chưa phát hành, cần các bản dựng core và abstractions tương thích. Mythosia.AI 8.0.0 / Abstractions 4.0.0 đã phát hành không chứa `Gpt6Sol`, `Gpt6Luna` hay `Gpt6Reasoning.None`. Phiên bản tối thiểu của tính năng Astra hiện có và mô hình mặc định của dịch vụ không đổi.

Dùng `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) hoặc `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`). Cả hai nhận văn bản, ảnh và trả văn bản: ngữ cảnh 1.050.000 token, đầu vào tối đa 922.000 và đầu ra tối đa 128.000. Tổng đầu vào, suy luận và đầu ra vẫn phải nằm trong giới hạn ngữ cảnh. `MaxTokens` đặt ngân sách đầu ra được yêu cầu, không phải kích thước ngữ cảnh.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto` tương ứng với `Medium`. Sol/Luna hỗ trợ `None`, `Low`, `Medium`, `High`, `XHigh`, `Max`, không hỗ trợ `Minimal`. Dùng `WithReasoning(ReasoningLevel.None)` theo yêu cầu hoặc `Gpt6Reasoning.None` trong `WithGpt6Parameters`. Chỉ Sol/Luna với `None` mới gửi `Temperature` / `TopP`; khi bật suy luận hai trường bị bỏ qua. Astra luôn cần suy luận và bỏ các tham số lấy mẫu. `AIRequestProfile.DisableReasoning` chọn `None` cho Sol/Luna và `Low` ở chế độ Standard cho Astra, đồng thời bỏ tóm tắt suy luận.

`Gpt6ReasoningMode.Standard` và `.Pro` giữ nguyên ID mô hình đã chọn. Cả ba GPT-6 hỗ trợ công cụ qua Responses, công cụ bất đồng bộ tùy chọn, chỉ dẫn bổ sung qua WebSocket Run và đổi suy luận giữ bộ nhớ đệm ở chế độ Standard một tác nhân. Kiểm tra `run.CanSteer`; việc nhận chỉ dẫn không thu hồi đầu ra trước đó. `WithSpeed(InferenceSpeed.Fast)` yêu cầu xử lý Fast trả phí độc lập với mức suy luận. Xem chế độ thực tế trong `result.Processing`; quyền tài khoản và việc máy chủ hạ cấp không được bảo đảm bởi khả năng cục bộ.

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### Mức độ suy luận

GPT-6 Astra / Sol / Luna và GPT-5.1–5.6 cho phép điều chỉnh mức suy luận để cân bằng tốc độ và độ sâu của câu trả lời:

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

`TranscribeAudioAsync` dùng `gpt-transcribe`; chữ ký công khai không thay đổi.

### Tạo hình ảnh

#### GPT Image 2.5

Chọn Flare để tạo bản phác thảo trực quan nhanh, hoặc Sunburst khi bản sửa cần tuân thủ chính xác hướng dẫn chỉnh sửa chi tiết. Cả hai tạo và chỉnh sửa ảnh qua `IImageGenerationService` hiện có; chọn mô hình ảnh không làm đổi mô hình chat.

| Mô hình | Khi nên chọn |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Tạo ảnh hằng ngày nhanh và có chất lượng cao. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Tạo và chỉnh sửa ảnh khi độ chính xác của thay đổi là ưu tiên. |

Đặt rõ `ImageGenerationRequest.Model`, hoặc thuộc tính kế thừa trên `ImageEditRequest`. Mặc định OpenAI vẫn là `AIModels.OpenAI.GptImage2`. Bí danh là `gpt-image-2.5-flare` và `gpt-image-2.5-sunburst`; để cố định bản ngày 8 tháng 9 năm 2026, dùng `GptImage2_5Flare_260908` hoặc `GptImage2_5Sunburst_260908` (ID tương ứng kết thúc bằng `-2026-09-08`).

Tạo bản phác thảo bằng Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "Một nhà nghỉ bằng kính lúc bình minh, tranh ý tưởng kiến trúc",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Sau đó chỉnh sửa ảnh vừa tạo bằng Sunburst:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Giữ thiết kế nhà nghỉ, xóa cảnh xung quanh và chuyển nền thành trong suốt.",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

Với hai mô hình và các bản cố định này, `Quality` chấp nhận `Auto`, `Low`, `Medium`, `High`, `XHigh`, `Max`. Dùng chất lượng thấp cho bản nháp và so sánh mức cao hơn cho sản phẩm cuối. `OutputFormat` nhận `Auto` / `Png`, `Jpeg`, `WebP`; `OutputCompression` từ 0–100 chỉ dành cho JPEG/WebP. Nền `Transparent` cần PNG/WebP. `Count` từ 1–10.

`Size` dùng `ImageSize.Auto` hoặc `ImageSize.Pixels(width, height)`: hai cạnh là bội số của 16, tỷ lệ 1:3–3:1, mỗi cạnh tối đa 3840 và diện tích 655360–8294400 pixel. Kích thước trên 2560×1440 là thử nghiệm. OpenAI từ chối `Preset`.

Chỉnh sửa nhận 1–16 ảnh tham chiếu JPEG/PNG/WebP không rỗng, mỗi ảnh dưới 50 MiB. Mặt nạ tùy chọn phải là PNG/WebP dưới 50 MiB, cùng định dạng và kích thước pixel với ảnh tham chiếu đầu tiên, có kênh alpha. Thư viện kiểm tra MIME và độ dài byte; nhà cung cấp kiểm tra kích thước pixel và alpha.

Các ví dụ dùng đường dẫn Image API hiện có: trả byte và chỉnh sửa multipart. Tích hợp này không cung cấp công cụ Responses `image_generation`, luồng ảnh từng phần hay `input_fidelity`. Đọc `GeneratedImage.Data` và `MediaType` trong kết quả.

Xem [hướng dẫn ảnh chính thức](https://developers.openai.com/api/docs/guides/image-generation), trang [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) và [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare).

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) bổ sung cập nhật tiến độ, chỉ dẫn theo lượt và chẩn đoán liên kết thinking từ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 cần lời mời truy cập. Cả hai đều từ chối ép chọn công cụ.

<a id="claude-opus-55"></a>

### Claude Opus 5.5: hiển thị tiến độ tác vụ công cụ kéo dài

Dùng Opus 5.5 để rà soát mã hoặc nghiên cứu tài liệu cần nhiều vòng gọi công cụ. API completion và Run vẫn giữ nguyên, nhưng tiến độ bị ẩn mặc định và việc giữ suy luận đòi hỏi cẩn thận khi sửa lịch sử. Đây là phần bổ sung chưa phát hành, không có trong các gói 8.0.0 / 4.0.0 đã công bố.

`ClaudeOpus5_5` chọn `claude-opus-5-5`: nhận văn bản/hình ảnh, trả văn bản, ngữ cảnh 1M và đầu ra tối đa 128K token. Theo kiểm tra ngày 2026-09-24, giá đầu vào/đầu ra chuẩn là $4/$20 mỗi triệu token; chế độ đặc biệt và công cụ có giá riêng. [Thông tin mô hình chính thức](https://platform.claude.com/docs/en/models/opus-5-5/overview).

Khi chưa đổi thiết lập dịch vụ, `Auto` dùng mức `Medium` và bỏ phần suy luận đọc được. Suy luận thích ứng luôn bật. Có thể chọn `Low`, `Medium`, `High`, `XHigh` hoặc `Max`; các giá trị chung `ReasoningLevel.None` và `Minimal` bị từ chối. Mô hình mặc định của dịch vụ không đổi.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

Ví dụ yêu cầu `Updates` và theo dõi `StreamingContentType.Reasoning`. Dùng `Summarized` để đọc tóm tắt suy luận hoặc `Omitted` để ẩn. Tham số hiển thị của `WithAdaptiveThinkingParameters(effort)` vẫn mặc định là `Summarized`, khác với dịch vụ chưa cấu hình. Với completion thông thường, đọc `LastThinkingContent` sau khi gọi. Không bảo đảm tiến độ xuất hiện theo chu kỳ cố định.

`ThinkingBudget` dương kiểu cũ được ánh xạ thành high/xhigh/max, không phải ngân sách token chính xác. Giá trị 0 hoặc âm không tắt được suy luận. Profile tắt suy luận dùng mức low và ẩn phần đọc được. `MaxTokens` gồm cả suy luận ẩn và câu trả lời; hãy đánh giá lại giới hạn đầu ra và chi phí khi chuyển đổi.

Mythosia giữ các khối thinking có chữ ký, kể cả khối rỗng, giữa các lượt hội thoại và kết quả công cụ. Tiếp tục dùng cùng dịch vụ và chat; không viết lại thông điệp cũ, system hay tools nếu muốn giữ suy luận. Có thể dùng `WithTurnInstruction`, `WithConversationInstruction` và `CachePreservation.Required`. `WithThinkingBinding` chọn `Error` hoặc `DropBlock`; `LastInputTransformations` cho biết các lần loại bỏ được báo cáo. Drop là loại bỏ suy luận. [Hướng dẫn lịch sử](fable-5-1.md) giải thích các điều khiển chung; mặc định và khả năng tương thích theo quy tắc của Opus 5.5.

Không đặt `ForceFunctionName`; hỗ trợ chọn công cụ thông thường và `FunctionsDisabled`. Prefill assistant bị từ chối và tham số sampling không được gửi. Opus 5.5 không đọc thinking của Fable/Mythos; trên Claude API, Fable 5.1 và Mythos 5.1 đọc được thinking của Opus 5.5. Đổi mô hình có thể làm mất suy luận trước đó. Phần bổ sung này không cung cấp computer toolset gốc, task budget, thay đổi công cụ trong hội thoại, nén phía máy chủ hay fallback máy chủ tự động. [Yêu cầu chuyển đổi](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [Phạm vi tính năng gốc](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

Với Opus 5.5, sửa trực tiếp nội dung phản hồi assistant đã lưu gây `InvalidOperationException` trước yêu cầu HTTP; `DropBlock` cũng không cho phép viết lại phản hồi có chữ ký. Hãy gửi phần sửa dưới dạng đầu vào người dùng mới hoặc bắt đầu hội thoại mới. Việc sửa nội dung user/system trước đó tuân theo chính sách gắn tiền tố của nhà cung cấp.

Opus 5.5 fast mode dùng được qua [WithSpeed](request-building.md#inference-speed) trên Claude API trực tiếp khi có quyền. Mức suy luận được giữ nguyên và chế độ có phí cao hơn được yêu cầu.

### Đếm token (API gốc)

`GetInputTokenCountAsync` có trên tất cả provider (xem [Tạo văn bản](completions.md#đếm-token)). Phiên bản Anthropic gọi endpoint `messages/count_tokens` chính thức, trả về **số token chính xác** thay vì ước tính cục bộ:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Prompt của bạn");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

Để đánh giá tài liệu dài hoặc xử lý nhiều vòng gọi công cụ, bạn có thể chọn Gemini 3.7 Flash hay 3.8 Flash qua adapter Google hiện có. Hỗ trợ bắt đầu từ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; mô hình mặc định vẫn là Gemini 3.6 Flash.

### Mức độ suy nghĩ

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("So sánh triển khai cuốn chiếu và blue-green, bao gồm rủi ro khi hoàn tác.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Dùng `Low` cho lượt xem xét nhẹ và `High` cho phân tích khó; tăng suy luận có thể tăng độ trễ và số token. Cả hai hỗ trợ `Low`, `Medium`, `High`, nhưng không hỗ trợ `Minimal` hay `None`. `GeminiThinkingLevel.Auto` bỏ qua giá trị ghi đè; mặc định của nhà cung cấp cho 3.8 là `Medium`. `ThinkingLevel` đặt cấu hình nền của dịch vụ, còn `WithReasoning(...)` ghi đè cho một yêu cầu logic. Adapter không gửi `temperature`, `topP`, `topK`. Giới hạn của nhà cung cấp là 1.048.576 token đầu vào và 65.536 token đầu ra. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### Độ phân giải và tỷ lệ theo mô hình ảnh Google

| Mô hình | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

10 tỷ lệ chuẩn là `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9`. Bộ 14 tỷ lệ bổ sung `1:4`, `4:1`, `1:8`, `8:1`. Mọi mô hình cũng cho phép `ImageAspectRatio.Auto`.

Dùng `ImageSize.Auto` hoặc `ImageSize.Preset(resolution, aspectRatio)`. `Auto` bỏ qua trường lựa chọn tương ứng. `GetImageCapabilities(model)` và `GenerateImagesAsync` / `EditImagesAsync` dùng cùng các lựa chọn theo mô hình. Giá trị chỉ định không được hỗ trợ gây `NotSupportedException` trước HTTP; không đổi kích thước hay gửi yêu cầu thay thế. ID mô hình tùy chỉnh chưa rõ giữ `Unknown` và được chuyển nguyên trạng sau khi kiểm tra các tùy chọn chung của nhà cung cấp.

Với Flash-Lite, [trang mô hình](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image) và phần văn bản của hướng dẫn nêu 1K, nhưng [bảng](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size) cũng có cột 512. Trước khi xác minh khác biệt này, thư viện thận trọng chỉ cho phép 1K; điều này không khẳng định đã quan sát máy chủ từ chối 512.

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

Để tạo bản nháp nhanh rồi kiểm tra kỹ mã hoặc tài liệu, chọn Grok 4.7 và điều chỉnh mức suy luận theo từng yêu cầu. Các API trả lời, streaming, Run, công cụ cục bộ, đầu ra có cấu trúc và đầu vào hình ảnh vẫn được dùng như trước. `grok-4.7` nhận văn bản/hình ảnh và trả văn bản, với cửa sổ ngữ cảnh 500.000 token. Tích hợp này cần các bản dựng core và abstractions chưa phát hành tương ứng; gói 8.0.0 / 4.0.0 đã công bố không chứa nó. Mô hình mặc định vẫn là Grok 4.5.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

Hỗ trợ `Low`, `Medium`, `High` và `XHigh`. `GrokReasoning.Auto` gốc bỏ qua `reasoning_effort`, dùng mặc định `High` của nhà cung cấp; `ReasoningLevel.Auto` chung cũng bỏ qua trường này và dùng mặc định `High` của nhà cung cấp cho yêu cầu đó. `None`, `Minimal` và `Max` bị từ chối trước khi gửi. `WithReasoning(...)` áp dụng cho yêu cầu logic, gồm các vòng công cụ và sửa đầu ra có cấu trúc; `WithGrokReasoning(...)` đặt cấu hình cơ sở. Hồ sơ nội bộ `DisableReasoning` dùng `Low`. Bản tóm tắt suy luận tùy chọn không phải toàn bộ suy luận nội bộ.

`WithSpeed(InferenceSpeed.Standard)` gửi `service_tier: "default"`; `Fast` gửi `"priority"` tới endpoint xAI được hỗ trợ và có thể tốn phí hơn. `ProviderDefault` không ghi đè. Máy chủ có thể hạ xuống xử lý thông thường; đọc cấp được báo cáo trong `result.Processing`. Đây là xử lý ưu tiên của `grok-4.7`, không phải biến thể “Grok 4.7 Fast” dành riêng cho Cursor/Grok Build; biến thể đó không có ID mô hình API công khai.

`GetCapabilities()` mô tả yêu cầu đã chọn tại chỗ, không kiểm tra quyền tài khoản. Tích hợp này dùng Chat Completions. Suy luận mã hóa riêng của Responses, tìm kiếm Web/X lưu trữ, công cụ bất đồng bộ gốc, cập nhật giữ bộ nhớ đệm và `SteerAsync` chưa được kết nối trong lần này. Hàm phía máy khách dùng vòng công cụ cục bộ hiện có; `run.CanSteer` là false.

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### Chọn mức suy luận phù hợp với tác vụ

Dùng mức thấp hơn để có bản nháp nhanh, rồi dành thêm suy luận cho các bước kiểm tra khó khi chất lượng quan trọng hơn thời gian trả lời. Chọn Grok 4.6 một cách tường minh để dùng mức bổ sung `XHigh`.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("So sánh triển khai cuốn chiếu và xanh-lục, gồm cả các bước khôi phục khi có lỗi.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 hỗ trợ `Low`, `Medium`, `High` và `XHigh` (`GrokReasoning.XHigh`). `Auto` bỏ qua `reasoning_effort`, giữ mặc định `High` của nhà cung cấp; `None` không thể tắt suy luận. Mức cao hơn có thể tăng độ trễ và lượng token. Để giữ tương thích, `XAIService` vẫn mặc định là Grok 4.5. Bản 4.5 hỗ trợ từ `Low` đến `High`, bản 4.3 từ `None` đến `High`; adapter từ chối `XHigh` trên các mô hình cũ này trước khi gửi.

`WithGrokReasoning(...)` và `WithGrokParameters(...)` hiện có đặt cấu hình nền của dịch vụ. Với Grok 4.6, `WithReasoning(...)` chung ghi đè cho một yêu cầu logic, gồm cả vòng công cụ và sửa đầu ra có cấu trúc, rồi khôi phục cấu hình nền. Các hồ sơ nội bộ `DisableReasoning` dùng `Low` cho mô hình luôn suy luận này. Những tùy chọn chung chưa tích hợp cập nhật giữ bộ nhớ đệm hay tìm kiếm web/tệp do xAI lưu trữ.

Grok 4.6 có thể trả về bản tóm tắt suy luận của nhà cung cấp dưới dạng `StreamingContentType.Reasoning` khi bật quan sát bằng `new StreamOptions().WithReasoning()`. Tóm tắt là tùy chọn, không phải toàn bộ suy luận nội bộ. Run dùng được cùng các tùy chọn; thay đổi hiển thị luồng không đổi mức suy luận được yêu cầu.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Dùng tính năng tạo ảnh để biến mô tả sản phẩm thành bản phác thảo trực quan, hoặc chỉnh sửa để ghép chủ thể và bối cảnh từ các ảnh tham chiếu. `XAIService` hỗ trợ cả hai qua cùng `IImageGenerationService` như OpenAI và Google, từ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

Mô hình ảnh mặc định độc lập là `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). Yêu cầu ảnh không thay đổi mô hình chat hoặc thêm vào lịch sử hội thoại.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "Một gian nhà bằng kính lúc bình minh, bố cục rộng",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

Khi chỉnh sửa, truyền byte của các ảnh theo thứ tự được nhắc trong lời nhắc:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Đặt chủ thể trong ảnh 1 vào bối cảnh của ảnh 2.",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

Mỗi `GeneratedImage.Data` chứa byte ảnh đã giải mã; chọn phần mở rộng tệp theo `MediaType`. Bộ điều hợp yêu cầu đầu ra base64 nhúng và không tải ảnh từ URL do nhà cung cấp lưu trữ. `Count` cho phép 1–10 ảnh đầu ra; chỉnh sửa nhận 1–5 ảnh tham chiếu JPEG, PNG hoặc WebP.

Với xAI, dùng `ImageSize.Auto` hoặc `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`. Các mức là `Auto`, `OneK`, `TwoK`; tỷ lệ phải được mô hình hỗ trợ. `Pixels(...)` bị từ chối vì không thể yêu cầu kích thước chính xác.

xAI chỉ hỗ trợ mặc định chung mới `ImageOutputFormat.Auto`. Không có bộ chọn codec nên `Jpeg`, `Png`, `WebP` chỉ định rõ bị từ chối trước khi gửi. Chọn phần mở rộng theo `GeneratedImage.MediaType`; thư viện không chuyển mã. Chất lượng: `ImageQuality.Auto`, `Low`, `Medium`; nền: chỉ `ImageBackground.Auto`. Không hỗ trợ nén chỉ định hay `Mask` riêng.

Google nhận `ImageSize.Auto` hoặc `Preset` với độ phân giải và tỷ lệ theo mô hình; xem [Tùy chọn ảnh theo mô hình Google](#google-image-options). Định dạng: `ImageOutputFormat.Auto` hoặc `Jpeg`; từ chối `Png`/`WebP`. Google và xAI từ chối `Pixels`; OpenAI nhận `Auto`/`Pixels` và từ chối `Preset`. Xem [ví dụ chuyển đổi](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Dùng DeepSeek Flash để có câu trả lời nhanh rồi rà soát kỹ hơn, hoặc giải thích biểu đồ và ảnh chụp màn hình. `AIModels.DeepSeek.Flash` (`deepseek-flash`) chọn V4.1 Flash ra mắt ngày 10/9/2026 với thị giác tích hợp. Tiếp tục dùng API completion, streaming, Run, gọi hàm và RAG, từ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

> Các gói đã phát hành `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 đã hỗ trợ Flash cơ bản. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, API Files và `DeepSeekImageFileContent` là các bổ sung trong mã nguồn chưa phát hành, cần bản dựng core và abstractions tương thích từ mã nguồn; chúng không có trong các gói đã phát hành này. [Ghi chú thay đổi chưa phát hành](../../src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased).

Chọn `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813) cho tác vụ chỉ có văn bản. Flash vẫn là mặc định và hỗ trợ ảnh; cả hai có suy luận Low/High/Max và cùng giới hạn đầu ra. Đặt `UseResponsesApi = true` trước khi tạo yêu cầu để dùng Responses với các API completion, streaming, Run và hàm cục bộ hiện có. Mặc định vẫn là `false` để giữ Chat Completions cho ứng dụng hiện tại; lựa chọn được giữ suốt yêu cầu và các vòng công cụ. Responses gửi lại toàn bộ hội thoại và suy luận gốc thay vì dựa vào ID phản hồi lưu trên máy chủ.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

Tải ảnh lên một lần để tái sử dụng trong nhiều câu hỏi hoặc hội thoại. `UploadFileAsync` nhận đường dẫn hoặc stream do bên gọi sở hữu cùng tên tệp; purpose là `user_data`. JPEG, PNG, GIF và WebP giới hạn 64 MiB. `DeepSeekImageFileContent` tham chiếu ảnh trên Flash bằng cả hai cơ chế truyền; không phải đầu vào PDF/tài liệu và V4 Pro từ chối. Bỏ thời hạn sẽ lưu vĩnh viễn; `expiresAfterSeconds` nhận 3600–2592000 giây. Giữ tệp đến khi mọi hội thoại tham chiếu kết thúc.

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync` đọc metadata, `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` đọc một trang, `DeleteFileAsync` xóa tệp. Khi `HasMore` là true, dùng `LastId` làm `After` tiếp theo; cũng hỗ trợ `Descending`. Không có endpoint tải nội dung tệp được công bố. Chat UI cung cấp Flash và V4 Pro, dùng danh mục hiện tại cho viết lại truy vấn; giá trị cũ `DeepSeekChat` chuyển sang Flash còn ID tùy ý được giữ nguyên.

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("So sánh triển khai cuốn chiếu và blue-green, bao gồm rủi ro hoàn tác.");

await using var run = await deepseek
    .CreateRequest("Kiểm tra các giả định trong phần so sánh đó.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

`ThinkingEnabled` vẫn mặc định là `false`. `WithDeepSeekReasoning(...)` bật suy luận và đặt `ReasoningEffort` lâu dài (`Auto`, `Low`, `High`, `Max`); `Auto` này bỏ effort để dùng mặc định nhà cung cấp `High`. `WithReasoning(...)` chung chỉ áp dụng cho một yêu cầu logic cùng các vòng công cụ: `None` tắt, `Minimal`/`Low` → `Low`, `Medium`/`High`/`XHigh` → `High`, `Max` → `Max`. `Auto` chung giữ cấu hình hiện tại. Tăng suy luận có thể tăng độ trễ và token. Chỉ đổi `ReasoningEffort` không bật suy luận.

Đăng ký hàm cục bộ bằng `WithFunction(...)` để truy vấn dữ liệu hoặc hành động qua mã của bạn. Công cụ hoạt động khi bật hoặc tắt suy luận. Chat Completions từ chối chọn công cụ bắt buộc/cưỡng chế khi suy luận; hãy dùng lựa chọn tự động với cơ chế này. Khi `UseResponsesApi = true`, có thể chỉ định hàm bằng `ForceFunctionName` ngay cả khi suy luận; bộ điều hợp đặt `type` và `name` trực tiếp trong `tool_choice` của Responses. Điều này không bật công cụ bất đồng bộ gốc. Bộ điều hợp giữ `reasoning_content` và ID gọi cho các vòng sau. Run và streaming cung cấp `StreamingContentType.Reasoning` khi bật `StreamOptions.WithReasoning()`; tùy chọn quan sát không tự bật suy luận. Thống kê gồm cache và suy luận nếu nhà cung cấp báo cáo. Khôi phục ngữ cảnh tự động dùng vòng streaming chung. Khi công cụ cần lịch sử suy luận gốc trước đó, việc nén tự động bị chặn để giữ lịch sử và lỗi vượt ngữ cảnh vẫn được trả về.

Gửi biểu đồ hoặc ảnh chụp qua các kiểu thông điệp hiện có:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Giải thích xu hướng biểu đồ và xác định các nhãn chưa rõ."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` nhận byte JPEG, PNG, GIF, WebP hoặc URL HTTP(S) công khai do nhà cung cấp tải. Ví dụ dùng thông điệp người dùng. API hiện tại cũng nhận ảnh trong thông điệp công cụ, nhưng handler đăng ký vẫn trả văn bản qua hợp đồng chung. Thông điệp ảnh `ActorRole.Function` tự tạo phải có ID tương ứng trong `MessageMetadataKeys.FunctionId` (`tool_call_id` khi truyền). Xem giới hạn kích thước và tổng dung lượng trong hướng dẫn thị giác hiện hành. Tạo ảnh vẫn chưa được hỗ trợ.

Cả hai có ngữ cảnh 1M và tối đa 384K (`393216`) token đầu ra; ngân sách mặc định vẫn là 8.000. Khi suy luận, bỏ temperature/penalty và `top_p` ít nhất 0,95; khi tắt thì bỏ `top_p`. Responses dùng API đầu ra có kiểu hiện có cho JSON schema gốc. Không hỗ trợ chạy nền, `store`/`previous_response_id` trên máy chủ, tìm kiếm lưu trữ, `CachePreservation.Required`, công cụ bất đồng bộ gốc, `SteerAsync` hay tạo ảnh. RAG cục bộ và vòng công cụ thông thường vẫn dùng được.

`V4Flash`, `Chat`, `Reasoner` vẫn là hằng obsolete chỉ cảnh báo, giữ wire ID gốc. Nhà cung cấp tạm chuyển alias đã ngừng `deepseek-v4-flash` sang V4.1 Flash; thư viện không viết lại hằng. Mã mới nên chọn `Flash`. `UseReasonerModel()` chọn Flash và suy luận `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Dùng Perplexity khi câu trả lời cần thông tin mới và nguồn để người đọc kiểm chứng. `PerplexityService` gọi Agent API; tìm kiếm và embedding độc lập giúp xây dựng khả năng truy xuất tài liệu cho mô hình trả lời mà bạn chọn.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("So sánh các phương pháp tái chế pin gần đây và dẫn nguồn.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

[Hướng dẫn Perplexity](perplexity.md) trình bày preset nghiên cứu, hàm cục bộ, công cụ do nhà cung cấp chạy và tác vụ nền kéo dài. Các API completion, streaming, Run và trích dẫn quen thuộc vẫn được sử dụng.

Bản phát hành này chuyển dịch vụ sang `/v1/agent`. `AIModels.Perplexity.Sonar` giờ chọn `perplexity/sonar`. Nhà cung cấp đã thông báo dừng endpoint Sonar cũ vào ngày 27 tháng 9 năm 2026; các tích hợp hiện có cần được chuyển đổi. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

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

[Tạo tùy chọn mô hình bằng định nghĩa hỗ trợ dùng chung](model-capabilities.md).
