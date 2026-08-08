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
    context: new AIRequestContext
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

var response = await service.GetCompletionAsync("Hôm nay là ngày mấy?", context: context);
```

**Dùng khi:** Inject metadata động (ngày, múi giờ user, thông tin session) thay đổi theo request.

### SystemMessageSuffix

Thêm văn bản vào cuối system message chỉ cho request này:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nLuôn trả lời bằng tiếng Việt."
};

var response = await service.GetCompletionAsync("Xin chào!", context: context);
```

**Dùng khi:** Thêm hướng dẫn hành vi theo request, RAG context, hoặc tùy chọn ngôn ngữ.

### AdditionalMessages

Chèn thêm tin nhắn vào hội thoại chỉ cho request này — hữu ích để inject tài liệu tham khảo hoặc few-shot example:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.Create().AddText("Tài liệu tham khảo: Chính sách hoàn tiền cho phép đổi trả trong 30 ngày.").Build()
    }
};

var response = await service.GetCompletionAsync("Tôi có đủ điều kiện hoàn tiền không?", context: context);
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

await service.GetCompletionAsync(userQuery, context: context);
```

**Dùng khi:** Khi một middleware layer (RAG, viết lại query) cần định dạng lại hoàn toàn prompt trước khi gửi đến model, trong khi vẫn giữ input gốc của user trong lịch sử hội thoại.

> **💡 Lưu ý:** Khi dùng `.WithRag()`, RAG pipeline tận dụng thuộc tính này tự động. Xem [Tùy chỉnh Pipeline — Cách hoạt động nội bộ](rag-pipeline.md#cách-hoạt-động-nội-bộ) để biết toàn bộ flow.

## So sánh trước và sau

### Kịch bản: RAG với inject ngày và context đã truy xuất

**Không có AIRequestContext:**

```csharp
// ❌ Lộn xộn, stateful, dễ sai
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nHôm nay: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nContext:\n{retrievedChunks}";

