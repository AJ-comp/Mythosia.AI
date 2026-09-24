# Hiển thị tùy chọn được mô hình đã chọn hỗ trợ

> Grok 4.7 là phần bổ sung chưa phát hành; xem [chọn mô hình, suy luận và tốc độ xử lý](providers.md#grok-47).

> GPT-6 Sol/Luna là phần bổ sung chưa phát hành. Xem [chọn mô hình và yêu cầu phiên bản](providers.md#gpt-6-sol-luna).

Với [Claude Opus 5.5](providers.md#claude-opus-55), capabilities cung cấp `Low` đến `Max`, gồm `XHigh`; không hỗ trợ `None`, `Minimal` và `ThinkingToggle`. `MaxOutputTokens` là 128000. Ẩn hiển thị không có nghĩa là tắt suy luận. Các định nghĩa này thuộc phần bổ sung chưa phát hành, không phải gói đã công bố.

Giao diện chat cần hiển thị suy luận, tìm kiếm, công cụ và ảnh phù hợp với kết nối. Tự giữ danh sách mô hình trong từng ứng dụng lặp lại quy tắc thư viện và dễ lệch khi nhà cung cấp, giao thức hoặc triển khai thay đổi. Bản chụp khả năng giúp giao diện và kiểm tra thực thi dùng chung định nghĩa mô hình.

API thuộc Mythosia.AI 8.0.0. Đây là mô tả cục bộ bất biến về hỗ trợ đã biết, không phải thăm dò tài khoản hoặc máy chủ trực tiếp. Các kiểu ở `Mythosia.AI.Models.Capabilities`.

Với yêu cầu nhạy cảm về thời gian chờ, chọn [tốc độ xử lý](request-building.md#inference-speed). `WithSpeed` giữ mô hình và mức suy luận; `Processing` báo chế độ thực tế. Fast là tùy chọn trả phí trên các tổ hợp được hỗ trợ.

## Before / After

Before: ứng dụng tự quản lý danh sách. Các danh sách dưới đây là mã của ứng dụng, không phải API thư viện.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: xem yêu cầu đã cấu hình rồi chọn tùy chọn được hỗ trợ. Chỉ lệnh completion cuối mới gửi yêu cầu mô hình; việc xem khả năng không gọi API.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Giải thích các tài liệu.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` phân biệt `Supported`, `Unsupported` và `Unknown`. Triển khai tùy chỉnh hoặc mô hình do máy chủ chọn có thể thiếu thông tin: `Unknown` không có nghĩa không hỗ trợ. Ví dụ chỉ bật suy luận bổ sung khi biết được hỗ trợ. Nếu chưa rõ, ứng dụng chọn giữ mặc định hoặc cho phép thử yêu cầu.

`request.GetCapabilities()` đọc mô hình, tùy chọn nhà cung cấp và profile đã được builder chụp lại. `service.GetCapabilities()` xem mặc định dịch vụ mà không tiêu thụ tùy chọn dành cho lần gọi kế tiếp. Cả hai không gửi HTTP, gọi callback ngữ cảnh hay bộ kiểm tra thực thi, đổi lịch sử hoặc bắt đầu việc. Danh sách cũng là bản chụp chỉ đọc. Truy vấn dịch vụ cũng xem các thiết lập tính năng đang chờ lần gọi kế tiếp và giữ nguyên chúng cho yêu cầu thực tế. Việc kiểm tra không tuần tự hóa giá trị mặc định của hàm hoặc tham số công cụ được lưu trữ, cũng không chuẩn bị hồ sơ thực thi hay dành trước ngân sách token.

Khả năng mô tả kết nối có thể hỗ trợ gì, không phải tùy chọn đang bật. Nhà cung cấp, giao thức và chế độ cũng quan trọng như tên mô hình. ID phản ánh ghi đè và chuyển đổi Qwen/Ollama; không chọn một mô hình đơn thì có thể là `null`. Chat UI mẫu cập nhật các điều khiển từ kết nối đang hoạt động và cấu hình hiện tại, kể cả công cụ đã đăng ký, thay vì chỉ dựa vào danh mục mô hình. Khả năng hỗ trợ lấy mẫu có thể thay đổi theo chế độ suy luận hoặc công cụ hiện có; hãy kiểm tra lại sau khi thay đổi các thiết lập đó. Trạng thái chưa rõ được hiển thị riêng với không hỗ trợ.

| API | Ý nghĩa |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | Hỗ trợ và mức của `WithReasoning` chung. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | Điều khiển suy luận riêng của nhà cung cấp và ngân sách gợi ý. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | Streaming, công cụ, công cụ bất đồng bộ gốc và chỉ dẫn khi đang chạy. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | Tìm kiếm được lưu trữ, thay đổi suy luận giữ cache, ảnh đầu vào và đầu ra có cấu trúc. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | Thiết lập lấy mẫu được hỗ trợ và giới hạn token đầu ra đã biết, có thể null. |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | Chưa phát hành: chế độ Supported/Unsupported/Unknown; kiểm tra quyền tài khoản riêng. |
| `Provider`, `Model` | Nhà cung cấp và mô hình gửi đi; có thể chưa rõ danh tính. |

`ReasoningLevels` dành cho `WithReasoning` chung; `NativeReasoningLevels` dành cho cấu hình riêng. `ThinkingBudgetPresets` gợi ý lựa chọn UI, không liệt kê mọi ngân sách hoặc toàn bộ khoảng số. `AsyncFunctionCalling` là thực thi công cụ bất đồng bộ gốc, không chỉ handler cục bộ trả `Task` hay chạy song song. `StructuredOutput` bao gồm API đầu ra có kiểu chung với phương án prompt và sửa lỗi; không bảo đảm giải mã ràng buộc gốc. Cả hai danh sách mức dùng `ReasoningLevel`; ngân sách gợi ý là số nguyên.

Bản chụp không bảo đảm quyền tài khoản hay máy chủ sẵn sàng, cũng không hợp thức hóa tổ hợp sai. Kiểm tra và lỗi thực thi vẫn còn. Trước chỉ dẫn bổ sung, kiểm tra `run.CanSteer` của phiên thật; mô hình hỗ trợ không có nghĩa Run vẫn đang chạy.

## Xem khả năng tạo ảnh riêng

Mô hình tạo ảnh độc lập với chat. `service.GetImageCapabilities(imageModel)` xem mô hình cụ thể; bỏ đối số để dùng mặc định ảnh của nhà cung cấp. Builder chat không chọn mô hình tạo ảnh. `Generation`, `Editing` và `Mask` giúp chọn thao tác ảnh cần hiển thị.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` và `AspectRatios` là danh sách có kiểu chỉ đọc. `MaxImages` và `MaxInputImages` là giới hạn đã biết, có thể null. Giá trị trong danh sách không bảo đảm mọi tổ hợp hợp lệ: vẫn kiểm tra kích thước, định dạng, chất lượng, mask và mô hình. Mô hình ảnh tùy chỉnh hoặc chưa rõ không bị đánh dấu không hỗ trợ.

Với Google, `Resolutions` và `AspectRatios` phụ thuộc mô hình ảnh được chọn và cũng áp dụng khi kiểm tra tạo/chỉnh sửa ảnh. Xem [bảng theo mô hình](providers.md#google-image-options), gồm chính sách thận trọng chỉ cho phép 1K của Flash-Lite. Giá trị chỉ định không được hỗ trợ bị từ chối trước HTTP; mô hình tùy chỉnh chưa rõ giữ `Unknown` và kiểm tra tùy chọn chung của nhà cung cấp.

Một `AIService` tùy chỉnh có định nghĩa đáng tin cậy có thể ghi đè protected `ResolveRequestCapabilities()`. Mặc định là `AIModelCapabilities.Unknown`. Không có trong danh mục không khiến triển khai thành không hỗ trợ. `IAIService` không thêm thành viên bắt buộc; phương thức nằm trên `AIService` và builder của nó.

Nếu hồ sơ của nhà cung cấp tùy chỉnh thay đổi cờ chế độ gốc, hãy ghi đè `ApplyCapabilityRequestProfile(AIRequestProfile)` và dùng `SetExecutionSetting(...)` chỉ để áp dụng các cờ cần cho bộ xác định khả năng. Hook mặc định không làm gì. Builder đã chụp các thiết lập chung của hồ sơ; việc kiểm tra không gọi `ApplyRequestProfile` hay `ApplyProviderSpecificRequestProfile`. Hook này không được xác thực, gọi callback, tuần tự hóa, dành trước ngân sách hoặc thay đổi trạng thái thuộc về dịch vụ hay bên gọi. Thiết lập tạm thời được khôi phục sau khi kiểm tra, kể cả khi phương thức ghi đè phát sinh ngoại lệ.

[Cấu hình yêu cầu](request-building.md) · [Tùy chọn nhà cung cấp và ảnh](providers.md) · [Điều khiển Run](execution-api-transition.md)
