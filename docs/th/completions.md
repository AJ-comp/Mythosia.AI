# การสร้างข้อความ

ใช้ [request builder](request-building.md) เพื่อแยกการตั้งค่าและสร้างรูปแบบที่ใช้ซ้ำได้ เรียก `CreateRequest(...)` ก่อน `With...` ส่วน property และ fluent method บน service ยังคงพฤติกรรมเดิม

`GetCompletionAsync` ยังเป็นวิธีที่สะดวกสำหรับรับคำตอบที่เสร็จแล้ว หากต้องการดูความคืบหน้าหรือควบคุมงานก่อนเสร็จ ให้ใช้ [Run](execution-api-transition.md)

<a id="completion-cancellation"></a>

## ยกเลิกคำตอบที่ไม่ต้องการแล้ว

เมื่อผู้ใช้ปิดหน้าจอ กดหยุด หรือแอปรอเกินเวลาที่กำหนด คำตอบอาจไม่จำเป็นอีกต่อไป ส่ง `CancellationToken` เพื่อหยุดการสื่อสารและงานฝั่งไคลเอนต์ รวมถึงหลีกเลี่ยงการเรียกเครื่องมือและโมเดลรอบถัดไป หากต้องการคำตอบที่เสร็จแล้ว ยังใช้ `GetCompletionAsync` ได้ การยกเลิกอย่างเดียวไม่ต้องสร้าง Run

### Before: ผู้เรียกไม่ส่งสัญญาณยกเลิก

```csharp
string answer = await service.CreateRequest("สรุปเอกสารนี้")
    .GetCompletionAsync();
```

### After: ยกเลิกตามผู้ใช้หรือเมื่อครบ 30 วินาที

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("สรุปเอกสารนี้")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("ยกเลิกแล้ว");
}
```

เก็บแหล่งโทเค็นไว้ระหว่างการเรียก และให้ปุ่มหยุดหรือเหตุการณ์ปิดหน้าจอเรียก `cancellation.Cancel()` ตัวอย่างยังกำหนดให้ยกเลิกหลัง 30 วินาทีด้วย ผู้เรียกจะได้รับ `OperationCanceledException` หลังเก็บกวาดงานเสร็จ เวลาที่ตั้งด้วย `CancellationTokenSource` ถือเป็นการยกเลิกจากผู้เรียก ส่วน `FunctionCallingPolicy.TimeoutSeconds` ยังคงพฤติกรรมข้อผิดพลาดเมื่อหมดเวลาเดิม

โอเวอร์โหลดบริการที่รับข้อความและ `Message` ผลลัพธ์แบบระบุชนิด request builder และ `MessageChain.SendAsync` / `SendOnceAsync` รับโทเค็นได้ การเรียกเดิมที่ไม่ส่งโทเค็นยังใช้ได้ จุดเรียกทางเลือก:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "สรุปเอกสารนี้", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "ส่งชื่อเรื่องและผู้เขียนเป็น JSON", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("สรุปเอกสารนี้")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("แปลประโยคนี้")
    .SendOnceAsync(cancellationToken: token);
```

