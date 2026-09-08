# Điều khiển tác vụ AI đang chạy bằng Run

> Các API này yêu cầu `Mythosia.AI` 7.1.0 trở lên, bao gồm `Mythosia.AI.Abstractions` 3.1.0 trở lên. Các ví dụ RAG yêu cầu `Mythosia.AI.Rag` 7.6.0 trở lên.

## Vì sao cần điều khiển một tác vụ trong khi nó đang chạy?

Để hoàn thành báo cáo, mô hình có thể phải tìm tài liệu, gọi API và viết nội dung qua nhiều bước. Trong lúc đó, người dùng có thể muốn xem tiến độ, dừng công việc hoặc bổ sung yêu cầu như “Chỉ dùng dữ liệu của năm nay”. Ứng dụng cần liên kết những thao tác này với tác vụ đang thực thi.

Run cung cấp một đối tượng đại diện cho tác vụ mà ứng dụng có thể lưu giữ. Chẳng hạn, màn hình trò chuyện có thể hiển thị văn bản vừa nhận được, báo khi công cụ đang chạy, gắn nút Dừng với thao tác hủy và gửi thêm chỉ dẫn nếu mô hình hỗ trợ. Tất cả đều tác động đến cùng một lần thực thi.

| Nhu cầu của ứng dụng | Cách thực hiện |
| --- | --- |
| Nhận câu trả lời hoàn chỉnh mà không điều khiển công việc đang chạy | Tiếp tục dùng `GetCompletionAsync`, kể cả các overload có kiểu và RAG. |
| Hiển thị văn bản ngay khi có và lấy toàn bộ khi hoàn thành | Khởi chạy với `onText`, sau đó chờ `run.Result`. |
| Hiển thị hoạt động của công cụ hoặc chờ xử lý đầu ra bất đồng bộ | Đọc sự kiện từ `run.StreamAsync()`. |
| Cho phép người dùng dừng công việc đang chạy | Gọi `run.Cancel()` trên đối tượng đã lưu. |
| Bổ sung yêu cầu trước khi tác vụ kết thúc | Kiểm tra `run.CanSteer`, rồi dùng `run.SteerAsync(...)` với mô hình hỗ trợ. |

`StartRunAsync` bắt đầu một tác vụ mô hình và trả về `AIRun`. Tác vụ tiếp tục dù bạn có theo dõi đầu ra hay không. Dùng cùng đối tượng đó để đọc luồng, lấy kết quả đã tích lũy, hủy hoặc bổ sung chỉ dẫn giữa lượt khi được hỗ trợ. `GetCompletionAsync`, gồm các overload có kiểu và RAG, vẫn là API tiện ích công khai dành cho nơi chỉ cần kết quả hoàn chỉnh.

## Hiển thị văn bản qua callback

Trên màn hình trò chuyện hoặc console, hiển thị ngay đoạn văn bản đầu tiên giúp người dùng đọc dần một câu trả lời dài trong khi mô hình đang viết.

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Đọc tài liệu và viết báo cáo.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

string answer = await run.Result;
```

`onText` là `Action<string>` tùy chọn được gắn trước khi công việc bắt đầu. Nó nhận văn bản theo thứ tự, không thực thi công cụ. Có thể bỏ qua nếu chỉ cần kết quả. Ngoại lệ trong callback sẽ hủy Run và khiến `Result` kết thúc với lỗi. Không truyền lambda `async` vào `onText`: nó sẽ trở thành `async void`, nên Run không thể chờ công việc hoặc lỗi bất đồng bộ của nó. Hãy dùng luồng sự kiện để xử lý đầu ra bất đồng bộ. Callback không tự động chuyển sang luồng UI.

`Result` nối tất cả sự kiện văn bản của Run, gồm cả nội dung trung gian giữa các lần gọi công cụ và văn bản được tạo trước khi có chỉ dẫn bổ sung. Nó không gửi yêu cầu mô hình thứ hai và không tạo lại một câu trả lời mới. Nếu cách trả kết quả hoàn chỉnh hiện tại phù hợp hơn, bạn có thể tiếp tục dùng `GetCompletionAsync`.

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

string answer = await run.Result;
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
string answer = await run.Result;
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

string answer = await run.Result;
```

GPT-6 Astra hỗ trợ chỉ dẫn giữa lượt qua kết nối Responses WebSocket. Các nhà cung cấp khác và mô hình không hỗ trợ vẫn dùng Run bình thường, nhưng `CanSteer` là `false`; thao tác gửi thêm chỉ dẫn báo không hỗ trợ thay vì âm thầm tạo lượt yêu cầu thông thường tiếp theo. `CanSteer` không bảo đảm Run vẫn hoạt động ở thời điểm gọi sau đó.

Run của Astra mở socket riêng. `HttpClient` được cung cấp cùng các message handler vẫn phục vụ lời gọi HTTP và không can thiệp vào socket này. Có thể ghi đè `OpenAIService.ConnectRunWebSocketAsync` để dùng cơ chế truyền tải tùy chỉnh.

