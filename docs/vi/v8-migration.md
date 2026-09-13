# Chuyển sang Mythosia.AI 8

Dùng phiên bản này khi cần tách cấu hình từng yêu cầu, cho người dùng dừng tác vụ và lưu câu trả lời cùng mức sử dụng và nguồn. Bản nâng cấp lớn này gom sáu thay đổi kiến trúc, cập nhật nhà cung cấp/mô hình và các bản sửa qua ba đợt kiểm tra đối kháng.

Chỉ cập nhật đồng bộ các gói đang dùng và biên dịch lại bên sử dụng. Mythosia.AI tự kéo phụ thuộc Abstractions tương ứng. Bảng đối chiếu bản nền đã xuất bản với các phiên bản tương thích trong đợt này.

| Gói | Bản nền đã xuất bản | Bản mục tiêu |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` chuyển từ `1.0.0-preview` sang bản ổn định `1.0.0`. Gói giữ nguyên API mô hình, trạng thái, phiên bản máy chủ và metrics hiện có, hoạt động độc lập và không phụ thuộc gói AI lõi.

## Chọn thay đổi theo nhu cầu

| Nhu cầu | Thay đổi và chuyển đổi |
| --- | --- |
| Phát hiện sai tùy chọn ảnh trước khi gửi | Thay chuỗi bằng `ImageQuality`, `ImageBackground`, `ImageOutputFormat` và `ImageSize.Pixels(...)` / `ImageSize.Preset(...)`. Khả năng hỗ trợ vẫn tùy nhà cung cấp. |
| Chuẩn bị nhiều yêu cầu mà không đổi cấu hình của nhau | Bắt đầu bằng `CreateRequest(...)`, giữ builder mới do mỗi `With...` trả về. Setter của dịch vụ vẫn đổi giá trị mặc định dùng chung. |
| Trả dữ liệu trực tiếp từ công cụ bất đồng bộ | Phương thức đăng ký qua thuộc tính trả đối tượng bằng `Task<T>` / `ValueTask<T>` và nhận `CancellationToken` được truyền vào. Ngoại lệ được ghi là lỗi; handler chuỗi vẫn được hỗ trợ. |
| Ngừng chờ khi người dùng hủy | Truyền `cancellationToken` vào completion, Run và đầu vào RAG được hỗ trợ. Nó dừng việc cục bộ và công cụ phối hợp, không bảo đảm hủy phía nhà cung cấp hay hoàn tác hành động bên ngoài. |
| Lưu câu trả lời, mức sử dụng và nguồn cùng nhau | `AIRun.Result` trả `Task<AIRunResult>`. Dùng `(await run.Result).Text` để lấy chuỗi. Kết quả vẫn được thu thập dù không đọc stream. |
| Hiển thị điều khiển hợp với mô hình | Dùng `request.GetCapabilities()` hoặc truy vấn dịch vụ/ảnh. `Supported`, `Unsupported`, `Unknown` là thông tin cục bộ của thư viện, không kiểm tra trực tiếp quyền tài khoản. |

## Cập nhật mã gọi và nhà cung cấp tùy chỉnh

Kiểu tùy chọn ảnh, `AIRun.Result` và chữ ký hủy đã đổi phá vỡ tương thích. Triển khai `IAIService` và override các overload công khai đã đổi phải thêm và chuyển tiếp token. Override nhà cung cấp `GetCompletionAsync(Message)` giữ chữ ký và chuyển tiếp `RequestCancellationToken`. `AIRun` tùy chỉnh phải trả `AIRunResult`. GetCompletionAsync giữ kết quả chuỗi; completion có kiểu và `StructuredStreamRun<T>.Result` giữ kiểu kết quả. StreamAsync nhận đầu vào của dịch vụ/RAG vẫn công khai trong v8. RunAgentAsync và RunAgentStreamAsync giữ hành vi tương thích và cảnh báo obsolete. Dùng Run cho luồng mới có tiến độ, hủy và chỉ dẫn giữa chừng khi được hỗ trợ.

## Một yêu cầu, kết quả và tiến độ tùy chọn

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAI dùng pixel; Google và xAI dùng `ImageSize.Preset(...)`. Chỉ đổi Auto khi nhà cung cấp hỗ trợ chọn định dạng; lưu theo `GeneratedImage.MediaType` trả về.

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

Công cụ đã đăng ký có thể trả đối tượng ứng dụng như dưới đây. Handler cấp thấp `HandlerWithCancellation` vẫn trả `Task<string>`; không cần lớp bọc đối tượng mới.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## Nhà cung cấp và xác minh

Bản này còn gồm tích hợp đã chuẩn bị cho Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash, Perplexity Agent và tạo/chỉnh sửa ảnh chung cho OpenAI, Google, xAI. Hằng mô hình bị xóa và endpoint Perplexity thay đổi có thể cần sửa mã gọi; xem hướng dẫn nhà cung cấp và ghi chú từng gói.

Cấu hình nghiên cứu bằng `PerplexityAgentOptions`. Kiểm thử Profile, Custom Skill và Connector đã chuẩn bị nhưng cần tài nguyên đã đăng ký. MCP vẫn là preview. Khi bắt đầu giải phóng, lời gọi lỗi `ObjectDisposedException`; khi vòng đọc đã dừng, lời gọi mới lỗi `McpException` thay vì chờ vô hạn.

Ba đợt kiểm tra đối kháng củng cố sao chép yêu cầu, kết quả công cụ, hủy/dọn dẹp, đếm token, xác thực phản hồi và vòng đời MCP. Đợt ba thêm 43 ca hồi quy; cả 2.703 kiểm thử đều đạt. Tài liệu được kiểm tra ở 13 ngôn ngữ. Đợt này không gọi API nhà cung cấp thật; kiểm thử đơn vị không chứng minh mọi tích hợp phụ thuộc tài nguyên tài khoản.

## Hướng dẫn chi tiết

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
