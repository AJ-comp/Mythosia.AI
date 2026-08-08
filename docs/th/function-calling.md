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
