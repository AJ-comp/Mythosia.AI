# Agent (ReAct Loop)

## ทำไมต้องใช้ Agent Loop?

function calling ปกติสามารถรัน **หลายฟังก์ชันจากคำตอบเดียวของ model เป็น batch ที่เรียงลำดับไว้** และดำเนินต่อผ่าน tool round ถัดไปได้ ส่วน Agent API นำกลไกนี้มาจัดเป็น ReAct loop ที่มุ่งสู่เป้าหมายพร้อม **ขีดจำกัดจำนวนขั้นตอน** ที่ชัดเจน โดยส่งผลลัพธ์ของแต่ละ batch กลับให้ model จนกว่าจะได้คำตอบสุดท้าย:

- "ค้นหา 3 บริษัท AI ชั้นนำและเปรียบเทียบราคาหุ้น" — ต้องค้นเว็บและดูราคาหุ้นหลายครั้ง
- "หานโยบายที่เกี่ยวข้อง ตรวจสถานะคำสั่งซื้อ แล้วบอกว่าฉันคืนสินค้าได้ไหม" — ต้องเชื่อมเครื่องมือต่าง ๆ ตามลำดับ
- Model อาจต้อง **ลองใหม่** หากผลการค้นหาแรกยังไม่เพียงพอ

การเขียน orchestration loop เองนั้นยุ่งยากและเสี่ยงผิดพลาด **Agent loop** (pattern ReAct: Reason → Act → Observe → Repeat) จัดการให้อัตโนมัติ — model ตัดสินใจขั้นตอนต่อไปเองจนได้คำตอบสุดท้าย

## การใช้งานพื้นฐาน

Register ฟังก์ชัน แล้วเรียก `RunAgentAsync` พร้อมเป้าหมาย:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "ค้นหาข้อมูลบนเว็บ",
        ("query", "คำค้นหา", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "ดึงราคาหุ้นปัจจุบัน",
        ("ticker", "สัญลักษณ์หุ้น", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "ราคาหุ้นปัจจุบันของ 3 บริษัท AI ชั้นนำคือเท่าไหร่?",
    maxSteps: 10
);

Console.WriteLine(result);
```

Model จะเรียกฟังก์ชันตามที่จำเป็น สังเกตผลลัพธ์ และตัดสินใจขั้นตอนต่อไป — จนกว่าจะได้คำตอบสุดท้าย

## maxSteps

`maxSteps` จำกัดจำนวนรอบ LLM→เรียกฟังก์ชัน หาก agent ยังไม่เสร็จภายในขีดจำกัด จะเกิด `AgentMaxStepsExceededException`:

```csharp
try
{
    string result = await service.RunAgentAsync("ค้นคว้าและสรุป...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponse มีสิ่งที่ model สร้างไว้จนถึงตอนนั้น
    Console.WriteLine($"หยุดก่อนกำหนด: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

ควบคุมพฤติกรรมของแต่ละรอบใน agent loop:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// หรือใช้ extension method:
service.WithMaxRounds(15).WithTimeout(60);
```

Policy ที่กำหนดไว้ล่วงหน้า:

```csharp
service.WithFastPolicy();    // timeout ต่ำ รอบน้อย — งานเบา
service.WithComplexPolicy(); // timeout สูง รอบมาก — งานซับซ้อน
```

## บริบทคำขอต่อการเรียกใช้

`RunAgentAsync` และ `RunAgentStreamAsync` รับ `AIRequestContext` แบบ optional เพื่อใส่ prefix/suffix ของ system message แบบไดนามิก เอกสารอ้างอิง หรือแทนที่ข้อความเป้าหมายได้ — **จำกัดอยู่ในการรัน agent รอบเดียว** โดยไม่แตะ system message ของ service หรือประวัติการสนทนา

```csharp
string result = await service.RunAgentAsync(
    goal: "หานโยบายการคืนเงิน และตรวจว่าคำสั่งซื้อ #1234 เข้าเงื่อนไขหรือไม่",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"วันนี้คือ {DateTime.UtcNow:yyyy-MM-dd}\n",
        SystemMessageSuffix = "\nอ้างอิงข้อของนโยบายที่ใช้เสมอ"
    });
```

เวอร์ชัน streaming รับพารามิเตอร์เดียวกัน:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "ค้นคว้าราคาหุ้นของ 3 บริษัท AI ชั้นนำ",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"โซนเวลาของผู้ใช้: {userTz}\n"
    }))
{
    // ประมวลผลเนื้อหา
}
```

Context ส่งผ่าน `AsyncLocal` ดังนั้นการรัน agent พร้อมกันบน service instance เดียวกันจะไม่รบกวนซึ่งกันและกัน

ดูรายชื่อคุณสมบัติทั้งหมดได้ที่ [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`)

> มีให้ใช้งานตั้งแต่ Mythosia.AI v6.3.0 ขึ้นไป

## หลักการทำงาน

แต่ละขั้นตอน:

1. LLM รับเป้าหมาย + ประวัติการสนทนา + นิยามฟังก์ชัน
2. ถ้า LLM เรียกฟังก์ชัน → รัน แล้วเพิ่มผลลัพธ์เข้าประวัติ
3. ถ้า LLM ตอบเป็นข้อความ → จบ loop คืนคำตอบนั้น
4. ถ้าถึง `maxSteps` → throw `AgentMaxStepsExceededException`
