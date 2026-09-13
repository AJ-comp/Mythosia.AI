# Perplexity: câu trả lời có nguồn, tìm kiếm và embedding

Dùng Perplexity khi câu trả lời cần thông tin mới và nguồn để người đọc kiểm chứng. `PerplexityService` gọi Agent API; tìm kiếm và embedding độc lập giúp xây dựng khả năng truy xuất tài liệu cho mô hình trả lời mà bạn chọn.

## Chọn công việc trước

Trả lời, lấy trang web và tạo vector cho chỉ mục là các công việc khác nhau. Chọn thành phần phụ trách, thay vì gọi mô hình trả lời cho mọi lần truy xuất.

| Nhu cầu | Thành phần |
| --- | --- |
| Câu trả lời nghiên cứu có nguồn | `PerplexityService` |
| Trang web cho mô hình hoặc giao diện khác | `PerplexitySearchClient` |
| Vector đoạn độc lập cho RAG | `PerplexityEmbeddingProvider` |
| Vector giữ ngữ cảnh đoạn lân cận cùng tài liệu | `PerplexityContextualizedEmbeddingProvider` |

Cài `Mythosia.AI`; ví dụ embedding cần thêm `Mythosia.AI.Rag`. Cung cấp khóa API và `HttpClient` do ứng dụng quản lý. Ví dụ dùng `apiKey`, `httpClient`, `cancellationToken` của ứng dụng.

## Trả lời bằng preset Agent

Preset kết hợp mô hình, chỉ dẫn, công cụ, mức suy luận và ngân sách do nhà cung cấp duy trì. `Fast` dùng để tra cứu nhanh, `Low` cho nghiên cứu thường ngày, `Medium` cho so sánh nhiều bước, `High` / `XHigh` cho nghiên cứu sâu. `WideResearch` phù hợp với nghiên cứu diện rộng; nên chạy nền khi dự kiến tác vụ kéo dài. Đây không phải ID mô hình.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "So sánh các phương pháp tái chế pin gần đây và dẫn nguồn.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Dùng `GetCompletionAsync` cho kết quả cuối, `StreamAsync` của dịch vụ cho streaming hiện có, hoặc `StartRunAsync` để quan sát và hủy. `(await run.Result).Text` nối văn bản đã phát. `run.Citations` và `LastCitations` giữ nguồn dù không đọc sự kiện trích dẫn. Sự kiện suy luận chỉ chứa nội dung được nhà cung cấp công khai, tùy mô hình.

`AIRunResult.RequestedModel` là mô hình đơn được gửi tường minh trong yêu cầu và chụp khi bắt đầu, gồm cả cấu hình ghi đè mô hình của nhà cung cấp. Giá trị là `null` nếu preset, profile hoặc định tuyến phía máy chủ chọn mô hình mà không gửi một trường mô hình đơn tường minh (ví dụ danh sách Perplexity `Models`). Giá trị này độc lập với mô hình thực tế trong phản hồi ở `Model`.

## Điều khiển nghiên cứu và công cụ

`WithPerplexityOptions(...)` đặt cấu hình lâu dài, được sao chép cho mỗi yêu cầu logic. `WithReasoning(...)` và `WithWebSearch(...)` chung áp dụng cho yêu cầu tiếp theo, gồm vòng hàm client và sửa đầu ra có kiểu. Viết lại truy vấn RAG nội bộ không nhận thiết lập tìm kiếm của câu trả lời cuối.

`UsePreset(...)` chọn preset trực tiếp. Preset/profile tự chọn mô hình; `ModelOverride` ghi đè rõ ràng. `DisableWebSearch` chỉ bỏ công cụ mặc định của adapter, không bảo đảm tắt tìm kiếm tích hợp trong preset. Tùy mô hình, dùng `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`; `None` và mức suy luận tường minh của Sonar trực tiếp bị từ chối. `DisableReasoning` nội bộ dùng mức thấp có sẵn hoặc bỏ tùy chọn, không bảo đảm tắt hẳn suy luận.

