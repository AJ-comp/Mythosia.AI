# Quản lý hội thoại

## Cách lịch sử hội thoại hoạt động

Mỗi lần gọi `GetCompletionAsync` hoặc `StreamAsync` đều thêm vào danh sách tin nhắn nội bộ của service. Điều này có nghĩa là model có context từ tất cả các lượt trước.

```csharp
await service.GetCompletionAsync("Màu yêu thích của tôi là xanh dương.");
var reply = await service.GetCompletionAsync("Màu yêu thích của tôi là màu gì?");
// → "Màu yêu thích của bạn là xanh dương."
```

Để bắt đầu lại từ đầu:

```csharp
service.ClearMessages();
```

## Summary Policy

### Tại sao cần tóm tắt tự động?

Mỗi tin nhắn trong lịch sử hội thoại đều được gửi đến model trong mỗi request. Khi hội thoại dài ra, điều này tạo ra hai vấn đề:

1. **Chi phí** — lịch sử dài hơn có nghĩa là nhiều input token hơn cho mỗi request
2. **Tràn context** — khi lịch sử vượt quá cửa sổ context của model (ví dụ 128K token với GPT-4o), request thất bại hoàn toàn

Bạn có thể tự cắt bớt tin nhắn cũ, nhưng điều đó làm mất context mà model có thể cần. **`SummaryConversationPolicy`** giải quyết bằng cách tự động nén các tin nhắn cũ thành một bản tóm tắt compact trong khi giữ nguyên các tin nhắn gần đây — model vẫn nắm được tinh thần của toàn bộ hội thoại mà không tốn quá nhiều token.

### Kích hoạt theo số tin nhắn

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // tóm tắt khi lịch sử vượt 20 tin nhắn
    keepRecentCount: 5  // giữ nguyên 5 tin nhắn gần nhất
);
```

### Kích hoạt theo số token

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // tóm tắt khi token vượt 3000
    keepRecentTokens: 1000  // giữ tin nhắn gần nhất đến 1000 token
);
```

### Kích hoạt theo cả hai (điều kiện HOẶC)

Kích hoạt tóm tắt khi **một trong hai** điều kiện — giới hạn token hoặc số tin nhắn — bị vượt:

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // tùy chọn, mặc định là triggerTokens / 3
    keepRecentCount: 7       // tùy chọn, mặc định là triggerCount / 4
);
```

Sau khi thiết lập, tóm tắt diễn ra tự động trong `GetCompletionAsync`. Không cần thay đổi gì khác.

### Cách hoạt động

1. Trước mỗi completion, policy kiểm tra xem hội thoại có vượt ngưỡng đã cấu hình không.
2. Nếu kích hoạt, các tin nhắn cũ được tóm tắt thành văn bản ngắn gọn bằng một LLM call stateless.
3. Bản tóm tắt được inject làm prefix của system message — model coi đây là context trước đó.
4. Các tin nhắn gần đây (kiểm soát bởi `KeepRecentCount` hoặc `KeepRecentTokens`) được giữ nguyên.

Khi dùng trigger dựa trên token, policy tự động dùng **số token input thực tế** được báo cáo bởi API (từ phản hồi streaming cuối cùng) thay vì ước tính cục bộ.

### Streaming

Tóm tắt không kích hoạt tự động trong `StreamAsync`. Gọi tường minh trước:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Tiếp tục câu chuyện của chúng ta..."))
    Console.Write(chunk.Content);
```

## Lưu và khôi phục bản tóm tắt

Lưu bản tóm tắt qua các session để model giữ được context sau khi khởi động lại:

```csharp
// Lưu
string saved = service.ConversationPolicy.CurrentSummary;
// → lưu vào database, file, v.v.

// Khôi phục trong session mới
service.ConversationPolicy.LoadSummary(saved);
```
