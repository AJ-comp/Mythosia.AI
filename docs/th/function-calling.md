# Function Calling

## ทำไมต้องใช้ Function Calling?

LLM สร้างได้แค่ข้อความ — ไม่สามารถตรวจสอบสภาพอากาศ query database หรือเรียก API เองได้ **หากไม่มี** function calling คุณต้องแปลความหมายของ model เอง:

```csharp
// ❌ ไม่มี function calling — parse ความตั้งใจเอง
var reply = await service.GetCompletionAsync("อากาศที่โซลเป็นยังไง?");
// reply = "ฉันต้องตรวจสอบบริการอากาศเพื่อตอบคำถามนั้น"

// คุณต้องหาเองว่า user ถามเรื่องอากาศ ดึงชื่อเมือง แล้วเรียก API เอง
if (reply.Contains("อากาศ"))
{
    var city = ExtractCity(reply); // regex ที่เปราะบาง
    var weather = await weatherApi.GetAsync(city);
    // ถามใหม่พร้อมข้อมูลอากาศ...
}
```

วิธีนี้เปราะบาง ขยายยาก และต้องเดาความตั้งใจของผู้ใช้ล่วงหน้า **ด้วย** function calling model จะตัดสินใจเอง **ว่าเมื่อไหร่** จะเรียกโค้ดของคุณและ **ส่งอะไร**:

```csharp
// ✅ ด้วย function calling — model จัดการความตั้งใจและดึงข้อมูลเอง
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "ดึงข้อมูลอากาศปัจจุบันของสถานที่",
        ("location", "ชื่อเมืองและประเทศ", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("อากาศที่โซลเป็นยังไง?");
// Model เรียก get_weather("Seoul, Korea") รับผลลัพธ์ แล้วตอบอย่างเป็นธรรมชาติ
```

คุณกำหนด **ว่าโค้ดทำอะไรได้บ้าง** model รู้เองว่า **เมื่อไหร่** และ **วิธีไหน** จะใช้

## ตัวอย่างเร็ว

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "ดึงข้อมูลอากาศปัจจุบันของสถานที่",
        ("location", "ชื่อเมืองและประเทศ", required: true),
        (string location) => $"อากาศที่ {location} แดดออก 22°C"
    );

var response = await service.GetCompletionAsync("อากาศที่โซลเป็นยังไง?");
// Model เรียก get_weather("Seoul, Korea") และนำผลลัพธ์ไปตอบ
```

## กำหนดฟังก์ชันด้วย Attribute

สำหรับฟังก์ชันที่ซับซ้อนขึ้น ใช้ attribute `[AiFunction]` และ `[AiParameter]`:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "ค้นหาในแคตาล็อกสินค้า")]
    public string SearchProducts(
        [AiParameter("คำค้นหา", required: true)] string query,
        [AiParameter("จำนวนผลลัพธ์สูงสุด")] int limit = 5)
    {
        // ... การ implement ของคุณ
        return JsonSerializer.Serialize(results);
    }
}
```

จากนั้น register:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Policy การเรียกฟังก์ชัน

ควบคุมว่า model จะเรียกฟังก์ชันได้เมื่อไหร่:

```csharp
using Mythosia.AI.Models.Functions;

// ให้ model ตัดสินใจเอง (ค่าเริ่มต้น)
service.FunctionCallMode = FunctionCallMode.Auto;

// บังคับให้ model เรียกฟังก์ชันทุกครั้ง
service.ForceFunctionName = "search_products";

// ปิด function calling
service.FunctionCallMode = FunctionCallMode.None;
```

## Register แบบกลุ่มจาก Class

Register method ที่มี `[AiFunction]` ทั้งหมดจาก object เดียว:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // สแกน instance method ที่มี [AiFunction]
```

สำหรับ static method:

```csharp
service.WithStaticFunctions<MyTools>();  // สแกน static method ที่มี [AiFunction]
```

## Async Function Handler

`WithFunction` ทุก overload มีคู่ `WithFunctionAsync` ที่รับ `Func<..., Task<string>>`:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "ดึงข้อมูลจาก API ภายนอก",
    ("url", "URL ที่ต้องการดึง", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

รองรับ 0 ถึง 3 parameter เหมือนกับ sync

## ปิดฟังก์ชันชั่วคราว

ปิด function calling สำหรับ request เดียวโดยไม่ต้องลบ registration:

```csharp
// Extension method — คืนผลลัพธ์โดยไม่ใช้ฟังก์ชัน
string answer = await service.AskWithoutFunctionsAsync("ตอบตรง ๆ เลย");