| Tùy chọn | Mục đích |
| --- | --- |
| `Preset` / `ModelOverride` | Chọn cấu hình nghiên cứu hoặc ghi đè mô hình bằng ID nhà cung cấp/mô hình. |
| `MaxSteps` | Giới hạn vòng lặp phía nhà cung cấp; 0 dùng mặc định. Tách biệt với `WithMaxRounds` cho lượt tiếp nối hàm cục bộ. |
| `ReasoningEffort` | Điều chỉnh suy luận; `Auto` bỏ qua ghi đè. Mức hỗ trợ tùy mô hình thực tế. |
| `DisableWebSearch` / `Tools` | Điều khiển công cụ web mặc định của adapter và công cụ phía nhà cung cấp được chỉ định. |
| `Models` | Đặt 1–5 mô hình dự phòng theo ưu tiên, thay cho mô hình đơn. Mọi ứng viên phải hỗ trợ tính năng yêu cầu. |
| `Profile` | Dùng cấu hình máy chủ đã lưu và tùy chọn cố định phiên bản. Không kết hợp với `Preset`. |
| `ServiceTier` | Yêu cầu xử lý mặc định, flex hoặc ưu tiên; nhà cung cấp có thể bỏ qua mức không hỗ trợ. |
| `Skills` | Dùng kỹ năng tích hợp, inline hoặc tùy chỉnh đã tải lên. Tài nguyên tùy chỉnh thuộc tài khoản Perplexity. |
| `LanguagePreference` / `PromptCacheKey` | Đặt ngôn ngữ hoặc gợi ý định tuyến cache; không bảo đảm cache hit. |
| `PreviousResponseId` / `Store` | Tiếp nối phản hồi đã xong hoặc điều khiển khả năng truy vấn lại. Dùng `StatelessMode` và chỉ gửi lượt mới. `Store = false` không tắt lưu trữ phía nhà cung cấp. |

`PerplexityHostedTool` nhận `Type` được hỗ trợ và `Parameters` JSON theo tài liệu: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox`, `mcp`. Máy chủ MCP và connector được quản lý chạy qua nhà cung cấp; thông tin xác thực, quyền và tài nguyên phải phù hợp kết nối. Đăng ký hàm ứng dụng bằng `Functions` / bộ dựng hàm. Bước phía nhà cung cấp và handler cục bộ có chủ thể thực thi khác nhau.

Các factory `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, `Connector` tạo công cụ. MCP chạy không chờ phê duyệt; giới hạn `allowedTools` khi cần. Connector là tính năng xem trước, tham chiếu tích hợp đã kết nối.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "Đọc tài liệu dự án và so sánh các khả năng liên quan.");
```

Công cụ, suy luận, ảnh và schema phụ thuộc mô hình. `WithFileSearch` không phải adapter kho vector Perplexity. Tệp sandbox, tệp đính kèm và dữ liệu MCP là tài nguyên riêng, không tự thành kho tìm kiếm tệp chung.

## Nguồn, ảnh và câu trả lời có cấu trúc

Dùng completion hoặc streaming có kiểu khi cần trường JSON. Adapter gửi schema gốc và giữ quy trình sửa. Các phần phản hồi và ID công cụ được giữ để tiếp nối; tránh xóa hoặc đổi thứ tự lịch sử giao thức. Ảnh dùng `Message` và `ImageContent` với byte JPEG/PNG/WebP/GIF hoặc URL HTTPS tùy mô hình. Đây không phải yêu cầu tạo ảnh.

Dấu vết phản hồi gốc được giữ trong siêu dữ liệu lịch sử, nhưng yêu cầu tiếp theo chỉ gửi lại các mục đầu vào được phép: `message`, `function_call` và `function_call_output`; dùng `PreviousResponseId` để tiếp tục toàn bộ trạng thái tác vụ do nhà cung cấp lưu giữ.

Trích dẫn có thể chỉ đến web hoặc nguồn khác của nhà cung cấp. Vị trí thuộc từng phản hồi/phần nội dung, không phải kết quả Run đã nối. Giữ URL và tiêu đề để hiển thị, kiểm chứng; nguồn trả về không tự xác thực mọi khẳng định.

## Giữ tác vụ dài tiếp tục chạy

Dùng thực thi nền của nhà cung cấp để nghiên cứu tiếp tục sau mất kết nối tạm thời hoặc lấy lại bằng ID. `AIRun` cục bộ điều khiển client hiện tại; phản hồi nền có vòng đời máy chủ riêng. Dừng đọc stream chỉ dừng quan sát. Muốn dừng việc từ xa, hãy hủy job rõ ràng.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "So sánh các phương pháp tái chế pin gần đây và dẫn nguồn.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` chụp đầu vào mà không thêm lịch sử, từ chối hàm cục bộ đang bật hoặc `Store = false`. `GetResponseAsync` đọc một lần; `WaitForCompletionAsync` thăm dò đến trạng thái kết thúc. Giữ `Id`, `LastSequenceNumber`; nối lại bằng `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`. `CancelAsync` hủy job từ xa; hủy token đọc/thăm dò chỉ dừng thao tác client đó. `LastResponse` có văn bản, trạng thái, usage, trích dẫn và `OutputJson`. Kiểm tra trạng thái trước khi dùng câu trả lời.

Dùng `ListFilesAsync` và `DownloadFileAsync(fileId)` để đọc tệp sandbox. Dịch vụ còn có `GetAgentResponseAsync`, `GetResponseFilesAsync`, `GetResponseFileContentAsync`. Chúng đọc sản phẩm phản hồi, không tạo hoặc tìm kho vector.

Với skill Office tích hợp, hãy dùng đường chạy nền trong hướng dẫn này: `StartBackgroundAsync`, rồi `WaitForCompletionAsync` / `GetResponseAsync` và các phương thức tệp. Dấu vết công cụ nội bộ trong các phản hồi đó có thể không phân biệt được với lời gọi hàm cục bộ thông thường.

