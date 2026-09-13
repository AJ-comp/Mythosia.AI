# Gọi hàm (Function Calling)

Chỉ cần kết quả cuối cùng và nút Dừng thì truyền `cancellationToken` vào `GetCompletionAsync`. Dùng Run cho sự kiện tiến độ hoặc chỉ dẫn bổ sung được hỗ trợ. Xem [hủy câu trả lời](completions.md#completion-cancellation).

Để có cấu hình độc lập và tái sử dụng biến thể, dùng [builder yêu cầu](request-building.md). Gọi `CreateRequest(...)` trước `With...`. Thuộc tính và phương thức fluent trên dịch vụ giữ nguyên hành vi.

## Tại sao cần Function Calling?

LLM chỉ có thể sinh văn bản — không thể tự kiểm tra thời tiết, truy vấn database hay gọi API. **Không có** function calling, bạn phải tự parse ý định của model:

```csharp
// ❌ Không có function calling — parse ý định thủ công
var reply = await service.GetCompletionAsync("Thời tiết ở Seoul như thế nào?");
// reply = "Tôi cần kiểm tra dịch vụ thời tiết để trả lời."

// Bạn phải tự xác định user muốn hỏi thời tiết, trích xuất "Seoul", tự gọi API
if (reply.Contains("thời tiết"))
{
    var city = ExtractCity(reply); // regex hoặc keyword dễ sai
    var weather = await weatherApi.GetAsync(city);
    // Hỏi lại với dữ liệu thời tiết đã có...
}
```

Cách này dễ gãy, khó mở rộng và phải đoán trước mọi ý định của người dùng. **Với** function calling, model tự quyết định **khi nào** gọi code của bạn và **truyền tham số** gì:

```csharp
// ✅ Với function calling — model tự xử lý ý định và trích xuất
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Lấy thông tin thời tiết hiện tại cho một địa điểm",
        ("location", "Tên thành phố và quốc gia", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("Thời tiết ở Seoul như thế nào?");
// Model gọi get_weather("Seoul, Korea"), nhận kết quả và trả lời tự nhiên.
```

Bạn định nghĩa **code có thể làm gì**; model tự biết **khi nào** và **cách** dùng.

## Ví dụ nhanh

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Lấy thông tin thời tiết hiện tại cho một địa điểm",
        ("location", "Tên thành phố và quốc gia", required: true),
        (string location) => $"Thời tiết tại {location} đang nắng, 22°C"
    );

var response = await service.GetCompletionAsync("Thời tiết ở Seoul như thế nào?");
// Model gọi get_weather("Seoul, Korea") và tích hợp kết quả vào câu trả lời.
```

## Định nghĩa hàm bằng attribute

Với các hàm phức tạp hơn, dùng attribute `[AiFunction]` và `[AiParameter]`:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Tìm kiếm trong danh mục sản phẩm")]
    public string SearchProducts(
        [AiParameter("Từ khóa tìm kiếm", required: true)] string query,
        [AiParameter("Số kết quả tối đa")] int limit = 5)
    {
        // ... triển khai của bạn
        return JsonSerializer.Serialize(results);
    }
}
```

Rồi đăng ký:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Policy gọi hàm

Kiểm soát khi nào model được phép gọi hàm:

```csharp
using Mythosia.AI.Models.Functions;

// Để model tự quyết (mặc định)
service.FunctionCallMode = FunctionCallMode.Auto;

// Bắt buộc model luôn gọi hàm
service.ForceFunctionName = "search_products";

// Tắt function calling
service.FunctionCallMode = FunctionCallMode.None;
```

[Claude Fable 5.1](fable-5-1.md) bổ sung cập nhật tiến độ, chỉ dẫn theo lượt và chẩn đoán liên kết thinking từ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 cần lời mời truy cập. Cả hai đều từ chối ép chọn công cụ.

## Đăng ký hàng loạt từ một class

