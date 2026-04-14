# AIRequestProfile

## Là gì?

`AIRequestProfile` cho phép bạn ghi đè các tham số tạo nội dung — temperature, max token, stateless mode, function calling — **chỉ cho một request duy nhất**. Cài đặt toàn cục của service không bị ảnh hưởng.

## Vấn đề nó giải quyết

Hãy tưởng tượng bạn có một chatbot được cấu hình cho hội thoại sáng tạo:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("Bạn là trợ lý viết văn sáng tạo.");
```

Giờ RAG pipeline của bạn cần viết lại query của user với temperature thấp và không có lịch sử. **Không có** `AIRequestProfile`, bạn phải làm thế này:

```csharp
// ❌ Không có AIRequestProfile — quản lý trạng thái thủ công
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("Viết lại query này: ...");

// Khôi phục tất cả — dễ quên, không thread-safe
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

Cách này dài dòng, dễ sai và **không hoạt động trong multi-threaded** (ví dụ web server xử lý nhiều user đồng thời). Nếu có exception trước khi khôi phục, service bị để lại trong trạng thái sai.

**Với** `AIRequestProfile`, chỉ một dòng:

```csharp
// ✅ Với AIRequestProfile — gọn gàng và an toàn
var rewritten = await service.GetCompletionAsync("Viết lại query này: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

Cài đặt toàn cục của service không bao giờ bị đụng đến. Không cần dọn dẹp. Thread-safe.

## Các thuộc tính

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Ghi đè temperature
    MaxTokens = 256,          // Ghi đè max output token
    Stateless = true,         // Không thêm lượt này vào lịch sử hội thoại
    DisableFunctions = true,  // Bỏ qua function calling cho request này
    DisableReasoning = true   // Bỏ qua reasoning/chain-of-thought cho request này
};

var response = await service.GetCompletionAsync("Prompt của bạn", profile);
```

Tất cả thuộc tính đều tùy chọn — chỉ set những gì bạn muốn ghi đè. Phần còn lại dùng giá trị hiện tại của service.

## Profile định sẵn

Cho các kịch bản phổ biến, có sẵn các profile tích hợp:

```csharp
// Viết lại query: temperature thấp, budget token nhỏ, stateless
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Tóm tắt: temperature cao hơn một chút, token vừa phải
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Ví dụ thực tế

### Viết lại query nội bộ trong RAG pipeline

```csharp
// Service chính cấu hình cho hội thoại với user
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// Viết lại query với cài đặt khác — service không thay đổi
var betterQuery = await service.GetCompletionAsync(
    $"Viết lại cho tìm kiếm: {userQuery}",
    RequestProfiles.QueryRewrite);

// Tiếp tục hội thoại bình thường — vẫn là Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### Tắt function cho một bước cụ thể

```csharp
// Service có function được đăng ký
service.WithFunction("search_web", "Tìm trên web", ...);

// Cho lần gọi này, bỏ qua function calling — chỉ trả lời trực tiếp
var directAnswer = await service.GetCompletionAsync(
    "2 + 2 bằng bao nhiêu?",
    new AIRequestProfile { DisableFunctions = true });
```

## Kết hợp với AIRequestContext

Cả hai có thể truyền cùng nhau để kiểm soát tối đa:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nHãy ngắn gọn." }
);
```

Xem [AIRequestContext](request-contexts.md) để biết thêm về cách inject nội dung vào request.
