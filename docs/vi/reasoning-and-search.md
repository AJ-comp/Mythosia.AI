# Chọn mức suy luận và trả lời kèm nguồn

> Các API này yêu cầu `Mythosia.AI` 7.1.0 trở lên, bao gồm `Mythosia.AI.Abstractions` 3.1.0 trở lên. Các ví dụ RAG yêu cầu `Mythosia.AI.Rag` 7.6.0 trở lên.

## Vì sao cần các tùy chọn này?

Mỗi giai đoạn cần một kiểu hỗ trợ khác nhau. Bản nháp đầu tiên có thể cần câu trả lời nhanh; việc kiểm tra các giả định của nó có thể đáng để dành thêm suy luận. Câu hỏi về sự kiện hôm nay cần thông tin mới, còn câu hỏi về sản phẩm cần tài liệu mô tả sản phẩm đó. Chỉ tăng mức suy luận không giúp mô hình có được một trong hai nguồn này.

Dùng Fluent API chung để diễn đạt nhu cầu của tác vụ tiếp theo. Nhà cung cấp được chọn sẽ chuyển các tùy chọn được hỗ trợ thành API gốc của họ. Ứng dụng có thể tiếp tục dùng `GetCompletionAsync` để nhận câu trả lời hoàn chỉnh hoặc dùng `StartRunAsync` để hiển thị tiến độ và điều khiển chính tác vụ đó.

| Tác vụ cần | Cấu hình |
| --- | --- |
| Soạn nháp nhanh rồi rà soát kỹ hơn | `WithReasoning(...)` |
| Thay đổi mức suy luận mà vẫn giữ tiền tố bộ nhớ đệm hội thoại đủ điều kiện | `WithReasoning(..., cache: CachePreservation.Required)` |
| Thông tin mới từ web | `WithWebSearch()` |
| Câu trả lời dựa trên tài liệu đã được nhà cung cấp lập chỉ mục | `WithFileSearch(store)` |

Các ví dụ giả định dịch vụ đã được khởi tạo với mô hình hỗ trợ tính năng tương ứng. Nhập `Mythosia.AI.Extensions` và `Mythosia.AI.Models`; sự kiện luồng còn dùng `Mythosia.AI.Models.Streaming`.

## Từ bản nháp nhanh đến quá trình rà soát kỹ

Bạn có thể dùng ít suy luận hơn để lập dàn ý, rồi yêu cầu cùng cuộc hội thoại kiểm tra những chi tiết khó:

```csharp
string outline = await service
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("Lập dàn ý cho kế hoạch di chuyển hệ thống.");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("Rà soát kế hoạch đó để xác định các tình huống lỗi và bước khôi phục.");
```

`ReasoningLevel` diễn đạt mức được yêu cầu, không phải ngân sách token cố định hay cam kết về chất lượng câu trả lời. Mỗi mô hình chấp nhận một tập mức riêng. `Auto` giữ hành vi đã cấu hình hoặc mặc định của nhà cung cấp; nó không có nghĩa là tự động thay thế mức không được hỗ trợ. Các thuộc tính ngân sách riêng của nhà cung cấp vẫn dùng được cho mô hình cung cấp ngân sách token thay vì các mức có tên.

