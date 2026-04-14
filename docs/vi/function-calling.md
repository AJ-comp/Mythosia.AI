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

[AiFunction("search_products", "Tìm kiếm trong danh mục sản phẩm")]
public static string SearchProducts(
    [AiParameter("Từ khóa tìm kiếm", required: true)] string query,
    [AiParameter("Số kết quả tối đa")] int limit = 5)
{
    // ... triển khai của bạn
    return JsonSerializer.Serialize(results);
}
```

Rồi đăng ký:

```csharp
service.AddFunction(SearchProducts);
```

## Policy gọi hàm

Kiểm soát khi nào model được phép gọi hàm:

```csharp
using Mythosia.AI.Models.Functions;

// Để model tự quyết (mặc định)
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// Bắt buộc model luôn gọi hàm
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// Tắt function calling
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
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

var fn = FunctionBuilder
    .Create("get_stock_price", "Lấy giá cổ phiếu hiện tại")
    .AddParameter("ticker", "Mã cổ phiếu", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