Đăng ký tất cả method có `[AiFunction]` từ một object cùng lúc:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // quét instance method có [AiFunction]
```

Với static method:

```csharp
service.WithStaticFunctions<MyTools>();  // quét static method có [AiFunction]
```

## Handler hàm bất đồng bộ

Tất cả overload của `WithFunction` đều có phiên bản `WithFunctionAsync` nhận `Func<..., Task<string>>`:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Lấy dữ liệu từ API bên ngoài",
    ("url", "URL cần fetch", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

Hỗ trợ từ 0 đến 3 tham số, giống phiên bản đồng bộ.

## Tạm thời vô hiệu hóa hàm

Tắt function calling cho một request duy nhất mà không cần xóa đăng ký:

```csharp
// Extension method — trả về kết quả với functions bị tắt
string answer = await service.AskWithoutFunctionsAsync("Trả lời trực tiếp đi");

// Hoặc bật/tắt thuộc tính
service.WithoutFunctions();  // đặt FunctionsDisabled = true
```

## Dùng FunctionBuilder

Xây dựng định nghĩa hàm theo cách lập trình:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Lấy giá cổ phiếu hiện tại")
    .AddParameter("ticker", "string", "Mã cổ phiếu", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

<a id="tool-execution-contract"></a>

## Trả về đối tượng từ công cụ bất đồng bộ và hủy công việc

Công cụ đọc tệp hoặc cơ sở dữ liệu thường trả về đối tượng sau thao tác I/O bất đồng bộ. Nút Dừng cũng cần truyền yêu cầu hủy đến thao tác còn đang chạy. Hàm đồng bộ đã hỗ trợ trả đối tượng; bản cập nhật này làm cho kết quả bất đồng bộ nhất quán và ghi ngoại lệ thành lỗi.

Before: hàm bất đồng bộ phải tự chuyển kết quả thành JSON. Trả về `Task<FileResult>` làm mất giá trị và chỉ gửi `"Success"`.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Đọc tệp văn bản")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: trả thẳng đối tượng và truyền token hủy được tiêm vào thao tác I/O. Ứng dụng không cần triển khai lớp bọc kết quả hoặc adapter mới.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Đọc tệp văn bản")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

Đăng ký bằng `[AiFunction]` hỗ trợ đối tượng thông thường, `Task<T>` và `ValueTask<T>`; giá trị không phải chuỗi được chuyển thành JSON. `string`, `Task<string>` và `ValueTask<string>` giữ nguyên văn bản, không thêm dấu ngoặc kép JSON. `Task` và `ValueTask` không có kết quả cũng được chờ hoàn tất. Hàm đồng bộ trả đối tượng vẫn hoạt động. Giá trị null trở thành `"Done"`; `Task` / `ValueTask` hoàn tất mà không có kết quả trở thành `"Success"`.

Thư viện cũng nhận diện giá trị bất đồng bộ lúc chạy: chờ `Task<T>` được trả về dưới kiểu `Task` hoặc `object`, hay `ValueTask<T>` dưới kiểu `object`, rồi chuyển kết quả theo cùng quy tắc. Mỗi `ValueTask` chỉ được sử dụng một lần.

Thư viện cung cấp tham số `CancellationToken` và loại nó khỏi schema đối số gửi cho mô hình. Đăng ký bằng `WithFunctions(...)` hoặc `WithStaticFunctions<T>()` trên service hay bộ dựng yêu cầu.

Phương thức công cụ `async void` bị từ chối khi đăng ký. Hãy trả `Task` hoặc `ValueTask` để có thể chờ hoàn tất, quan sát lỗi và dọn dẹp xong sau khi hủy.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Đọc report.txt và tóm tắt.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, hủy token truyền cho `StartRunAsync`, hoặc giải phóng run đang hoạt động sẽ truyền đến công cụ cục bộ có hỗ trợ hủy. Hàm phải sử dụng token; không thể cưỡng chế dừng mã bỏ qua nó. Lời gọi chưa bắt đầu được bỏ qua và ghi kết quả hủy; hàm đã bắt đầu vẫn được chờ để giữ cặp lời gọi/kết quả trong lịch sử. Run bị hủy không bắt đầu vòng mô hình tiếp theo.

Nếu callback hủy ném ngoại lệ khi khởi chạy run thất bại hoặc khi giải phóng kết nối MCP, việc dọn dẹp phiên hoặc kênh truyền vẫn được thử. Lỗi ban đầu và lỗi dọn dẹp được giữ lại, gộp trong `AggregateException` khi cần. Các lời gọi bất đồng bộ đồng thời tới `McpConnection.DisposeAsync()` cùng chờ một quá trình dọn dẹp. Kênh truyền được đóng trước khi chờ vòng lặp đọc kết thúc, để các thao tác đọc cần đóng kết nối có thể hoàn tất.

Để lời gọi công cụ đến muộn không chờ mãi khi đóng kết nối, kể từ lúc bắt đầu giải phóng kết nối, các thao tác mới `InitializeAsync`, `RefreshToolsAsync` và `CallToolAsync` bị từ chối bằng `ObjectDisposedException`. Nếu phản hồi có đúng ID yêu cầu nhưng nội dung sai định dạng, phản hồi đó bị bỏ qua còn yêu cầu vẫn được giữ trong danh sách chờ; phản hồi hợp lệ sau đó, lệnh hủy từ bên gọi hoặc việc dọn dẹp kết nối vẫn có thể kết thúc lời gọi. Nếu việc đọc đã kết thúc vì máy chủ đóng luồng hoặc thao tác đọc từ kênh truyền bị lỗi, các thao tác mới sẽ thất bại với `McpException` thay vì chờ phản hồi không thể đến; hãy tạo kết nối mới để tiếp tục.

Khi có lỗi thật, hãy ném ngoại lệ. Bộ thực thi ghi `FunctionCallResult.IsError = true`, thay vì coi chuỗi `"Error: ..."` là thành công. Chuỗi mà hàm cố ý trả về vẫn là kết quả bình thường. Kết quả công cụ bị hủy có cả `IsCancelled = true` và `IsError = true`.

Khi đăng ký bằng mã, dùng overload `WithHandler` có hai đối số:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Đọc tệp văn bản")
    .AddParameter("path", "string", "Đường dẫn tệp", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

Handler chuỗi một đối số hiện có vẫn được hỗ trợ. Khi tạo định nghĩa trực tiếp, có thể gán `Func<Dictionary<string, object>, CancellationToken, Task<string>>` cho `HandlerWithCancellation`. Gán `Handler` hoặc `HandlerWithCancellation` thay thế cùng một handler, không đăng ký hai lần chạy. API cấp thấp này vẫn trả chuỗi; đăng ký phương thức chịu trách nhiệm tự chuyển đối tượng thành JSON.

Đây là xử lý kết quả và hủy hàm .NET cục bộ, không cần tính năng `AllowAsync` gốc của nhà cung cấp. Chỉ dừng đọc `run.StreamAsync(token)` sẽ dừng theo dõi, không dừng run. Xem [hướng dẫn Run](execution-api-transition.md) và [giao thức nhà cung cấp](https://developers.openai.com/api/docs/guides/async-tool-calling).

## Gọi công cụ bất đồng bộ của mô hình

Trong khi chờ truy vấn thời tiết chậm, mô hình vẫn có thể giới thiệu đồ dùng du lịch thông thường không phụ thuộc kết quả thời tiết. Gọi công cụ bất đồng bộ ở cấp mô hình giúp tiếp tục công việc độc lập trong thời gian chờ; quyết định phụ thuộc kết quả vẫn phải đợi kết quả trả về.

GPT-6 Astra và tính năng gọi công cụ bất đồng bộ được hỗ trợ từ `Mythosia.AI` 7.1.0, với các kiểu dùng chung trong `Mythosia.AI.Abstractions` 3.1.0.

`FunctionDefinition.AllowAsync` mặc định là `false`. Chỉ đặt thành `true` hoặc gọi `FunctionBuilder.WithAsync()` khi mô hình có thể tiếp tục công việc khác trong lúc hàm này chạy. Dùng `WithAsync(false)` để tắt quyền này. Có thể dùng lại cùng định nghĩa hàm và handler với nhiều nhà cung cấp.

Đăng ký bằng attribute cũng hỗ trợ quyền này: `[AiFunction("lookup", "Tra cứu dữ liệu", AllowAsync = true)]`.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Trả về thời tiết minh họa của Seoul")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Kiểm tra thời tiết minh họa của Seoul. Trong lúc chờ, hãy liệt kê ba vật dụng cần thiết cho chuyến đi.");
```

