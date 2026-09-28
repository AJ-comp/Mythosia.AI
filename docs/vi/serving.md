# Quản lý máy chủ mô hình đang chạy

Màn hình chọn mô hình hoặc công cụ vận hành cần biết tình trạng máy chủ, các mô hình hiện có và trạng thái nạp trước khi gửi prompt. Các gói Serving thống nhất những kiểm tra này cho Ollama, llama.cpp và vLLM, còn thao tác riêng của từng môi trường phải được gọi rõ ràng.

Dùng các gói này để cung cấp danh sách cho bộ chọn mô hình, hiển thị máy chủ có truy cập được hay không, quản lý việc giữ mô hình trong bộ nhớ khi môi trường hỗ trợ hoặc đọc số liệu của bộ máy. Khi đổi môi trường chạy, mã tra cứu chung của ứng dụng có thể giữ nguyên.

Các máy khách này kết nối tới máy chủ HTTP có sẵn. Việc cài đặt và lưu trữ bộ máy, thuê GPU, trò chuyện và tạo embedding thuộc về các thành phần riêng. Trò chuyện tiếp tục qua dịch vụ AI thích hợp, chẳng hạn `QwenService` cho vLLM; các nhà cung cấp embedding RAG vẫn tách biệt. Quá trình tra cứu không tự động nạp mô hình. SGLang chưa được triển khai.

## Chọn gói

| Gói | Phiên bản | Mục đích |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | Các giao diện chung cho mã ứng dụng hoặc bộ chuyển đổi quản lý tự xây dựng. Không phụ thuộc gói khác. |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Tra cứu Ollama, tải mô hình về và chủ động nạp trước hoặc giải phóng khỏi bộ nhớ. |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | Tra cứu llama.cpp, đọc số liệu và quản lý mô hình trong chế độ Router. |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | Tra cứu vLLM và đọc số liệu qua API chung hoặc API riêng của vLLM hiện có. |

Cả bốn gói đều nhắm tới .NET Standard 2.1. Cài bộ chuyển đổi bạn dùng; gói abstractions sẽ được thêm tự động. Các bộ chuyển đổi phụ thuộc vào giao diện chung và Newtonsoft.Json, độc lập với các gói AI lõi và RAG.

## Tra cứu mà không thay đổi trạng thái máy chủ

Cài gói cụ thể cho môi trường đang dùng. Ví dụ dùng Ollama; với máy chủ khác, chọn `VllmServer` hoặc `LlamaCppServer` trong namespace tương ứng. Quá trình dò tìm chỉ gửi yêu cầu đọc, không gửi lệnh nạp, sinh nội dung hay tải mô hình.

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

Endpoint là địa chỉ gốc của máy chủ, có thể kèm tiền tố đường dẫn của reverse proxy. Khóa API là tùy chọn và được gửi dưới dạng thông tin xác thực Bearer cho từng yêu cầu. Máy khách không thay đổi `HttpClient.DefaultRequestHeaders` hoặc giải phóng `HttpClient` được truyền vào; hãy tái sử dụng và giải phóng nó theo vòng đời ứng dụng. Trong ví dụ Ollama này, thời gian chờ cũng áp dụng cho nội dung phản hồi dạng luồng, vì vậy cần dành đủ thời gian để tải mô hình.

## Giao diện chung và tùy chọn

| Giao diện | Mục đích |
| --- | --- |
| `IModelServer` | Thông tin, tình trạng, mô hình và khả năng quan sát được của máy chủ. |
| `IModelLifecycle` | Lệnh nạp và dỡ mô hình tường minh; tùy chọn. |
| `IModelDownloader` | Tải mô hình tường minh có tiến độ; tùy chọn. |
| `IModelMetricsProvider` | Các mẫu số liệu giữ nguyên nhãn; tùy chọn. |

Việc triển khai một giao diện cho biết máy khách có thao tác đó; `ServingCapabilities` cho biết những gì có thể xác nhận ở endpoint đang kết nối. `Supported` không bảo đảm quyền truy cập hoặc thành công với mọi mô hình. `Unsupported` nghĩa là không khả dụng trong chế độ hoặc endpoint đã quan sát. `Unknown` nghĩa là thiếu bằng chứng, kể cả khi xác thực hoặc kết nối thất bại; không được coi là không hỗ trợ.

`InstallationState` và `LoadState` là hai quan sát khác nhau. `Unknown` không có nghĩa là không tồn tại hoặc đã dỡ khỏi bộ nhớ. `SizeBytes`, `MemoryBytes`, `ContextLength` không được báo cáo sẽ là `null`, không phải số không. Endpoint quản lý hoạt động tốt không chứng minh một mô hình cụ thể đã sẵn sàng suy luận.

