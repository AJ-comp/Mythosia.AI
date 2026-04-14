# การสร้างข้อความ

## แบบ Single Turn

วิธีใช้งานที่ง่ายที่สุด — ส่งข้อความแล้วรับคำตอบ:

```csharp
var response = await service.GetCompletionAsync("เมืองหลวงของฝรั่งเศสคืออะไร?");
Console.WriteLine(response); // Paris
```

## System Prompt

กำหนด system prompt เพื่อให้ model รับบทบาทหรือทำตามคำสั่งที่ต้องการ:

```csharp
service.SystemPrompt = "คุณคือผู้ช่วยที่ตอบกระชับ ตอบในประโยคเดียว";

var response = await service.GetCompletionAsync("อธิบาย recursion");
```

## การสนทนาหลายรอบ

ข้อความจะถูกสะสมโดยอัตโนมัติ ทุกครั้งที่เรียก `GetCompletionAsync` จะเพิ่มเข้าไปในประวัติการสนทนา:

```csharp
await service.GetCompletionAsync("ชื่อของฉันคือ Alice");
var response = await service.GetCompletionAsync("ฉันชื่ออะไร?");
// → "ชื่อของคุณคือ Alice"
```

หากต้องการล้างประวัติการสนทนา:

```csharp
service.ClearMessages();
```

## สร้างข้อความด้วยตนเอง

ใช้ `MessageBuilder` เพื่อสร้างข้อความแบบ explicit:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("สรุปข้อความนี้: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (รับรูปภาพ)

Provider ที่รองรับ vision สามารถรับรูปภาพพร้อมข้อความได้:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("แผนผังนี้แสดงอะไร?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Quick Ask (Static API)

สำหรับการถามแบบครั้งเดียวโดยไม่ต้องสร้าง service instance ใช้ `QuickAskAsync` ซึ่งตรวจจับ provider จากชื่อ model อัตโนมัติ:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "เมืองหลวงของฝรั่งเศสคืออะไร?",
    model: AIModels.OpenAI.Gpt4oMini  // ค่าเริ่มต้น
);
```

แบบมีรูปภาพ:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "อธิบายรูปภาพนี้",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4Vision
);
```

## Method ที่ใช้งานกับรูปภาพได้สะดวก

วิเคราะห์รูปภาพโดยไม่ต้องใช้ `MessageBuilder` — service อ่านไฟล์และระบุ MIME type อัตโนมัติ:

```csharp
// จาก file path
var response = await service.GetCompletionWithImageAsync(
    "แผนผังนี้แสดงอะไร?", "diagram.png");

// จาก URL
var response = await service.GetCompletionWithImageUrlAsync(
    "อธิบายรูปนี้", "https://example.com/photo.jpg");
```

## ลองใหม่จากข้อความล่าสุด

ลบคำตอบล่าสุดของ assistant และส่งข้อความล่าสุดของ user ใหม่อีกครั้ง:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

มีประโยชน์เมื่อคำตอบก่อนหน้าไม่เป็นที่น่าพอใจ

## การนับ Token

ประมาณการใช้ token ก่อนส่ง request ใช้ได้กับ **ทุก provider**:

```csharp
// นับ token สำหรับประวัติการสนทนาปัจจุบัน
uint conversationTokens = await service.GetInputTokenCountAsync();

// นับ token สำหรับ prompt ที่ระบุ
uint promptTokens = await service.GetInputTokenCountAsync("prompt ของคุณ");
```

OpenAI และ provider ส่วนใหญ่ใช้การประมาณแบบ local ด้วย TikToken ส่วน Anthropic และ Google เรียก API นับ token ของตนเองเพื่อความแม่นยำ

## Fluent Message Chain

`BeginMessage()` มี API แบบ fluent สำหรับสร้างและส่งข้อความในครั้งเดียว — รองรับ text, รูปภาพ, streaming และการตั้งค่า policy:

```csharp
// text + รูปภาพ → ส่ง
string response = await service.BeginMessage()
    .AddText("แผนผังนี้แสดงอะไร?")
    .AddImage("diagram.png")
    .SendAsync();

// ถามแบบครั้งเดียว (ไม่บันทึกประวัติ)
string answer = await service.BeginMessage()
    .AddText("แปลเป็นภาษาเกาหลี")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("แต่งบทกวีเกี่ยวกับฤดูใบไม้ผลิ")
    .StreamAsync(chunk => Console.Write(chunk));

// พร้อม timeout และ policy แบบกำหนดเอง
string result = await service.BeginMessage()
    .AddText("วิเคราะห์รูปภาพนี้")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` ยังรองรับ `IAsyncEnumerable`:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("เล่าเรื่องให้ฉันฟัง").StreamAsync())
    Console.Write(chunk);
```

## ควบคุมความยาว Output และ Temperature

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // ยิ่งต่ำยิ่งแน่นอน
```