// หรือสลับ property
service.WithoutFunctions();  // ตั้งค่า FunctionsDisabled = true
```

## ใช้ FunctionBuilder

สร้างนิยามฟังก์ชันด้วยโค้ด:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("ดึงราคาหุ้นปัจจุบัน")
    .AddParameter("ticker", "string", "สัญลักษณ์หุ้น", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

## การเรียกเครื่องมือแบบอะซิงโครนัสของโมเดล

การค้นฐานข้อมูลหรือเรียก API ภายนอกที่ช้าไม่จำเป็นต้องหยุดทุกส่วนของงาน เช่น ระหว่างรอข้อมูลอากาศ โมเดลอาจให้คำแนะนำทั่วไปสำหรับการเดินทางซึ่งไม่ขึ้นกับผลนั้นก่อนได้ การเรียกเครื่องมือแบบอะซิงโครนัสช่วยให้ทำส่วนที่เป็นอิสระต่อ แล้วนำผลเฉพาะมาใช้เมื่อได้รับ

การรองรับ GPT-6 Astra และการเรียกเครื่องมือแบบอะซิงโครนัสเริ่มใน `Mythosia.AI` 7.1.0 โดยชนิดข้อมูลร่วมอยู่ใน `Mythosia.AI.Abstractions` 3.1.0

`FunctionDefinition.AllowAsync` มีค่าเริ่มต้นเป็น `false` ตั้งเป็น `true` หรือเรียก `FunctionBuilder.WithAsync()` เฉพาะเมื่อโมเดลสามารถทำงานอื่นต่อระหว่างที่ฟังก์ชันนี้กำลังทำงานได้ ใช้ `WithAsync(false)` เพื่อปิดสิทธิ์นี้ โดยใช้คำนิยามฟังก์ชันและ handler เดิมกับผู้ให้บริการหลายรายได้

การลงทะเบียนด้วย attribute ใช้สิทธิ์เดียวกันได้: `[AiFunction("lookup", "ค้นหาข้อมูล", AllowAsync = true)]`

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("ส่งคืนสภาพอากาศตัวอย่างของโซล")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "ตรวจสอบสภาพอากาศตัวอย่างของโซล ระหว่างรอให้แนะนำของจำเป็นสำหรับการเดินทางสามอย่าง");
```

Mythosia ส่ง `async: true` สำหรับ GPT-6 Astra ผ่าน Responses API หากโมเดลหรือ API ไม่รองรับ จะไม่ส่งฟิลด์นี้และรอผลจาก handler เดิม โดยไม่เปลี่ยนค่า `AllowAsync` ผู้ให้บริการต้องระบุว่าการเรียกจริงเป็นแบบอะซิงโครนัสด้วย (`FunctionCall.IsAsync`) การเปิดสิทธิ์จึงไม่ได้รับประกันว่าจะทำงานแบบอะซิงโครนัสเสมอ

`WithFunctionAsync` ใช้ลงทะเบียน handler แบบอะซิงโครนัสของ .NET ส่วน `FunctionExecutionMode.Parallel` ควบคุมการทำงานของ handler ในแอป ทั้งสองอย่างไม่เปิดสิทธิ์นี้โดยอัตโนมัติ `AllowAsync` อนุญาตให้โมเดลทำงานต่อก่อนที่ผลของฟังก์ชันจะมาถึง `FunctionExecutionMode` ยังคงควบคุมการเรียกทั่วไป งานอะซิงโครนัสที่อนุญาตอาจทำงานซ้อนกันได้แม้ในโหมด `Sequential` โดยงานในพูลแยกนี้ใช้ขีดจำกัด `MaxConcurrency` ร่วมกัน

งานที่ยังไม่เสร็จอยู่ภายในคำขอ `GetCompletionAsync`, `service.StreamAsync` เดิม หรือ `AIRun` ที่สร้างด้วย `StartRunAsync` ผลของเครื่องมือแต่ละรายการส่งกลับด้วย ID การเรียกเดิม และการจบงานสำเร็จจะรอประมวลผลที่ค้างอยู่ทั้งหมด นี่ไม่ใช่บริการงานเบื้องหลังแยกต่างหาก การหยุดอ่าน `run.StreamAsync()` หยุดเฉพาะการสังเกต ไม่ได้ยกเลิก Run

เมื่อใช้เครื่องมืออะซิงโครนัส `GetCompletionAsync` จะคืนข้อความอิสระระหว่างทางและข้อความสุดท้ายที่สะสมตามลำดับหลังคำขอเสร็จ ส่วน `service.StreamAsync` เดิมและ `run.StreamAsync()` ส่งข้อความทันทีที่ได้รับ และ `AIRun.Result` รวมเหตุการณ์ข้อความของ Run ไม่ว่าจะมีการอ่านสตรีมหรือไม่

handler ไม่ได้รับโทเคนยกเลิก เมื่อยกเลิกการทำงาน หมดเวลา เกิดข้อผิดพลาด หรือปิดสตรีมคำขอแบบเดิม การเก็บกวาดจะรอ handler ที่เริ่มแล้วให้เสร็จ สำหรับ Run การหยุดสังเกตไม่ได้ทำเช่นนั้น ให้ยกเลิกตัว Run ด้วย `Cancel()` โทเคนตอนเริ่ม หรือ `DisposeAsync()` การเชื่อมต่อนี้ครอบคลุม handler ที่ลงทะเบียนไว้ ดู[คู่มือ API อย่างเป็นทางการ](https://developers.openai.com/api/docs/guides/async-tool-calling)

การประมวลผลสตรีมเริ่ม handler หลังได้รับการเรียกฟังก์ชันครบถ้วนและตรวจสอบจุดแบ่งคำตอบที่ถูกต้องแล้ว การเรียกฟังก์ชันที่ยังไม่ครบจะไม่ถูกนำไปทำงาน จากนั้นโมเดลสามารถดำเนินรอบถัดไปขณะที่งานอะซิงโครนัสยังทำอยู่ หากไม่มีการเรียกใหม่แต่ยังมีงานค้าง Mythosia จะรอผลก่อนดำเนินต่อ ระหว่างมีการเรียกค้างอยู่จะปิดการสรุปและลองใหม่อัตโนมัติเมื่อบริบทเกินขีดจำกัด เพื่อไม่ให้การเรียกที่ยังไม่เสร็จหายไปจากประวัติ
