# Giữ cấu hình của từng yêu cầu độc lập

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