Trong cuộc hội thoại dài, thay đổi thiết lập suy luận ở cấp cao nhất của yêu cầu có thể làm mất hiệu lực tiền tố lời nhắc có thể tái sử dụng. Với mô hình được hỗ trợ, hãy yêu cầu cơ chế của nhà cung cấp để thay đổi mức suy luận mà vẫn giữ tiền tố đó:

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("Kiểm tra lại các giả định trong câu trả lời trước.");
```

`Required` là cam kết về cách gửi thay đổi. Nó **không bảo đảm** có lần truy cập trúng bộ nhớ đệm, token miễn phí hay độ trễ thấp hơn: các điều kiện hợp lệ, thời gian lưu giữ và giá của nhà cung cấp vẫn áp dụng. Mô hình không hỗ trợ sẽ ném `NotSupportedException` trước khi gửi yêu cầu. Hãy dùng cùng cuộc hội thoại đang được theo dõi, mô hình và điểm cuối; không cắt bớt hoặc sắp xếp lại lịch sử chứa các cập nhật này. Nếu cần thay đổi những điều kiện đó, hãy bắt đầu cuộc hội thoại mới. Việc tự động nén lịch sử bị chặn khi còn yêu cầu giữ tiền tố.

Mức suy luận giữ bộ nhớ đệm được chấp nhận sẽ trở thành mức đang áp dụng của cuộc hội thoại cho đến khi có thay đổi tường minh tiếp theo. `WithReasoning(level)` thông thường chỉ áp dụng cho yêu cầu logic tương ứng; nó không âm thầm thay thế thiết lập được duy trì đó. Thay đổi diễn ra **giữa các phản hồi của mô hình**. Nó không đổi mức suy luận của phản hồi đang được tạo và tách biệt với `run.SteerAsync`, vốn gửi chỉ dẫn bổ sung đến một Run đang chạy có hỗ trợ tính năng này.

## Trả lời câu hỏi cần thông tin mới

Bật tìm kiếm web gốc khi câu trả lời cần dựa trên thông tin ngoài dữ liệu huấn luyện của mô hình:

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("Tìm thông báo phát hành mới nhất và trích dẫn nguồn.");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

Nhà cung cấp thực thi công cụ được lưu trữ này. Không cần đăng ký hay chạy trình xử lý hàm cục bộ. Bật tìm kiếm chỉ làm cho công cụ khả dụng với mô hình; mô hình có thể quyết định một lời nhắc cụ thể không cần tìm kiếm. Tham chiếu nguồn có sẵn khi nhà cung cấp trả về chúng.

OpenAI và Anthropic cũng chấp nhận `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`. Công cụ Google được tích hợp không cung cấp danh sách miền cho phép này, nên yêu cầu có giới hạn miền sẽ bị từ chối thay vì tìm trên toàn bộ web.

## Trả lời từ tài liệu đã được nhà cung cấp lập chỉ mục

Nếu ứng dụng đã có chỉ mục tài liệu do nhà cung cấp lưu trữ, hãy dùng kho đó để làm căn cứ cho câu trả lời mà không phải tự triển khai vòng truy xuất:

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .WithFileSearch(documents)
    .GetCompletionAsync("Tìm trong tài liệu chính sách của chúng ta. Thời hạn hủy dịch vụ là bao lâu?");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Với Google, dùng `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` cùng dịch vụ Google. Kho thuộc về nhà cung cấp, tài khoản và môi trường triển khai cụ thể; không thể truyền ID kho OpenAI cho Google. Trước khi sử dụng ở đây, hãy tạo kho, tải tài liệu lên và lập chỉ mục qua API hoặc bảng điều khiển của nhà cung cấp. API này chỉ tìm trong các kho hiện có và không tải tệp cục bộ lên.

Tìm kiếm tệp do nhà cung cấp lưu trữ và [quy trình RAG](rag.md) của thư viện đáp ứng những nhu cầu thiết lập khác nhau. Chọn tìm kiếm do nhà cung cấp lưu trữ khi họ đã quản lý chỉ mục. Chọn RAG khi ứng dụng cần kiểm soát bộ nạp, cách chia đoạn, embedding, truy xuất hoặc kho vector. `RagEnabledService` cũng chuyển tiếp `WithReasoning`, `WithWebSearch` và `WithFileSearch` đến câu trả lời cuối cùng; bước viết lại truy vấn nội bộ không kế thừa các tùy chọn này. Tham chiếu truy xuất RAG vẫn nằm trên `RagProcessedQuery`, tách biệt với nguồn `AICitation` do nhà cung cấp trả về.

## Hiển thị tiến độ và lưu lại nguồn

Dùng cùng các tùy chọn trước `StartRunAsync`. Hàm gọi lại văn bản có thể cập nhật giao diện trong khi Run lưu các nguồn cho câu trả lời hoàn chỉnh:

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "Tìm các thông báo gần đây và so sánh những thay đổi.",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` vẫn có sẵn khi không đọc luồng, khi chỉ quan sát văn bản hoặc sau khi bộ đệm quan sát đầu ra đã đầy. Nó chứa các tham chiếu nguồn của nhà cung cấp được thu thập trong Run, gồm cả phản hồi trung gian. `service.LastCitations` hoặc `GetLastCitations()` qua `IAIService` mô tả yêu cầu logic gần nhất; khi hiển thị nhiều câu trả lời, hãy giữ Run tương ứng hoặc sao chép ảnh chụp danh sách trích dẫn của nó.

Để nhận sự kiện nguồn ngay khi chúng đến, chỉ dùng một trình đọc sự kiện:

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "Tìm và giải thích các thay đổi mới nhất.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nNguồn: {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

