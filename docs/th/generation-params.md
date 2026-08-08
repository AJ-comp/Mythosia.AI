# พารามิเตอร์การสร้าง

## Properties ทั่วไป

AI service instance ทุกตัวมี property เหล่านี้:

```csharp
service.Temperature = 0.7f;        // ความสุ่ม [0, 2] ยิ่งต่ำยิ่งแน่นอน
service.TopP = 1.0f;               // เกณฑ์ nucleus sampling
service.MaxTokens = 1024;          // จำนวน token output สูงสุด
service.FrequencyPenalty = 0.0f;   // ลดโทษ token ที่ซ้ำกัน
service.PresencePenalty = 0.0f;    // ลดโทษ token ที่เคยปรากฏแล้ว
```


## Fluent Extension Methods

Method เหล่านี้คืนค่า `this` เพื่อรองรับการ chain:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("คุณคือผู้ช่วยที่มีประโยชน์")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Method | คำอธิบาย |
|--------|-------------|
| `.WithSystemMessage(string)` | กำหนด system prompt |
| `.WithTemperature(float)` | จำกัดค่าในช่วง [0, 2] |
| `.WithMaxTokens(uint)` | จำนวน token output สูงสุด |
| `.WithStatelessMode(bool)` | ปิดการสะสมประวัติการสนทนา |

## Stateless Mode

เมื่อเปิดใช้งาน แต่ละ request จะเป็นอิสระ — ไม่ส่งหรือบันทึกประวัติการสนทนา:

```csharp
service.StatelessMode = true;

// เทียบเท่ากับ:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

เหมาะสำหรับการถามแบบครั้งเดียวที่ไม่ต้องการประวัติ

## การถามแบบครั้งเดียว

Extension method เหล่านี้รัน query เดียวโดยไม่กระทบประวัติการสนทนา:

```csharp
// Text prompt
string response = await service.AskOnceAsync("2+2 เท่ากับเท่าไหร่?");

// Message (multimodal)
string response = await service.AskOnceAsync(message);

// รูปภาพจาก file path
string response = await service.AskOnceWithImageAsync("อธิบายรูปนี้", "photo.jpg");
```

## การเปลี่ยน Model

เปลี่ยน model กลางคันโดยยังคงประวัติการสนทนาไว้:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// หรือใช้ extension method — ล้างประวัติและเริ่มใหม่:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## จัดการหลายการสนทนา

Service instance เดียวรองรับหลาย conversation thread ที่เป็นอิสระจากกัน:

```csharp
// เริ่ม conversation block ใหม่
service.AddNewChat();
var chat1 = service.ActivateChat;

// สลับไปยัง block อื่น
service.SetActivateChat(chat2Id);

// เข้าถึง block ทั้งหมด
var allChats = service.ChatRequests;
```

## ตรวจสอบสถานะการสนทนา

ดูคำตอบล่าสุดของ assistant หรือสรุป session ปัจจุบัน:

```csharp
// ดูข้อความล่าสุดของ assistant (null ถ้ายังไม่มี)
string? lastReply = service.GetLastAssistantResponse();

// ดูสรุปสถานะ service ปัจจุบัน
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: You are a helpful assistant.
```

## คัดลอกการตั้งค่า Service

โคลนการตั้งค่าทั้งหมดจาก service instance อื่น (ไม่รวมประวัติการสนทนา):

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
