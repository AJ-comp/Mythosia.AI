# Function Calling

> GPT-6 Sol/Luna: ต้องใช้ Mythosia.AI 8.1.0 / Abstractions 4.1.0 [การเลือกโมเดลและรุ่นที่ต้องใช้](providers.md#gpt-6-sol-luna)

หากต้องการเพียงคำตอบสุดท้ายและปุ่มหยุด ให้ส่ง `cancellationToken` ไปยัง `GetCompletionAsync` ใช้ Run สำหรับเหตุการณ์ความคืบหน้าหรือคำสั่งเพิ่มเติมที่รองรับ ดู[การยกเลิกคำตอบ](completions.md#completion-cancellation)

ใช้ [request builder](request-building.md) เพื่อแยกการตั้งค่าและสร้างรูปแบบที่ใช้ซ้ำได้ เรียก `CreateRequest(...)` ก่อน `With...` ส่วน property และ fluent method บน service ยังคงพฤติกรรมเดิม

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

[Claude Fable 5.1](fable-5-1.md) เพิ่มข้อความความคืบหน้า คำสั่งเฉพาะเทิร์น และการวินิจฉัย thinking binding ตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 ส่วน Mythos 5.1 ต้องได้รับเชิญ และทั้งสองรุ่นไม่รองรับการบังคับเลือกเครื่องมือ

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

<a id="tool-execution-contract"></a>

## คืนออบเจ็กต์จากเครื่องมืออะซิงโครนัสและยกเลิกงาน

เครื่องมืออ่านไฟล์หรือฐานข้อมูลมักคืนออบเจ็กต์หลังทำ I/O แบบอะซิงโครนัส ปุ่มหยุดควรส่งการยกเลิกไปถึงงานที่กำลังทำอยู่ด้วย ฟังก์ชันแบบซิงโครนัสคืนออบเจ็กต์ได้อยู่แล้ว การปรับปรุงนี้ทำให้ผลลัพธ์อะซิงโครนัสทำงานเหมือนกัน และบันทึกข้อยกเว้นเป็นความล้มเหลว

Before: ฟังก์ชันอะซิงโครนัสต้องแปลงผลลัพธ์เป็น JSON เอง หากคืน `Task<FileResult>` ค่าจะหายไปและส่งเพียง `"Success"`

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "อ่านไฟล์ข้อความ")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: คืนออบเจ็กต์โดยตรงและส่งโทเคนยกเลิกที่ไลบรารีฉีดให้ไปยังงาน I/O แอปไม่ต้องสร้างตัวห่อผลลัพธ์หรืออะแดปเตอร์ใหม่

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "อ่านไฟล์ข้อความ")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

การลงทะเบียนด้วย `[AiFunction]` รองรับออบเจ็กต์ทั่วไป, `Task<T>` และ `ValueTask<T>` ค่าที่ไม่ใช่สตริงจะแปลงเป็น JSON ส่วน `string`, `Task<string>` และ `ValueTask<string>` คงข้อความเดิมโดยไม่เพิ่มเครื่องหมายคำพูด JSON และจะรอ `Task` กับ `ValueTask` ที่ไม่มีผลลัพธ์ด้วย การคืนออบเจ็กต์แบบซิงโครนัสยังใช้ได้ตามเดิม ค่าที่คืนเป็น null จะเป็น `"Done"` ส่วน `Task` / `ValueTask` ที่เสร็จโดยไม่มีผลลัพธ์จะเป็น `"Success"`

ไลบรารียังตรวจชนิดค่าขณะทำงานด้วย: จะรอ `Task<T>` ที่คืนในรูป `Task` หรือ `object` และ `ValueTask<T>` ที่คืนในรูป `object` แล้วแปลงผลลัพธ์ตามกฎเดียวกัน โดยใช้ผลของแต่ละ `ValueTask` เพียงครั้งเดียว

ไลบรารีส่งพารามิเตอร์ `CancellationToken` ให้เองและไม่นำไปใส่ในสคีมาอาร์กิวเมนต์ที่แสดงแก่โมเดล ลงทะเบียนด้วย `WithFunctions(...)` หรือ `WithStaticFunctions<T>()` บน service หรือ request builder ได้เหมือนกัน