var fewShotIndex = service.ActivateChat.Messages.Count;
service.ActivateChat.Messages.Add(MessageBuilder.Create().AddText(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.ActivateChat.Messages.RemoveAt(fewShotIndex);
```

**Với AIRequestContext:**

```csharp
// ✅ Gọn gàng, stateless, không tác dụng phụ
var answer = await service.GetCompletionAsync(userQuery,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Hôm nay: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nContext:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText(fewShotExample).Build()
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
            MessageBuilder.Create().AddText("Ví dụ: ...").Build()
        }
    }
);
```

Xem [AIRequestProfile](request-profiles.md) để biết thêm về cách ghi đè tham số tạo nội dung.

## Tự động chèn bằng `SystemMessageProvider`

### Vấn đề nó giải quyết

Một app chat điển hình có nhiều điểm vào LLM đều cần cùng một baseline — ngày hôm nay, thư mục hiện tại, thông tin phiên. **Không có** `SystemMessageProvider`, mỗi nơi gọi phải nhớ dựng và truyền context đó:

```csharp
// ❌ Không có SystemMessageProvider — mỗi điểm vào phải nhớ chèn
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Phản hồi chat chính
var answer = await service.GetCompletionAsync(userMessage,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 2. Bộ tạo tiêu đề (thêm vào sau)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 3. Bộ tóm tắt (thêm vào muộn hơn nữa)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 4. Lệnh gọi agent — dễ quên! Compiler không cảnh báo bạn
var agentResult = await service.RunAgentAsync(goal);  // ← thiếu ngày, bug âm thầm
```

Vấn đề của cách tiếp cận này:

- Cùng một snippet dựng context bị **lặp lại** ở mỗi nơi gọi
- Các điểm vào mới (lệnh `RunAgentAsync` ở trên) **dễ bị bỏ sót** — không có kiểm tra lúc compile
- Mỗi tính năng mới thêm lệnh gọi LLM đều phải nhớ quy ước này
- Tests cũng phải sao chép lại việc thiết lập context ở mỗi nơi gọi

Với `SystemMessageProvider`, bạn đăng ký baseline **một lần** và mọi lệnh gọi ra đều tự động nhận được:

```csharp
// ✅ Với SystemMessageProvider — đăng ký một lần, áp dụng mọi nơi
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// Tất cả những lệnh này đều tự động nhận baseline — không cần boilerplate mỗi lệnh gọi
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← cũng nhận baseline

// Các điểm vào streaming cũng vậy — cùng baseline, không cần boilerplate mỗi lệnh gọi
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### Cách hoạt động

Đăng ký callback một lần qua helper fluent `WithSystemMessageProvider`. Mỗi lệnh gọi ra (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) tự động gọi nó để dựng context cơ sở:

```csharp
// Thường tại thời điểm tạo service / cấu hình DI
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### Overload async cho provider dùng IO

Khi context cơ sở đến từ database, cache hoặc lệnh gọi HTTP, hãy dùng overload async để provider không phải chặn trên `.Result` / `.GetAwaiter().GetResult()`. Giải quyết overload chọn đúng overload theo arity của lambda — không tham số cho sync, một `CancellationToken` cho async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

Các đường dẫn không streaming (`GetCompletionAsync`, `RunAgentAsync`) không hỗ trợ hủy theo thiết kế — chữ ký của chúng không nhận `CancellationToken`, và `CancellationToken.None` luôn được truyền tới provider. Nếu provider của bạn cần hủy (ví dụ: truy vấn DB lâu), hãy dùng các đường dẫn streaming (`StreamAsync`, `RunAgentStreamAsync`), chúng sẽ truyền token của người gọi tới callback provider.

### Gộp với context per-call tường minh

Khi một lệnh gọi có provider đã đăng ký **và** cũng truyền `AIRequestContext` tường minh, cả hai được gộp theo từng trường:

| Trường | Quy tắc gộp |
|---|---|
| `SystemMessagePrefix` | tường minh thắng nếu non-null, nếu không thì provider |
| `SystemMessageSuffix` | tường minh thắng nếu non-null, nếu không thì provider |
| `RequestMessageOverride` | tường minh thắng nếu non-null, nếu không thì provider |
| `AdditionalMessages` | nối chuỗi (provider trước, sau đó tường minh) |

Lý do: trường hợp phổ biến là "provider cung cấp baseline, một lệnh gọi cụ thể muốn thay thế một trường vô hướng hoặc thêm tin nhắn bổ sung" — ghi đè cấp trường giữ cho ngữ nghĩa có thể dự đoán mà không gây nối chuỗi bất ngờ.

### Gọi mỗi request

Provider được gọi **một lần mỗi request**, nên giá trị trả về có thể phản ánh trạng thái tức thời (timestamp, phiên, v.v.). Trả về `null` là no-op — giống với việc để `SystemMessageProvider` không được thiết lập cho lệnh gọi đó.

### Tóm lại: khi nào chọn công cụ này — giao điểm của ba điều kiện

Lùi lại một bước từ các ví dụ và quy tắc hợp nhất ở trên, `SystemMessageProvider` là công cụ chuyên dụng cho trường hợp **ba điều kiện đồng thời thỏa mãn**:

1. **Một baseline cần có mặt ở mọi lệnh gọi LLM** — không muốn phải nhớ inject thủ công tại mỗi điểm vào
2. **Giá trị phải được tính động tại thời điểm gọi** — thời gian hiện tại, thư mục đang hoạt động, người dùng đã đăng nhập và các giá trị khác không thể cố định khi khởi động
3. **Trạng thái vĩnh viễn (`SystemMessage`, lịch sử hội thoại) không được bị ô nhiễm** — giá trị không được rò rỉ sang các lệnh gọi sau

Nếu thiếu bất kỳ một trong ba điều kiện, câu trả lời đúng là một công cụ đơn giản hơn:

| Tình huống | Công cụ đúng | Lý do |
|---|---|---|
| Baseline **cố định (không thay đổi)** trong suốt phiên | `service.SystemMessage = "..."` | Gán một lần là đủ, không cần provider |
| **Chỉ một lệnh gọi cụ thể** cần xử lý đặc biệt | Truyền `AIRequestContext` tường minh tại điểm gọi | Không phải baseline dùng chung — một lần inject |
| Dùng chung + động + không ô nhiễm **(cả ba)** | **`SystemMessageProvider`** | Công cụ chuyên dụng cho giao ba điều kiện này |

#### Tại sao điều này không mâu thuẫn với nguyên tắc "dùng một lần" của `AIRequestContext`

Bản chất của `AIRequestContext` không phải là "chỉ dùng một lần" mà là **"không bao giờ làm ô nhiễm trạng thái vĩnh viễn"**. `SystemMessageProvider` là một factory **chạy lại callback trên mỗi request**, tạo ra **một `AIRequestContext` hoàn toàn mới giới hạn trong request đó**. Ngữ cảnh sinh ra vẫn là per-request scoped, giá trị không bao giờ rò rỉ vào lịch sử hội thoại, và ở lệnh gọi tiếp theo callback lại chạy để phản ánh giá trị **tại thời điểm đó**. Vậy nên provider không vi phạm nguyên tắc thiết kế của `AIRequestContext` — nó chỉ **tự động hóa nguyên tắc đó**.

Cụ thể, đăng ký provider dưới đây **không** sửa đổi `service.SystemMessage` hay `service.ActivateChat.Messages`:

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- Qua nửa đêm, lần chạy lại provider ở lệnh gọi tiếp theo tự động phản ánh **ngày mới** (không tĩnh)
- Một tuần sau mở lịch sử hội thoại cũng không thấy "Today is ..." bị gắn vào các request cũ
- Ngay cả khi dùng service dùng chung trong môi trường đa người dùng, mỗi lệnh gọi đều sinh ra ngữ cảnh độc lập riêng

> Có sẵn trong Mythosia.AI v6.3.0+.
