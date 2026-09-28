<div align="center">

🌐 [English](../../README.md) · [한국어](../ko/README.md) · [日本語](../ja/README.md) · [Français](../fr/README.md) · [Deutsch](../de/README.md) · [Русский](../ru/README.md) · [Українська](../uk/README.md) · [简体中文](../zh-Hans/README.md) · [繁體中文](../zh-Hant/README.md) · [Tiếng Việt](../vi/README.md) · [ภาษาไทย](README.md) · [Português](../pt/README.md) · [Español](../es/README.md)

<br>

[![OPEN SOURCE](https://img.shields.io/badge/OPEN%20SOURCE%20·%20.NET%20·%20NUGET-111827?style=flat-square&labelColor=111827&color=111827)](https://github.com/AJ-comp/Mythosia.AI)

<img width="694" height="181" alt="title_60" src="https://github.com/user-attachments/assets/57fd8c63-5b9b-46f6-be30-274354808c0d" />

### ไลบรารี .NET แบบโมดูลสำหรับสร้างแอปพลิเคชัน AI อัจฉริยะ

**เปลี่ยน provider เชื่อม RAG โหลดเอกสาร — ทั้งหมดผ่าน API เดียว**

<br>

[![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg?style=for-the-badge&logo=nuget&label=NuGet&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Downloads](https://img.shields.io/nuget/dt/Mythosia.AI.svg?style=for-the-badge&logo=nuget&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Docs](https://img.shields.io/badge/Docs-GitHub%20Pages-0ea5e9?style=for-the-badge&logo=readthedocs&logoColor=white)](https://aj-comp.github.io/Mythosia.AI/)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-6d28d9?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)

<br>

**[📖 เริ่มต้นใช้งาน](https://aj-comp.github.io/Mythosia.AI/)** &nbsp;·&nbsp; **[API Reference](https://aj-comp.github.io/Mythosia.AI/api/)** &nbsp;·&nbsp; **[GitHub ↗](https://github.com/AJ-comp/Mythosia.AI)**

<br>

</div>

## Demo / ทดสอบ (Chat UI)

ลองโมเดลและการค้นหาเอกสารใน Playground ก่อนเขียนโค้ดเชื่อมต่อแอปพลิเคชัน

ชมวิดีโอจากหน้าจอ Playground ปัจจุบัน: เลือกโมเดล เปลี่ยนภาษา และสำรวจการตั้งค่าเอกสารกับ pipeline RAG วิดีโอมีคำบรรยายภาษาอังกฤษ

https://github.com/user-attachments/assets/4cf90210-b000-41be-8317-a467e93e7504

### รันตัวอย่าง

รัน **`Mythosia.AI.Samples.ChatUi`** บนเครื่องของคุณ:

```bash
# จาก root ของ repository
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

<details>
<summary>ส่วนควบคุมและภาษาของ Playground</summary>

ค้นหาโมเดลตามชื่อหรือผู้ให้บริการและปรับการตั้งค่าทางด้านซ้าย สนทนาตรงกลาง และดูข้อมูลการประมวลผลใน Inspector ทางด้านขวาเพื่อทดลองก่อนนำไปใช้ในแอปพลิเคชัน กด Stop เพื่อหยุดรอคำตอบ โดยจะเลือกความเร็วได้เฉพาะกับโมเดลและจุดเชื่อมต่อที่รองรับ และ Fast อาจมีค่าใช้จ่ายเพิ่มเติม บนหน้าจอขนาดเล็ก Models และ Inspector จะเปิดเป็นแผงเลื่อน ดูวิธีรันในเครื่อง เพิ่มเอกสาร และตั้งค่ากระบวนการค้นคืนได้ใน[คู่มือ Chat UI](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md)

แผง Pipeline รองรับการตั้งค่าคีย์ มิติ และเวลาหมดอายุสำหรับ Voyage Context 4, Gemini Embedding 2 และ Perplexity แบบ contextual ส่วน Documents แสดงจำนวนส่วนข้อความและเวกเตอร์ พร้อมยกเลิกการสร้างดัชนีได้ การคืนค่าที่บันทึกไว้ การเชื่อมต่อฐานข้อมูลใหม่ และตัวอย่างโค้ดใช้การตั้งค่าที่เลือก ต้องสร้างดัชนีใหม่เมื่อเปลี่ยนโมเดลหรือมิติ

ใช้ตัวเลือกภาษาด้านบนเพื่อสลับภาษาหน้าจอได้ 13 ภาษา โดยไม่สูญเสียข้อความที่พิมพ์หรือการตั้งค่า ผู้ให้บริการทั้งเจ็ดรายจะแสดงเป็นกลุ่มที่พับไว้ เลือกขยายกลุ่มหรือค้นหาโมเดลได้

</details>

## ทำไมต้อง Mythosia.AI?

- **เปลี่ยนผู้ให้บริการผ่าน API เดียว** สำหรับแชต streaming การเรียกเครื่องมือ และคำตอบแบบมีโครงสร้าง
- **สร้างคำตอบจากเอกสารของคุณ** ด้วยตัวโหลด embedding การค้นคืน และการจัดอันดับใหม่
- **แยกการตั้งค่าแต่ละคำขอออกจากกัน** และควบคุมงานที่กำลังทำผ่าน Run API ร่วมกัน
- **เลือกเฉพาะแพ็กเกจที่จำเป็น** ตั้งแต่ไลบรารีหลัก ไปจนถึง RAG และส่วนเชื่อมต่อที่เก็บเวกเตอร์

## ติดตั้ง Package ไหน?

```
dotnet add package Mythosia.AI                    # เริ่มจากนี้ (แค่นี้ก็พอ)
dotnet add package Mythosia.AI.Rag                # เพิ่มเติม: ถ้าต้องการ RAG
dotnet add package Mythosia.VectorDb.Postgres     # เพิ่มเติม: ถ้าต้องการ vector store สำหรับ production
```

| ขั้นตอน | Package | เมื่อไหร่ |
| :--: | --- | --- |
| **1** | **`Mythosia.AI`** | **เริ่มจากนี้** — สร้างข้อความ streaming เรียกฟังก์ชัน structured output (OpenAI / Claude / Gemini / Grok / DeepSeek / Perplexity) |
| **2** | **`Mythosia.AI.Rag`** | เมื่อต้องการ RAG — แบ่งข้อความ embedding hybrid search reranking InMemory store document loaders (Word / Excel / PowerPoint / PDF) |
| **3** | **`Mythosia.VectorDb.Postgres`** / **`Qdrant`** / **`Pinecone`** | เมื่อต้องการ vector store สำหรับ production แทน InMemory — เลือกหนึ่งตัว |

เตรียมการตั้งค่าอิสระด้วย `CreateRequest(...).WithTemperature(...).GetCompletionAsync()` ดู Before/After, Run, profile และข้อจำกัดของบทสนทนาร่วมกันใน [คู่มือคำขอ](request-building.md)

คำขอที่ต้องคำนึงถึงเวลารอสามารถเลือก[ความเร็วในการประมวลผล](request-building.md#inference-speed) ได้ `WithSpeed` คงโมเดลและระดับการคิด ส่วน `Processing` รายงานโหมดที่ใช้จริง Fast เป็นตัวเลือกเสียเงินสำหรับการผสมที่รองรับ

## เริ่มต้นอย่างรวดเร็ว

### สร้างข้อความพื้นฐาน

```csharp
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
var response = await service.GetCompletionAsync("สวัสดี!");
```

### Streaming

```csharp
await using var run = await service.StartRunAsync(
    "เล่าเรื่องให้ฟังหน่อย",
    onText: text => Console.Write(text));
string answer = (await run.Result).Text;
```

### Streaming พร้อม reasoning

OpenAI, Claude, Gemini, Grok และ DeepSeek Flash ส่งเนื้อหาการใช้เหตุผลของผู้ให้บริการผ่านรูปแบบ streaming เดียวกัน เปิดการใช้เหตุผลในบริการหรือคำขอ แล้วสังเกตด้วย `StreamOptions.WithReasoning()`:

```csharp
await using var run = await service.StartRunAsync(
    message, options: new StreamOptions().WithReasoning());
await foreach (var content in run.StreamAsync())
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[คิด] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### การเรียกใช้ฟังก์ชัน

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "ดึงข้อมูลสภาพอากาศปัจจุบันของสถานที่",
        ("location", "ชื่อเมืองและประเทศ", required: true),
        (string location) => $"สภาพอากาศที่ {location} แดดออก 32°C"
    );

var response = await service.GetCompletionAsync("อากาศที่กรุงเทพเป็นอย่างไร?");
```

ตามค่าเริ่มต้น เครื่องมือที่โมเดลส่งมาในคำตอบเดียวจะทำงานตามลำดับ หากฟังก์ชันที่ลงทะเบียนเป็นอิสระต่อกัน คุณสามารถเปิดการทำงานขนานโดยจำกัดจำนวน handler ได้:

```csharp
using Mythosia.AI.Models.Functions;

service.DefaultPolicy = new FunctionCallingPolicy
{
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 3
};
```

ผลลัพธ์ของ batch ปกติจะส่งกลับให้โมเดลตามลำดับการเรียกเดิมของผู้ให้บริการ เมื่อยกเลิก ระบบข้ามการเรียกที่ยังไม่เริ่มและใส่ผลการยกเลิกที่ตรงกัน เครื่องมือที่เริ่มแล้วจะได้รับ token หากรองรับ และระบบรอจนเสร็จเพื่อรักษาคู่การเรียก/ผลลัพธ์ในประวัติ

`FunctionCallingPolicy.TimeoutSeconds` ครอบคลุมลูป streaming ทุก round รวมทั้ง header ของคำตอบและเนื้อหา SSE โดยไม่เริ่มจับเวลาใหม่ระหว่าง round ของเครื่องมือ เมื่อหมดเวลาของ policy จะเกิด `AIServiceException` ส่วนการยกเลิกโดยผู้เรียกยังคงเป็น `OperationCanceledException` ที่ผูกกับ token ของผู้เรียก

หากมีงานที่ทำได้โดยไม่ต้องรอผล เช่น อธิบายของที่ควรเตรียมเดินทางระหว่างเครื่องมือกำลังดึงข้อมูลอากาศ โมเดลสามารถทำส่วนนั้นก่อนได้โดยไม่ต้องหยุดรอทั้งคำตอบ ใช้ `FunctionDefinition.AllowAsync = true` หรือ `FunctionBuilder.WithAsync()` เพื่อเลือกอนุญาตการเรียกเครื่องมือแบบอะซิงโครนัสของ GPT-6 Astra / Sol / Luna ผ่าน Responses ค่าเริ่มต้นคือ `false` ส่วนโมเดลที่ไม่รองรับจะรอผลจาก handler เดิม ดูตัวอย่างและอายุของคำขอใน[คู่มือการเรียกฟังก์ชัน](function-calling.md#async-tool-calling)

กลไกนี้แยกจาก handler C# `async` และการจัดตาราง handler แบบขนาน โมเดลที่ไม่รองรับจะไม่ได้รับตัวเลือก API ที่ใช้ไม่ได้

### การสร้างและแก้ไขภาพ

สร้างภาพร่างจากข้อความหรือแก้ไขภาพเดิมผ่านความสามารถเสริมที่ใช้ร่วมกันได้ใน OpenAI, Google และ xAI โดยเลือกโมเดลภาพแยกจากโมเดลแชต:

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Pixels(1024, 1024),
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

ดูตัวอย่างสร้างและแก้ไขภาพใน[คู่มือผู้ให้บริการ](providers.md#image-generation) หรือดูการเปลี่ยน API ครั้งใหญ่ใน[ตัวเลือกภาพแบบมีชนิดและการย้าย](providers.md#image-options-migration) xAI ใช้ `ImageOutputFormat.Auto` และควรเลือกนามสกุลไฟล์จาก `GeneratedImage.MediaType`

preset ของ Google ขึ้นกับโมเดล: Flash รองรับ 512/1K/2K/4K, Flash-Lite ปัจจุบันอนุญาต 1K และ Pro รองรับ 1K/2K/4K โดย Flash/Lite มีอัตราส่วนภาพ 14 แบบ ส่วน Pro มีมาตรฐาน 10 แบบ ทุกโมเดลรับ `Auto` ตรวจ `GetImageCapabilities(model)` ก่อนแสดงตัวเลือก ขนาดหรืออัตราส่วนที่ระบุชัดแต่ไม่รองรับจะถูกปฏิเสธก่อนส่ง HTTP ทั้งตอนสร้างและแก้ไขภาพ ดู[ตารางโมเดลและความต่างของเอกสาร Flash-Lite](providers.md#google-image-options)

### Structured output (พื้นฐาน)

```csharp
// Deserialize ผลลัพธ์ LLM เป็น C# POCO โดยตรง พร้อม auto-recovery
var result = await service.GetCompletionAsync<WeatherResponse>(
    "อากาศที่กรุงเทพเป็นอย่างไร?");
```

### Structured output (รายการ)

```csharp
// Collection ทำงานได้โดยตรง ไม่ต้องมี wrapper
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "ดึง entity ทั้งหมดจากเอกสารนี้...");
```

### Structured output (streaming)

```csharp
// Stream fragment แบบ real-time + รับ object ที่ deserialize แล้วเมื่อเสร็จ
var run = service.BeginStream(prompt).As<MyDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);          // UI real-time

MyDto dto = await run.Result;      // parse และ auto-recovery แล้ว
```

### นโยบายสรุปบทสนทนา

```csharp
// สรุปข้อความเก่าอัตโนมัติเมื่อบทสนทนายาว
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// Trigger ตามจำนวน token
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// ใช้งานตามปกติ — การสรุปเกิดขึ้นอัตโนมัติ
await service.GetCompletionAsync("ต่อการสนทนา...");

// เมื่อ streaming ให้เรียก policy สรุปก่อน StreamAsync()
await service.ApplySummaryPolicyIfNeededAsync();
await foreach (var chunk in service.StreamAsync("ต่อไป..."))
    Console.Write(chunk.Content);

// บันทึก/โหลดสรุประหว่าง session
string saved = service.ConversationPolicy.CurrentSummary;
policy.LoadSummary(saved);
```

### RAG (Retrieval-Augmented Generation)

เลือกการค้นคืนด้วยคำสำคัญ ความหมาย หรือ hybrid โดยไม่บังคับให้ทุกคำค้นสร้าง embedding `UseKeywordSearch()` ข้าม embedding ของคำค้น; `UseRetriever(...)` เชื่อมดัชนีภายนอก; `UseHybridSearch(HybridSearchOptions)` ส่งค่าน้ำหนักและการตั้งค่าผู้สมัครอย่างชัดเจน การนำเข้าเอกสารยังคงสร้างเวกเตอร์ ดู[โหมดการค้นคืนและการรองรับของสโตร์](rag-hybrid-search.md)

```bash
dotnet add package Mythosia.AI.Rag
```

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("นโยบายการคืนสินค้าคืออะไร?");
```

หากต้องการให้ agent ควบคุมการค้นคืน ให้ลงทะเบียนสโตร์ผ่าน `WithAgenticRag(...)` และเริ่มด้วย `service.WithMaxRounds(10).StartRunAsync(...)` รอ `run.Result` หรืออ่าน `run.StreamAsync()` จากงานเดียวกัน ดูตัวอย่างเต็มใน [README ของ Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md)

#### รักษาบริบทของเอกสารและหน้าที่ของคำค้น

ข้อความแต่ละส่วนอาจต้องอาศัยเนื้อหาข้างเคียง และคำถามค้นหามีหน้าที่ต่างจากเอกสารที่ทำดัชนี RAG 8.2.0 เพิ่ม embedding แบบมีบริบทของ Voyage และ Gemini Embedding 2 สำหรับข้อความที่สกัดจาก TXT, Markdown และ PDF

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

[การตั้งค่าและสัญญาของผู้ให้บริการ](rag-embedding.md#retrieval-aware-embeddings): `UseVoyageEmbedding(...)`, `UseGeminiEmbedding(...)`, `IRetrievalEmbeddingProvider` / `EmbeddingDocument` (RAG 8.2.0, RAG Abstractions 6.4.0).

## Provider ที่รองรับ

> Grok 4.7: ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การเลือกโมเดล การให้เหตุผล และความเร็ว](providers.md#grok-47)

> GPT-6 Sol/Luna: ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การเลือกโมเดลและรุ่นที่ต้องใช้](providers.md#gpt-6-sol-luna)

> Claude Opus 5.5: ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การตั้งค่าและการย้ายมาใช้](providers.md#claude-opus-55)

| Provider | Package | Model |
| --- | --- | --- |
| **OpenAI** | `Mythosia.AI` | GPT-6 Astra / Sol / Luna, GPT-5.6 Sol / Terra / Luna, GPT-5.5 / 5.5 Pro / 5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro / 5.3 Codex / 5.2 / 5.2 Pro / 5.1, GPT-4.1 / 4.1 Mini, GPT-4o / 4o Mini |
| **Anthropic** | `Mythosia.AI` | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), [Opus 5.5](providers.md#claude-opus-55) / 5 / 4.8 / 4.7 / 4.6 / 4.5, Sonnet 5 / 4.6 / 4.5, Haiku 4.5 |
| **Google** | `Mythosia.AI` | Gemini 3.8 Flash, Gemini 3.7 Flash, Gemini 3.6 Flash, Gemini 3.5 Flash/Flash-Lite, Gemini 3.1 Pro Preview/Flash-Lite, Gemini 3 Flash Preview, Gemini 2.5 Pro/Flash/Flash-Lite, Gemini 3.1 Flash Image, Gemini 3.1 Flash-Lite Image, Gemini 3 Pro Image |
| **xAI** | `Mythosia.AI` | Grok 4.7, Grok 4.6, Grok 4.5 (ค่าเริ่มต้น), Grok 4.3, Grok 4.20 (reasoning / non-reasoning), Grok Build |
| **DeepSeek** | `Mythosia.AI` | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | `Mythosia.AI` | Preset ของ Agent API และ `perplexity/sonar` |
| **Alibaba / Qwen** | `Mythosia.AI.Providers.Alibaba` | Qwen Max / Plus / Turbo / Qwen3 / Qwen3.5 variants |

ใช้ Perplexity เมื่อคำตอบต้องอาศัยข้อมูลล่าสุดและมีแหล่งอ้างอิงให้ผู้อ่านตรวจสอบ `PerplexityService` เรียก Agent API ส่วนการค้นหาและ embedding แบบแยกใช้สร้างการดึงเอกสารให้โมเดลตอบคำถามที่คุณเลือกได้ [Perplexity Agent API การค้นหา และ embedding](perplexity.md).

สำหรับการตรวจเอกสารยาวหรืองานที่เรียกเครื่องมือหลายรอบ สามารถเลือก Gemini 3.7 Flash หรือ 3.8 Flash ผ่านอะแดปเตอร์ Google เดิมได้ รองรับตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 โดยโมเดลเริ่มต้นยังเป็น Gemini 3.6 Flash

หากต้องการร่างอย่างรวดเร็วแล้วตรวจทานเชิงลึก ให้เลือก Grok 4.6 อย่างชัดเจนและตั้งระดับ `Low` ถึง `XHigh` รองรับตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 โดย `XAIService` ยังใช้ Grok 4.5 เป็นค่าเริ่มต้น ดู[การตั้งค่า Grok](providers.md#xai-xaiservice)

สร้างภาพร่างหรือรวมภาพอ้างอิงด้วย [Grok Imagine Image 2.0](providers.md#grok-imagine-image-20) ผ่าน `IImageGenerationService` ใช้ `OutputFormat = ImageOutputFormat.Auto` และเลือกนามสกุลตาม `MediaType` เพราะ xAI เลือก codec ไม่ได้ ดู[การย้ายตัวเลือกภาพ](providers.md#image-options-migration) โมเดลแชตไม่เปลี่ยน

เลือก Flare สำหรับภาพร่างที่รวดเร็ว และ Sunburst สำหรับการแก้ไขอย่างแม่นยำ [การสร้างและแก้ไขภาพ GPT Image 2.5](providers.md#gpt-image-25) ใช้ API ภาพเดิมโดยระบุโมเดลต่อคำขอ ค่าเริ่มต้นของ OpenAI ยังคงเป็น GPT Image 2

สำหรับวิเคราะห์กราฟ ภาพหน้าจอ เรียกฟังก์ชันภายใน หรือตรวจคำตอบเชิงลึก ใช้ [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash) การใช้เหตุผลปิดโดยค่าเริ่มต้น เปิดด้วย `WithDeepSeekReasoning(...)` หรือ `WithReasoning(...)` ต่อคำขอ

งานข้อความล้วนเลือก `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813) ได้ Flash ยังเป็นค่าเริ่มต้นและรองรับภาพ ทั้งสองรองรับเหตุผล Low/High/Max และขีดจำกัดเอาต์พุตเดียวกัน ตั้ง `UseResponsesApi = true` ก่อนสร้างคำขอเพื่อใช้ Responses ผ่าน API completion, streaming, Run และฟังก์ชันภายในเดิม ค่าเริ่มต้นยังเป็น `false` เพื่อคง Chat Completions ของแอปเดิม และจะเก็บตัวเลือกนี้ตลอดคำขอรวมรอบเครื่องมือ Responses ส่งประวัติสนทนาและเหตุผลต้นฉบับทั้งหมดซ้ำ โดยไม่พึ่ง ID คำตอบที่เก็บบนเซิร์ฟเวอร์

ใช้ภาพที่อัปโหลดซ้ำในหลายคำถามกับ Flash ผ่าน `DeepSeekImageFileContent` ได้ทั้ง Chat Completions และ Responses ส่วน V4 Pro รองรับเฉพาะข้อความและปฏิเสธภาพ ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การอัปโหลด ใช้ภาพซ้ำ และข้อจำกัด](providers.md#deepseek-deepseekservice)

> Claude Fable 5 และ Claude Mythos 5 ต้องเก็บข้อมูล 30 วัน และไม่รองรับข้อตกลงไม่เก็บข้อมูล adaptive thinking เปิดอยู่เสมอ หากผู้เรียกปิดการใช้เหตุผล Mythosia จะใช้ effort ระดับต่ำและไม่ขอสรุปการคิด Mythos 5 จำกัดเฉพาะลูกค้า Project Glasswing ที่ได้รับอนุมัติ

## คู่มือและการย้ายเวอร์ชัน

สำหรับ TXT และ Markdown ให้เลือก[ตัวแบ่งตามกฎ](text-splitters.md) ตามโครงสร้างเอกสาร มีการตรวจขนาด overlap และขอบเขต Unicode พร้อมเก็บหัวข้อ โค้ด และแถวตารางของ Markdown จำนวนอักขระหรือคำไม่ใช่เพดาน token ของโมเดล เงื่อนไขตารางและการเยื้องโค้ดยังคงความหมายเดิม ส่วนการทำซ้ำบริบท Markdown ที่มากเกินไปจะหยุดด้วยข้อยกเว้นที่ชัดเจน

เพื่อไม่ให้การทำดัชนีที่ดูเหมือนสำเร็จเขียนทับชังก์หรือจับคู่เวกเตอร์ผิด [การตรวจสอบดัชนี](rag-pipeline.md#indexing-validation) จะปฏิเสธ ID และ batch embedding ที่ไม่ถูกต้องก่อนบันทึก ตัวแบ่งที่กำหนดเองต้องสร้าง ID ที่ไม่ซ้ำและสืบทอดเมทาดาทาของเอกสาร

[ID ไฟล์ที่คงที่](document-loaders.md#file-source-identity) [การตรวจสอบเวกเตอร์คำถาม](rag-embedding.md#query-embedding-validation) และ[การจัดเก็บรายเอกสารพร้อมยกเลิก URL](rag-pipeline.md#custom-persistence) ช่วยป้องกันการลงทะเบียนซ้ำ การค้นหาที่ผิด และชิ้นข้อมูลเก่าค้างอยู่

แพ็กเกจพรีวิวเสริม `Mythosia.AI.Rag.Search.Pixie` ใช้เปรียบเทียบการค้นหานิวรัลแบบ sparse ในเครื่องกับการค้นหาเดิม โดยคงผู้ให้บริการ dense embedding และเก็บดัชนี PIXIE ในหน่วยความจำ ไม่ย้ายสโตร์ถาวรหรือเปลี่ยนการค้นหาเริ่มต้นโดยอัตโนมัติ [คู่มือเชื่อมต่อและเปรียบเทียบ PIXIE (ภาษาอังกฤษ)](../rag-pixie-search.md).

[โครงสร้างพื้นฐานประเมินการค้นคืน](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Rag.Evaluation/README.md) รองรับชุดข้อมูลใช้ซ้ำ adapter การค้นหา รายงานที่เก็บถาวร และการตรวจ regression ขยายตัวประเมินเดิมเพื่อทดสอบวิธีค้นหาใหม่และชุดเอกสารของคุณได้

แยกการตั้งค่าคำขอ หยุดงาน และรับคำตอบพร้อมการใช้โทเค็นและแหล่งที่มา [คู่มือย้ายไป v8](v8-migration.md) รวมการเปลี่ยนสถาปัตยกรรมหกด้าน ตัวอย่าง และขอบเขตการตรวจสอบ

> เวอร์ชันแพ็กเกจที่เอกสารนี้อ้างอิง: [Mythosia.AI 8.1.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v810), [Abstractions 4.1.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v410), [Alibaba 3.0.1](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v301), [RAG 8.2.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v820), [PostgreSQL 10.8.1](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081), [MCP 0.1.1-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v011-preview), [Serving.Abstractions 1.0.0](../../src/serving/Mythosia.AI.Serving.Abstractions/RELEASE_NOTES.md#v100), [Serving.Ollama 1.0.0](../../src/serving/Mythosia.AI.Serving.Ollama/RELEASE_NOTES.md#v100), [Serving.LlamaCpp 1.0.0](../../src/serving/Mythosia.AI.Serving.LlamaCpp/RELEASE_NOTES.md#v100), [Serving.Vllm 1.1.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v110). ดูเวอร์ชันแพ็กเกจการค้นคืน เอกสาร และเวกเตอร์ที่เหลือใน[ตารางแพตช์ก่อนหน้า](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811) และ[รุ่นที่ออกพร้อมกันก่อนหน้า](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v810)

> [แพตช์ RAG 8.1.1 / PostgreSQL 10.8.1](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811): แรปเปอร์ RAG ที่เชื่อมต่ออยู่แล้วจะรับการเปลี่ยนตัวเขียนคำถามใหม่ระหว่างทำงาน และการค้นหาไฮบริดแบบผสมของ PostgreSQL จะใช้การตั้งค่าค้นหาเวกเตอร์ที่กำหนดไว้ แพ็กเกจหลัก `Mythosia.AI` ยังคงเป็น 8.1.0

---

## สถาปัตยกรรม

<a href="../assets/architecture.svg">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="../assets/architecture-dark.svg">
    <img src="../assets/architecture.svg" alt="สถาปัตยกรรม Mythosia.AI: AI หลัก การประสาน RAG ตัวโหลดเอกสาร ที่เก็บเวกเตอร์ อินเทอร์เฟซร่วม การเชื่อมต่อ MCP และการจัดการ Ollama, llama.cpp, vLLM แบบอิสระ" width="1600">
  </picture>
</a>

### รายละเอียดการพึ่งพาของแพ็กเกจ

ลูกศรแสดงการอ้างอิงแพ็กเกจโดยตรง แพ็กเกจร่วมปรากฏในหลายภาพ ไคลเอนต์ Serving ใช้อินเทอร์เฟซจัดการร่วมกันและไม่ขึ้นกับ AI หลัก

#### AI หลักและส่วนขยาย

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Extensions["ส่วนขยายผู้ให้บริการและเครื่องมือ"]
        Alibaba["Mythosia.AI.<br/>Providers.Alibaba"]:::extension
        Mcp["Mythosia.AI.Mcp"]:::extension
    end
    AI["Mythosia.AI"]:::core
    AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
    subgraph Independent["การจัดการเซิร์ฟเวอร์แบบอิสระ"]
        ServingAbs["Mythosia.AI.Serving.<br/>Abstractions"]:::contract
        OllamaServing["Mythosia.AI.<br/>Serving.Ollama"]:::extension
        LlamaCppServing["Mythosia.AI.<br/>Serving.LlamaCpp"]:::extension
        VllmServing["Mythosia.AI.<br/>Serving.Vllm"]:::extension
        OllamaServing --> ServingAbs
        LlamaCppServing --> ServingAbs
        VllmServing --> ServingAbs
    end
    Alibaba --> AI
    Mcp --> AI
    AI --> AIAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### RAG และการโหลดเอกสาร

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    Rag["Mythosia.AI.Rag"]:::rag
    subgraph Contracts["สัญญา API ของ AI และ RAG"]
        AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
        RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    end
    InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
    subgraph Documents["การโหลดเอกสาร"]
        Office["Mythosia.Documents.<br/>Office"]:::documents
        Pdf["Mythosia.Documents.<br/>Pdf"]:::documents
        LoaderAbs["Mythosia.Documents.<br/>Abstractions"]:::contract
        Office --> LoaderAbs
        Pdf --> LoaderAbs
    end
    Rag --> AIAbs
    Rag --> RagAbs
    Rag --> InMem
    Rag --> Office
    Rag --> Pdf
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### ที่เก็บเวกเตอร์และการค้นหา

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Stores["ที่เก็บเวกเตอร์"]
        InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
        Pg["Mythosia.VectorDb.<br/>Postgres"]:::store
        Qd["Mythosia.VectorDb.<br/>Qdrant"]:::store
        Pine["Mythosia.VectorDb.<br/>Pinecone"]:::store
    end
    subgraph Search["การค้นหาด้วยโครงข่ายประสาทเทียมแบบเลือกใช้"]
        Pixie["Mythosia.AI.Rag.<br/>Search.Pixie"]:::rag
    end
    RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    VdbAbs["Mythosia.VectorDb.<br/>Abstractions"]:::contract
    InMem --> RagAbs
    InMem --> VdbAbs
    RagAbs --> VdbAbs
    Pg --> VdbAbs
    Qd --> VdbAbs
    Pine --> VdbAbs
    Pixie --> VdbAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

## Package ทั้งหมด

### Core

| Package | NuGet | คำอธิบาย |
| --- | --- | --- |
| [Mythosia.AI](../../src/core/Mythosia.AI/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI) | ไลบรารี core — provider ในตัว streaming เรียกฟังก์ชัน และรองรับ multimodal |
| [Mythosia.AI.Abstractions](../../src/core/Mythosia.AI.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Abstractions) | Interface `IAIService` และ model ร่วม — contract package สำหรับไลบรารี |
| [Mythosia.AI.Providers.Alibaba](../../src/core/Mythosia.AI.Providers.Alibaba/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Providers.Alibaba.svg)](https://www.nuget.org/packages/Mythosia.AI.Providers.Alibaba) | Package provider Alibaba / Qwen บน `Mythosia.AI` |

### RAG

| Package | NuGet | คำอธิบาย |
| --- | --- | --- |
| [Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag) | Fluent extension RAG สำหรับ IAIService ด้วย API `.WithRag()` |
| [Mythosia.AI.Rag.Abstractions](../../src/rag/Mythosia.AI.Rag.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag.Abstractions) | Interface และ model ของ component ใน RAG pipeline |

### Document Loaders

| Package | NuGet | คำอธิบาย |
| --- | --- | --- |
| [Mythosia.Documents.Abstractions](../../src/loaders/Mythosia.Documents.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | Interface และ model ของ document loader (`IDocumentLoader`, `DoclingDocument`) |
| [Mythosia.Documents.Office](../../src/loaders/Mythosia.Documents.Office/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Office.svg)](https://www.nuget.org/packages/Mythosia.Documents.Office) | Parser OpenXml สำหรับ Word / Excel / PowerPoint |
| [Mythosia.Documents.Pdf](../../src/loaders/Mythosia.Documents.Pdf/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Pdf.svg)](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | Parser PDF โดยใช้ PdfPig |

### Vector Stores

> **เลือกหนึ่งหรือหลายตัว** — ทุกตัว implement `IVectorStore` จาก package Abstractions

| Package | NuGet | คำอธิบาย |
| --- | --- | --- |
| [Mythosia.VectorDb.Abstractions](../../src/vectordb/Mythosia.VectorDb.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Abstractions) | Contract `IVectorStore` · `VectorRecord` · `VectorFilter` |
| [Mythosia.VectorDb.InMemory](../../src/vectordb/Mythosia.VectorDb.InMemory/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.InMemory.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.InMemory) | Store ใน RAM — ไม่ต้องมี infrastructure เหมาะสำหรับ prototype |
| [Mythosia.VectorDb.Pinecone](../../src/vectordb/Mythosia.VectorDb.Pinecone/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Pinecone.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Pinecone) | Pinecone HTTP API — แยกตาม index/namespace/scope |
| [Mythosia.VectorDb.Postgres](../../src/vectordb/Mythosia.VectorDb.Postgres/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Postgres.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Postgres) | PostgreSQL + pgvector — index HNSW / IVFFlat พร้อม production |
| [Mythosia.VectorDb.Qdrant](../../src/vectordb/Mythosia.VectorDb.Qdrant/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Qdrant.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Qdrant) | Qdrant gRPC client — Cosine / Euclidean / Dot auto-provision |

### Serving — Control Plane

สร้างหน้าจอเลือกโมเดลและดูสถานะเซิร์ฟเวอร์ด้วย API จัดการร่วมสำหรับ Ollama, llama.cpp และ vLLM ที่กำลังทำงาน `IModelServer` อ่านสถานะ โมเดล และความสามารถ โดยการสำรวจจะไม่โหลดหรือดาวน์โหลดโมเดล ไคลเอนต์เหล่านี้เชื่อมต่อกับเซิร์ฟเวอร์ที่มีอยู่ ไม่ได้โฮสต์เอนจินหรือส่งคำขอแชต

อินเทอร์เฟซเสริม `IModelLifecycle`, `IModelDownloader` และ `IModelMetricsProvider` ให้เรียกการทำงานอย่างชัดเจนเมื่อมีให้ใช้ โปรดตรวจสอบความสามารถของเซิร์ฟเวอร์ที่เชื่อมต่อ: `Unknown` หมายถึงหลักฐานไม่เพียงพอ ไม่ใช่ `Unsupported` และ `Supported` ก็ไม่ได้รับประกันว่าจะสำเร็จกับทุกโมเดล สถานะการติดตั้งและการโหลดที่ไม่ทราบจะยังคงเป็นไม่ทราบ

การตรวจสอบกับเซิร์ฟเวอร์จริงผ่านบน Ollama **0.34.4** (`qwen2.5:0.5b`), llama.cpp **b11146** ในโหมด Router และโมเดลเดี่ยว (Qwen2.5 0.5B, Q4_K_M) และ vLLM **0.30.0** (โมเดล Qwen ขนาดเล็ก) ผลลัพธ์ใช้กับการตั้งค่าที่ตรวจสอบแล้วเท่านั้น ดูขอบเขตการทำงานที่ตรวจสอบและข้อจำกัดของแต่ละเอนจินใน[คู่มือจัดการเซิร์ฟเวอร์](serving.md)

| Package | NuGet | คำอธิบาย |
| --- | --- | --- |
| [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Abstractions) | อินเทอร์เฟซจัดการร่วมและข้อมูลเซิร์ฟเวอร์ โมเดล ความสามารถที่แก้ไขไม่ได้ |
| [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Ollama.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Ollama) | รายการและสถานะ Ollama การโหลด/นำออกอย่างชัดเจน และดาวน์โหลดแบบสตรีม |
| [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.LlamaCpp.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.LlamaCpp) | ตรวจสอบ llama.cpp คำสั่ง router ที่ยืนยันโหมดแล้ว และเมตริกโดยไม่โหลดอัตโนมัติ |
| [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Vllm.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Vllm) | การ์ดโมเดล สถานะ รุ่น และเมตริกพร้อมป้ายกำกับของ vLLM โดยคง API เดิม |

## โครงสร้าง Repository

```text
src/
  core/
    Mythosia.AI/                        # ไลบรารี AI หลัก
    Mythosia.AI.Abstractions/           # Interface IAIService และ model ร่วม
    Mythosia.AI.Providers.Alibaba/      # Package provider Alibaba / Qwen
  loaders/
    Mythosia.Documents.Abstractions/    # Contract document loader (IDocumentLoader, DoclingDocument)
    Mythosia.Documents.Office/          # Loader เอกสาร Office (Word/Excel/PowerPoint)
    Mythosia.Documents.Pdf/             # Loader เอกสาร PDF
  rag/
    Mythosia.AI.Rag/                    # RAG Fluent API และ pipeline
    Mythosia.AI.Rag.Abstractions/       # Interface และ model RAG (RagDocument)
  serving/
    Mythosia.AI.Serving.Abstractions/  # อินเทอร์เฟซจัดการเซิร์ฟเวอร์โมเดลร่วม
    Mythosia.AI.Serving.Ollama/        # จัดการ Ollama และดาวน์โหลดอย่างชัดเจน
    Mythosia.AI.Serving.LlamaCpp/      # จัดการ llama.cpp แบบโมเดลเดี่ยวและ router
    Mythosia.AI.Serving.Vllm/          # การจัดการและเมตริก vLLM
  vectordb/
    Mythosia.VectorDb.Abstractions/     # Contract vector store
    Mythosia.VectorDb.InMemory/         # Vector store ใน RAM
    Mythosia.VectorDb.Pinecone/         # Vector store Pinecone
    Mythosia.VectorDb.Postgres/         # PostgreSQL + pgvector
    Mythosia.VectorDb.Qdrant/           # Vector store Qdrant
apps/                                   # แอปพลิเคชันตัวอย่าง
tests/                                  # Project test unit / integration
```

## การติดตั้ง

```bash
dotnet add package Mythosia.AI
```

สำหรับ LINQ operation ขั้นสูงกับ stream:

```bash
dotnet add package System.Linq.Async
```

## เอกสาร

สำหรับการร่างคำตอบเร็วแล้วตรวจอย่างละเอียด หรือคำตอบที่อ้างอิงข้อมูลปัจจุบันและเอกสารที่เก็บไว้ ดู[การให้เหตุผลและค้นหาพร้อมแหล่งที่มา](reasoning-and-search.md)

- **[📖 เว็บไซต์เอกสารทั้งหมด](https://aj-comp.github.io/Mythosia.AI/)** — เอกสารที่สร้างด้วย DocFX ครอบคลุมทุกฟีเจอร์ pipeline RAG ที่เก็บเวกเตอร์ และ API
- [คู่มือเริ่มต้น](getting-started.md)
- [README Mythosia.AI](../../src/core/Mythosia.AI/README.md) — API reference ฉบับสมบูรณ์: เรียกฟังก์ชัน streaming และตั้งค่า model
- [README Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md) — การใช้งาน RAG pipeline และ custom implementation
- [คู่มือ loader](document-loaders.md)
- [Release notes](../../src/core/Mythosia.AI/RELEASE_NOTES.md)

## ตรวจสอบความเร็วกับผู้ให้บริการจริง

รันจาก root ของ repository:

```powershell
./build/test-inference-speed-live.ps1
```

ชุดทดสอบนี้มีค่าใช้จ่าย โดยใช้การตั้งค่า Key Vault เดิมและ prompt สังเคราะห์ ทดสอบ Anthropic Opus 5.5, OpenAI GPT-6 Astra, Gemini 3.8 Flash และ Grok 4.6 ในโหมด ProviderDefault/Standard/Fast ผ่าน completion และ Run รวม 24 กรณี ข้อผิดพลาดสิทธิ์บัญชี การไม่รายงานโหมดที่ใช้จริง และการลดโหมดโดยเซิร์ฟเวอร์ไม่นับว่าตรวจ Fast สำเร็จ ทุกกรณีต้องผ่านโดยไม่มีการข้าม รายงานอยู่ใน `artifacts/test-results/inference-speed-live` ใช้ `-NoBuild` หลัง build การทดสอบ Release ปัจจุบันแล้วเท่านั้น คำสั่งนี้อธิบายวิธีรัน ไม่ได้ยืนยันว่าบัญชีปัจจุบันผ่านการทดสอบแล้ว

## สัญญาอนุญาต

โปรเจกต์นี้เผยแพร่ภายใต้ [สัญญาอนุญาต MIT](https://github.com/AJ-comp/Mythosia.AI/blob/main/LICENSE)

## ที่มา

เดิมโปรเจกต์นี้เป็นส่วนหนึ่งของ [Mythosia](https://github.com/AJ-comp/Mythosia)

[สร้างตัวเลือกโมเดลจากคำนิยามการรองรับร่วมกัน](model-capabilities.md).
