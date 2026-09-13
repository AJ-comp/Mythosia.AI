# แยกการตั้งค่าของแต่ละคำขอให้เป็นอิสระ

การสรุปเอกสารอาจต้องใช้ Temperature ต่ำ ส่วนร่างงานสร้างสรรค์อาจต้องใช้ค่าสูง การเตรียมร่างไม่ควรเปลี่ยนการตั้งค่าคำขอสรุปที่เตรียมไว้แล้ว ใช้ `CreateRequest` เมื่อต้องการตั้งค่าแต่ละการเรียกต่างกัน หรือสร้างหลายรูปแบบจากคำขอพื้นฐานเดียวกัน

หากต้องการคำตอบ การใช้โทเคน และแหล่งอ้างอิงพร้อมกัน ให้ใช้ `AIRunResult` ที่ได้จาก `await run.Result` สตริงอยู่ใน `result.Text` โดยไม่ต้องอ่านสตรีม นี่คือการเปลี่ยน API ในMythosia.AI 8.0.0 ชนิดผลลัพธ์ของ `GetCompletionAsync` และ `StructuredStreamRun<T>.Result` ยังคงเดิม [ผลลัพธ์ Run และการย้ายรุ่น](execution-api-transition.md#run-result).

หากต้องการเพียงคำตอบสุดท้ายและปุ่มหยุด ให้ส่ง `cancellationToken` ไปยัง `GetCompletionAsync` ใช้ Run สำหรับเหตุการณ์ความคืบหน้าหรือคำสั่งเพิ่มเติมที่รองรับ ดู[การยกเลิกคำตอบ](completions.md#completion-cancellation)

> ตัวอย่าง `CreateRequest` ต้องใช้Mythosia.AI 8.0.0 / Abstractions 4.0.0 รุ่น 7.1 เดิมที่เพิ่ม Run และตัวเลือกคำขอทั่วไปยังไม่มี builder แพ็กเกจเดิมใช้ overload ของ service ต่อได้

## Before: ใช้ service เดียวกัน

`WithTemperature` เดิมบน service เปลี่ยนค่าใน service และคืน instance เดิม ตัวแปรทั้งสองด้านล่างจึงอ้างถึง instance เดียวกันและใช้ค่าที่ตั้งทีหลัง เมธอดเหล่านี้ยังใช้ตั้งค่าเริ่มต้นของ service ได้

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("อธิบายเอกสารนี้"); // 0.8
```

## After: แยกคำขอเป็นอิสระ

`CreateRequest` เก็บสำเนาค่าเริ่มต้น แต่ละ `With...` บน builder คืน builder ใหม่โดยไม่แก้ของเดิม การทำงานใช้ค่าของคำขอโดยตรงและไม่เขียนทับค่า service ชั่วคราว

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("อธิบายเอกสารนี้");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// ใช้ 0.2 โดย creative และค่าเริ่มต้นของ service ไม่เปลี่ยน
```

ต้องใช้ builder ที่คืนมา หากเรียก `basis.WithTemperature(0.2f);` แล้วทิ้งผลลัพธ์ `basis` จะไม่เปลี่ยน

Builder ตรวจค่าแทนการปรับให้เอง: Temperature 0–2, TopP 0–1 และ penalty −2–2 โดยไม่รับ NaN หรืออนันต์ จำนวน token รอบ การทำงานพร้อมกัน และ timeout ที่ระบุต้องเป็นบวก ค่าผิดจะเกิด `ArgumentException` / `ArgumentOutOfRangeException` ส่วน helper Temperature เดิมของ service ยังคงปรับค่าให้อยู่ในช่วง

## หน้าที่ของแต่ละออบเจ็กต์

`AIService` ดูแลการเชื่อมต่อ provider ค่าเริ่มต้น และสถานะบทสนทนา ชนิด public `Mythosia.AI.Builders.AIRequestBuilder` ให้ fluent API ส่วนชนิดภายใน `AIRequest` ส่งอินพุตและค่าที่กำหนดแล้วไปยังส่วนทำงาน ไม่ต้องเรียก `Build()` เอง: `GetCompletionAsync()` คืน `Task<string>` และ `StartRunAsync()` คืน `Task<AIRun>` ไม่ได้คืน `AIRequest` เป็นคำตอบ

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## เริ่ม Run ด้วยการตั้งค่าเดียวกัน

ใช้ `GetCompletionAsync()` เมื่อต้องการคำตอบที่เสร็จแล้ว หรือ `StartRunAsync()` เพื่อแสดงความคืบหน้า และเพิ่มคำสั่งระหว่างทำงานเมื่อโมเดลรองรับ ส่ง prompt ให้ `CreateRequest` และไม่ส่งซ้ำให้เมธอดทำงาน `run.StreamAsync()` ใช้สังเกต Run นั้น ส่วนเงื่อนไขการรองรับ `run.SteerAsync(...)` ยังคงเดิม

```csharp
await using var run = await service
    .CreateRequest("อธิบายเอกสารนี้")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

เครื่องมือภายในคืนออบเจ็กต์ผ่าน `Task<T>` / `ValueTask<T>` และรับ `CancellationToken` ที่ไลบรารีฉีดให้ได้ `run.Cancel()` หรือโทเคนตอนเริ่มส่งการยกเลิกถึงเครื่องมือที่รองรับ แต่การหยุดอ่านสตรีมอย่างเดียวไม่ทำเช่นนั้น ข้อยกเว้นจะบันทึกเป็นความล้มเหลว เมื่อยกเลิกจะข้ามการเรียกที่รออยู่ และขั้นตอนเก็บกวาดยังรอเครื่องมือที่เริ่มแล้วแต่ไม่ใช้โทเคน ดู[ผลลัพธ์ ข้อผิดพลาด และการยกเลิก](function-calling.md#tool-execution-contract)

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## ใช้ profile และ context ซ้ำ

`WithProfile` คัดลอก `AIRequestProfile` และ `WithContext` คัดลอก `AIRequestContext` การแก้ออบเจ็กต์ต้นฉบับภายหลังไม่เปลี่ยนคำขอที่เตรียมไว้ Builder ตั้งค่าการสุ่ม คำสั่งระบบ โหมดไร้สถานะ นโยบายฟังก์ชัน และตัวเลือก reasoning กับการค้นหาที่รองรับได้ การตรวจสอบความสามารถของ provider ยังมีผล

`WithFunctions(params FunctionDefinition[])` เพิ่มสำเนานิยามฟังก์ชัน ส่วน `WithFunctions(toolInstance)` และ `WithStaticFunctions<T>()` จาก `Mythosia.AI.Extensions` รองรับฟังก์ชันที่ใช้ attribute เดิม ลงทะเบียนก่อน `CreateRequest` เพื่อตั้งค่า service หรือหลังเพื่อใช้เฉพาะคำขอ `CreateRequest` จะรับและใช้ตัวเลือกเดิมที่รอการเรียกครั้งถัดไป เก็บ builder ไว้หากต้องการใช้ตัวเลือกเหล่านั้นซ้ำ

```csharp
var request = service
    .CreateRequest("เขียนคำถามนี้ใหม่ให้เหมาะกับการค้นหา")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nรักษาความหมายเดิมไว้"
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## สิ่งที่คัดลอกและสถานะที่ยังใช้ร่วมกัน

ค่าทั่วไปและค่าเริ่มต้นของ provider ถูกเก็บเมื่อเรียก `CreateRequest` การเปลี่ยนค่า service ภายหลังไม่กระทบคำขอ เนื้อหาข้อความชนิดในตัว ชุดตัวเลือกที่รองรับ profile context และนโยบายจะถูกคัดลอก ส่วน handler ของฟังก์ชัน callback ของ context แบบไดนามิก และเนื้อหาข้อความแบบกำหนดเองยังเก็บ reference เดิม อย่าแก้เนื้อหาแบบกำหนดเอง และคำนึงว่า delegate อ่านสถานะภายนอกได้ Context แบบไดนามิกจะถูกประเมินตอนทำงาน

หลังจับข้อมูลแล้ว คุณสามารถคืนทรัพยากรของ `JsonDocument` ต้นฉบับหรือแก้ค่า `JsonNode` ต้นฉบับได้ โดยค่า JSON ที่เก็บในเมทาดาทาของคำขอหรืออาร์กิวเมนต์การเรียกฟังก์ชันจะไม่เปลี่ยน และแต่ละการทำงานจะได้รับสำเนาแยกกัน หากสาย `Items` ในสคีมาเครื่องมือวนกลับมาหาตัวเองหรือซ้อนเกิน 64 ระดับ จะเกิด `ArgumentException` ระหว่างการจับข้อมูล (`CreateRequest` หรือ `WithFunctions`) เพื่อรายงานสคีมาที่ไม่ถูกต้องก่อนเริ่มทำงาน แทนที่จะทำให้สแตกของกระบวนการหมด

การคัดลอกยังคงจำนวนมิติและดัชนีเริ่มต้นของอาร์เรย์ รวมถึงกฎเปรียบเทียบคีย์ของคอนเทนเนอร์มาตรฐาน `Dictionary<,>`, `SortedDictionary<,>` และ `SortedList<,>` ดังนั้นการค้นหาคีย์ที่ไม่แยกตัวพิมพ์ใหญ่และเล็กจึงยังทำงานเช่นเดิมในคำขอ ค่าเปล่า `default(JsonElement)` (`Undefined`) จะถูกเก็บไว้ตามเดิม ออบเจ็กต์เมทาดาทาที่ผู้ใช้กำหนดเองและระบบไม่รู้จักยังคงใช้การอ้างอิงเดิม เจ้าของจึงต้องไม่เปลี่ยนค่า หรือจัดการการเข้าถึงให้สอดคล้องกัน

ค่ามาตรฐาน `ReadOnlyCollection<T>` และ `ReadOnlyDictionary<TKey, TValue>` ยังคงชนิดเดิมเมื่ออยู่ในอาร์เรย์หรือพจนานุกรมที่ระบุชนิด คอลเล็กชันพื้นฐานที่รองรับจะถูกคัดลอกโดยคงมุมมองแบบอ่านอย่างเดียว การอ้างอิงร่วม และการอ้างอิงวนไว้ ส่วน `Hashtable` และ `SortedList` แบบไม่ใช่ generic ยังคงกฎเปรียบเทียบคีย์ด้วย

Builder ไม่ใช่บทสนทนาใหม่ แต่ใช้บทสนทนาที่ active ของ service ณ เวลาทำงานและไม่ได้ตรึงประวัติเมื่อสร้าง การเรียกแบบมีสถานะยังแก้ประวัติร่วมกัน ใช้ `WithStatelessMode()` หากไม่ต้องการอ่านหรือสะสมประวัติ ข้อจำกัดหนึ่ง Run ที่ active ต่อ service ยังอยู่ การแยกการตั้งค่าไม่ได้รับประกันการทำงานพร้อมกันบน service เดียวกัน ใช้ service แยกสำหรับบทสนทนาอิสระที่ทำงานพร้อมกัน

## การเรียกและส่วนขยายเดิม

`GetCompletionAsync` และจุดเรียกเดิมยังรองรับ `BeginMessage()` / `MessageChain` ยังคงสร้างข้อความแบบแก้ไขได้ แต่ส่วนทำงานใช้เส้นทางคำขอใหม่ ใช้ `CreateRequest` เมื่อต้องการแตกแขนงและใช้การตั้งค่าซ้ำ API นี้อยู่บน `AIService` และ implementation ของ provider โดยไม่เพิ่มสมาชิกที่บังคับใน `IAIService` ผู้ใช้ interface หรือ RAG wrapper ใช้ API profile context และการทำงานเดิมต่อได้

[สร้างตัวเลือกโมเดลจากคำนิยามการรองรับร่วมกัน](model-capabilities.md).
