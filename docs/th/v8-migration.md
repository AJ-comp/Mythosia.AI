# ย้ายไปใช้ Mythosia.AI 8

ใช้รุ่นนี้เมื่อต้องแยกการตั้งค่าแต่ละคำขอ ให้ผู้ใช้หยุดงานที่กำลังทำ และเก็บคำตอบพร้อมการใช้โทเค็นและแหล่งที่มา รุ่นหลักนี้รวมการเปลี่ยนสถาปัตยกรรมหกด้าน การอัปเดตผู้ให้บริการและโมเดล และการแก้ไขจากการตรวจสอบเชิงปฏิปักษ์สามรอบ

อัปเดตเฉพาะแพ็กเกจที่ใช้ร่วมกันแล้วบิลด์โปรเจกต์ที่อ้างอิงใหม่ Mythosia.AI จะดึง Abstractions รุ่นที่ตรงกัน ตารางเปรียบเทียบรุ่นฐานที่เผยแพร่แล้วกับรุ่นที่ใช้ร่วมกันได้ในครั้งนี้

| แพ็กเกจ | รุ่นฐานที่เผยแพร่แล้ว | รุ่นเป้าหมาย |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` เปลี่ยนจาก `1.0.0-preview` เป็นรุ่นเสถียร `1.0.0` ในครั้งนี้ โดยคง API เดิมสำหรับโมเดล สถานะ เวอร์ชันเซิร์ฟเวอร์ และ metrics เป็นแพ็กเกจอิสระที่ไม่ขึ้นกับแพ็กเกจ AI หลัก

## เลือกการเปลี่ยนแปลงจากความต้องการ

| ความต้องการ | การเปลี่ยนและการย้าย |
| --- | --- |
| ตรวจคำผิดในตัวเลือกภาพก่อนส่ง | แทนสตริงด้วย `ImageQuality`, `ImageBackground`, `ImageOutputFormat` และ `ImageSize.Pixels(...)` / `ImageSize.Preset(...)` การรองรับยังต่างกันตามผู้ให้บริการ |
| เตรียมหลายคำขอโดยไม่เปลี่ยนค่าของกันและกัน | เริ่มด้วย `CreateRequest(...)` และเก็บ builder ใหม่ที่แต่ละ `With...` ส่งกลับ setter ของบริการยังเปลี่ยนค่าเริ่มต้นที่ใช้ร่วมกัน |
| คืนข้อมูลจากเครื่องมืออะซิงโครนัสโดยตรง | เมธอดที่ลงทะเบียนด้วยแอตทริบิวต์คืนออบเจ็กต์ผ่าน `Task<T>` / `ValueTask<T>` และรับ `CancellationToken` ที่ฉีดให้ได้ ข้อยกเว้นนับเป็นความล้มเหลว และ handler สตริงเดิมยังใช้ได้ |
| เลิกรอเมื่อผู้ใช้ยกเลิก | ส่ง `cancellationToken` ให้ completion, Run และทางเข้า RAG ที่รองรับ จะหยุดงานภายในและเครื่องมือที่ร่วมมือ แต่ไม่รับประกันการหยุดฝั่งผู้ให้บริการหรือย้อนการกระทำภายนอกที่เสร็จแล้ว |
| เก็บคำตอบ การใช้โทเค็น และแหล่งที่มาร่วมกัน | `AIRun.Result` คืน `Task<AIRunResult>` ใช้ `(await run.Result).Text` เมื่อต้องการสตริง ระบบรวบรวมผลแม้ไม่ได้อ่านสตรีม |
| แสดงตัวควบคุมที่เหมาะกับโมเดล | ใช้ `request.GetCapabilities()` หรือคำสั่งตรวจความสามารถบริการ/ภาพ `Supported`, `Unsupported`, `Unknown` เป็นข้อมูลภายในไลบรารี ไม่ใช่การตรวจสิทธิ์บัญชีแบบสด |

## แก้โค้ดเรียกและผู้ให้บริการที่เขียนเอง

ชนิดตัวเลือกภาพ `AIRun.Result` และซิกเนเจอร์ยกเลิกที่เปลี่ยนเป็นการเปลี่ยนสัญญาที่ไม่เข้ากัน การเขียน `IAIService` เองและ override ของ overload สาธารณะที่เปลี่ยนต้องเพิ่มและส่งต่อ token ส่วน override ผู้ให้บริการ `GetCompletionAsync(Message)` คงซิกเนเจอร์และส่งต่อ `RequestCancellationToken` การเขียน `AIRun` เองต้องคืน `AIRunResult` GetCompletionAsync ยังคืนสตริง และ completion แบบกำหนดชนิดกับ `StructuredStreamRun<T>.Result` คงชนิดผลลัพธ์ StreamAsync ของบริการ/RAG ที่รับอินพุตยังเป็นสาธารณะใน v8 RunAgentAsync และ RunAgentStreamAsync คงพฤติกรรมเข้ากันได้และคำเตือน obsolete ใช้ Run สำหรับขั้นตอนใหม่ที่แสดงความคืบหน้า ยกเลิก และส่งคำสั่งระหว่างงานเมื่อรองรับ

## คำขอเดียว พร้อมผลลัพธ์และความคืบหน้าตามต้องการ

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAI ใช้พิกเซล ส่วน Google และ xAI ใช้ `ImageSize.Preset(...)` เปลี่ยน Auto เมื่อผู้ให้บริการรองรับการเลือกฟอร์แมตเท่านั้น และบันทึกตาม `GeneratedImage.MediaType` ที่คืนมา

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

เครื่องมือที่ลงทะเบียนคืนออบเจ็กต์ของแอปได้ดังตัวอย่าง `HandlerWithCancellation` ระดับล่างยังคืน `Task<string>` และไม่ต้องสร้างตัวห่อออบเจ็กต์ใหม่

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## ผู้ให้บริการและการตรวจสอบ

รุ่นนี้ยังรวมการเชื่อมต่อที่เตรียมไว้สำหรับ Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash, Perplexity Agent และการสร้าง/แก้ภาพร่วมกันของ OpenAI, Google, xAI ค่าคงที่โมเดลที่ลบและ endpoint Perplexity ที่เปลี่ยนอาจต้องแก้โค้ดเรียก ดูขอบเขตในคู่มือผู้ให้บริการและบันทึกแต่ละแพ็กเกจ

ตั้งค่าการวิจัยด้วย `PerplexityAgentOptions` การทดสอบ Profile, Custom Skill และ Connector เตรียมไว้แล้ว แต่ต้องมีทรัพยากรที่ลงทะเบียน MCP ยังคงเป็น preview เมื่อเริ่มปล่อยการเชื่อมต่อ การเรียกจะล้มเหลวด้วย `ObjectDisposedException` เมื่อวงอ่านหยุดแล้ว การเรียกใหม่จะล้มเหลวด้วย `McpException` แทนการรอไม่สิ้นสุด

การตรวจสอบเชิงปฏิปักษ์สามรอบปรับปรุงการคัดลอกคำขอ ผลเครื่องมือ การยกเลิก/เก็บกวาด การนับโทเค็น การตรวจผลตอบกลับ และวงจรชีวิต MCP รอบสามเพิ่มกรณีถดถอย 43 กรณี และผ่านทั้งหมด 2,703 การทดสอบ เอกสารตรวจใน 13 ภาษา รอบนี้ไม่ได้เรียก API ผู้ให้บริการจริง ผลหน่วยทดสอบจึงไม่ได้ยืนยันทุกการเชื่อมต่อที่ต้องใช้ทรัพยากรในบัญชี

## คู่มือรายละเอียด

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