โทเค็นส่งต่อถึงการเตรียมคำขอ การส่งและอ่าน HTTP เครื่องมือภายในที่รองรับการยกเลิก และรอบโมเดลถัดไป เมื่อพบการยกเลิก จะข้ามเครื่องมือที่รออยู่และรอบถัดไป การเก็บกวาดรักษาคู่การเรียกเครื่องมือกับผลลัพธ์ที่บันทึกไว้ เครื่องมือที่เริ่มแล้วแต่ไม่สนใจโทเค็นจึงอาจทำให้การเก็บกวาดช้าลง ไม่ย้อนคืนการกระทำที่เสร็จแล้วหรือลบประวัติ ดู[ข้อกำหนดเครื่องมือ](function-calling.md#tool-execution-contract)

ไม่รับประกันว่าผู้ให้บริการจะหยุดการประมวลผลหรือคิดค่าบริการ OpenAI ระบุให้ปิดการเชื่อมต่อเพื่อยกเลิก Responses ปกติ ส่วน Google ระบุชัดว่ายกเลิกเฉพาะไคลเอนต์และยังคิดค่าบริการตามการใช้งาน [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal) งานเบื้องหลังต้องเรียก `CancelAsync()` โดยตรง การยกเลิก `WaitForCompletionAsync(cancellationToken: ...)` หยุดเพียงการรอ คำขอปกติจะไม่เปลี่ยนเป็นงานเบื้องหลัง ดู [Perplexity](perplexity.md)

<a id="completion-cancellation-migration"></a>

ส่วนเพิ่มเติมนี้อยู่ในMythosia.AI 8.0.0 การเรียกที่ไม่ส่งโทเค็นและอาร์กิวเมนต์ profile/context ตามตำแหน่งเดิมยังใช้ได้ในระดับซอร์ส แต่ผู้ใช้แพ็กเกจต้องคอมไพล์ใหม่ ผู้พัฒนา `IAIService` เองต้องเพิ่ม `CancellationToken cancellationToken = default` ท้ายลายเซ็นทั้งสองและส่งต่อ ผู้ให้บริการที่สืบทอด `AIService` คง override `GetCompletionAsync(Message)` เดิมและส่ง `RequestCancellationToken` แบบ protected ไปยังการสื่อสาร ตัว builder และ Run เองไม่ได้ต้องการการเปลี่ยนอินเทอร์เฟซนี้ คลาสลูกที่ override โอเวอร์โหลด public virtual ที่เปลี่ยน เช่น การตอบแบบ string/profile/context เมธอดช่วยเรื่องภาพ หรือ `RunAgentAsync` ต้องเพิ่มและส่งต่อ `CancellationToken` ใหม่ด้วย เฉพาะ override ของผู้ให้บริการที่รับ `Message` ตัวเดียวเท่านั้นที่คงลายเซ็นเดิม delegate ที่อ้างถึงเมธอดซึ่งเปลี่ยนลายเซ็นโดยตรงอาจต้องเปลี่ยนเป็น lambda ที่ระบุว่าจะส่งหรือละโทเค็น

## แบบ Single Turn

วิธีใช้งานที่ง่ายที่สุด — ส่งข้อความแล้วรับคำตอบ:

```csharp
var response = await service.GetCompletionAsync("เมืองหลวงของฝรั่งเศสคืออะไร?");
Console.WriteLine(response); // Paris
```

## System Prompt

กำหนด system prompt เพื่อให้ model รับบทบาทหรือทำตามคำสั่งที่ต้องการ:

```csharp
service.SystemMessage = "คุณคือผู้ช่วยที่ตอบกระชับ ตอบในประโยคเดียว";

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
service.ActivateChat.ClearMessages();
```

## สร้างข้อความด้วยตนเอง

ใช้ `MessageBuilder` เพื่อสร้างข้อความแบบ explicit:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("สรุปข้อความนี้: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (รับรูปภาพ)

Provider ที่รองรับ vision สามารถรับรูปภาพพร้อมข้อความได้:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("แผนผังนี้แสดงอะไร?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

สำหรับวิเคราะห์กราฟ ภาพหน้าจอ เรียกฟังก์ชันภายใน หรือตรวจคำตอบเชิงลึก ใช้ [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash) การใช้เหตุผลปิดโดยค่าเริ่มต้น เปิดด้วย `WithDeepSeekReasoning(...)` หรือ `WithReasoning(...)` ต่อคำขอ

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
    model: AIModels.OpenAI.Gpt4_1
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

Perplexity: [ตอบด้วย preset ของ Agent / แหล่งอ้างอิง ภาพ และคำตอบที่มีโครงสร้าง](perplexity.md).
