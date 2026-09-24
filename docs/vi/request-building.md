# Giữ cấu hình của từng yêu cầu độc lập

> Grok 4.7: Cần Mythosia.AI 8.1.0 / Abstractions 4.1.0. [chọn mô hình, suy luận và tốc độ xử lý](providers.md#grok-47)

Bản tóm tắt có thể cần nhiệt độ thấp, còn bản nháp sáng tạo cần giá trị cao hơn. Chuẩn bị bản nháp không được làm đổi cấu hình của yêu cầu tóm tắt đã chuẩn bị. Dùng `CreateRequest` để đặt cấu hình riêng cho từng lần gọi hoặc tạo nhiều biến thể từ một yêu cầu cơ sở.

Để nhận câu trả lời, mức sử dụng và nguồn cùng lúc, dùng bản chụp `AIRunResult` do `await run.Result` trả về. Chuỗi ở `result.Text`; không cần đọc luồng. Đây là thay đổi của Mythosia.AI 8.0.0; kiểu trả về của `GetCompletionAsync` và `StructuredStreamRun<T>.Result` giữ nguyên. [Kết quả Run và chuyển đổi](execution-api-transition.md#run-result).

Chỉ cần kết quả cuối cùng và nút Dừng thì truyền `cancellationToken` vào `GetCompletionAsync`. Dùng Run cho sự kiện tiến độ hoặc chỉ dẫn bổ sung được hỗ trợ. Xem [hủy câu trả lời](completions.md#completion-cancellation).

> Ví dụ `CreateRequest` cần Mythosia.AI 8.0.0 / Abstractions 4.0.0. Bản 7.1 trước đây giới thiệu Run và tùy chọn chung chưa có builder. Gói cũ có thể tiếp tục dùng các overload của dịch vụ.

## Before: dùng chung dịch vụ

`WithTemperature` hiện có trên dịch vụ thay đổi dịch vụ và trả về cùng một đối tượng. Hai biến dưới đây cùng tham chiếu đối tượng đó, nên giá trị đặt sau áp dụng cho cả hai. Các phương thức này vẫn dùng được để cấu hình giá trị mặc định của dịch vụ.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Giải thích tài liệu này."); // 0.8
```

## After: các biến thể độc lập

`CreateRequest` chụp lại giá trị mặc định. Mỗi `With...` của builder trả về builder mới mà không đổi bản gốc. Khi thực thi, cấu hình đã chụp được sử dụng trực tiếp, không tạm ghi đè cấu hình dịch vụ.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Giải thích tài liệu này.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Dùng 0.2; creative và cấu hình mặc định không đổi.
```

Hãy dùng builder được trả về. Bỏ kết quả của `basis.WithTemperature(0.2f);` sẽ không thay đổi `basis`.

Builder kiểm tra thay vì âm thầm chỉnh giá trị: nhiệt độ 0–2, TopP 0–1, penalty −2–2; từ chối NaN và vô cực. Giới hạn token, vòng, đồng thời và timeout được chỉ định phải dương. Giá trị sai gây `ArgumentException` / `ArgumentOutOfRangeException`. Helper nhiệt độ cũ trên dịch vụ vẫn điều chỉnh về khoảng hợp lệ.

## Vai trò của các đối tượng

`AIService` quản lý kết nối nhà cung cấp, giá trị mặc định và hội thoại. Kiểu công khai `Mythosia.AI.Builders.AIRequestBuilder` cung cấp fluent API. Kiểu nội bộ `AIRequest` chuyển đầu vào và cấu hình đã chốt đến phần thực thi. Không cần gọi `Build()`: `GetCompletionAsync()` trả về `Task<string>`, `StartRunAsync()` trả về `Task<AIRun>`. Câu trả lời không phải là `AIRequest`.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Bắt đầu Run với cùng cấu hình

Dùng `GetCompletionAsync()` để lấy câu trả lời hoàn chỉnh, hoặc `StartRunAsync()` để hiển thị tiến độ và thêm chỉ dẫn khi mô hình hỗ trợ. Prompt được truyền vào `CreateRequest`, không truyền lại vào phương thức thực thi. `run.StreamAsync()` quan sát Run đó; điều kiện hỗ trợ `run.SteerAsync(...)` không đổi.

```csharp
await using var run = await service
    .CreateRequest("Giải thích tài liệu này.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Công cụ cục bộ có thể trả đối tượng qua `Task<T>` / `ValueTask<T>` và nhận `CancellationToken` được tiêm. `run.Cancel()` hoặc token lúc khởi chạy truyền đến công cụ có hỗ trợ hủy; chỉ dừng đọc luồng thì không. Ngoại lệ được ghi là lỗi. Khi hủy, lời gọi đang chờ được bỏ qua; bước dọn dẹp vẫn chờ công cụ đã chạy nhưng bỏ qua token. Xem [kết quả, lỗi và hủy](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Tái sử dụng profile và ngữ cảnh

`WithProfile` sao chép `AIRequestProfile`, còn `WithContext` sao chép `AIRequestContext`. Sửa đối tượng gốc sau đó không làm đổi yêu cầu đã chuẩn bị. Builder cấu hình lấy mẫu, chỉ dẫn hệ thống, chế độ không trạng thái, chính sách hàm và các tùy chọn suy luận, tìm kiếm được hỗ trợ. Việc kiểm tra khả năng của nhà cung cấp vẫn áp dụng.

`WithFunctions(params FunctionDefinition[])` thêm bản sao định nghĩa hàm. `WithFunctions(toolInstance)` và `WithStaticFunctions<T>()` từ `Mythosia.AI.Extensions` hỗ trợ hàm có attribute hiện có. Đăng ký trước `CreateRequest` cho mặc định dịch vụ, sau đó cho yêu cầu. `CreateRequest` lấy và tiêu thụ tùy chọn cũ đang chờ lần gọi tiếp theo; giữ builder để tái sử dụng chúng.

```csharp
var request = service
    .CreateRequest("Viết lại câu hỏi này để tìm kiếm.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nGiữ nguyên ý nghĩa ban đầu."
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## Cấu hình được sao chép và trạng thái còn dùng chung

Cấu hình chung và mặc định của nhà cung cấp được chụp khi gọi `CreateRequest`; các thay đổi sau đó không ảnh hưởng yêu cầu. Nội dung tin nhắn tích hợp, tập hợp tùy chọn được hỗ trợ, profile, ngữ cảnh và chính sách được sao chép. Handler hàm, callback ngữ cảnh động và nội dung tin nhắn tùy chỉnh giữ nguyên tham chiếu. Không sửa nội dung tùy chỉnh; delegate vẫn có thể đọc trạng thái bên ngoài. Ngữ cảnh động được đánh giá khi thực thi.

Sau khi chụp dữ liệu, bạn có thể giải phóng `JsonDocument` gốc hoặc sửa các giá trị `JsonNode` gốc mà không đổi JSON được lưu trong siêu dữ liệu yêu cầu hay đối số gọi hàm; mỗi lần thực thi nhận một bản sao riêng. Chuỗi `Items` trong lược đồ công cụ có vòng lặp hoặc vượt quá 64 cấp sẽ gây `ArgumentException` khi chụp (`CreateRequest` hoặc `WithFunctions`), giúp báo lỗi lược đồ trước khi thực thi thay vì làm cạn ngăn xếp của tiến trình.

Việc sao chép cũng giữ nguyên số chiều và chỉ số bắt đầu của mảng, cùng quy tắc so sánh khóa của các bộ chứa chuẩn `Dictionary<,>`, `SortedDictionary<,>` và `SortedList<,>`. Vì vậy, việc tra khóa không phân biệt chữ hoa và chữ thường vẫn hoạt động như cũ trong yêu cầu. Giá trị rỗng `default(JsonElement)` (`Undefined`) được giữ nguyên. Các đối tượng siêu dữ liệu tùy chỉnh chưa được nhận diện vẫn giữ tham chiếu; chủ sở hữu phải tránh thay đổi hoặc điều phối truy cập.

Các giá trị chuẩn `ReadOnlyCollection<T>` và `ReadOnlyDictionary<TKey, TValue>` giữ nguyên kiểu bên trong mảng và từ điển có kiểu xác định. Các tập hợp nền được hỗ trợ được sao chép nhưng vẫn giữ khung nhìn chỉ đọc, tham chiếu dùng chung và tham chiếu vòng. `Hashtable` và `SortedList` không generic cũng giữ nguyên quy tắc so sánh khóa.

Builder không phải hội thoại riêng. Nó dùng hội thoại đang hoạt động của dịch vụ tại lúc thực thi, không đóng băng lịch sử khi tạo. Lần gọi có trạng thái vẫn cập nhật lịch sử chung. Dùng `WithStatelessMode()` để không đọc hoặc tích lũy lịch sử. Giới hạn một Run đang hoạt động trên mỗi dịch vụ vẫn giữ nguyên. Cấu hình độc lập không bảo đảm chạy song song trên cùng dịch vụ; hãy dùng dịch vụ riêng cho các hội thoại đồng thời độc lập.

## Các cách gọi và phần mở rộng hiện có

`GetCompletionAsync` và các điểm gọi hiện có vẫn được hỗ trợ. `BeginMessage()` / `MessageChain` giữ cách tạo tin nhắn có thể thay đổi, nhưng thực thi qua luồng yêu cầu mới. Dùng `CreateRequest` để phân nhánh cấu hình. API thuộc `AIService` và các triển khai nhà cung cấp; không thêm thành viên bắt buộc vào `IAIService`. Mã chỉ dùng interface trừu tượng hoặc wrapper RAG tiếp tục dùng API profile, ngữ cảnh và thực thi hiện có.

[Tạo tùy chọn mô hình bằng định nghĩa hỗ trợ dùng chung](model-capabilities.md).

<a id="inference-speed"></a>

## Chọn tốc độ xử lý theo tác vụ

Yêu cầu có người dùng đang chờ có thể cần xử lý trả phí với độ trễ thấp; báo cáo nền có thể dùng xử lý thường. `WithSpeed` chọn chế độ nhưng giữ nguyên mô hình và mức suy luận. Cần Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ProviderDefault` không ghi đè mà giữ thiết lập dịch vụ/nhà cung cấp; mặc định dự án có thể đã là Fast. `Standard` yêu cầu xử lý thường một cách rõ ràng. `Fast` yêu cầu chế độ trả phí độ trễ thấp và có thể tăng chi phí. Giữ builder trả về: ba nhánh độc lập, yêu cầu gốc không đổi.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

Kiểm tra `GetSpeedSupport(InferenceSpeed.Fast)` trước khi hiển thị lựa chọn. `StandardSpeed` và `FastSpeed` cũng phân biệt Supported, Unsupported, Unknown. Supported cục bộ không xác minh quyền tài khoản, năng lực máy chủ hay độ trễ. Standard/Fast được yêu cầu rõ nhưng không hỗ trợ hoặc chưa biết sẽ thất bại, không âm thầm đổi mô hình hay mức suy luận. Dùng `ProviderDefault` để giữ đường gọi cũ.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` giữ `AIProcessingInfo` bất biến ngay cả khi không đọc luồng. `RequestIndex` bắt đầu từ 1, chỉ lần thử suy luận của nhà cung cấp, gồm cả continuation máy chủ, không phải số vòng công cụ hay số yêu cầu HTTP; lời gọi tiếp theo, thử lại và sửa định dạng có thể thêm bản ghi. `AppliedSpeed` là null nếu máy chủ không báo chế độ nhận diện được, kể cả lần thử lỗi. `RawAppliedMode` và `ResponseId` giữ giá trị được báo. `IsDowngraded` chỉ true khi yêu cầu Fast và được báo Standard rõ ràng; false không xác nhận đã dùng Fast.

Sau completion thông thường, đọc ngay `AIService.LastProcessing`; yêu cầu logic sau sẽ thay thế khung nhìn này. Bản ghi đã lấy vẫn bất biến. Phương thức mở rộng dịch vụ áp dụng cho yêu cầu logic kế tiếp và các vòng công cụ, không đặt mặc định vĩnh viễn. Tóm tắt phụ trợ, viết lại truy vấn nội bộ và profile nội bộ không kế thừa ghi đè tốc độ hay trộn quan sát vào yêu cầu chính.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

Các giá trị mô tả chế độ của nhà cung cấp, không phải số token mỗi giây đo được. OpenAI, xAI, Google có thể hạ cấp ở máy chủ; Mythosia không tự thử lại với tốc độ khác. Anthropic fast mode cần quyền trên Claude API trực tiếp; đổi tốc độ có thể vô hiệu hóa cache prompt. Gemini Developer API priority cần Tier 2/3. Kiểm tra riêng mô hình, API, quyền và giá. Tùy chọn này không cấu hình tạo ảnh, embedding hay Batch API gốc. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

Khi giữ tham chiếu `IAIService`, dùng `GetLastProcessing()` trong `Mythosia.AI.Extensions`. Nó đọc `IAIProcessingInfoService` tùy chọn và trả danh sách rỗng nếu không có chẩn đoán. `IAIService` không thêm thành viên bắt buộc. Với RAG, `RagEnabledService.WithSpeed(...)` cấu hình câu trả lời kế tiếp sau truy xuất; `LastProcessing` mô tả câu trả lời đó. Viết lại truy vấn nội bộ được tách riêng và Run trả cùng các bản ghi `Processing`.

Danh sách Fast được triển khai nằm dưới đây. Kiểm tra Standard riêng bằng `GetSpeedSupport(InferenceSpeed.Standard)`. Mô hình ngoài danh sách, endpoint bên thứ ba và nhà cung cấp tương thích OpenAI không tự kế thừa chế độ trả phí.

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — các mô hình Claude đã biết khác, gồm Sonnet 5 | bỏ `speed` và beta fast-mode | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

Với các mô hình Claude còn lại này, Standard dùng yêu cầu thường hiện có. Nếu máy chủ không báo siêu dữ liệu xử lý, `AppliedSpeed` vẫn là null; thư viện không suy ra Standard từ giá trị yêu cầu.
