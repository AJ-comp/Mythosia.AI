# Đầu ra có cấu trúc

## Tại sao cần Structured Output?

LLM mặc định trả về văn bản tự do. Nếu ứng dụng của bạn cần **xử lý response theo chương trình** — lưu vào database, truyền cho API khác hay hiển thị trong UI có kiểu — bạn phải tự parse văn bản đó. Điều này dẫn đến regex hoặc `string.Contains` dễ gãy khi model thay đổi cách diễn đạt.

Structured output giải quyết vấn đề này bằng cách yêu cầu model trả về JSON khớp với schema của một kiểu C#. Mythosia.AI tự động tạo schema, inject vào prompt và deserialize — kể cả **tự động sửa JSON** cho các lỗi định dạng nhỏ mà model có thể tạo ra.

### Khi nào nên dùng

- Trích xuất thực thể, phân loại hoặc dữ liệu có cấu trúc từ văn bản thô
- Xây dựng API response có kiểu từ nội dung do AI tạo ra
- Đưa output AI vào pipeline xuôi dòng cần shape dữ liệu cụ thể
- Bất kỳ kịch bản nào cần **output đáng tin cậy, machine-readable** từ model

## Vấn đề nó giải quyết

Giả sử bạn cần trích xuất dữ liệu thời tiết từ response của model. **Không có** structured output:

```csharp
// ❌ Không có structured output — parse thủ công dễ sai
var text = await service.GetCompletionAsync("Thời tiết ở Seoul như thế nào?");
// text = "Thời tiết tại Seoul đang nắng với nhiệt độ 22°C."

// Bây giờ bạn phải tự parse...
var city = "Seoul"; // hardcoded? regex?
var tempMatch = Regex.Match(text, @"(\d+)°C");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// Nếu model nói "hai mươi hai độ" thay vì "22°C"? 💥
```

Cách này gãy khi model thay đổi cách diễn đạt. **Với** structured output:

```csharp
// ✅ Với structured output — type-safe, tự động
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Thời tiết ở Seoul như thế nào?");

Console.WriteLine(result.City);         // Seoul
Console.WriteLine(result.Condition);    // Sunny
Console.WriteLine(result.TemperatureC); // 22
```

Model được yêu cầu trả về JSON khớp với kiểu C# của bạn. Mythosia.AI tự động deserialize. Nếu model tạo ra JSON hơi sai format (thiếu dấu phẩy, có text thừa), **auto-repair** tích hợp sẽ sửa trước khi deserialize.

## Cơ bản

Truyền type parameter vào `GetCompletionAsync`:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Thời tiết ở Seoul như thế nào?");

Console.WriteLine(result.City);        // Seoul
Console.WriteLine(result.Condition);   // Sunny
Console.WriteLine(result.TemperatureC); // 22
```

## Collection

Các kiểu collection dùng trực tiếp — không cần DTO wrapper:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Trích xuất tất cả người và tổ chức từ đoạn văn này: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Streaming + Structured Output

Stream text theo thời gian thực đồng thời nhận đối tượng đã deserialize cuối cùng:

```csharp
var run = service.BeginStream("Tạo tóm tắt sản phẩm").As<ProductDto>();

// Output real-time
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Kết quả đã parse cuối cùng
ProductDto product = await run.Result;
```

## Policy Structured Output

Kiểm soát mức độ nghiêm ngặt yêu cầu model tạo structured output:

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

// Strict: cho phép tối đa ba lần tự động sửa
service.WithStructuredOutputPolicy(StructuredOutputPolicy.Strict);

// NoRetry: trả về lỗi xác thực đầu tiên mà không thử sửa lại
service.WithStructuredOutputPolicy(StructuredOutputPolicy.NoRetry);
```
