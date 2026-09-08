# Gọi hàm (Function Calling)

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

Khi dùng công cụ bất đồng bộ, `GetCompletionAsync` tích lũy phần giải thích độc lập trung gian và văn bản cuối cùng theo thứ tự rồi trả về sau khi yêu cầu kết thúc. `StreamAsync` cũ và `run.StreamAsync()` đều thông báo văn bản từng vòng khi nhận được; `run.Result` cũng là kết quả nối toàn bộ văn bản của Run.

Handler không nhận token hủy. Khi hủy, hết thời gian, gặp lỗi hoặc kết thúc sớm luồng cũ nhận đầu vào, bước dọn dẹp vẫn chờ các handler đã bắt đầu chạy xong. Chỉ ngừng đọc `run.StreamAsync()` không dừng thực thi; để hủy Run, dùng `run.Cancel()`, token lúc khởi chạy hoặc giải phóng Run. Tích hợp này áp dụng cho hàm đã đăng ký; xem giao thức trong [hướng dẫn API chính thức](https://developers.openai.com/api/docs/guides/async-tool-calling).

Khi streaming, handler chỉ bắt đầu sau khi xác nhận lời gọi hàm đầy đủ và ranh giới phản hồi hợp lệ; sau đó vòng mô hình tiếp theo có thể chạy trong lúc công việc bất đồng bộ còn tiếp diễn. Sự kiện lời gọi chưa hoàn chỉnh không kích hoạt thực thi. Nếu mô hình không trả về lời gọi mới mà vẫn còn công việc đang chạy, Mythosia chờ kết quả rồi tiếp tục. Khi còn lời gọi chờ kết quả, cơ chế tự tóm tắt và thử lại do vượt giới hạn ngữ cảnh bị tắt để tránh làm mất lời gọi chưa hoàn tất khỏi lịch sử.
