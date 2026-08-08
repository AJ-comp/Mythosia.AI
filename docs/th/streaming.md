# Streaming

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

Provider ที่รองรับ reasoning ทุกตัว (OpenAI, Claude, Gemini, Grok, DeepSeek) ใช้ pattern เดียวกัน ส่ง `StreamOptions` พร้อม reasoning:

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

`StreamingContentType.Reasoning` คือกระบวนการคิดภายในของ model ส่วน `StreamingContentType.Text` คือคำตอบสุดท้าย

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