Gửi, lấy, hủy và nối lại stream nền không bật `SteerAsync` giữa phản hồi hoặc công cụ client bất đồng bộ gốc. Nối lại quan sát phản hồi hiện có, không gửi lại tác vụ. Giữ ID phản hồi và cursor của nhà cung cấp.

## Tìm kiếm mà không tạo câu trả lời

`PerplexitySearchClient` lấy trang cho xếp hạng, giao diện hoặc LLM khác. Nó không gọi mô hình trả lời và không sửa lịch sử `PerplexityService`.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "phương pháp tái chế pin",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` nhận một hoặc nhiều truy vấn; hỗ trợ Web/People, quốc gia, tên miền, ngôn ngữ, ngày xuất bản/cập nhật và độ mới. Chọn `ContentSize` hoặc `MaxTokens` / `MaxTokensPerPage`, không dùng đồng thời. Kết quả gồm thứ tự, tiêu đề, URL, đoạn trích và ngày của nhà cung cấp; thứ tự không phải điểm liên quan.

`ContentSize` chỉ hỗ trợ tìm kiếm Web. Hãy bỏ tùy chọn này với People; client sẽ từ chối tổ hợp đó trước khi gửi.

## Dùng vector trong chỉ mục riêng

Embedding tiêu chuẩn xử lý đoạn độc lập và triển khai `IEmbeddingProvider` cho bộ dựng RAG hiện có. Embedding ngữ cảnh giữ thứ tự đoạn và nhóm tài liệu. API riêng ngăn gộp phẳng các tài liệu không liên quan.

| Hằng mô hình | ID nhà cung cấp | Số chiều mặc định |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Có thể trả hàng trong vòng 30 ngày.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("Tôi có thể trả hàng trong bao lâu?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Có thể trả hàng trong vòng 30 ngày.", "Giữ hóa đơn khi yêu cầu hoàn tiền." },
    new[] { "Giao hàng tiêu chuẩn mất ba ngày.", "Giao nhanh hoạt động vào ngày làm việc." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "Tôi có thể trả hàng trong bao lâu?", cancellationToken);
```

Dùng cùng mô hình, số chiều và mã hóa cho tài liệu và truy vấn. `GetQueryEmbeddingAsync` gửi truy vấn như tài liệu riêng đến cùng mô hình ngữ cảnh. Kết quả giữ thứ tự tài liệu và đoạn; không tự nối vào bộ dựng RAG đầu vào phẳng.

API float giải mã vector base64 signed-int8 và chuẩn hóa cho tương đồng. API binary tường minh trả bit đóng gói và dùng khoảng cách Hamming, không ngầm coi bit là tọa độ float. Số chiều đầy đủ là 1024 cho 0.6B và 2560 cho 4B; giảm chiều theo giới hạn nhà cung cấp. Giới hạn lô, độ dài, tổng token và tốc độ tài khoản vẫn áp dụng.

API binary gồm `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync` và `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync` theo ngữ cảnh. `PerplexityBinaryEmbedding` có `Dimensions`, bản sao `ToArray()` và `HammingDistance`; khoảng cách nhỏ hơn nghĩa là giống hơn. Số chiều binary phải chia hết cho tám. Tối đa 512 văn bản tiêu chuẩn, hoặc 512 tài liệu/16.000 đoạn ngữ cảnh. Nhà cung cấp kiểm tra 32K token mỗi văn bản/tài liệu và 120K tổng.

## Chuyển mã Sonar hiện có

Bản phát hành chủ động xóa adapter cũ trước ngày dừng endpoint được công bố là 27 tháng 9 năm 2026. `PerplexityService` gọi `/v1/agent`; `AIModels.Perplexity.Sonar` giờ là `perplexity/sonar`. Hàm tìm kiếm và kiểu phản hồi riêng Sonar đã bị xóa. Dùng completion/Run/trích dẫn chung, preset Agent và `PerplexitySearchClient` độc lập.

Ánh xạ: Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`. Không bảo đảm văn bản, chi phí hay hành vi giống hệt. Preset động có thể thay đổi; dùng mô hình tường minh hoặc profile có phiên bản nếu cần cố định.

Không hỗ trợ steering gốc, công cụ client bất đồng bộ gốc và `CachePreservation.Required`. Router/Gateway nằm ngoài tích hợp. Khả dụng tùy nhà cung cấp, mô hình và tài khoản; hướng dẫn không khẳng định mọi tổ hợp đã qua kiểm thử API thật có phí.

Profile, skill tùy chỉnh và connector cần tài nguyên đã đăng ký trong tài khoản. Hình dạng yêu cầu được kiểm tra bằng unit test; chưa xác minh lời gọi API thực tế thành công với các tài nguyên này.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