Mythosia gửi `async: true` cho GPT-6 Astra qua Responses API. Với mô hình và API chưa hỗ trợ, thư viện bỏ trường này và chờ kết quả của cùng handler, không thay đổi `AllowAsync`. Nhà cung cấp cũng phải đánh dấu lời gọi thực tế là bất đồng bộ (`FunctionCall.IsAsync`); bật quyền không đảm bảo lúc nào cũng chạy bất đồng bộ.

`WithFunctionAsync` đăng ký handler bất đồng bộ của .NET, còn `FunctionExecutionMode.Parallel` điều khiển cách chạy handler cục bộ. Cả hai đều không tự bật quyền này. `AllowAsync` cho phép mô hình tiếp tục trước khi nhận kết quả của hàm. `FunctionExecutionMode` vẫn điều khiển các lời gọi thông thường. Công việc bất đồng bộ được cho phép có thể chạy chồng nhau ngay cả ở chế độ `Sequential`, và toàn bộ nhóm công việc riêng này dùng chung giới hạn `MaxConcurrency`.

Công việc của công cụ được quản lý trong `GetCompletionAsync`, `StreamAsync` cũ nhận đầu vào hoặc `AIRun` do `StartRunAsync` trả về; kết quả được gửi bằng ID lời gọi ban đầu. Trả kết quả cuối cùng thành công hoặc hoàn thành Run đều chờ xử lý xong kết quả còn lại. Run tiếp tục độc lập với việc theo dõi đầu ra, nhưng công cụ không trở thành phiên tác vụ nền công khai tồn tại tách khỏi Run.

