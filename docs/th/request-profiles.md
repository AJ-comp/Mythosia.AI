# AIRequestProfile

## คืออะไร?

`AIRequestProfile` ให้คุณ override พารามิเตอร์การสร้าง — temperature, max token, stateless mode, function calling — **เฉพาะสำหรับ request เดียวเท่านั้น** การตั้งค่าทั่วไปของ service ไม่ถูกแตะต้อง

## ปัญหาที่แก้ได้

สมมติคุณมี chatbot ที่ตั้งค่าสำหรับการสนทนาเชิงสร้างสรรค์:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("คุณคือผู้ช่วยเขียนเชิงสร้างสรรค์");
```

ตอนนี้ RAG pipeline ต้องการเขียน query ใหม่ด้วย temperature ต่ำและไม่มีประวัติ **โดยไม่มี** `AIRequestProfile` คุณต้องทำแบบนี้:

```csharp
// ❌ ไม่มี AIRequestProfile — จัดการ state เอง
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("เขียน query นี้ใหม่: ...");

// คืนค่าทั้งหมด — ลืมง่าย ไม่ thread-safe
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

วิธีนี้ยืดยาด เสี่ยงผิดพลาด และ **มีปัญหาใน multi-threaded** (เช่น web server ที่รองรับหลาย user พร้อมกัน) ถ้ามี exception ก่อนคืนค่า service จะอยู่ในสถานะผิดพลาด

**ด้วย** `AIRequestProfile` แค่บรรทัดเดียว:

```csharp
// ✅ ด้วย AIRequestProfile — สะอาดและปลอดภัย
var rewritten = await service.GetCompletionAsync("เขียน query นี้ใหม่: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

การตั้งค่าทั่วไปของ service ไม่ถูกแตะ ไม่ต้อง cleanup และ thread-safe

## Properties ที่ใช้ได้

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Override temperature
    MaxTokens = 256,          // Override max output token
    Stateless = true,         // ไม่เพิ่มรอบนี้เข้าประวัติการสนทนา
    DisableFunctions = true,  // ข้าม function calling สำหรับ request นี้
    DisableReasoning = true   // ข้าม reasoning สำหรับ request นี้
};

var response = await service.GetCompletionAsync("prompt ของคุณ", profile);
```

ทุก property เป็น optional — ตั้งเฉพาะที่ต้องการ override ส่วนที่เหลือใช้ค่าปัจจุบันของ service

## Profile สำเร็จรูป

สำหรับกรณีทั่วไป มี profile พร้อมใช้:

```csharp
// เขียน query ใหม่: temperature ต่ำ budget token น้อย stateless
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// สรุป: temperature สูงขึ้นเล็กน้อย token ปานกลาง
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## ตัวอย่างจริง

### เขียน query ภายใน RAG pipeline

```csharp
// Service หลักสำหรับ user
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// เขียน query ใหม่ด้วยการตั้งค่าต่างกัน — service ไม่เปลี่ยน
var betterQuery = await service.GetCompletionAsync(
    $"เขียนใหม่สำหรับการค้นหา: {userQuery}",
    RequestProfiles.QueryRewrite);

// สนทนาต่อตามปกติ — ยังคง Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### ปิดฟังก์ชันสำหรับขั้นตอนเฉพาะ

```csharp
// Service มีฟังก์ชัน register ไว้
service.WithFunction("search_web", "ค้นหาเว็บ", ...);

// request นี้ข้าม function calling — ตอบตรง ๆ
var directAnswer = await service.GetCompletionAsync(
    "2 + 2 เท่ากับเท่าไหร่?",
    new AIRequestProfile { DisableFunctions = true });
```

## ใช้ร่วมกับ AIRequestContext

ส่งทั้งคู่พร้อมกันเพื่อควบคุมสูงสุด:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nตอบกระชับ" }
);
```

ดู [AIRequestContext](request-contexts.md) สำหรับรายละเอียดการ inject เนื้อหาเข้า request