`SteerAsync` thành công có nghĩa máy chủ đã nhận đầu vào vào hàng đợi, không có nghĩa mô hình đã áp dụng. Tiếp tục theo dõi cùng Run hoặc chờ kết quả qua phần thực thi tiếp nối. Văn bản đã gửi và hành động đã hoàn thành không bị hoàn tác; công cụ đã bắt đầu cũng không bị hủy chỉ vì có chỉ dẫn bổ sung. Thư viện xử lý việc tiếp nối và ghép kết quả công cụ trên cùng kết nối. Xem [hướng dẫn chỉ dẫn giữa lượt](https://developers.openai.com/api/docs/guides/steering) và [chế độ WebSocket](https://developers.openai.com/api/docs/guides/websocket-mode) của OpenAI. Đầu vào xếp hàng thuộc về kết nối hiện tại; không được giả định nó còn tồn tại sau khi ngắt kết nối, và không gửi lại một cách máy móc chỉ dẫn đã được chấp nhận.

## Tác vụ dùng công cụ và các phương thức agent cũ

Những câu hỏi như “Kiểm tra chính sách hoàn tiền và trạng thái đơn hàng này” cần nhiều nguồn thông tin. Đăng ký công cụ tìm tài liệu và tra cứu đơn hàng để mô hình chọn lời gọi cần thiết. Giới hạn số vòng khống chế việc tiếp tục yêu cầu công cụ trước khi tác vụ phải hoàn thành hoặc báo lỗi.

Function calling thông thường đã hỗ trợ nhiều vòng trao đổi giữa mô hình và công cụ. `StartRunAsync` dùng cùng các hàm đã đăng ký và chính sách thực thi; không cần chế độ agent riêng, bộ lập kế hoạch hoặc công tắc `WithAgentic`.

`RunAgentAsync` và `RunAgentStreamAsync` vẫn gọi được nhưng hiện có cảnh báo `[Obsolete]`. Chữ ký hiện tại, mặc định `maxSteps = 10` và cách báo lỗi vượt bước của API cũ được giữ trong quá trình chuyển đổi. Với lời gọi mới, dùng:

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Tìm chính sách, kiểm tra đơn hàng và giải thích kết quả.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

Mặc định chung của `FunctionCallingPolicy.MaxRounds` là 20, vì vậy cần ghi rõ 10 nếu muốn giữ giới hạn agent cũ. `WithMaxRounds` cấu hình chính sách ghi đè cho một yêu cầu, không thay đổi `DefaultPolicy`; hãy cấu hình trước khi bắt đầu. Các phương thức agent cũ sao chép chính sách mặc định hiện tại rồi áp dụng `maxSteps` của từng lần gọi. Run mới dùng quy ước lỗi thực thi chung, không bảo đảm chuyển đổi sang `AgentMaxStepsExceededException`/`PartialResponse` như trước. Nếu đang phụ thuộc quy ước này, hãy giữ lời gọi cũ cho đến khi đã chuyển đổi phần xử lý ngoại lệ.

## RAG, MCP và ranh giới giữa các gói

- `RagEnabledService.StartRunAsync` hỗ trợ đầu vào chuỗi hoặc `Message`, `onText`, `RagQueryOptions` cho từng truy vấn, `streamOptions` và hủy. Nó truy xuất trước khi bắt đầu Run bên dưới, giữ nội dung hình ảnh/âm thanh và metadata, giữ đầu vào gốc trong lịch sử hội thoại, rồi gửi văn bản bổ sung qua request context. Phần bổ sung gắn với câu hỏi gốc của người dùng, nên không thay kết quả công cụ hoặc chỉ dẫn sau đó bằng prompt RAG ban đầu. Gửi chỉ dẫn vào Run trả về cập nhật công việc của mô hình nhưng không tự chạy lại truy xuất RAG.
- `WithAgenticRag` tiếp tục đăng ký một công cụ tìm kiếm. Dùng công cụ đó qua `StartRunAsync` để mô hình có thể tìm tiếp khi cần. Việc đăng ký MCP qua `WithMcpServerAsync` không thay đổi. Giải phóng kết nối MCP dùng chung riêng với các Run sử dụng nó.
- `IAIRunService` là khả năng tùy chọn trong `Mythosia.AI.Abstractions`; `IAIService` không có thành viên bắt buộc mới. Dịch vụ tùy chỉnh phải triển khai `IAIRunService` để hỗ trợ khởi chạy Run từ RAG. Dịch vụ không hỗ trợ bị từ chối trước khi bắt đầu lập chỉ mục RAG.
- RAG giữ quan hệ phụ thuộc vào Abstractions, còn các nhà cung cấp đóng gói riêng vẫn giữ phương thức completion ghi đè công khai và điểm mở rộng nhà cung cấp có thể truy cập. Thay đổi này không làm lỗi thời API kho vector, trình tải tài liệu hoặc quản trị máy chủ.

## Tính tương thích và phiên bản chính tiếp theo

| API | Trạng thái hiện tại |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Vẫn công khai và được hỗ trợ, gồm các biến thể interface, nhà cung cấp và RAG. |
| `StartRunAsync` / `AIRun` | API chung để thực thi và điều khiển. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Có cảnh báo lỗi thời; giữ hành vi hiện tại để tương thích. |
| `service.StreamAsync` và RAG `StreamAsync` nhận đầu vào | Vẫn gọi được trong bản cập nhật nhỏ này; dự kiến rút khỏi API công khai ở phiên bản chính tiếp theo. |
| `run.StreamAsync()` | Theo dõi đầu ra của tác vụ có sẵn, không nhận đầu vào yêu cầu. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | Giữ API streaming có kiểu hiện tại; `Stream()` chỉ xuất dữ liệu của nó không phải phương thức yêu cầu dịch vụ cũ. |

Phiên bản chính tiếp theo sẽ thay đổi các điểm vào streaming công khai, đồng thời giữ phần thực thi và hook nhà cung cấp cần thiết. Chuyển một phương thức public thành private hoặc protected vẫn phá vỡ tương thích mã nguồn và nhị phân của bên gọi, kể cả khi giữ thân phương thức. Các tiện ích như chuỗi tin nhắn, lời gọi một lần, tóm tắt, viết lại truy vấn và xếp hạng lại không bị đánh dấu lỗi thời chỉ vì chúng dùng phương thức thực thi hiện có.
