# AIRequestContext

## คืออะไร?

`AIRequestContext` ให้คุณเปลี่ยน **สิ่งที่ model เห็น** สำหรับ request เดียว — inject คำสั่งเพิ่มเติม เพิ่มเอกสารอ้างอิง หรือแทนที่ข้อความของ user ทั้งหมด — โดยไม่เปลี่ยน system message หรือประวัติการสนทนาของ service อย่างถาวร

## ปัญหาที่แก้ได้

ลองนึกถึง RAG pipeline ที่ต้องดึงเอกสารที่เกี่ยวข้องและใส่ลงใน prompt **โดยไม่มี** `AIRequestContext` คุณต้องแก้ system message ตรง ๆ:

```csharp
// ❌ ไม่มี AIRequestContext — ทำให้ system message ปนเปื้อน
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\nใช้ context ต่อไปนี้ในการตอบ:\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// คืนค่า — แต่ context นี้ติดค้างในประวัติการสนทนาแล้ว
service.SystemMessage = originalSystem;
```

ปัญหาของวิธีนี้:

- Context ที่ดึงมา **รั่วเข้าประวัติการสนทนา** — request ถัดไปยังเห็นอยู่
- การคืนค่า system message ไม่ช่วยลบ context ออกจากประวัติ
- ใน web app หลาย user การ mutate shared state ทำให้เกิด race condition

**ด้วย** `AIRequestContext` การ inject ถูกจำกัดไว้ใน request เดียว:

```csharp
// ✅ ด้วย AIRequestContext — สะอาด มีขอบเขต ไม่มีผลข้างเคียง
var answer = await service.GetCompletionAsync(userQuestion,
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nใช้ context ต่อไปนี้ในการตอบ:\n{retrievedDocs}"
    });
```

System message ถูกแก้เฉพาะ call นี้เท่านั้น request ถัดไปเห็น system message เดิม ไม่ต้อง cleanup

## Properties ที่ใช้ได้

### SystemMessagePrefix

เพิ่มข้อความต้น system message เฉพาะ request นี้:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "วันนี้คือ 2026-03-31\n"
};

var response = await service.GetCompletionAsync("วันนี้วันที่เท่าไหร่?", context: context);
```

**ใช้เมื่อ:** Inject metadata แบบ dynamic (วันที่ timezone ของ user ข้อมูล session) ที่เปลี่ยนตาม request

### SystemMessageSuffix

เพิ่มข้อความท้าย system message เฉพาะ request นี้:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nตอบเป็นภาษาไทยเสมอ"
};

var response = await service.GetCompletionAsync("สวัสดี!", context: context);
```

**ใช้เมื่อ:** เพิ่มคำสั่งพฤติกรรมต่อ request เช่น RAG context หรือการตั้งค่าภาษา

### AdditionalMessages

แทรกข้อความพิเศษเข้าการสนทนาเฉพาะ request นี้ — มีประโยชน์สำหรับ inject เอกสารอ้างอิงหรือ few-shot example:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.Create().AddText("เอกสารอ้างอิง: นโยบายคืนสินค้าอนุญาตให้คืนภายใน 30 วัน").Build()
    }
};

var response = await service.GetCompletionAsync("ฉันคืนสินค้าได้ไหม?", context: context);
```

**ใช้เมื่อ:** ให้เอกสารอ้างอิง few-shot example หรือ context เสริมที่ไม่ควรติดอยู่ในประวัติ

### RequestMessageOverride

แทนที่ข้อความของ user ทั้งหมดสำหรับ request นี้ prompt เดิมถูกละเว้น:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"อิงจาก context ต่อไปนี้ ตอบคำถาม\n\nContext: {docs}\n\nคำถาม: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context: context);
```

**ใช้เมื่อ:** middleware layer (RAG หรือ query rewriting) ต้องการปรับรูปแบบ prompt ทั้งหมดก่อนส่งถึง model ขณะที่ยังเก็บข้อความต้นฉบับของ user ไว้ในประวัติ

