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
    new AIRequestContext
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

var response = await service.GetCompletionAsync("วันนี้วันที่เท่าไหร่?", context);
```

**ใช้เมื่อ:** Inject metadata แบบ dynamic (วันที่ timezone ของ user ข้อมูล session) ที่เปลี่ยนตาม request

### SystemMessageSuffix

เพิ่มข้อความท้าย system message เฉพาะ request นี้:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nตอบเป็นภาษาไทยเสมอ"
};

var response = await service.GetCompletionAsync("สวัสดี!", context);
```

**ใช้เมื่อ:** เพิ่มคำสั่งพฤติกรรมต่อ request เช่น RAG context หรือการตั้งค่าภาษา

### AdditionalMessages

แทรกข้อความพิเศษเข้าการสนทนาเฉพาะ request นี้ — มีประโยชน์สำหรับ inject เอกสารอ้างอิงหรือ few-shot example:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("เอกสารอ้างอิง: นโยบายคืนสินค้าอนุญาตให้คืนภายใน 30 วัน").Build()
    }
};

var response = await service.GetCompletionAsync("ฉันคืนสินค้าได้ไหม?", context);
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

await service.GetCompletionAsync(userQuery, context);
```

**ใช้เมื่อ:** middleware layer (RAG หรือ query rewriting) ต้องการปรับรูปแบบ prompt ทั้งหมดก่อนส่งถึง model ขณะที่ยังเก็บข้อความต้นฉบับของ user ไว้ในประวัติ

> **💡 หมายเหตุ:** เมื่อใช้ `.WithRag()` RAG pipeline ใช้ property นี้อัตโนมัติ ดู [การปรับแต่ง Pipeline](rag-pipeline.md#how-it-works-internally)

## เปรียบเทียบก่อนและหลัง

**ไม่มี AIRequestContext:**

```csharp
// ❌ ยุ่งเหยิง มี state ง่ายต่อการผิดพลาด
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nวันนี้: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nContext:\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2);
```

**ด้วย AIRequestContext:**

```csharp
// ✅ สะอาด stateless ไม่มีผลข้างเคียง
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"วันนี้: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nContext:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
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
            MessageBuilder.User("ตัวอย่าง: ...").Build()
        }
    }
);
```

ดู [AIRequestProfile](request-profiles.md) สำหรับรายละเอียดการ override พารามิเตอร์