Khi dùng công cụ bất đồng bộ, `GetCompletionAsync` tích lũy phần giải thích độc lập trung gian và văn bản cuối cùng theo thứ tự rồi trả về sau khi yêu cầu kết thúc. `StreamAsync` cũ và `run.StreamAsync()` đều thông báo văn bản từng vòng khi nhận được; `(await run.Result).Text` cũng là kết quả nối toàn bộ văn bản của Run.

Công cụ cục bộ có thể trả đối tượng qua `Task<T>` / `ValueTask<T>` và nhận `CancellationToken` được tiêm. `run.Cancel()` hoặc token lúc khởi chạy truyền đến công cụ có hỗ trợ hủy; chỉ dừng đọc luồng thì không. Ngoại lệ được ghi là lỗi. Khi hủy, lời gọi đang chờ được bỏ qua; bước dọn dẹp vẫn chờ công cụ đã chạy nhưng bỏ qua token. Xem [kết quả, lỗi và hủy](function-calling.md#tool-execution-contract).

Khi streaming, handler chỉ bắt đầu sau khi xác nhận lời gọi hàm đầy đủ và ranh giới phản hồi hợp lệ; sau đó vòng mô hình tiếp theo có thể chạy trong lúc công việc bất đồng bộ còn tiếp diễn. Sự kiện lời gọi chưa hoàn chỉnh không kích hoạt thực thi. Nếu mô hình không trả về lời gọi mới mà vẫn còn công việc đang chạy, Mythosia chờ kết quả rồi tiếp tục. Khi còn lời gọi chờ kết quả, cơ chế tự tóm tắt và thử lại do vượt giới hạn ngữ cảnh bị tắt để tránh làm mất lời gọi chưa hoàn tất khỏi lịch sử.

Perplexity: [Điều khiển nghiên cứu và công cụ](perplexity.md).