> **💡 หมายเหตุ:** เมื่อใช้ `.WithRag()` RAG pipeline ใช้ property นี้อัตโนมัติ ดู [การปรับแต่ง Pipeline](rag-pipeline.md#การทำงานภายใน)

## เปรียบเทียบก่อนและหลัง

**ไม่มี AIRequestContext:**

```csharp
// ❌ ยุ่งเหยิง มี state ง่ายต่อการผิดพลาด
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nวันนี้: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nContext:\n{retrievedChunks}";

var fewShotIndex = service.ActivateChat.Messages.Count;
service.ActivateChat.Messages.Add(MessageBuilder.Create().AddText(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.ActivateChat.Messages.RemoveAt(fewShotIndex);
```

**ด้วย AIRequestContext:**

```csharp
// ✅ สะอาด stateless ไม่มีผลข้างเคียง
var answer = await service.GetCompletionAsync(userQuery,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"วันนี้: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nContext:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText(fewShotExample).Build()
        }
    });
```

## ใช้ร่วมกับ AIRequestProfile

ส่งทั้งคู่พร้อมกันเพื่อควบคุมสูงสุด:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nContext:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText("ตัวอย่าง: ...").Build()
        }
    }
);
```

ดู [AIRequestProfile](request-profiles.md) สำหรับรายละเอียดการ override พารามิเตอร์

## การ inject อัตโนมัติด้วย `SystemMessageProvider`

### ปัญหาที่แก้ได้

แอปแชททั่วไปมีจุดเข้า LLM หลายจุดที่ต้องการ baseline เดียวกัน — วันที่วันนี้, โฟลเดอร์ที่ใช้งาน, ข้อมูล session **โดยไม่มี** `SystemMessageProvider` ทุกจุดที่เรียกใช้ต้องจำให้สร้างและส่ง context นั้นเอง:

```csharp
// ❌ ไม่มี SystemMessageProvider — ทุก entry point ต้องจำว่าต้อง inject
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. คำตอบแชทหลัก
var answer = await service.GetCompletionAsync(userMessage,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 2. ตัวสร้างชื่อเรื่อง (เพิ่มทีหลัง)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 3. ตัวสรุป (เพิ่มทีหลังอีก)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 4. เรียก agent — ลืมง่าย! คอมไพเลอร์ไม่เตือน
var agentResult = await service.RunAgentAsync(goal);  // ← ไม่มีวันที่ บั๊กเงียบ ๆ
```

ปัญหาของวิธีนี้:

- Snippet การสร้าง context เดียวกันถูก **ทำซ้ำ** ที่ทุกจุดเรียก
- Entry point ใหม่ (`RunAgentAsync` ด้านบน) **มองข้ามได้ง่าย** — ไม่มีการตรวจตอน compile
- ฟีเจอร์ใหม่ทุกตัวที่เพิ่ม LLM call ต้องจำ convention นี้
- Tests ก็ต้อง replicate การ setup context ที่ทุกจุดเรียก

ด้วย `SystemMessageProvider` คุณลงทะเบียน baseline **ครั้งเดียว** และทุก call ขาออกจะรับไปอัตโนมัติ:

```csharp
// ✅ ด้วย SystemMessageProvider — ลงทะเบียนครั้งเดียว ใช้ได้ทุกที่
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// ทุก call เหล่านี้ได้รับ baseline อัตโนมัติ — ไม่ต้องเขียน boilerplate ต่อ call
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← ได้รับ baseline ด้วย

// จุดเข้าแบบ streaming ก็เช่นกัน — baseline เดียวกัน ไม่ต้องเขียน boilerplate ต่อ call
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### วิธีการทำงาน

ลงทะเบียน callback ครั้งเดียวผ่าน fluent helper `WithSystemMessageProvider` ทุก call ขาออก (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) จะเรียกมันโดยอัตโนมัติเพื่อสร้าง baseline context:

```csharp
// โดยทั่วไปตอนสร้าง service / ตั้งค่า DI
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

### Async overload สำหรับ provider ที่มี IO

เมื่อ baseline context มาจากฐานข้อมูล, cache หรือการเรียก HTTP ให้ใช้ async overload เพื่อให้ provider ไม่ต้อง block บน `.Result` / `.GetAwaiter().GetResult()` การ resolve overload จะเลือกตัวที่ถูกต้องตาม arity ของ lambda — ไม่มีอาร์กิวเมนต์สำหรับ sync, หนึ่ง `CancellationToken` สำหรับ async:

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

Path ที่ไม่ใช่ streaming (`GetCompletionAsync`, `RunAgentAsync`) ไม่รองรับการยกเลิกโดยการออกแบบ — ลายเซ็นไม่รับ `CancellationToken` และจะส่ง `CancellationToken.None` ไปยัง provider เสมอ หาก provider ของคุณต้องการการยกเลิก (เช่น query DB ที่ใช้เวลานาน) ให้ใช้ path แบบ streaming (`StreamAsync`, `RunAgentStreamAsync`) ซึ่งจะส่งผ่าน token ของผู้เรียกไปยัง callback ของ provider

### การ merge กับ context per-call ที่ระบุชัดเจน

เมื่อ call มี provider ที่ลงทะเบียนไว้ **และ** ยังส่ง `AIRequestContext` ชัดเจนด้วย ทั้งสองจะถูก merge ทีละ field:

| Field | กฎ merge |
|---|---|
| `SystemMessagePrefix` | ชัดเจนชนะถ้า non-null, ไม่เช่นนั้นใช้ provider |
| `SystemMessageSuffix` | ชัดเจนชนะถ้า non-null, ไม่เช่นนั้นใช้ provider |
| `RequestMessageOverride` | ชัดเจนชนะถ้า non-null, ไม่เช่นนั้นใช้ provider |
| `AdditionalMessages` | เชื่อมต่อ (provider ก่อน, แล้วตามด้วยชัดเจน) |

เหตุผล: กรณีทั่วไปคือ "provider ให้ baseline, call เฉพาะต้องการแทนที่ field scalar เดียวหรือเพิ่มข้อความพิเศษ" — override ระดับ field รักษาความหมายให้คาดการณ์ได้โดยไม่เกิดการเชื่อมต่อที่คาดไม่ถึง

### Invocation ต่อ call

Provider ถูกเรียก **ครั้งเดียวต่อ request** ดังนั้นค่าที่ return จึงสะท้อนสถานะ ณ ขณะนั้นได้ (timestamp, session ฯลฯ) การ return `null` เป็น no-op — เหมือนกับการไม่ตั้งค่า `SystemMessageProvider` สำหรับ call นั้น

### สรุป: เมื่อใดควรเลือกเครื่องมือนี้ — จุดร่วมของสามเงื่อนไข

เมื่อถอยออกมาดูจากตัวอย่างและกฎการรวมข้างต้น `SystemMessageProvider` คือเครื่องมือเฉพาะเมื่อ **สามเงื่อนไขต่อไปนี้เป็นจริงพร้อมกัน**:

1. **ต้องมี baseline ร่วมในทุก call ของ LLM** — ไม่อยากต้องจำว่าต้อง inject เองทุก entry point
2. **ค่าต้องถูกประเมินแบบ dynamic ณ เวลา call** — เวลาปัจจุบัน โฟลเดอร์ที่ active ผู้ใช้ที่ล็อกอินอยู่ และค่าอื่น ๆ ที่ไม่สามารถตรึงได้ตอนเริ่มระบบ
3. **สถานะถาวร (`SystemMessage`, ประวัติบทสนทนา) ต้องไม่ถูกปนเปื้อน** — ค่านั้นต้องไม่รั่วไปยัง call ถัด ๆ ไป

หากขาดเงื่อนไขใดเงื่อนไขหนึ่ง เครื่องมือที่ง่ายกว่าจะเป็นคำตอบที่ถูกต้อง:

| สถานการณ์ | เครื่องมือที่ถูก | เหตุผล |
|---|---|---|
| baseline **คงที่ (ไม่เปลี่ยน)** ตลอดทั้ง session | `service.SystemMessage = "..."` | กำหนดครั้งเดียวพอ ไม่ต้องใช้ provider |
| **มีเพียง call เฉพาะเจาะจง** ที่ต้องการการจัดการพิเศษ | ส่ง `AIRequestContext` แบบชัดเจน ณ จุดที่ call | ไม่ใช่ baseline ร่วม แต่เป็นการ inject ครั้งเดียว |
| ร่วม + dynamic + ไม่ปนเปื้อน **(ทั้งสาม)** | **`SystemMessageProvider`** | เครื่องมือเฉพาะสำหรับจุดร่วมของสามเงื่อนไขนี้ |

#### เหตุใดจึงไม่ขัดแย้งกับหลัก "ใช้ครั้งเดียว" ของ `AIRequestContext`

แก่นแท้ของ `AIRequestContext` ไม่ใช่ "ใช้แค่ครั้งเดียว" แต่คือ **"ไม่ทำให้สถานะถาวรปนเปื้อน"** `SystemMessageProvider` คือ factory ที่ **รัน callback ซ้ำในทุก request** โดยสร้าง **`AIRequestContext` ใหม่ทั้งหมดที่จำกัดขอบเขตในแต่ละ request** ขึ้นมา ผลลัพธ์ context ที่ได้ยังคงเป็น per-request scoped ค่าจะไม่รั่วไปยังประวัติบทสนทนา และใน call ถัดไป callback จะรันใหม่เพื่อสะท้อนค่า **ณ เวลานั้น** ดังนั้น provider จึงไม่ได้ละเมิดหลักการออกแบบของ `AIRequestContext` — เพียงแค่ **อัตโนมัติหลักการนั้นให้**

ในทางปฏิบัติ การลงทะเบียน provider ด้านล่างนี้ **ไม่** แก้ไข `service.SystemMessage` หรือ `service.ActivateChat.Messages` เลย:

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- เมื่อผ่านเที่ยงคืน การรัน provider ซ้ำใน call ถัดไปจะสะท้อน **วันที่ใหม่** โดยอัตโนมัติ (ไม่ได้เป็นค่าคงที่)
- เมื่อเปิดประวัติบทสนทนาดูอีกสัปดาห์ ก็จะไม่พบ "Today is ..." ฝังอยู่ใน request เก่า ๆ
- แม้ใช้ service ร่วมกันในสภาพแวดล้อมหลายผู้ใช้ แต่ละ call ก็สร้าง context อิสระของตัวเอง

> ใช้งานได้ใน Mythosia.AI v6.3.0+
