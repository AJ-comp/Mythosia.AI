# Agent (ReAct Loop)

## ทำไมต้องใช้ Agent Loop?

ด้วย function calling ปกติ model จะเรียกฟังก์ชัน **หนึ่งครั้ง** ต่อ request คุณรัน แล้วการสนทนาก็ดำเนินต่อ แต่งานจริงหลายอย่างต้องการ **หลายขั้นตอน** ที่ model ต้องวางแผนและดำเนินการเอง:

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
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

## หลักการทำงาน

แต่ละขั้นตอน:

1. LLM รับเป้าหมาย + ประวัติการสนทนา + นิยามฟังก์ชัน
2. ถ้า LLM เรียกฟังก์ชัน → รัน แล้วเพิ่มผลลัพธ์เข้าประวัติ
3. ถ้า LLM ตอบเป็นข้อความ → จบ loop คืนคำตอบนั้น
4. ถ้าถึง `maxSteps` → throw `AgentMaxStepsExceededException`
