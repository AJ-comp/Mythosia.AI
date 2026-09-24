# Chọn mức suy luận và trả lời kèm nguồn

> Grok 4.7 là phần bổ sung chưa phát hành; xem [chọn mô hình, suy luận và tốc độ xử lý](providers.md#grok-47).

> GPT-6 Sol/Luna là phần bổ sung chưa phát hành. Xem [chọn mô hình và yêu cầu phiên bản](providers.md#gpt-6-sol-luna).

[Claude Opus 5.5](providers.md#claude-opus-55) là phần bổ sung chưa phát hành: luôn bật suy luận, mặc định mức medium và ẩn hiển thị. Cần yêu cầu rõ tiến độ đọc được; mặc định và quy tắc gắn với mô hình khác Fable 5.1.

Để có cấu hình độc lập và tái sử dụng biến thể, dùng [builder yêu cầu](request-building.md). Gọi `CreateRequest(...)` trước `With...`. Thuộc tính và phương thức fluent trên dịch vụ giữ nguyên hành vi.

> Các API này yêu cầu `Mythosia.AI` 7.1.0 trở lên, bao gồm `Mythosia.AI.Abstractions` 3.1.0 trở lên. Các ví dụ RAG yêu cầu `Mythosia.AI.Rag` 7.6.0 trở lên.

> Ví dụ `CreateRequest` cần phiên bản hiện đang phát triển. Bản 7.1 trước đây giới thiệu Run và tùy chọn chung chưa có builder. Gói cũ có thể tiếp tục dùng các overload của dịch vụ.

[Claude Fable 5.1](fable-5-1.md) bổ sung cập nhật tiến độ, chỉ dẫn theo lượt và chẩn đoán liên kết thinking từ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 cần lời mời truy cập. Cả hai đều từ chối ép chọn công cụ.

Với yêu cầu nhạy cảm về thời gian chờ, chọn [tốc độ xử lý](request-building.md#inference-speed). `WithSpeed` giữ mô hình và mức suy luận; `Processing` báo chế độ thực tế. Fast là tùy chọn trả phí trên các tổ hợp được hỗ trợ.

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
    .CreateRequest("Lập dàn ý cho kế hoạch di chuyển hệ thống.")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("Rà soát kế hoạch đó để xác định các tình huống lỗi và bước khôi phục.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash chấp nhận `Low`, `Medium`, `High` qua `WithReasoning`; không hỗ trợ `Minimal`, `None` hay `CachePreservation.Required`. Completion, streaming, Run, công cụ và tìm kiếm tích hợp dùng các luồng hiện có cùng giới hạn kết hợp của Google. Xem [ví dụ cấu hình Google](providers.md#google-googleaiservice).

`ReasoningLevel` diễn đạt mức được yêu cầu, không phải ngân sách token cố định hay cam kết về chất lượng câu trả lời. Mỗi mô hình chấp nhận một tập mức riêng. `Auto` giữ hành vi đã cấu hình hoặc mặc định của nhà cung cấp; nó không có nghĩa là tự động thay thế mức không được hỗ trợ. Các thuộc tính ngân sách riêng của nhà cung cấp vẫn dùng được cho mô hình cung cấp ngân sách token thay vì các mức có tên.

Trong cuộc hội thoại dài, thay đổi thiết lập suy luận ở cấp cao nhất của yêu cầu có thể làm mất hiệu lực tiền tố lời nhắc có thể tái sử dụng. Với mô hình được hỗ trợ, hãy yêu cầu cơ chế của nhà cung cấp để thay đổi mức suy luận mà vẫn giữ tiền tố đó:

```csharp
string review = await service
    .CreateRequest("Kiểm tra lại các giả định trong câu trả lời trước.")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required` là cam kết về cách gửi thay đổi. Nó **không bảo đảm** có lần truy cập trúng bộ nhớ đệm, token miễn phí hay độ trễ thấp hơn: các điều kiện hợp lệ, thời gian lưu giữ và giá của nhà cung cấp vẫn áp dụng. Mô hình không hỗ trợ sẽ ném `NotSupportedException` trước khi gửi yêu cầu. Hãy dùng cùng cuộc hội thoại đang được theo dõi, mô hình và điểm cuối; không cắt bớt hoặc sắp xếp lại lịch sử chứa các cập nhật này. Nếu cần thay đổi những điều kiện đó, hãy bắt đầu cuộc hội thoại mới. Việc tự động nén lịch sử bị chặn khi còn yêu cầu giữ tiền tố.

Mức suy luận giữ bộ nhớ đệm được chấp nhận sẽ trở thành mức đang áp dụng của cuộc hội thoại cho đến khi có thay đổi tường minh tiếp theo. `WithReasoning(level)` thông thường chỉ áp dụng cho yêu cầu logic tương ứng; nó không âm thầm thay thế thiết lập được duy trì đó. Thay đổi diễn ra **giữa các phản hồi của mô hình**. Nó không đổi mức suy luận của phản hồi đang được tạo và tách biệt với `run.SteerAsync`, vốn gửi chỉ dẫn bổ sung đến một Run đang chạy có hỗ trợ tính năng này.

## Trả lời câu hỏi cần thông tin mới

Bật tìm kiếm web gốc khi câu trả lời cần dựa trên thông tin ngoài dữ liệu huấn luyện của mô hình:

```csharp
string answer = await service
    .CreateRequest("Tìm thông báo phát hành mới nhất và trích dẫn nguồn.")
    .WithWebSearch()
    .GetCompletionAsync();

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
    .CreateRequest("Tìm trong tài liệu chính sách của chúng ta. Thời hạn hủy dịch vụ là bao lâu?")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Với Google, dùng `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` cùng dịch vụ Google. Kho thuộc về nhà cung cấp, tài khoản và môi trường triển khai cụ thể; không thể truyền ID kho OpenAI cho Google. Trước khi sử dụng ở đây, hãy tạo kho, tải tài liệu lên và lập chỉ mục qua API hoặc bảng điều khiển của nhà cung cấp. API này chỉ tìm trong các kho hiện có và không tải tệp cục bộ lên.

`CreateRequest(...).With...` giữ tùy chọn trong builder độc lập. Tái sử dụng builder sẽ áp dụng chúng cho mỗi lần thực thi và các vòng công cụ. `service.WithReasoning`, `service.WithWebSearch` và `service.WithFileSearch` cũ vẫn trả về kiểu dịch vụ cụ thể và tiêu thụ tùy chọn trong yêu cầu logic tiếp theo. Chúng vẫn dùng được với `IAIRequestFeatureService` và wrapper RAG. Cả hai API đều không bảo đảm chạy đồng thời trên cùng dịch vụ.

## Hiển thị tiến độ và lưu lại nguồn

Dùng cùng các tùy chọn trước `StartRunAsync`. Hàm gọi lại văn bản có thể cập nhật giao diện trong khi Run lưu các nguồn cho câu trả lời hoàn chỉnh:

```csharp
await using var run = await service
    .CreateRequest("Tìm các thông báo gần đây và so sánh những thay đổi.")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` vẫn có sẵn khi không đọc luồng, khi chỉ quan sát văn bản hoặc sau khi bộ đệm quan sát đầu ra đã đầy. Nó chứa các tham chiếu nguồn của nhà cung cấp được thu thập trong Run, gồm cả phản hồi trung gian. `service.LastCitations` hoặc `GetLastCitations()` qua `IAIService` mô tả yêu cầu logic gần nhất; khi hiển thị nhiều câu trả lời, hãy giữ Run tương ứng hoặc sao chép ảnh chụp danh sách trích dẫn của nó.

Để nhận sự kiện nguồn ngay khi chúng đến, chỉ dùng một trình đọc sự kiện:

```csharp
await using var run = await service
    .CreateRequest("Tìm và giải thích các thay đổi mới nhất.")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nNguồn: {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

Các trường trích dẫn có thể là null khi nhà cung cấp không gửi giá trị. `ResponseId`, `OutputIndex` và `ContentIndex` xác định phản hồi và phần nội dung nguồn. `StartIndex` và `EndIndex` giữ độ lệch cục bộ cùng quy ước đánh chỉ mục của nhà cung cấp; chúng **không phải** vị trí trong `(await run.Result).Text` đã nối lại. Không dùng trực tiếp các giá trị này để đánh chỉ mục vào toàn bộ câu trả lời và đặt trích dẫn.

## Kiểm tra mức hỗ trợ của nhà cung cấp và phạm vi yêu cầu

| Nhà cung cấp đã tích hợp | Mức suy luận có tên | Thay đổi giữ bộ nhớ đệm | Tìm kiếm web | Tìm kiếm tệp |
| --- | --- | --- | --- | --- |
| OpenAI | Các mô hình suy luận được hỗ trợ; mức tùy theo mô hình | GPT-6 Astra / Sol / Luna Standard, chế độ một tác nhân | Các mô hình Responses được hỗ trợ | Các mô hình Responses được hỗ trợ và kho vector hiện có |
| Anthropic | Mô hình có điều khiển effort gốc | Opus 5 / 5.5 / Fable 5.1 / Mythos 5.1 được hỗ trợ, dùng tính năng beta của nhà cung cấp | Các mô hình Claude được hỗ trợ | Không có bộ điều hợp kho gốc; dùng RAG |
| Google | Các mức Gemini 3; Gemini 2.5 giữ ngân sách riêng của nhà cung cấp | Không hỗ trợ | Các mô hình văn bản Gemini được hỗ trợ | Các mô hình văn bản Gemini được hỗ trợ và kho tìm kiếm tệp hiện có |
| xAI | Grok 4.7 / 4.6: `Auto`, `Low`, `Medium`, `High`, `XHigh` | Không hỗ trợ | Không có adapter chung | Không có adapter chung |
| DeepSeek | Flash / V4 Pro: `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max`; ánh xạ Low/High/Max gốc | Không hỗ trợ | Chưa có bộ điều hợp chung | Chưa có bộ điều hợp chung |
| Perplexity | `Auto` hoặc `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max` tùy mô hình; Sonar không hỗ trợ effort tường minh | Không hỗ trợ | Agent `web_search` | Chưa có bộ điều hợp chung |
| Dịch vụ khác | Thiết lập riêng của nhà cung cấp vẫn dùng được; các tùy chọn chung này cần bộ điều hợp | Không được hỗ trợ bởi nhóm bộ điều hợp này | Không có bộ điều hợp chung | Không có bộ điều hợp chung |

Bộ điều hợp kiểm tra các giới hạn đã biết về mô hình, mức, phương thức truyền tải và tổ hợp trước khi gửi; nhà cung cấp xác minh các quy tắc riêng của mô hình chưa thể kiểm tra cục bộ. Đặc biệt, **không thể kết hợp tìm kiếm web và tìm kiếm tệp của Google trong cùng một yêu cầu**. Thư viện không âm thầm loại bỏ tính năng, hạ mức suy luận, bỏ qua hạn chế miền hay chuyển sang dịch vụ tìm kiếm bên ngoài. Khi được hỗ trợ, công cụ gốc có thể cùng tồn tại với các hàm phía máy khách đã đăng ký; vòng công cụ của Run vẫn tuân theo chính sách hàm và `WithMaxRounds`.

`CreateRequest(...).With...` giữ tùy chọn trong builder độc lập. Tái sử dụng builder sẽ áp dụng chúng cho mỗi lần thực thi và các vòng công cụ. `service.WithReasoning`, `service.WithWebSearch` và `service.WithFileSearch` cũ vẫn trả về kiểu dịch vụ cụ thể và tiêu thụ tùy chọn trong yêu cầu logic tiếp theo. Chúng vẫn dùng được với `IAIRequestFeatureService` và wrapper RAG. Cả hai API đều không bảo đảm chạy đồng thời trên cùng dịch vụ.

Các triển khai `IAIService` tùy chỉnh vẫn tương thích. Chúng có thể hỗ trợ bề mặt tính năng này qua `IAIRequestFeatureService`; gọi những phương thức hỗ trợ trên triển khai không có khả năng đó sẽ ném ngoại lệ rõ ràng. API lấy câu trả lời hoàn chỉnh, truyền luồng và cấu hình riêng của nhà cung cấp hiện có vẫn dùng được. Xem [Điều khiển Run](execution-api-transition.md) để biết cách hủy, quan sát và gửi chỉ dẫn bổ sung.

Giao thức nhà cung cấp: [Thay đổi suy luận OpenAI](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [Công cụ OpenAI](https://developers.openai.com/api/docs/guides/tools), [Thay đổi effort Anthropic](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Tìm kiếm web Anthropic](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Dùng nguồn Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Google File Search](https://ai.google.dev/gemini-api/docs/file-search).

Với Perplexity, mô hình thực sự được chọn quyết định mức effort; máy chủ có thể từ chối tổ hợp không tương thích. Không hỗ trợ `None`. Tìm kiếm mặc định và công cụ preset/profile là cấu hình lâu dài của nhà cung cấp; tùy chọn chung cho yêu cầu không tắt chúng.

Perplexity: [Perplexity Agent API, tìm kiếm và embedding](perplexity.md).
