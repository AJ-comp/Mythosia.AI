# Tham số tạo nội dung

## Thuộc tính chung

Tất cả AI service instance đều có các thuộc tính sau:

```csharp
service.Temperature = 0.7f;        // Độ ngẫu nhiên [0, 2]. Thấp hơn = xác định hơn
service.TopP = 1.0f;               // Ngưỡng nucleus sampling
service.MaxTokens = 1024;          // Số token output tối đa
service.FrequencyPenalty = 0.0f;   // Phạt token lặp lại
service.PresencePenalty = 0.0f;    // Phạt token đã xuất hiện
service.MaxMessageCount = 20;      // Kích thước cửa sổ hội thoại
```

## Phương thức fluent

Các phương thức này trả về `this` để có thể chain:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("Bạn là trợ lý hữu ích.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Phương thức | Mô tả |
|--------|-------------|
| `.WithSystemMessage(string)` | Đặt system prompt |
| `.WithTemperature(float)` | Giá trị nằm trong [0, 2] |
| `.WithMaxTokens(uint)` | Số token output tối đa |
| `.WithStatelessMode(bool)` | Tắt tích lũy lịch sử hội thoại |

## Chế độ Stateless

Khi bật, mỗi request độc lập — không gửi hay lưu lịch sử hội thoại:

```csharp
service.StatelessMode = true;

// Tương đương:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Hữu ích cho các truy vấn một lần không cần lưu lịch sử.

## Truy vấn một lần

Các extension method này chạy một truy vấn duy nhất mà không ảnh hưởng đến lịch sử hội thoại:

```csharp
// Prompt văn bản
string response = await service.AskOnceAsync("2+2 bằng bao nhiêu?");

// Tin nhắn (multimodal)
string response = await service.AskOnceAsync(message);

// Hình ảnh từ đường dẫn file
string response = await service.AskOnceWithImageAsync("Mô tả ảnh này", "photo.jpg");
```

## Chuyển đổi model

Đổi model giữa chừng trong session mà vẫn giữ lịch sử hội thoại:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// Hoặc dùng extension method — xóa lịch sử và bắt đầu mới:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Quản lý nhiều hội thoại

Một service instance có thể chứa nhiều luồng hội thoại độc lập:

```csharp
// Bắt đầu một khối hội thoại mới
var chat1 = service.AddNewChat();

// Chuyển sang khối khác
service.SetActivateChat(chat2Id);

// Truy cập tất cả các khối
var allChats = service.ChatRequests;
```

## Kiểm tra trạng thái hội thoại

Lấy phản hồi cuối của assistant hoặc tóm tắt nhanh session hiện tại:

```csharp
// Lấy tin nhắn cuối của assistant (null nếu chưa có)
string? lastReply = service.GetLastAssistantResponse();

// Lấy tóm tắt văn bản về trạng thái service hiện tại
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: You are a helpful assistant.
```

## Sao chép cấu hình service

Sao chép toàn bộ cài đặt từ service instance khác (không kèm lịch sử hội thoại):

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
