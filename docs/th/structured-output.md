# Structured Output

## ทำไมต้องใช้ Structured Output?

LLM ตอบกลับเป็นข้อความอิสระโดยค่าเริ่มต้น หากแอปของคุณต้องการ **ประมวลผล response แบบโปรแกรม** — บันทึกลง database ส่งให้ API อื่น หรือแสดงใน UI แบบมี type — คุณต้องแปลงข้อความนั้นเอง ซึ่งนำไปสู่ regex หรือ `string.Contains` ที่เปราะบางและพังเมื่อ model เปลี่ยนการพูด

Structured output แก้ปัญหานี้โดยสั่งให้ model ส่ง JSON ที่ตรงกับ schema ของ C# type Mythosia.AI จัดการสร้าง schema, inject prompt และ deserialize โดยอัตโนมัติ — รวมถึง **ซ่อมแซม JSON อัตโนมัติ** สำหรับข้อผิดพลาดเล็กน้อยที่ model อาจสร้างขึ้น

### เมื่อไหร่ควรใช้

- ดึงข้อมูลที่มีโครงสร้างจากข้อความดิบ เช่น การจำแนกประเภท หรือ entity
- สร้าง API response แบบมี type จากเนื้อหาที่ AI สร้าง
- ส่ง output ของ AI ไปยัง pipeline ที่ต้องการข้อมูลในรูปแบบที่กำหนด
- ทุกกรณีที่ต้องการ **output ที่เชื่อถือได้ สามารถอ่านด้วยโปรแกรม**

## ปัญหาที่แก้ได้

สมมติคุณต้องดึงข้อมูลอากาศจาก response ของ model **โดยไม่มี** structured output:

```csharp
// ❌ ไม่มี structured output — parse เองอย่างเปราะบาง
var text = await service.GetCompletionAsync("อากาศที่โซลเป็นยังไง?");
// text = "อากาศที่โซลแดดออก อุณหภูมิ 22°C"

// ต้อง parse เอง...
var city = "Seoul"; // hardcode? regex?
var tempMatch = Regex.Match(text, @"(\d+)°C");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// ถ้า model พูดว่า "ยี่สิบสององศา" แทน "22°C"? 💥
```

**ด้วย** structured output:

```csharp
// ✅ ด้วย structured output — type-safe อัตโนมัติ
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "อากาศที่โซลเป็นยังไง?");

Console.WriteLine(result.City);         // Seoul
Console.WriteLine(result.Condition);    // Sunny
Console.WriteLine(result.TemperatureC); // 22
```

Model จะถูกสั่งให้ส่ง JSON ตาม C# type ของคุณ Mythosia.AI deserialize อัตโนมัติ ถ้า model ส่ง JSON ผิดเล็กน้อย (เช่น ขาด comma) **auto-repair** จะแก้ไขก่อน deserialize

## การใช้งานพื้นฐาน

ส่ง type parameter เข้า `GetCompletionAsync`:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "อากาศที่โซลเป็นยังไง?");

Console.WriteLine(result.City);        // Seoul
Console.WriteLine(result.Condition);   // Sunny
Console.WriteLine(result.TemperatureC); // 22
```

## Collection

ใช้ collection type ได้เลย — ไม่ต้องมี wrapper DTO:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "ดึงบุคคลและองค์กรทั้งหมดจากข้อความนี้: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Streaming + Structured Output

Stream ข้อความแบบ real-time พร้อมได้รับออบเจกต์ที่ deserialize แล้วในตอนท้าย:

```csharp
var run = service.BeginStream("สร้างสรุปสินค้า").As<ProductDto>();

// Output real-time
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// ผลลัพธ์ที่ parse แล้ว
ProductDto product = await run.Result;
```

## Structured Output Policy

ควบคุมความเข้มงวดในการขอให้ model สร้าง structured output:

```csharp
using Mythosia.AI.Models;

// ค่าเริ่มต้น: ขอให้ model ส่ง JSON ตาม schema
service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;

// ผ่อนคลาย: ให้อิสระกับ model มากขึ้น พึ่ง auto-repair
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient;
```
