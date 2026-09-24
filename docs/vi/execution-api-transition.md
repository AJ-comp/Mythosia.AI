# Điều khiển tác vụ AI đang chạy bằng Run

> GPT-6 Sol/Luna là phần bổ sung chưa phát hành. Xem [chọn mô hình và yêu cầu phiên bản](providers.md#gpt-6-sol-luna).

Chỉ cần kết quả cuối cùng và nút Dừng thì truyền `cancellationToken` vào `GetCompletionAsync`. Dùng Run cho sự kiện tiến độ hoặc chỉ dẫn bổ sung được hỗ trợ. Xem [hủy câu trả lời](completions.md#completion-cancellation).

Để có cấu hình độc lập và tái sử dụng biến thể, dùng [builder yêu cầu](request-building.md). Gọi `CreateRequest(...)` trước `With...`. Thuộc tính và phương thức fluent trên dịch vụ giữ nguyên hành vi.

> Để nhận câu trả lời, mức sử dụng và nguồn cùng lúc, dùng bản chụp `AIRunResult` do `await run.Result` trả về. Chuỗi ở `result.Text`; không cần đọc luồng. Đây là thay đổi của Mythosia.AI 8.0.0; kiểu trả về của `GetCompletionAsync` và `StructuredStreamRun<T>.Result` giữ nguyên. [Kết quả Run và chuyển đổi](#run-result).

> Ví dụ `CreateRequest` cần Mythosia.AI 8.0.0 / Abstractions 4.0.0. Bản 7.1 trước đây giới thiệu Run và tùy chọn chung chưa có builder. Gói cũ có thể tiếp tục dùng các overload của dịch vụ.

Với yêu cầu nhạy cảm về thời gian chờ, chọn [tốc độ xử lý](request-building.md#inference-speed). `WithSpeed` giữ mô hình và mức suy luận; `Processing` báo chế độ thực tế. Fast là tùy chọn trả phí trên các tổ hợp được hỗ trợ.

## Vì sao cần điều khiển một tác vụ trong khi nó đang chạy?

Để hoàn thành báo cáo, mô hình có thể phải tìm tài liệu, gọi API và viết nội dung qua nhiều bước. Trong lúc đó, người dùng có thể muốn xem tiến độ, dừng công việc hoặc bổ sung yêu cầu như “Chỉ dùng dữ liệu của năm nay”. Ứng dụng cần liên kết những thao tác này với tác vụ đang thực thi.

Run cung cấp một đối tượng đại diện cho tác vụ mà ứng dụng có thể lưu giữ. Chẳng hạn, màn hình trò chuyện có thể hiển thị văn bản vừa nhận được, báo khi công cụ đang chạy, gắn nút Dừng với thao tác hủy và gửi thêm chỉ dẫn nếu mô hình hỗ trợ. Tất cả đều tác động đến cùng một lần thực thi.

| Nhu cầu của ứng dụng | Cách thực hiện |
| --- | --- |
| Nhận câu trả lời hoàn chỉnh với tùy chọn hủy | `GetCompletionAsync(..., cancellationToken: token)` |
| Hiển thị văn bản ngay khi có và lấy toàn bộ khi hoàn thành | Khởi chạy với `onText`, sau đó chờ `run.Result`. |
| Hiển thị hoạt động của công cụ hoặc chờ xử lý đầu ra bất đồng bộ | Đọc sự kiện từ `run.StreamAsync()`. |
| Cho phép người dùng dừng công việc đang chạy | Gọi `run.Cancel()` trên đối tượng đã lưu. |
| Bổ sung yêu cầu trước khi tác vụ kết thúc | Kiểm tra `run.CanSteer`, rồi dùng `run.SteerAsync(...)` với mô hình hỗ trợ. |

`StartRunAsync` bắt đầu một tác vụ mô hình và trả về `AIRun`. Tác vụ tiếp tục dù bạn có theo dõi đầu ra hay không. Dùng cùng đối tượng đó để đọc luồng, lấy kết quả đã tích lũy, hủy hoặc bổ sung chỉ dẫn giữa lượt khi được hỗ trợ. `GetCompletionAsync`, gồm các overload có kiểu và RAG, vẫn là API tiện ích công khai dành cho nơi chỉ cần kết quả hoàn chỉnh.

<a id="run-result"></a>

## Nhận câu trả lời, mức sử dụng và nguồn cùng lúc

Dù màn hình chỉ hiển thị câu trả lời hoàn chỉnh, ứng dụng vẫn có thể cần lưu số token và nguồn. Trước đây `run.Result` chỉ trả chuỗi: phải tự gom sự kiện luồng để lấy mức sử dụng và đọc nguồn riêng trên Run. `AIRunResult` thu thập các dữ liệu này ngay cả khi không đọc luồng.

Before — hợp đồng Run cũ

```csharp
string answer = await run.Result;
```

After — Mythosia.AI 8.0.0

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Phân tích các tài liệu.")
    .StartRunAsync();

AIRunResult result = await run.Result;
string answer = result.Text;
int? totalTokens = result.Usage?.TotalTokens;
var citations = result.Citations;
string? requestedModel = result.RequestedModel;
string? actualModel = result.Model;
var finishReason = result.FinishReason;
```

Hiển thị văn bản ngay khi đến vẫn nhận cùng kết quả. Callback, `run.StreamAsync()`, `SteerAsync` và thao tác hủy giữ nguyên vai trò.

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Phân tích các tài liệu.")
    .StartRunAsync(onText: text => Console.Write(text));

AIRunResult result = await run.Result;
Console.WriteLine($"\nTokens: {result.Usage?.TotalTokens}");
```

Kết quả hoàn thành là một bản chụp. `Usage` và đối tượng trích dẫn được sao chép; sửa đối tượng nhận được không đổi kết quả đã lưu. Có thể đọc sau khi giải phóng Run. Bộ lọc luồng, dừng đọc hay tràn bộ đệm quan sát không làm mất dữ liệu kết quả.

`Usage` cộng mức sử dụng được báo cáo ở mỗi vòng mô hình đúng một lần; không cộng trùng sự kiện từng vòng và tổng cuối. Nếu không có báo cáo thì là `null`, không ước tính dữ liệu thiếu. Không gồm các yêu cầu tóm tắt phụ riêng biệt nên đây không phải tổng hóa đơn hay mức dùng toàn tài khoản. Nếu chỉ một số vòng báo cáo mức sử dụng, tổng chỉ gồm các vòng đó, không bảo đảm bao quát toàn bộ mức sử dụng.

Giá trị `TotalTokens` được nhà cung cấp báo cáo rõ ràng vẫn được giữ nguyên dù số liệu đầu vào và đầu ra chưa đầy đủ; tổng qua các vòng cộng chính những giá trị đã báo cáo này.

Bộ đếm token vẫn dùng `Int32`. Nếu tổng qua các vòng vượt `Int32.MaxValue`, luồng hoặc Run thất bại với `OverflowException` thay vì trả về số bị tràn. Việc dọn dẹp vẫn hoàn tất và `run.Result` không bị treo chờ ngay cả khi bước cộng tổng cuối cùng thất bại.

Chuỗi do `run.StreamAsync()` trả về chỉ được duyệt một lần. Duyệt lại hoặc duyệt đồng thời cùng chuỗi sẽ gây `InvalidOperationException`; bên đọc ban đầu và quá trình thực thi vẫn độc lập.

`Provider` là tên adapter; `RequestedModel` là mô hình đơn được gửi tường minh trong yêu cầu và chụp khi bắt đầu, gồm cả cấu hình ghi đè mô hình của nhà cung cấp. Giá trị là `null` nếu preset, profile hoặc định tuyến phía máy chủ chọn mô hình mà không gửi một trường mô hình đơn tường minh (ví dụ danh sách Perplexity `Models`). Giá trị này độc lập với mô hình thực tế trong phản hồi ở `Model`. `Model` là ID thực tế do nhà cung cấp báo trong phản hồi của vòng cuối, hoặc `null`, không lấy ID yêu cầu để thay thế. `RoundCount` đếm vòng LLM của thư viện, không đếm từng công cụ hay bước nội bộ của agent được lưu trữ. Nhà cung cấp tùy chỉnh không báo số vòng cho giá trị `0`.

`FinishReason` dùng `AIFinishReason` (`Unknown`, `Stop`, `MaxTokens`, `ToolCalls`, `ContentFilter`, `Other`); `RawFinishReason` giữ giá trị kết thúc gốc. Nếu thiếu thì là `Unknown`/`null`. Các trường này chỉ mô tả kết quả thành công. Lỗi hiện có, kể cả vượt số vòng, vẫn khiến `Result` thất bại. Hủy vẫn ném `OperationCanceledException`, không đổi thành một kết quả thành công.

`Text` giữ toàn bộ văn bản phát ra theo thứ tự, kể cả nội dung trung gian và trước chỉ dẫn bổ sung. Kết quả chờ thực thi và dọn dẹp xong. `Citations` là nguồn cuối; vẫn đọc được `run.Citations` khi đang chạy. Vị trí trích dẫn thuộc phần nội dung gốc, không phải vị trí trong văn bản nối.

<a id="run-result-migration"></a>

**Chuyển đổi bản chính:** `AIRun.Result` đổi từ `Task<string>` sang `Task<AIRunResult>`. Lấy chuỗi bằng `(await run.Result).Text`. Triển khai `AIRun` tùy chỉnh phải đổi override và tạo `AIRunResult`; bên sử dụng phải biên dịch lại. Kiểu kết quả ở `Mythosia.AI.Models.Runs`; `TokenUsage` và `AIFinishReason` ở `Mythosia.AI.Models.Streaming`. `GetCompletionAsync` giữ `Task<string>`, `StructuredStreamRun<T>.Result` giữ `Task<T>`. Ví dụ yêu cầu Mythosia.AI 8.0.0, không phải gói Run 7.1/3.1 ban đầu.

## Hiển thị văn bản qua callback

Trên màn hình trò chuyện hoặc console, hiển thị ngay đoạn văn bản đầu tiên giúp người dùng đọc dần một câu trả lời dài trong khi mô hình đang viết.

```csharp
await using var run = await service
    .CreateRequest("Đọc tài liệu và viết báo cáo.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Công cụ cục bộ có thể trả đối tượng qua `Task<T>` / `ValueTask<T>` và nhận `CancellationToken` được tiêm. `run.Cancel()` hoặc token lúc khởi chạy truyền đến công cụ có hỗ trợ hủy; chỉ dừng đọc luồng thì không. Ngoại lệ được ghi là lỗi. Khi hủy, lời gọi đang chờ được bỏ qua; bước dọn dẹp vẫn chờ công cụ đã chạy nhưng bỏ qua token. Xem [kết quả, lỗi và hủy](function-calling.md#tool-execution-contract).

`onText` là `Action<string>` tùy chọn được gắn trước khi công việc bắt đầu. Nó nhận văn bản theo thứ tự, không thực thi công cụ. Có thể bỏ qua nếu chỉ cần kết quả. Ngoại lệ trong callback sẽ hủy Run và khiến `Result` kết thúc với lỗi. Không truyền lambda `async` vào `onText`: nó sẽ trở thành `async void`, nên Run không thể chờ công việc hoặc lỗi bất đồng bộ của nó. Hãy dùng luồng sự kiện để xử lý đầu ra bất đồng bộ. Callback không tự động chuyển sang luồng UI.

`(await run.Result).Text` nối tất cả sự kiện văn bản của Run, gồm cả nội dung trung gian giữa các lần gọi công cụ và văn bản được tạo trước khi có chỉ dẫn bổ sung. Nó không gửi yêu cầu mô hình thứ hai và không tạo lại một câu trả lời mới. Nếu cách trả kết quả hoàn chỉnh hiện tại phù hợp hơn, bạn có thể tiếp tục dùng `GetCompletionAsync`.

## Đọc sự kiện văn bản, công cụ và mức sử dụng

Khi tác vụ tìm tài liệu hoặc gọi API nghiệp vụ, chỉ văn bản có thể không giải thích được lý do phải chờ. Sự kiện có kiểu giúp hiển thị hoạt động công cụ bên cạnh câu trả lời và ghi nhận mức sử dụng khi nhà cung cấp trả thông tin này.

```csharp
await using var run = await service.StartRunAsync(
    "Tìm tài liệu và giải thích kết quả.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[Đang gọi công cụ]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[Đã nhận kết quả công cụ]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[Tổng số token: {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = (await run.Result).Text;
```

`run.StreamAsync()` nhận token hủy theo dõi tùy chọn, không nhận prompt. Nó theo dõi tác vụ đã được `StartRunAsync` khởi chạy. Thư viện tự thực thi các hàm đã đăng ký; đừng chạy lại công cụ khi nhận sự kiện dùng để hiển thị. Tùy chọn hiển thị văn bản cũng không vô hiệu hóa công cụ đã đăng ký trong Run.

Callback lúc khởi chạy và `run.StreamAsync()` có thể cùng theo dõi một Run; luồng sự kiện hỗ trợ một bên đọc. Ví dụ, hiển thị văn bản bằng `onText` và chỉ xử lý sự kiện công cụ trong luồng để tránh hiển thị hai lần. Bộ đệm giữ tối đa 1,024 sự kiện chưa đọc, kể cả khi đã gắn callback. Bên đọc bắt đầu muộn vẫn nhận sự kiện từ đầu nếu chúng còn nằm trong giới hạn; vượt giới hạn sẽ làm việc theo dõi luồng báo lỗi rõ ràng, trong khi callback, tác vụ và `Result` vẫn tiếp tục. Không coi luồng là nhật ký phát lại không giới hạn. Chờ `Result` không yêu cầu phải đọc hết luồng sự kiện.

Run tìm trên web hoặc trong tài liệu cũng có thể hiển thị nguồn của câu trả lời. Xem ví dụ `WithWebSearch`, `WithFileSearch`, `run.Citations` và sự kiện nguồn trong [hướng dẫn suy luận và tìm kiếm](reasoning-and-search.md).

## Xử lý đầu ra bất đồng bộ

Khi cần xử lý đầu ra bất đồng bộ, hãy chờ thao tác bên trong vòng lặp đọc thay vì dùng callback `onText` bất đồng bộ:

```csharp
using var writer = new StreamWriter("report.txt");
await using var run = await service.StartRunAsync(
    "Viết báo cáo.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = (await run.Result).Text;
```

## Hủy và giải phóng tài nguyên

- Thoát khỏi `await foreach` hoặc hủy token chỉ được truyền cho `run.StreamAsync(token)` sẽ dừng việc theo dõi; tác vụ vẫn chạy.
- `run.Cancel()`, token truyền cho `StartRunAsync` và việc giải phóng một Run còn hoạt động đều hủy quá trình thực thi.
- `await using` bảo đảm `DisposeAsync()` chờ tác vụ tạo đầu ra và quá trình dọn dẹp của nhà cung cấp. Công cụ không hỗ trợ hủy có thể mất thời gian để kết thúc; giải phóng tài nguyên không hoàn tác hành động đã xong.
- Mỗi dịch vụ cho phép một tác vụ `StartRunAsync` đang hoạt động. Khởi chạy chồng lấn sẽ bị từ chối. Dùng dịch vụ riêng cho các tác vụ đồng thời độc lập; không trộn lời gọi API cũ hoặc sửa thiết lập dịch vụ khi Run còn hoạt động.

Run chụp lại đầu vào và chính sách dành cho yêu cầu đang chờ áp dụng trước khi thực thi nền. Nội dung văn bản, hình ảnh, âm thanh tích hợp sẵn và các mảng byte phương tiện được sao chép. Lớp con `MessageContent` tùy chỉnh vẫn giữ nguyên đối tượng, nên không được thay đổi cho đến khi Run kết thúc.

`FunctionCallingPolicy.TimeoutSeconds` được chụp lại sẽ đặt một hạn chót chung cho phần chuẩn bị Run và toàn bộ các vòng mô hình/công cụ. Hết thời gian sẽ báo `AIServiceException`; người dùng hủy sẽ đưa kết quả về trạng thái bị hủy. Quá trình dọn dẹp vẫn chờ những hàm xử lý không hỗ trợ hủy.

## Gửi thêm chỉ dẫn khi công việc đang chạy

Giả sử người dùng bắt đầu lập kế hoạch dự án rồi mới nhận ra dự án phải hoàn thành trong hai tuần. Cơ chế điều chỉnh giữa lượt (steering) cho phép ứng dụng gửi yêu cầu mới khi mô hình vẫn đang làm việc. Nó hữu ích cho việc sửa yêu cầu hoặc đổi phạm vi được phát hiện trong một tác vụ dài. Với câu hỏi mới sau khi tác vụ đã kết thúc, hãy bắt đầu yêu cầu tiếp theo như bình thường.

```csharp
await using var run = await service.StartRunAsync(
    "Soạn bản kế hoạch dự án.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// Gọi từ trình xử lý chỉ dẫn bổ sung của UI khi Run còn hoạt động.
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("Run này không hỗ trợ chỉ dẫn giữa lượt.");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = (await run.Result).Text;
```

GPT-6 Astra / Sol / Luna hỗ trợ chỉ dẫn giữa lượt qua kết nối Responses WebSocket. Các nhà cung cấp khác và mô hình không hỗ trợ vẫn dùng Run bình thường, nhưng `CanSteer` là `false`; thao tác gửi thêm chỉ dẫn báo không hỗ trợ thay vì âm thầm tạo lượt yêu cầu thông thường tiếp theo. `CanSteer` không bảo đảm Run vẫn hoạt động ở thời điểm gọi sau đó.

Run của GPT-6 mở socket riêng. `HttpClient` được cung cấp cùng các message handler vẫn phục vụ lời gọi HTTP và không can thiệp vào socket này. Có thể ghi đè `OpenAIService.ConnectRunWebSocketAsync` để dùng cơ chế truyền tải tùy chỉnh.

`SteerAsync` thành công có nghĩa máy chủ đã nhận đầu vào vào hàng đợi, không có nghĩa mô hình đã áp dụng. Tiếp tục theo dõi cùng Run hoặc chờ kết quả qua phần thực thi tiếp nối. Văn bản đã gửi và hành động đã hoàn thành không bị hoàn tác; công cụ đã bắt đầu cũng không bị hủy chỉ vì có chỉ dẫn bổ sung. Thư viện xử lý việc tiếp nối và ghép kết quả công cụ trên cùng kết nối. Xem [hướng dẫn chỉ dẫn giữa lượt](https://developers.openai.com/api/docs/guides/steering) và [chế độ WebSocket](https://developers.openai.com/api/docs/guides/websocket-mode) của OpenAI. Đầu vào xếp hàng thuộc về kết nối hiện tại; không được giả định nó còn tồn tại sau khi ngắt kết nối, và không gửi lại một cách máy móc chỉ dẫn đã được chấp nhận.

## Tác vụ dùng công cụ và các phương thức agent cũ

Những câu hỏi như “Kiểm tra chính sách hoàn tiền và trạng thái đơn hàng này” cần nhiều nguồn thông tin. Đăng ký công cụ tìm tài liệu và tra cứu đơn hàng để mô hình chọn lời gọi cần thiết. Giới hạn số vòng khống chế việc tiếp tục yêu cầu công cụ trước khi tác vụ phải hoàn thành hoặc báo lỗi.

Function calling thông thường đã hỗ trợ nhiều vòng trao đổi giữa mô hình và công cụ. `StartRunAsync` dùng cùng các hàm đã đăng ký và chính sách thực thi; không cần chế độ agent riêng, bộ lập kế hoạch hoặc công tắc `WithAgentic`.

`RunAgentAsync` và `RunAgentStreamAsync` vẫn gọi được nhưng hiện có cảnh báo `[Obsolete]`. Chữ ký hiện tại, mặc định `maxSteps = 10` và cách báo lỗi vượt bước của API cũ được giữ trong quá trình chuyển đổi. Với lời gọi mới, dùng:

```csharp
await using var run = await service
    .CreateRequest("Tìm chính sách, kiểm tra đơn hàng và giải thích kết quả.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

Mặc định chung của `FunctionCallingPolicy.MaxRounds` là 20, vì vậy cần ghi rõ 10 nếu muốn giữ giới hạn agent cũ. `WithMaxRounds` cấu hình chính sách ghi đè cho một yêu cầu, không thay đổi `DefaultPolicy`; hãy cấu hình trước khi bắt đầu. Các phương thức agent cũ sao chép chính sách mặc định hiện tại rồi áp dụng `maxSteps` của từng lần gọi. Run mới dùng quy ước lỗi thực thi chung, không bảo đảm chuyển đổi sang `AgentMaxStepsExceededException`/`PartialResponse` như trước. Nếu đang phụ thuộc quy ước này, hãy giữ lời gọi cũ cho đến khi đã chuyển đổi phần xử lý ngoại lệ.

## RAG, MCP và ranh giới giữa các gói

- `RagEnabledService.StartRunAsync` hỗ trợ đầu vào chuỗi hoặc `Message`, `onText`, `RagQueryOptions` cho từng truy vấn, `streamOptions` và hủy. Nó truy xuất trước khi bắt đầu Run bên dưới, giữ nội dung hình ảnh/âm thanh và metadata, giữ đầu vào gốc trong lịch sử hội thoại, rồi gửi văn bản bổ sung qua request context. Phần bổ sung gắn với câu hỏi gốc của người dùng, nên không thay kết quả công cụ hoặc chỉ dẫn sau đó bằng prompt RAG ban đầu. Gửi chỉ dẫn vào Run trả về cập nhật công việc của mô hình nhưng không tự chạy lại truy xuất RAG.
- `WithAgenticRag` tiếp tục đăng ký một công cụ tìm kiếm. Dùng công cụ đó qua `StartRunAsync` để mô hình có thể tìm tiếp khi cần. Việc đăng ký MCP qua `WithMcpServerAsync` không thay đổi. Giải phóng kết nối MCP dùng chung riêng với các Run sử dụng nó.
- `IAIRunService` là khả năng tùy chọn trong `Mythosia.AI.Abstractions`; `IAIService` không có thành viên bắt buộc mới. Dịch vụ tùy chỉnh phải triển khai `IAIRunService` để hỗ trợ khởi chạy Run từ RAG. Dịch vụ không hỗ trợ bị từ chối trước khi bắt đầu lập chỉ mục RAG.
- RAG giữ quan hệ phụ thuộc vào Abstractions, còn các nhà cung cấp đóng gói riêng vẫn giữ phương thức completion ghi đè công khai và điểm mở rộng nhà cung cấp có thể truy cập. Thay đổi này không làm lỗi thời API kho vector, trình tải tài liệu hoặc quản trị máy chủ.

## Chuyển sang Mythosia.AI 8

| API | Trạng thái hiện tại |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Vẫn công khai và được hỗ trợ, gồm các biến thể interface, nhà cung cấp và RAG. |
| `StartRunAsync` / `AIRun` | API chung để thực thi và điều khiển. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Có cảnh báo lỗi thời; giữ hành vi hiện tại để tương thích. |
| `service.StreamAsync` và RAG `StreamAsync` nhận đầu vào | StreamAsync nhận đầu vào của dịch vụ/RAG vẫn công khai trong v8. Dùng StartRunAsync cho điều khiển mới; run.StreamAsync() chỉ quan sát run đã tồn tại. |
| `run.StreamAsync()` | Theo dõi đầu ra của tác vụ có sẵn, không nhận đầu vào yêu cầu. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | Giữ API streaming có kiểu hiện tại; `Stream()` chỉ xuất dữ liệu của nó không phải phương thức yêu cầu dịch vụ cũ. |

[Chuyển sang Mythosia.AI 8](v8-migration.md).

Perplexity: [Giữ tác vụ dài tiếp tục chạy](perplexity.md).

[Tạo tùy chọn mô hình bằng định nghĩa hỗ trợ dùng chung](model-capabilities.md).