Các trường trích dẫn có thể là null khi nhà cung cấp không gửi giá trị. `ResponseId`, `OutputIndex` và `ContentIndex` xác định phản hồi và phần nội dung nguồn. `StartIndex` và `EndIndex` giữ độ lệch cục bộ cùng quy ước đánh chỉ mục của nhà cung cấp; chúng **không phải** vị trí trong `run.Result` đã nối lại. Không dùng trực tiếp các giá trị này để đánh chỉ mục vào toàn bộ câu trả lời và đặt trích dẫn.

## Kiểm tra mức hỗ trợ của nhà cung cấp và phạm vi yêu cầu

| Nhà cung cấp đã tích hợp | Mức suy luận có tên | Thay đổi giữ bộ nhớ đệm | Tìm kiếm web | Tìm kiếm tệp |
| --- | --- | --- | --- | --- |
| OpenAI | Các mô hình suy luận được hỗ trợ; mức tùy theo mô hình | GPT-6 Astra Standard, chế độ một tác nhân | Các mô hình Responses được hỗ trợ | Các mô hình Responses được hỗ trợ và kho vector hiện có |
| Anthropic | Mô hình có điều khiển effort gốc | Opus 5 / Fable 5.1 / Mythos 5.1 được hỗ trợ, dùng tính năng beta của nhà cung cấp | Các mô hình Claude được hỗ trợ | Không có bộ điều hợp kho gốc; dùng RAG |
| Google | Các mức Gemini 3; Gemini 2.5 giữ ngân sách riêng của nhà cung cấp | Không hỗ trợ | Các mô hình văn bản Gemini được hỗ trợ | Các mô hình văn bản Gemini được hỗ trợ và kho tìm kiếm tệp hiện có |
| Dịch vụ khác | Thiết lập riêng của nhà cung cấp vẫn dùng được; các tùy chọn chung này cần bộ điều hợp | Không được hỗ trợ bởi nhóm bộ điều hợp này | Không có bộ điều hợp chung | Không có bộ điều hợp chung |

Mô hình, mức, phương thức truyền tải và tổ hợp tính năng được kiểm tra trước khi gửi yêu cầu. Đặc biệt, **không thể kết hợp tìm kiếm web và tìm kiếm tệp của Google trong cùng một yêu cầu**. Thư viện không âm thầm loại bỏ tính năng, hạ mức suy luận, bỏ qua hạn chế miền hay chuyển sang dịch vụ tìm kiếm bên ngoài. Khi được hỗ trợ, công cụ gốc có thể cùng tồn tại với các hàm phía máy khách đã đăng ký; vòng công cụ của Run vẫn tuân theo chính sách hàm và `WithMaxRounds`.

Các phương thức Fluent giữ kiểu cụ thể của dịch vụ và sao chép tùy chọn đầu vào. Các thành phần khác null được hợp nhất cho yêu cầu logic kế tiếp, gồm cả vòng công cụ và các lần sửa đầu ra có cấu trúc, rồi được dùng hết. Tìm kiếm không tự bật cho những lần gọi không liên quan về sau; hãy thêm lại `WithWebSearch` hoặc `WithFileSearch` khi cần. Run đã bắt đầu giữ nguyên thiết lập được chụp lại. Cũng như những cấu hình dịch vụ có thể thay đổi khác, không đổi thiết lập hoặc bắt đầu các yêu cầu chồng lấn trên cùng dịch vụ khi một yêu cầu còn đang chạy.

Các triển khai `IAIService` tùy chỉnh vẫn tương thích. Chúng có thể hỗ trợ bề mặt tính năng này qua `IAIRequestFeatureService`; gọi những phương thức hỗ trợ trên triển khai không có khả năng đó sẽ ném ngoại lệ rõ ràng. API lấy câu trả lời hoàn chỉnh, truyền luồng và cấu hình riêng của nhà cung cấp hiện có vẫn dùng được. Xem [Điều khiển Run](execution-api-transition.md) để biết cách hủy, quan sát và gửi chỉ dẫn bổ sung.

Giao thức nhà cung cấp: [Thay đổi suy luận OpenAI](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [Công cụ OpenAI](https://developers.openai.com/api/docs/guides/tools), [Thay đổi effort Anthropic](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Tìm kiếm web Anthropic](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Dùng nguồn Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Google File Search](https://ai.google.dev/gemini-api/docs/file-search).