## Khác biệt giữa các môi trường

| Thao tác | Ollama | llama.cpp một mô hình | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| Thông tin, tình trạng và danh sách mô hình | Có | Có | Có | Có |
| Chủ động nạp / giải phóng | Có, bằng yêu cầu sinh nội dung rỗng | Không hỗ trợ | Có, sau khi xác nhận chế độ Router | Máy khách này không hỗ trợ |
| Tải mô hình về | Có, với tiến độ dạng luồng | Không hỗ trợ | Thao tác chủ động; cần endpoint tải và sự kiện SSE | Máy khách này không hỗ trợ |
| Số liệu | Chưa triển khai | Số liệu máy chủ khi được bật | Overload theo mô hình trên lớp cụ thể; mô hình phải được nạp sẵn | Số liệu máy chủ khi có sẵn |

Bảng này mô tả các thao tác của máy khách, không bảo đảm mọi phiên bản máy chủ, cấu hình quyền hoặc mô hình đều hỗ trợ. Hãy kiểm tra khả năng của endpoint đang kết nối và xử lý lỗi thao tác.

**Ollama:** `/api/tags` cung cấp mô hình đã đăng ký, `/api/ps` cung cấp mô hình đang chạy. Mô hình từ xa có thể đăng ký mà không có trọng số cục bộ; nếu không có tiến trình cục bộ, trạng thái nạp vẫn chưa biết. Nạp trước dùng yêu cầu `/api/generate` rỗng và keep-alive mặc định của máy chủ. Mô hình chỉ dùng embedding không được tự chuyển sang API khác. Dỡ mô hình dùng `keep_alive: 0` và không xóa tệp. Chưa triển khai số liệu giám sát.

**llama.cpp:** `/props` phải xác nhận rõ chế độ bộ định tuyến trước lệnh vòng đời hoặc tải xuống. Chế độ một mô hình không hỗ trợ các lệnh này và giữ nguyên trạng thái ngủ đã quan sát. Tải qua bộ định tuyến đăng ký `/models/sse` trước, sau đó gửi `POST /models`, và chỉ thành công khi có sự kiện `download_finished` của đúng mô hình. Chỉ truy cập được SSE chưa chứng minh khả năng tải. Số liệu toàn máy chủ dành cho chế độ một mô hình; bộ định tuyến cần overload cụ thể `GetMetricsAsync(modelId, token)`, gửi `autoload=false` để tra cứu không làm nạp mô hình.

**vLLM:** giữ các bí danh phục vụ và trường `root` tùy chọn, nhưng trạng thái cài đặt và nạp chung vẫn chưa biết. Danh sách mô hình và số liệu được kiểm tra bằng phản hồi thực tế; không hỗ trợ vòng đời hay tải xuống. Các phương thức và DTO cũ của `VllmServer` vẫn có trên máy khách cụ thể; phương thức chung về tình trạng, mô hình và số liệu dùng triển khai giao diện tường minh.


## Gọi rõ ràng một thao tác quản lý

Tải mô hình và thay đổi trạng thái lưu trong bộ nhớ tiêu tốn mạng, đĩa hoặc bộ nhớ thiết bị. Chỉ gọi khi ứng dụng cần thao tác đó. Phần tiếp theo của ví dụ Ollama tải một mô hình nhỏ và nạp tạm thời để quan sát trạng thái. Dùng đúng ID mô hình của máy chủ, gồm cả thẻ Ollama hoặc thẻ lượng tử hóa llama.cpp.

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

Ví dụ dùng một mô hình riêng cho kiểm thử rồi giải phóng nó sau đó. Ứng dụng sản xuất tự quyết định thời điểm giải phóng; không giải phóng mô hình mà các yêu cầu khác vẫn sử dụng. Thao tác dọn dẹp có thời hạn riêng và có thể thất bại nếu máy chủ không khả dụng.

Tiến độ mô tả một tệp hoặc một giai đoạn. Bộ đếm byte bị thiếu không phải là số không hay phần trăm của toàn bộ mô hình. Lệnh nạp thành công xác nhận lệnh, không xác nhận trạng thái sẵn sàng hay việc lưu trong bộ nhớ vô thời hạn; hãy quan sát `LoadState` với thời gian chờ giới hạn khi cần biết mô hình đã sẵn sàng. Xem hướng dẫn của gói cụ thể về giao thức tải của llama.cpp Router và giới hạn phiên bản. Có thể thử thao tác được yêu cầu rõ ràng khi trạng thái hỗ trợ là `Unknown` sau khi xác minh cấu hình máy chủ; việc tra cứu khả năng tự nó không bao giờ khởi động thao tác.

