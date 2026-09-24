# Streaming

> Grok 4.7: ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การเลือกโมเดล การให้เหตุผล และความเร็ว](providers.md#grok-47)

หากต้องการคำตอบ การใช้โทเคน และแหล่งอ้างอิงพร้อมกัน ให้ใช้ `AIRunResult` ที่ได้จาก `await run.Result` สตริงอยู่ใน `result.Text` โดยไม่ต้องอ่านสตรีม นี่คือการเปลี่ยน API ในMythosia.AI 8.0.0 ชนิดผลลัพธ์ของ `GetCompletionAsync` และ `StructuredStreamRun<T>.Result` ยังคงเดิม [ผลลัพธ์ Run และการย้ายรุ่น](execution-api-transition.md#run-result).


ใช้ [request builder](request-building.md) เพื่อแยกการตั้งค่าและสร้างรูปแบบที่ใช้ซ้ำได้ เรียก `CreateRequest(...)` ก่อน `With...` ส่วน property และ fluent method บน service ยังคงพฤติกรรมเดิม

แสดงข้อความทันทีที่ได้รับเพื่อให้ผู้ใช้ติดตามการเขียนคำตอบได้ ดูวิธีเพิ่มเหตุการณ์เครื่องมือและปุ่มหยุดใน[คู่มือ Run](execution-api-transition.md)

```csharp
await using var run = await service.StartRunAsync(
    "สรุปเอกสาร",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

## ตัวอย่างความเข้ากันได้กับ API เดิม

StreamAsync ของบริการ/RAG ที่รับอินพุตยังเป็นสาธารณะใน v8 ใช้ StartRunAsync สำหรับการควบคุมใหม่ ส่วน run.StreamAsync() ใช้สังเกต run ที่เริ่มแล้วเท่านั้น

## Streaming พื้นฐาน

ใช้ `StreamAsync` เพื่อรับ token ขณะที่กำลังสร้าง:

```csharp
await foreach (var token in service.StreamAsync("เล่าเรื่องให้ฉันฟัง"))
{
    Console.Write(token);
}
```

## Streaming พร้อมประเภทเนื้อหา

`StreamAsync` สามารถคืนค่าเป็นออบเจกต์ `StreamingContent` ที่มีทั้งข้อความและประเภทของมัน:

```csharp
await foreach (var content in service.StreamAsync("อธิบาย quantum computing", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Reasoning Streaming

OpenAI, Claude, Gemini, Grok และ DeepSeek Flash ส่งเนื้อหาการใช้เหตุผลของผู้ให้บริการผ่านรูปแบบ streaming เดียวกัน เปิดการใช้เหตุผลในบริการหรือคำขอ แล้วสังเกตด้วย `StreamOptions.WithReasoning()`:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("แก้: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[กำลังคิด] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

Gemini 3.7/3.8 Flash ใช้เหตุการณ์ streaming และ Run เดิม โดย `StreamingContentType.Reasoning` มีสรุปหรือความคืบหน้าที่ผู้ให้บริการส่งกลับ ไม่รับประกันการเปิดเผยเหตุผลภายในทั้งหมด `StreamOptions.WithReasoning()` เลือกเอาต์พุตนี้ ส่วน `WithReasoning(ReasoningLevel...)` ของบริการใช้ตั้งระดับการใช้เหตุผล

Grok 4.6 อาจส่งสรุปการให้เหตุผลที่ผู้ให้บริการเลือกเปิดเผยผ่านเหตุการณ์เหล่านี้เช่นกัน ตัวเลือกสตรีมเลือกผลลัพธ์ที่แสดง ส่วน `WithReasoning(ReasoningLevel...)` เลือกระดับของหนึ่งงาน การไม่มีสรุปไม่ได้หมายความว่าปิดการให้เหตุผล ดู[การตั้งค่า Grok](providers.md#xai-xaiservice)

DeepSeek Flash ส่ง `reasoning_content` ผ่านเหตุการณ์เดิมเมื่อเปิดการใช้เหตุผล `StreamOptions.WithReasoning()` ควบคุมการสังเกต ส่วน `WithDeepSeekReasoning(...)` หรือ `WithReasoning(...)` ของบริการควบคุมการใช้เหตุผล ดู [DeepSeek](providers.md#deepseek-deepseekservice)

## Streaming ร่วมกับ Structured Output

Stream text แบบ real-time และรับออบเจกต์ที่ deserialize แล้วเมื่อเสร็จ:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Stream token ไปที่ UI ทันที
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// ดึงผลลัพธ์ที่ parse แล้วหลัง streaming เสร็จ
MyDto result = await run.Result;
```

## การใช้ Token

เมื่อ streaming เสร็จสิ้น event `Completion` สุดท้ายจะมีออบเจกต์ `TokenUsage` พร้อมข้อมูลการใช้งานโดยละเอียด:

```csharp
await foreach (var content in service.StreamAsync("อธิบาย quantum computing", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nToken ที่ป้อน:  {content.Usage.InputTokens}");
        Console.WriteLine($"Token ที่สร้าง: {content.Usage.OutputTokens}");
        Console.WriteLine($"Token ทั้งหมด:  {content.Usage.TotalTokens}");
    }
}
```

### Property ของ TokenUsage

| Property | คำอธิบาย |
|---|---|
| `InputTokens` | Token ใน input/prompt |
| `OutputTokens` | Token ใน output/completion |
| `TotalTokens` | Input + Output |
| `CachedInputTokens` | Token จาก cache (ลดค่าใช้จ่าย) |
| `CacheCreationTokens` | Token ที่เขียนลง cache (Anthropic) |
| `ReasoningTokens` | Token ที่ใช้สำหรับ reasoning |
| `CacheHitRatio` | อัตราส่วน cache hit (0.0–1.0) |
| `VisibleOutputTokens` | Token output ไม่รวม reasoning |

### ตรวจสอบประสิทธิภาพ Cache

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"อัตรา cache hit: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Input ที่ไม่ได้จาก cache: {content.Usage.NonCachedInputTokens}");
}
```

## Preset ของ StreamOptions

`StreamOptions` มี preset และ fluent builder สำหรับควบคุมสิ่งที่ stream ส่งออกมา:

```csharp
// ครบทุกฟีเจอร์ — metadata, function call, reasoning
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// เบาสุด — เฉพาะ text ไม่มี metadata
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// สำหรับ function calling
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* จัดการ Text, FunctionCall, FunctionResult, Completion */ }
```

Fluent builder สำหรับปรับแต่ง:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // รวม chain-of-thought
    .WithMetadata()        // รวมข้อมูล model ใน Completion
    .WithFunctionCalls();  // เปิด function calling ระหว่าง stream
```

ให้ถือว่าชิ้นข้อมูลที่แสดงเป็นผลลัพธ์ชั่วคราวจนกว่า `run.Result` จะสำเร็จ เส้นทางสตรีมมิงร่วมที่เข้ากันได้กับ OpenAI และเส้นทางสตรีมมิงของ DeepSeek จะปฏิเสธข้อความ เหตุผล หรือข้อมูลเครื่องมือใหม่หลังสัญญาณสิ้นสุดที่ชัดเจน รวมถึงการเปลี่ยนเหตุผลของการสิ้นสุด: `run.Result` จะโยนข้อยกเว้น รอบที่ล้มเหลวจะไม่ถูกบันทึกในประวัติและจะไม่เรียกใช้เครื่องมือของรอบนั้น การจัดการความล้มเหลวนี้ไม่ย้อนคืนรอบก่อนหน้าหรือการกระทำที่ดำเนินการภายนอกไปแล้ว อนุญาตให้เดลตาสุดท้ายมาพร้อมเหตุการณ์สิ้นสุดครั้งแรก และให้เหตุการณ์ที่มีเฉพาะข้อมูลการใช้งานตามมาได้

## Stateless Streaming (StreamOnceAsync)

Stream response โดยไม่กระทบประวัติการสนทนา — เทียบเท่ากับ streaming ของ `AskOnceAsync`:

```csharp
await foreach (var chunk in service.StreamOnceAsync("แปลเป็นภาษาฝรั่งเศส"))
    Console.Write(chunk);
```

รองรับ `Message` สำหรับ multimodal input:

```csharp
var message = MessageBuilder.Create().AddText("อธิบายรูปนี้").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## สรุปการสนทนาก่อน Streaming

Policy การสรุปอัตโนมัติไม่ทำงานระหว่าง streaming ให้เรียก `ApplySummaryPolicyIfNeededAsync` ก่อน `StreamAsync` อย่างชัดเจน:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("ต่อจากที่คุยไว้...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [ให้งานที่ใช้เวลานานทำต่อ / Citation อาจชี้ไปยังเว็บหรือแหล่งอื่น ตำแหน่งเป็นของแต่ละคำตอบและส่วนเนื้อหา ไม่ใช่ผล Run ที่ต่อกัน เก็บ URL และชื่อเพื่อแสดงและตรวจสอบ การมีแหล่งอ้างอิงไม่ยืนยันทุกข้อความที่โมเดลสร้าง](perplexity.md).
