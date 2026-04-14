# AIRequestContext

## Là gì?

`AIRequestContext` cho phép bạn thay đổi **những gì model nhìn thấy** cho một request duy nhất — inject thêm hướng dẫn, thêm tài liệu tham khảo, hoặc thay thế hoàn toàn tin nhắn của user — mà không thay đổi vĩnh viễn system message hay lịch sử hội thoại của service.

## Vấn đề nó giải quyết

Hãy xem xét một RAG pipeline cần truy xuất tài liệu liên quan và đưa chúng vào prompt. **Không có** `AIRequestContext`, bạn phải sửa trực tiếp system message:

```csharp
// ❌ Không có AIRequestContext — làm ô nhiễm system message
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\nDùng context sau để trả lời:\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// Khôi phục — nhưng context này đã lọt vào lịch sử hội thoại rồi
service.SystemMessage = originalSystem;
```

Vấn đề với cách này:

- Context đã truy xuất **rò rỉ vào lịch sử hội thoại** — các request tương lai vẫn thấy nó
- Khôi phục system message không xóa được ô nhiễm lịch sử
- Trong web app multi-user, thay đổi shared state gây race condition

**Với** `AIRequestContext`, việc inject được giới hạn trong đúng một request:

```csharp
// ✅ Với AIRequestContext — gọn gàng, có phạm vi, không tác dụng phụ
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nDùng context sau để trả lời:\n{retrievedDocs}"
    });
```

System message chỉ được sửa đổi cho lần gọi này. Request tiếp theo thấy system message gốc. Không cần dọn dẹp.

## Các thuộc tính

### SystemMessagePrefix

Thêm văn bản vào đầu system message chỉ cho request này:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "Hôm nay là 2026-03-31.\n"
};

var response = await service.GetCompletionAsync("Hôm nay là ngày mấy?", context);
```

**Dùng khi:** Inject metadata động (ngày, múi giờ user, thông tin session) thay đổi theo request.

### SystemMessageSuffix

Thêm văn bản vào cuối system message chỉ cho request này:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nLuôn trả lời bằng tiếng Việt."
};

var response = await service.GetCompletionAsync("Xin chào!", context);
```

**Dùng khi:** Thêm hướng dẫn hành vi theo request, RAG context, hoặc tùy chọn ngôn ngữ.

### AdditionalMessages

Chèn thêm tin nhắn vào hội thoại chỉ cho request này — hữu ích để inject tài liệu tham khảo hoặc few-shot example:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("Tài liệu tham khảo: Chính sách hoàn tiền cho phép đổi trả trong 30 ngày.").Build()
    }
};

var response = await service.GetCompletionAsync("Tôi có đủ điều kiện hoàn tiền không?", context);
```

**Dùng khi:** Cung cấp tài liệu tham khảo, few-shot example hoặc context phụ không nên lưu vào lịch sử.

### RequestMessageOverride

Thay thế hoàn toàn tin nhắn của user cho request này. Prompt gốc bị bỏ qua:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"Dựa trên context sau, trả lời câu hỏi.\n\nContext: {docs}\n\nCâu hỏi: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context);
```

**Dùng khi:** Khi một middleware layer (RAG, viết lại query) cần định dạng lại hoàn toàn prompt trước khi gửi đến model, trong khi vẫn giữ input gốc của user trong lịch sử hội thoại.

> **💡 Lưu ý:** Khi dùng `.WithRag()`, RAG pipeline tận dụng thuộc tính này tự động. Xem [Tùy chỉnh Pipeline — Cách hoạt động nội bộ](rag-pipeline.md#how-it-works-internally) để biết toàn bộ flow.

## So sánh trước và sau

### Kịch bản: RAG với inject ngày và context đã truy xuất

**Không có AIRequestContext:**

```csharp
// ❌ Lộn xộn, stateful, dễ sai
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nHôm nay: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nContext:\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2);
```

**Với AIRequestContext:**

```csharp
// ✅ Gọn gàng, stateless, không tác dụng phụ
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"Hôm nay: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nContext:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
        }
    });
```

## Kết hợp với AIRequestProfile

Cả hai có thể truyền cùng nhau để kiểm soát tối đa một request:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nContext:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("Ví dụ: ...").Build()
        }
    }
);
```

Xem [AIRequestProfile](request-profiles.md) để biết thêm về cách ghi đè tham số tạo nội dung.