เมธอดเครื่องมือแบบ `async void` จะถูกปฏิเสธตอนลงทะเบียน ให้คืน `Task` หรือ `ValueTask` เพื่อให้รอจนเสร็จ ตรวจพบข้อผิดพลาด และเก็บกวาดหลังยกเลิกได้

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("อ่าน report.txt และสรุปให้หน่อย")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()` การยกเลิกโทเคนที่ส่งให้ `StartRunAsync` หรือการคืนทรัพยากรของ run ที่ทำงานอยู่ จะส่งไปถึงเครื่องมือภายในที่รองรับการยกเลิก ฟังก์ชันต้องใช้โทเคน ไม่สามารถบังคับหยุดโค้ดที่ไม่สนใจโทเคนได้ การเรียกที่ยังไม่เริ่มจะถูกข้ามและบันทึกผลยกเลิก ส่วนฟังก์ชันที่เริ่มแล้วจะยังรอให้จบเพื่อรักษาคู่การเรียกกับผลลัพธ์ในประวัติ run ที่ถูกยกเลิกจะไม่เริ่มรอบโมเดลถัดไป

หาก callback การยกเลิกโยนข้อยกเว้นเมื่อเริ่ม run ไม่สำเร็จหรือเมื่อคืนทรัพยากรการเชื่อมต่อ MCP ระบบยังพยายามเก็บกวาดเซสชันหรือช่องทางรับส่งข้อมูล ข้อผิดพลาดเดิมและข้อผิดพลาดระหว่างเก็บกวาดจะยังคงอยู่ โดยรวมไว้ใน `AggregateException` เมื่อจำเป็น การเรียก `McpConnection.DisposeAsync()` แบบอะซิงโครนัสพร้อมกันจะรอการเก็บกวาดชุดเดียวกัน ระบบปิดช่องทางรับส่งข้อมูลก่อนรอให้ลูปอ่านสิ้นสุด เพื่อให้การอ่านที่ต้องอาศัยการปิดการเชื่อมต่อจบลงได้

เพื่อไม่ให้การเรียกเครื่องมือที่เข้ามาภายหลังรอค้างระหว่างปิดการเชื่อมต่อ เมื่อเริ่มคืนทรัพยากรของการเชื่อมต่อแล้ว ระบบจะปฏิเสธการเรียก `InitializeAsync`, `RefreshToolsAsync` และ `CallToolAsync` ใหม่ด้วย `ObjectDisposedException` หากคำตอบมี ID ตรงกับคำขอแต่เนื้อหาผิดรูปแบบ ระบบจะข้ามคำตอบนั้นโดยยังเก็บคำขอไว้ในรายการรอ คำตอบที่ถูกต้องในภายหลัง การยกเลิกจากผู้เรียก หรือการเก็บกวาดการเชื่อมต่อจึงยังสามารถทำให้การเรียกนั้นสิ้นสุดได้ หากการอ่านสิ้นสุดแล้วเพราะเซิร์ฟเวอร์ปิดสตรีมหรือการอ่านจากช่องทางรับส่งข้อมูลล้มเหลว การทำงานใหม่จะล้มเหลวด้วย `McpException` แทนที่จะรอคำตอบที่ไม่อาจมาถึงได้ ให้สร้างการเชื่อมต่อใหม่เพื่อทำงานต่อ

เมื่อเกิดความล้มเหลวจริงให้โยนข้อยกเว้น ตัวทำงานจะบันทึก `FunctionCallResult.IsError = true` แทนการมองสตริง `"Error: ..."` เป็นผลสำเร็จ สตริงที่ฟังก์ชันตั้งใจคืนยังเป็นผลลัพธ์ปกติ ผลเครื่องมือที่ถูกยกเลิกมีทั้ง `IsCancelled = true` และ `IsError = true`

หากลงทะเบียนด้วยโค้ด ให้ใช้ overload ของ `WithHandler` ที่รับสองอาร์กิวเมนต์:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("อ่านไฟล์ข้อความ")
    .AddParameter("path", "string", "พาธไฟล์", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

handler ที่รับอาร์กิวเมนต์เดียวและคืนสตริงยังใช้ได้ หากสร้าง definition โดยตรง ให้กำหนด `Func<Dictionary<string, object>, CancellationToken, Task<string>>` ที่ `HandlerWithCancellation` การกำหนด `Handler` หรือ `HandlerWithCancellation` จะแทนที่ handler เดียวกัน ไม่ได้ลงทะเบียนให้ทำงานสองครั้ง API ระดับนี้ยังคืนสตริง ส่วนการแปลงออบเจ็กต์เป็น JSON อัตโนมัติเป็นหน้าที่ของการลงทะเบียนเมธอด

นี่คือการจัดการผลลัพธ์และการยกเลิกฟังก์ชัน .NET ภายใน ไม่ต้องอาศัยความสามารถ `AllowAsync` ของ provider การหยุดอ่าน `run.StreamAsync(token)` เพียงอย่างเดียวหยุดเฉพาะการสังเกต แต่ run ยังทำงานต่อ ดู[คู่มือ Run](execution-api-transition.md)และ[โปรโตคอล provider](https://developers.openai.com/api/docs/guides/async-tool-calling)

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

Mythosia ส่ง `async: true` สำหรับ GPT-6 Astra / Sol / Luna ผ่าน Responses API หากโมเดลหรือ API ไม่รองรับ จะไม่ส่งฟิลด์นี้และรอผลจาก handler เดิม โดยไม่เปลี่ยนค่า `AllowAsync` ผู้ให้บริการต้องระบุว่าการเรียกจริงเป็นแบบอะซิงโครนัสด้วย (`FunctionCall.IsAsync`) การเปิดสิทธิ์จึงไม่ได้รับประกันว่าจะทำงานแบบอะซิงโครนัสเสมอ

`WithFunctionAsync` ใช้ลงทะเบียน handler แบบอะซิงโครนัสของ .NET ส่วน `FunctionExecutionMode.Parallel` ควบคุมการทำงานของ handler ในแอป ทั้งสองอย่างไม่เปิดสิทธิ์นี้โดยอัตโนมัติ `AllowAsync` อนุญาตให้โมเดลทำงานต่อก่อนที่ผลของฟังก์ชันจะมาถึง `FunctionExecutionMode` ยังคงควบคุมการเรียกทั่วไป งานอะซิงโครนัสที่อนุญาตอาจทำงานซ้อนกันได้แม้ในโหมด `Sequential` โดยงานในพูลแยกนี้ใช้ขีดจำกัด `MaxConcurrency` ร่วมกัน

งานที่ยังไม่เสร็จอยู่ภายในคำขอ `GetCompletionAsync`, `service.StreamAsync` เดิม หรือ `AIRun` ที่สร้างด้วย `StartRunAsync` ผลของเครื่องมือแต่ละรายการส่งกลับด้วย ID การเรียกเดิม และการจบงานสำเร็จจะรอประมวลผลที่ค้างอยู่ทั้งหมด นี่ไม่ใช่บริการงานเบื้องหลังแยกต่างหาก การหยุดอ่าน `run.StreamAsync()` หยุดเฉพาะการสังเกต ไม่ได้ยกเลิก Run

เมื่อใช้เครื่องมืออะซิงโครนัส `GetCompletionAsync` จะคืนข้อความอิสระระหว่างทางและข้อความสุดท้ายที่สะสมตามลำดับหลังคำขอเสร็จ ส่วน `service.StreamAsync` เดิมและ `run.StreamAsync()` ส่งข้อความทันทีที่ได้รับ และ `AIRunResult.Text` รวมเหตุการณ์ข้อความของ Run ไม่ว่าจะมีการอ่านสตรีมหรือไม่

เครื่องมือภายในคืนออบเจ็กต์ผ่าน `Task<T>` / `ValueTask<T>` และรับ `CancellationToken` ที่ไลบรารีฉีดให้ได้ `run.Cancel()` หรือโทเคนตอนเริ่มส่งการยกเลิกถึงเครื่องมือที่รองรับ แต่การหยุดอ่านสตรีมอย่างเดียวไม่ทำเช่นนั้น ข้อยกเว้นจะบันทึกเป็นความล้มเหลว เมื่อยกเลิกจะข้ามการเรียกที่รออยู่ และขั้นตอนเก็บกวาดยังรอเครื่องมือที่เริ่มแล้วแต่ไม่ใช้โทเคน ดู[ผลลัพธ์ ข้อผิดพลาด และการยกเลิก](function-calling.md#tool-execution-contract)

การประมวลผลสตรีมเริ่ม handler หลังได้รับการเรียกฟังก์ชันครบถ้วนและตรวจสอบจุดแบ่งคำตอบที่ถูกต้องแล้ว การเรียกฟังก์ชันที่ยังไม่ครบจะไม่ถูกนำไปทำงาน จากนั้นโมเดลสามารถดำเนินรอบถัดไปขณะที่งานอะซิงโครนัสยังทำอยู่ หากไม่มีการเรียกใหม่แต่ยังมีงานค้าง Mythosia จะรอผลก่อนดำเนินต่อ ระหว่างมีการเรียกค้างอยู่จะปิดการสรุปและลองใหม่อัตโนมัติเมื่อบริบทเกินขีดจำกัด เพื่อไม่ให้การเรียกที่ยังไม่เสร็จหายไปจากประวัติ

Perplexity: [ควบคุมการวิจัยและเครื่องมือ](perplexity.md).