## Hủy và lỗi

Truyền token hủy cho cả tra cứu lẫn lệnh. Hủy dừng công việc HTTP và chờ của máy khách này, không bảo đảm hủy phía máy chủ, hoàn tác hoặc xóa các lớp đã tải. Cấu hình `HttpClient` được truyền vào theo thời lượng thao tác; máy khách không nhận quyền sở hữu nó.

Giữ nhãn số liệu khi so sánh mô hình hoặc engine. Số liệu thiếu không phải số không; giá trị có thể gồm `NaN` hoặc vô cực. `ServingException` là kiểu lỗi chung; lỗi quản lý chung không chứa nguyên văn nội dung phản hồi hay thông tin xác thực. Các lệnh vLLM cũ vẫn giữ chi tiết lỗi trước đây.

`GetHealthAsync` phân loại lỗi endpoint thành trạng thái tình trạng, nhưng vẫn truyền tiếp việc hủy từ bên gọi. Các thao tác khác có thể ném `ServingException`; chế độ llama.cpp đã biết là không hỗ trợ có thể ném `NotSupportedException`. Hết thời gian chờ hay yêu cầu thất bại đều không chứng minh thao tác từ xa đã được hoàn tác. Phương thức tải chỉ hoàn tất thành công sau khi môi trường báo đã xong: thành công cuối cùng rồi EOF với Ollama, hoặc sự kiện `download_finished` tương ứng với llama.cpp Router.

## Phạm vi đã kiểm chứng

Kiểm thử ngoại tuyến bao quát các phản hồi thành công được kiểm soát, phản hồi sai định dạng, lỗi và hủy. Các kiểm tra riêng trên máy chủ thật dùng một NVIDIA A40, mô hình Qwen công khai nhỏ và các bản dựng sau:

| Môi trường | Mô hình thử nghiệm | Thao tác quản lý đã kiểm chứng |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | Tra cứu, tải mới, nạp/giải phóng, lỗi thiếu mô hình đã loại thông tin nhạy cảm, hủy trước khi bắt đầu và hủy sau khi có tiến độ tải một phần. |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | Tra cứu, sự kiện tải, nạp/giải phóng, số liệu theo mô hình không tự nạp, lỗi và hủy tải. |
| llama.cpp b11146, một mô hình | Cùng mô hình GGUF | Tra cứu, số liệu máy chủ, hủy và từ chối rõ ràng các lệnh vòng đời Router. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | Tra cứu, số liệu máy chủ và hủy trước khi bắt đầu. |

Các yêu cầu suy luận HTTP gốc ngắn cũng trả về văn bản được tạo trong cả bốn cấu hình. Chúng xác nhận bộ máy hoạt động, không xác nhận bộ chuyển đổi trò chuyện của dịch vụ AI, chất lượng mô hình, thông lượng hay khả năng tương thích với mọi bản dựng. Các cấu hình trên đã được thử nghiệm, không phải phiên bản tối thiểu được hỗ trợ. Kiểm tra hủy tải dùng các mô hình thử nghiệm khác lớn hơn và không khẳng định có hoàn tác phía máy chủ. Lần tải Ollama đầu tiên thất bại; thử lại và tải mới sau khi xóa mô hình đều thành công, nhưng chưa xác định được nguyên nhân chính xác của lỗi đầu tiên.

Dùng [hướng dẫn kiểm chứng thực tế cần bật rõ ràng](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md) để kiểm tra endpoint đã triển khai. Hướng dẫn phân biệt trình chạy quản lý được lưu trong kho mã với các phép thử suy luận và hủy bổ sung dùng khi kiểm chứng. Báo cáo thực thi chi tiết nằm ngoài tài liệu công bố.

## Hướng dẫn từng gói

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — Giao diện quản lý chung và ảnh chụp bất biến của máy chủ, mô hình, khả năng.
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Danh sách và tình trạng Ollama, nạp/dỡ tường minh và tải xuống dạng luồng.
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — Tra cứu llama.cpp, quản lý bộ định tuyến có xác minh và số liệu không tự nạp.
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — Thẻ mô hình, tình trạng, phiên bản và số liệu có nhãn vLLM; giữ API cụ thể cũ.
