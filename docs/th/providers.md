# ความสามารถเฉพาะของแต่ละ Provider

> ตัวอย่าง `CreateRequest` ต้องใช้Mythosia.AI 8.0.0 / Abstractions 4.0.0 รุ่น 7.1 เดิมที่เพิ่ม Run และตัวเลือกคำขอทั่วไปยังไม่มี builder แพ็กเกจเดิมใช้ overload ของ service ต่อได้

<a id="image-options-migration"></a>
โหมดและข้อมูลที่คืนมาขึ้นกับผู้ให้บริการ โมเดล และ API ตรวจ[ตัวเลือกความเร็วร่วม](request-building.md#inference-speed) กับ capabilities และแยกการขอ Fast ออกจากการได้รับ Fast จริง

## ย้ายไปใช้ตัวเลือกภาพแบบระบุชนิด

เลือกคุณภาพและรูปแบบด้วย enum และการเติมคำอัตโนมัติ พร้อมแยกขนาดพิกเซลที่แน่นอนออกจากระดับความละเอียด เพื่อลดการพิมพ์ผิดและป้องกันการแปลงขนาดไปเป็นระดับอื่นโดยไม่แจ้ง

เป็นการเปลี่ยนแปลงที่ไม่เข้ากันกับโค้ดเดิมสำหรับMythosia.AI 8.0.0: `Quality`, `Background`, `OutputFormat` เป็น enum ส่วน `Size` เป็น `ImageSize` และนำพร็อพเพอร์ตี `AspectRatio` ที่แยกต่างหากออก ค่าเริ่มต้น `OutputFormat` เป็น `ImageOutputFormat.Auto` เมธอดสร้างและแก้ไขภาพยังเหมือนเดิม

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)` ขอขนาดที่แน่นอน ส่วน `Preset(resolution, aspectRatio)` ขอระดับความละเอียดและอัตราส่วน โดยผู้ให้บริการกำหนดพิกเซลจริง ใช้ `ImageSize.Auto` เมื่อไม่จำกัดขนาด เปลี่ยนจากพิกเซลเป็น preset เฉพาะเมื่อแอปยอมรับขนาดโดยประมาณได้

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

ค่า enum ที่ไม่ได้กำหนดและชุดตัวเลือกที่โมเดลไม่รองรับจะถูกปฏิเสธก่อน HTTP การมีสมาชิก enum ไม่ได้แปลว่าทุกโมเดลรองรับ Google รองรับคุณภาพเพียง `ImageQuality.Auto` ส่วน xAI รองรับ `Auto`, `Low`, `Medium`

เมื่อ `EditImagesAsync` คืนค่า `Task` แล้ว คุณสามารถนำบัฟเฟอร์อินพุตกลับมาใช้ใหม่ได้ คำขอที่เริ่มแล้วจะเก็บข้อมูลภาพของตนเอง รวมถึงไบต์มาสก์ของ OpenAI การแก้ไขอาร์เรย์ `ImageInput.Data` ต้นฉบับภายหลังจึงไม่เปลี่ยนเนื้อหาที่อัปโหลด

เพื่อไม่ให้บันทึกผลลัพธ์ที่ถูกขัดจังหวะเป็นภาพที่เสร็จสมบูรณ์ การสร้างและแก้ไขภาพของ Google กำหนดให้ผลลัพธ์ผู้สมัครทุกรายการจบด้วย `finishReason: STOP` หากรายการใดถูกบล็อก ยังไม่สมบูรณ์ หรือไม่มีสถานะสิ้นสุดนี้ การเรียกทั้งหมดจะโยน `AIServiceException` หากข้อมูล base64 แบบอินไลน์หรือข้อมูล MIME ของภาพขาดหายหรือไม่ถูกต้อง การเรียกทั้งหมดจะล้มเหลวเช่นกัน โดยไม่เดาว่าเป็น PNG การตรวจสอบนี้ไม่ได้ยืนยันว่าไบต์ของไฟล์ตรงกับรูปแบบภาพที่ระบุ

## OpenAI (OpenAIService)

> การรองรับ GPT-6 Astra และการเรียกเครื่องมือแบบอะซิงโครนัสเริ่มใน `Mythosia.AI` 7.1.0 โดยชนิดข้อมูลร่วมอยู่ใน `Mythosia.AI.Abstractions` 3.1.0

หากเครื่องมือใช้เวลานานในการดึงข้อมูล GPT-6 Astra / Sol / Luna สามารถอธิบายหรือทำส่วนอื่นของงานที่ไม่ขึ้นกับผลนั้นต่อได้ระหว่างรอ ใช้ `FunctionDefinition.AllowAsync = true` หรือ `FunctionBuilder.WithAsync()` เพื่อเลือกอนุญาตการเรียกเครื่องมือแบบอะซิงโครนัสของ GPT-6 Astra / Sol / Luna ผ่าน Responses ค่าเริ่มต้นคือ `false` ส่วนโมเดลที่ไม่รองรับจะรอผลจาก handler เดิม ดูตัวอย่างและอายุของคำขอใน[คู่มือการเรียกฟังก์ชัน](function-calling.md)

ดูวิธีตั้งระดับการให้เหตุผลข้ามผู้ให้บริการและใช้ข้อมูลใหม่หรือเอกสารที่จัดทำดัชนีแล้วใน[คู่มือการให้เหตุผลและการค้นหา](reasoning-and-search.md) ซึ่งระบุโมเดลที่รองรับ การรักษาแคช และข้อจำกัดการใช้ตัวเลือกร่วมกัน

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna (ยังไม่เผยแพร่)

เลือก GPT-6 Sol สำหรับงานเขียนโค้ดซับซ้อน การใช้เครื่องมือ และงานเอเจนต์ หรือเลือก Luna เมื่อต้องประมวลผลข้อความหรือภาพจำนวนมากโดยควบคุมต้นทุน ทั้งคู่ใช้ API คำตอบสมบูรณ์ สตรีม และ Run เดิม จึงเปลี่ยนโมเดลได้โดยไม่เปลี่ยนลำดับการเรียกของแอป

> ส่วนเพิ่มนี้ยังไม่เผยแพร่ และต้องใช้บิลด์ core กับ abstractions ที่ตรงกัน แพ็กเกจ Mythosia.AI 8.0.0 / Abstractions 4.0.0 ที่เผยแพร่แล้วไม่มี `Gpt6Sol`, `Gpt6Luna` หรือ `Gpt6Reasoning.None` รุ่นขั้นต่ำของฟีเจอร์ Astra เดิมและโมเดลเริ่มต้นของบริการไม่เปลี่ยน

ใช้ `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) หรือ `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`) ทั้งคู่รับข้อความและภาพและส่งออกข้อความ มีบริบท 1,050,000 โทเคน อินพุตสูงสุด 922,000 และเอาต์พุตสูงสุด 128,000 โทเคน อินพุต การให้เหตุผล และเอาต์พุตรวมกันต้องอยู่ในขีดจำกัดบริบท `MaxTokens` กำหนดงบเอาต์พุตที่ขอ ไม่ใช่ขนาดบริบท

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto` ใช้ `Medium` โดย Sol/Luna รองรับ `None`, `Low`, `Medium`, `High`, `XHigh`, `Max` แต่ไม่รองรับ `Minimal` ใช้ `WithReasoning(ReasoningLevel.None)` ต่อคำขอ หรือ `Gpt6Reasoning.None` ใน `WithGpt6Parameters` เฉพาะ Sol/Luna ที่ใช้ `None` เท่านั้นที่ส่ง `Temperature` / `TopP` เมื่อเปิดการให้เหตุผลจะละทั้งสองฟิลด์ Astra ต้องใช้การให้เหตุผลเสมอและไม่ส่งค่าการสุ่ม `AIRequestProfile.DisableReasoning` เลือก `None` สำหรับ Sol/Luna และ `Low` ในโหมด Standard สำหรับ Astra โดยไม่ส่งสรุปการให้เหตุผล

`Gpt6ReasoningMode.Standard` และ `.Pro` ใช้รหัสโมเดลเดิมที่เลือกไว้ GPT-6 ทั้งสามรุ่นรองรับเครื่องมือผ่าน Responses เครื่องมือแบบอะซิงโครนัสที่เลือกเปิดได้ คำสั่งเพิ่มผ่าน WebSocket Run และการเปลี่ยนระดับการให้เหตุผลโดยรักษาแคชในโหมด Standard เอเจนต์เดียว ตรวจ `run.CanSteer` ก่อนส่งคำสั่ง การรับคำสั่งไม่ย้อนคืนเอาต์พุตก่อนหน้า `WithSpeed(InferenceSpeed.Fast)` ขอการประมวลผล Fast แบบเสียค่าใช้จ่ายแยกจากระดับการให้เหตุผล ดูโหมดที่ใช้จริงใน `result.Processing` สิทธิ์บัญชีและการลดระดับโดยเซิร์ฟเวอร์แยกจากข้อมูลความสามารถในไลบรารี

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### ระดับ Reasoning

GPT-6 Astra / Sol / Luna และ GPT-5.1–5.6 ปรับระดับการใช้เหตุผลได้ เพื่อเลือกสมดุลระหว่างความเร็วและความลึกของคำตอบ:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol เป็นรุ่นเรือธง ส่วน Terra และ Luna เป็นตัวเลือกที่ประหยัดกว่า
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4 series
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 series
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra ใช้ Responses API เป็นค่าเริ่มต้น และการเรียกฟังก์ชันต้องใช้ API นี้ `Auto` ใช้ค่าเริ่มต้นของไลบรารีคือ `Medium` และไม่รองรับ `None` กับ `Minimal` ส่วน `AIRequestProfile.DisableReasoning = true` จะใช้ระดับ `Low` ในโหมด `Standard` และไม่ส่งสรุปการให้เหตุผล เลือก `Gpt6ReasoningMode.Pro` เพื่อใช้โหมด Pro กับรหัสโมเดลเดิม `gpt-6-astra`

### Text-to-Speech

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "สวัสดีครับ",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Speech-to-Text (การถอดความ)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "th"  // optional, ISO-639-1
);
```

`TranscribeAudioAsync` ใช้ `gpt-transcribe` โดยไม่เปลี่ยน public signature

### สร้างภาพ

#### GPT Image 2.5

เลือก Flare เมื่อต้องการสร้างภาพร่างอย่างรวดเร็ว หรือ Sunburst เมื่อต้องแก้ไขภาพให้ตรงตามคำสั่งอย่างละเอียด ทั้งสองโมเดลสร้างและแก้ไขภาพผ่าน `IImageGenerationService` เดิมได้ โดยไม่เปลี่ยนโมเดลแชต

| โมเดล | ควรเลือกเมื่อใด |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | สร้างภาพสำหรับงานประจำวันได้รวดเร็วและมีคุณภาพสูง |
| `AIModels.OpenAI.GptImage2_5Sunburst` | สร้างและแก้ไขภาพที่ให้ความสำคัญกับความแม่นยำในการแก้ไข |

ระบุ `ImageGenerationRequest.Model` หรือพร็อพเพอร์ตีที่ `ImageEditRequest` สืบทอดมาอย่างชัดเจน ค่าเริ่มต้นของ OpenAI ยังคงเป็น `AIModels.OpenAI.GptImage2` ชื่อแฝงคือ `gpt-image-2.5-flare` และ `gpt-image-2.5-sunburst` หากต้องการตรึงรุ่นวันที่ 8 กันยายน 2026 ให้ใช้ `GptImage2_5Flare_260908` หรือ `GptImage2_5Sunburst_260908` โดย ID จะลงท้ายด้วย `-2026-09-08`

สร้างภาพร่างด้วย Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "ศาลากระจกยามพระอาทิตย์ขึ้น ภาพแนวคิดทางสถาปัตยกรรม",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

จากนั้นแก้ไขภาพที่สร้างด้วย Sunburst:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "คงรูปแบบศาลาไว้ ลบสิ่งแวดล้อมโดยรอบ และทำให้พื้นหลังโปร่งใส",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

`Quality` ของทั้งสองโมเดลและรุ่นที่ตรึงไว้รองรับ `Auto`, `Low`, `Medium`, `High`, `XHigh`, `Max` ใช้คุณภาพต่ำสำหรับภาพร่างแล้วเปรียบเทียบระดับที่สูงขึ้นสำหรับงานขั้นสุดท้าย `OutputFormat` รองรับ `Auto` / `Png`, `Jpeg`, `WebP` และ `OutputCompression` ตั้งได้ 0–100 เฉพาะ JPEG/WebP พื้นหลัง `Transparent` ต้องใช้ PNG/WebP ส่วน `Count` คือ 1–10

`Size` ใช้ `ImageSize.Auto` หรือ `ImageSize.Pixels(width, height)` ทั้งสองด้านต้องเป็นเท่าของ 16 อัตราส่วน 1:3–3:1 แต่ละด้านไม่เกิน 3840 และพื้นที่ 655360–8294400 พิกเซล ขนาดเกิน 2560×1440 ยังเป็นการทดลอง OpenAI ปฏิเสธ `Preset`

การแก้ไขรับภาพอ้างอิง JPEG/PNG/WebP ที่ไม่ว่าง 1–16 ภาพ แต่ละภาพเล็กกว่า 50 MiB มาสก์เสริมต้องเป็น PNG/WebP ขนาดต่ำกว่า 50 MiB มีรูปแบบและขนาดพิกเซลตรงกับภาพอ้างอิงแรก และมีช่องอัลฟา ไลบรารีตรวจ MIME และความยาวไบต์ ส่วนผู้ให้บริการตรวจขนาดพิกเซลและอัลฟา

ตัวอย่างใช้เส้นทาง Image API เดิมที่คืนค่าไบต์และแก้ไขแบบ multipart การเชื่อมต่อนี้ยังไม่เปิดเครื่องมือ Responses `image_generation`, สตรีมภาพบางส่วน หรือ `input_fidelity` อ่าน `GeneratedImage.Data` และ `MediaType` จากผลลัพธ์

ดู[คู่มือภาพอย่างเป็นทางการ](https://developers.openai.com/api/docs/guides/image-generation) และหน้าโมเดล [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) กับ [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare)

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) เพิ่มข้อความความคืบหน้า คำสั่งเฉพาะเทิร์น และการวินิจฉัย thinking binding ตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 ส่วน Mythos 5.1 ต้องได้รับเชิญ และทั้งสองรุ่นไม่รองรับการบังคับเลือกเครื่องมือ

<a id="claude-opus-55"></a>

### Claude Opus 5.5: แสดงความคืบหน้าของงานที่เรียกเครื่องมือหลายรอบ

ใช้ Opus 5.5 เมื่อต้องตรวจโค้ดหรือค้นคว้าเอกสารโดยเรียกเครื่องมือหลายรอบ ยังคงใช้ API completion และ Run เดิม แต่ค่าเริ่มต้นซ่อนความคืบหน้า และต้องระวังการแก้ประวัติหากต้องการเก็บการให้เหตุผลไว้ การรองรับนี้ยังไม่เผยแพร่และไม่มีในแพ็กเกจ 8.0.0 / 4.0.0 ที่เผยแพร่แล้ว

`ClaudeOpus5_5` เลือก `claude-opus-5-5` รับข้อความ/รูปภาพและส่งออกข้อความ มีบริบท 1M และเอาต์พุตสูงสุด 128K โทเค็น ราคาปกติที่ตรวจสอบเมื่อ 2026-09-24 คืออินพุต/เอาต์พุต $4/$20 ต่อหนึ่งล้านโทเค็น ส่วนโหมดพิเศษและเครื่องมือคิดราคาแยก [ข้อมูลโมเดลทางการ](https://platform.claude.com/docs/en/models/opus-5-5/overview).

เมื่อไม่เปลี่ยนการตั้งค่าบริการ `Auto` ใช้ effort ระดับ `Medium` และละข้อความการให้เหตุผลที่อ่านได้ การคิดแบบ adaptive เปิดตลอดเวลา เลือก `Low`, `Medium`, `High`, `XHigh` หรือ `Max` ได้ ส่วนค่ากลาง `ReasoningLevel.None` และ `Minimal` จะถูกปฏิเสธ โมเดลเริ่มต้นของบริการไม่เปลี่ยน

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

ตัวอย่างร้องขอ `Updates` และอ่าน `StreamingContentType.Reasoning` ใช้ `Summarized` เพื่อรับสรุปการให้เหตุผล หรือ `Omitted` เพื่อซ่อน พารามิเตอร์การแสดงผลของ `WithAdaptiveThinkingParameters(effort)` ยังมีค่าเริ่มต้นเป็น `Summarized` ซึ่งต่างจากบริการที่ไม่ได้ตั้งค่า สำหรับ completion ปกติ ให้อ่าน `LastThinkingContent` หลังเรียกเสร็จ ไม่รับประกันการอัปเดตตามช่วงเวลาคงที่

`ThinkingBudget` แบบเดิมที่เป็นบวกจะถูกแปลงเป็น effort high/xhigh/max ไม่ใช่งบโทเค็นที่แน่นอน ค่า 0 หรือค่าลบไม่สามารถปิดการคิดได้ โปรไฟล์ที่ปิดการให้เหตุผลจะใช้ effort ต่ำและซ่อนข้อความที่อ่านได้ `MaxTokens` รวมทั้งการคิดที่ซ่อนอยู่และคำตอบ จึงควรประเมินขีดจำกัดเอาต์พุตและค่าใช้จ่ายใหม่เมื่อย้ายโมเดล

Mythosia เก็บบล็อก thinking ที่มีลายเซ็น รวมถึงบล็อกว่าง ระหว่างรอบสนทนาและผลเครื่องมือ ใช้บริการและแชตเดิมต่อ และอย่าเขียนข้อความก่อนหน้า system หรือ tools ใหม่หากต้องการรักษาการให้เหตุผล ใช้ `WithTurnInstruction`, `WithConversationInstruction` และ `CachePreservation.Required` ได้ `WithThinkingBinding` เลือก `Error` หรือ `DropBlock` และ `LastInputTransformations` แสดงรายการที่เซิร์ฟเวอร์รายงานว่าถูกทิ้ง Drop คือการทิ้งการให้เหตุผล [คู่มือประวัติ](fable-5-1.md) อธิบายการควบคุมร่วม ส่วนค่าเริ่มต้นและความเข้ากันได้ให้ใช้กฎของ Opus 5.5

อย่าตั้ง `ForceFunctionName` การเลือกเครื่องมือปกติและ `FunctionsDisabled` ยังรองรับอยู่ Assistant prefill ถูกปฏิเสธและไม่ส่งพารามิเตอร์ sampling Opus 5.5 อ่าน thinking ของ Fable/Mythos ไม่ได้ แต่ Fable 5.1 และ Mythos 5.1 บน Claude API อ่าน thinking ของ Opus 5.5 ได้ การสลับโมเดลจึงอาจสูญเสียการให้เหตุผลเดิม ส่วนเพิ่มนี้ไม่ได้เปิด API สำหรับ computer toolset แบบเนทีฟ, task budget, การเปลี่ยนเครื่องมือระหว่างสนทนา, การบีบอัดฝั่งเซิร์ฟเวอร์ หรือ fallback ฝั่งเซิร์ฟเวอร์อัตโนมัติ [เงื่อนไขการย้าย](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [ขอบเขตฟีเจอร์เนทีฟ](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

สำหรับ Opus 5.5 การแก้เนื้อหาคำตอบ assistant ที่บันทึกไว้โดยตรงจะเกิด `InvalidOperationException` ก่อนส่ง HTTP แม้ใช้ `DropBlock` ก็ไม่อนุญาตให้เขียนคำตอบที่มีลายเซ็นใหม่ ให้ส่งคำแก้ไขเป็นอินพุตผู้ใช้ใหม่หรือเริ่มบทสนทนาใหม่ ส่วนการแก้เนื้อหา user/system ก่อนหน้าจะใช้กฎการผูกส่วนต้นของผู้ให้บริการ

Opus 5.5 fast mode ใช้ผ่าน [WithSpeed](request-building.md#inference-speed) บน Claude API โดยตรงได้เมื่อมีสิทธิ์ คงระดับการให้เหตุผลเดิมและขอโหมดที่คิดราคาพรีเมียม

### การนับ Token (Native API)

`GetInputTokenCountAsync` ใช้ได้กับทุก provider (ดู [การสร้างข้อความ](completions.md#การนบ-token)) การ implement ของ Anthropic เรียก endpoint `messages/count_tokens` โดยตรง คืนค่า **จำนวน token ที่แม่นยำ** ไม่ใช่การประมาณ:

```csharp
uint tokens = await service.GetInputTokenCountAsync("prompt ของคุณ");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

สำหรับการตรวจเอกสารยาวหรืองานที่เรียกเครื่องมือหลายรอบ สามารถเลือก Gemini 3.7 Flash หรือ 3.8 Flash ผ่านอะแดปเตอร์ Google เดิมได้ รองรับตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 โดยโมเดลเริ่มต้นยังเป็น Gemini 3.6 Flash

### ระดับการคิด

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("เปรียบเทียบการติดตั้งแบบ rolling กับ blue-green รวมถึงความเสี่ยงในการย้อนกลับ")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

ใช้ `Low` สำหรับการตรวจเบื้องต้น และ `High` สำหรับการวิเคราะห์ที่ซับซ้อน การใช้เหตุผลมากขึ้นอาจเพิ่มเวลาและจำนวนโทเค็น ทั้งสองโมเดลรองรับ `Low`, `Medium`, `High` แต่ไม่รองรับ `Minimal` หรือ `None` ค่า `GeminiThinkingLevel.Auto` จะไม่ส่งค่าทับค่าเริ่มต้น ซึ่งผู้ให้บริการกำหนดเป็น `Medium` สำหรับ 3.8 ส่วน `ThinkingLevel` ตั้งค่าพื้นฐานของบริการ และ `WithReasoning(...)` ใช้ทับเฉพาะคำขอเชิงตรรกะหนึ่งครั้ง อะแดปเตอร์ไม่ส่ง `temperature`, `topP`, `topK` ขีดจำกัดของผู้ให้บริการคืออินพุต 1,048,576 และเอาต์พุต 65,536 โทเค็น [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### ความละเอียดและอัตราส่วนตามโมเดลภาพ Google

| โมเดล | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

อัตราส่วนมาตรฐาน 10 แบบคือ `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9` ชุด 14 แบบเพิ่ม `1:4`, `4:1`, `1:8`, `8:1` ทุกโมเดลยังรับ `ImageAspectRatio.Auto` ด้วย

ใช้ `ImageSize.Auto` หรือ `ImageSize.Preset(resolution, aspectRatio)` โดย `Auto` จะไม่ส่งตัวเลือกนั้น `GetImageCapabilities(model)` และ `GenerateImagesAsync` / `EditImagesAsync` ใช้ตัวเลือกตามโมเดลชุดเดียวกัน ค่าที่ระบุชัดเจนแต่ไม่รองรับจะทำให้เกิด `NotSupportedException` ก่อน HTTP โดยไม่มีการปรับขนาดหรือส่งคำขอทดแทน ID โมเดลกำหนดเองที่ไม่ทราบยังคงเป็น `Unknown` และส่งค่าตามเดิมหลังตรวจสอบตัวเลือกทั่วไปของผู้ให้บริการ

สำหรับ Flash-Lite [หน้าโมเดล](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image) และข้อความในคู่มือระบุ 1K แต่[ตาราง](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size) มีคอลัมน์ 512 ด้วย ระหว่างรอตรวจสอบความแตกต่างนี้ ไลบรารีอนุญาตเฉพาะ 1K อย่างระมัดระวัง ไม่ได้ยืนยันว่าได้ทดสอบพบว่าเซิร์ฟเวอร์ปฏิเสธ 512

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

เมื่อต้องการร่างคำตอบอย่างรวดเร็วแล้วตรวจโค้ดหรือเอกสารอย่างละเอียด ให้เลือก Grok 4.7 และปรับระดับการให้เหตุผลในแต่ละคำขอ ใช้ API เดิมสำหรับคำตอบ สตรีม Run เครื่องมือภายใน ผลลัพธ์แบบมีโครงสร้าง และภาพขาเข้าได้ `grok-4.7` รับข้อความและภาพ คืนข้อความ และมีบริบท 500,000 โทเคน ต้องใช้บิลด์ core และ abstractions ที่ยังไม่เผยแพร่และตรงกัน แพ็กเกจ 8.0.0 / 4.0.0 ที่เผยแพร่แล้วไม่มีส่วนนี้ โมเดลเริ่มต้นยังเป็น Grok 4.5

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

รองรับ `Low`, `Medium`, `High` และ `XHigh` โดย `GrokReasoning.Auto` แบบเนทีฟละ `reasoning_effort` เพื่อใช้ค่าเริ่มต้น `High` ของผู้ให้บริการ ส่วน `ReasoningLevel.Auto` แบบรวมก็ละฟิลด์นี้เพื่อใช้ค่าเริ่มต้น `High` ของผู้ให้บริการในคำขอนั้น `None`, `Minimal` และ `Max` ถูกปฏิเสธก่อนส่ง `WithReasoning(...)` ใช้กับคำขอเชิงตรรกะรวมรอบเครื่องมือและการแก้ผลลัพธ์แบบมีโครงสร้าง ส่วน `WithGrokReasoning(...)` ตั้งค่าพื้นฐานของบริการ โปรไฟล์ภายใน `DisableReasoning` ใช้ `Low` สรุปการให้เหตุผลที่อาจส่งมาไม่ใช่กระบวนการคิดภายในทั้งหมด

`WithSpeed(InferenceSpeed.Standard)` ส่ง `service_tier: "default"` ส่วน `Fast` ส่ง `"priority"` ไปยัง endpoint xAI ที่รองรับและอาจมีค่าใช้จ่ายเพิ่ม `ProviderDefault` ไม่เขียนทับค่า เซิร์ฟเวอร์อาจลดเป็นการประมวลผลปกติ จึงควรอ่านระดับที่รายงานจาก `result.Processing` นี่คือการจัดลำดับความสำคัญของ `grok-4.7` ไม่ใช่รุ่น “Grok 4.7 Fast” ที่ใช้เฉพาะ Cursor/Grok Build ซึ่งไม่มี ID โมเดลสำหรับ API สาธารณะ

`GetCapabilities()` อธิบายความสามารถของคำขอที่เลือกจากข้อมูลภายใน ไม่ได้ตรวจสิทธิ์บัญชี การเชื่อมต่อนี้ใช้ Chat Completions ยังไม่เชื่อมการให้เหตุผลแบบเข้ารหัสของ Responses การค้นหา Web/X ที่โฮสต์ เครื่องมืออะซิงโครนัสเนทีฟ การเปลี่ยนค่าที่รักษาแคช หรือ `SteerAsync` ฟังก์ชันฝั่งไคลเอนต์ใช้ลูปเครื่องมือภายในเดิม และ `run.CanSteer` เป็น false

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### เลือกระดับการให้เหตุผลให้เหมาะกับงาน

ใช้ระดับต่ำเพื่อร่างฉบับแรกอย่างรวดเร็ว แล้วเพิ่มการให้เหตุผลสำหรับการตรวจสอบที่ยากและให้ความสำคัญกับคุณภาพมากกว่าเวลาตอบ หากต้องการระดับเพิ่มเติม `XHigh` ให้เลือก Grok 4.6 อย่างชัดเจน

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("เปรียบเทียบการปรับใช้แบบทยอยและแบบบลู-กรีน รวมขั้นตอนกู้คืนเมื่อเกิดข้อผิดพลาด")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 รองรับ `Low`, `Medium`, `High` และ `XHigh` (`GrokReasoning.XHigh`) ส่วน `Auto` จะละ `reasoning_effort` เพื่อใช้ค่าเริ่มต้น `High` ของผู้ให้บริการ และใช้ `None` ปิดการให้เหตุผลไม่ได้ ระดับที่สูงขึ้นอาจเพิ่มความหน่วงและการใช้โทเคน เพื่อรักษาความเข้ากันได้ `XAIService` ยังใช้ Grok 4.5 เป็นค่าเริ่มต้น รุ่น 4.5 รองรับ `Low` ถึง `High` และ 4.3 รองรับ `None` ถึง `High` อะแดปเตอร์จะปฏิเสธ `XHigh` บนโมเดลเก่าเหล่านี้ก่อนส่ง

`WithGrokReasoning(...)` และ `WithGrokParameters(...)` ที่มีอยู่ตั้งค่าพื้นฐานของบริการ สำหรับ Grok 4.6 เมธอดร่วม `WithReasoning(...)` จะแทนที่เพียงหนึ่งคำขอเชิงตรรกะ รวมรอบเครื่องมือและการซ่อมผลลัพธ์แบบมีโครงสร้าง แล้วคืนค่าพื้นฐาน โปรไฟล์ภายใน `DisableReasoning` ใช้ `Low` กับโมเดลที่ให้เหตุผลเสมอนี้ ตัวเลือกร่วมยังไม่ได้เชื่อมต่อการเปลี่ยนที่รักษาแคชหรือการค้นหาเว็บ/ไฟล์ที่ xAI โฮสต์

Grok 4.6 อาจคืนสรุปการให้เหตุผลของผู้ให้บริการเป็น `StreamingContentType.Reasoning` เมื่อเปิดการสังเกตด้วย `new StreamOptions().WithReasoning()` สรุปเป็นข้อมูลที่ผู้ให้บริการเลือกส่ง ไม่ใช่การให้เหตุผลภายในทั้งหมด ใช้ตัวเลือกเดียวกันกับ Run ได้ และการเปลี่ยนการแสดงสตรีมไม่เปลี่ยนระดับการให้เหตุผลที่ร้องขอ

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

ใช้การสร้างภาพเพื่อเปลี่ยนคำอธิบายสินค้าเป็นภาพร่าง หรือใช้การแก้ไขเพื่อรวมตัวแบบและฉากหลังจากภาพอ้างอิง `XAIService` รองรับทั้งสองแบบผ่าน `IImageGenerationService` เดียวกับ OpenAI และ Google ตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0

โมเดลภาพเริ่มต้นที่แยกจากแชตคือ `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`) คำขอภาพไม่เปลี่ยนโมเดลแชตและไม่เพิ่มลงในประวัติการสนทนา

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "ศาลากระจกยามพระอาทิตย์ขึ้น องค์ประกอบภาพแนวกว้าง",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

เมื่อต้องการแก้ไข ให้ส่งไบต์ของภาพตามลำดับที่กล่าวถึงในพรอมป์ต์:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "วางตัวแบบจากภาพที่ 1 ลงในฉากของภาพที่ 2",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

`GeneratedImage.Data` แต่ละรายการมีไบต์ภาพที่ถอดรหัสแล้ว ให้เลือกนามสกุลไฟล์ตาม `MediaType` อะแดปเตอร์ขอผลลัพธ์ base64 แบบฝังและไม่ดาวน์โหลด URL ภาพของผู้ให้บริการ `Count` รับผลลัพธ์ 1–10 ภาพ ส่วนการแก้ไขรับภาพอ้างอิง JPEG, PNG หรือ WebP จำนวน 1–5 ภาพ

สำหรับ xAI ใช้ `ImageSize.Auto` หรือ `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)` ระดับคือ `Auto`, `OneK`, `TwoK` และอัตราส่วนต้องรองรับโดยโมเดล `Pixels(...)` จะถูกปฏิเสธเพราะขอขนาดที่แน่นอนไม่ได้

xAI รองรับเพียงค่าเริ่มต้นร่วมใหม่ `ImageOutputFormat.Auto` ไม่มีตัวเลือก codec ดังนั้น `Jpeg`, `Png`, `WebP` ที่ระบุชัดเจนจะถูกปฏิเสธก่อนส่ง เลือกนามสกุลจาก `GeneratedImage.MediaType` ไลบรารีไม่แปลงรหัส คุณภาพรองรับ `ImageQuality.Auto`, `Low`, `Medium` พื้นหลังรองรับเพียง `ImageBackground.Auto` ไม่รองรับการระบุการบีบอัดหรือ `Mask` แยก

Google รับ `ImageSize.Auto` หรือ `Preset` ที่มีความละเอียดและอัตราส่วนตามโมเดล ดู[ตัวเลือกภาพตามโมเดล Google](#google-image-options). รูปแบบคือ `ImageOutputFormat.Auto` หรือ `Jpeg` โดยปฏิเสธ `Png`/`WebP` Google และ xAI ปฏิเสธ `Pixels` ส่วน OpenAI รับ `Auto`/`Pixels` และปฏิเสธ `Preset` ดู[ตัวอย่างการย้าย](#image-options-migration)

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

ใช้ DeepSeek Flash เมื่อต้องการคำตอบเร็วแล้วตรวจทานให้ละเอียด หรืออธิบายกราฟและภาพหน้าจอ `AIModels.DeepSeek.Flash` (`deepseek-flash`) เลือก V4.1 Flash ที่เปิดตัววันที่ 10 กันยายน 2026 พร้อมความเข้าใจภาพในตัว ใช้ API completion, streaming, Run, ฟังก์ชัน และ RAG เดิมได้ ตั้งแต่ `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0

> แพ็กเกจที่เผยแพร่แล้ว `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 รองรับ Flash พื้นฐานแล้ว ส่วน `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, Files API และ `DeepSeekImageFileContent` เป็นส่วนเพิ่มในซอร์สโค้ดที่ยังไม่เผยแพร่ ต้องใช้บิลด์ core และ abstractions จากซอร์สโค้ดรุ่นที่ตรงกัน และยังไม่มีในแพ็กเกจที่เผยแพร่ข้างต้น [บันทึกการเปลี่ยนแปลงที่ยังไม่เผยแพร่](../../src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased)

งานข้อความล้วนเลือก `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813) ได้ Flash ยังเป็นค่าเริ่มต้นและรองรับภาพ ทั้งสองรองรับเหตุผล Low/High/Max และขีดจำกัดเอาต์พุตเดียวกัน ตั้ง `UseResponsesApi = true` ก่อนสร้างคำขอเพื่อใช้ Responses ผ่าน API completion, streaming, Run และฟังก์ชันภายในเดิม ค่าเริ่มต้นยังเป็น `false` เพื่อคง Chat Completions ของแอปเดิม และจะเก็บตัวเลือกนี้ตลอดคำขอรวมรอบเครื่องมือ Responses ส่งประวัติสนทนาและเหตุผลต้นฉบับทั้งหมดซ้ำ โดยไม่พึ่ง ID คำตอบที่เก็บบนเซิร์ฟเวอร์

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

อัปโหลดภาพครั้งเดียวเมื่อจะใช้ซ้ำในหลายคำถามหรือบทสนทนา `UploadFileAsync` รับพาธ หรือ stream ที่ผู้เรียกเป็นเจ้าของพร้อมชื่อไฟล์ โดย purpose เป็น `user_data` รองรับ JPEG, PNG, GIF, WebP สูงสุด 64 MiB `DeepSeekImageFileContent` อ้างอิงภาพบน Flash ผ่านการส่งทั้งสองแบบ ไม่ใช่อินพุต PDF/เอกสาร และ V4 Pro จะปฏิเสธ หากไม่กำหนดอายุจะเก็บถาวร ส่วน `expiresAfterSeconds` รับ 3600–2592000 วินาที เก็บไฟล์ไว้จนทุกบทสนทนาที่อ้างอิงจบลง

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync` อ่าน metadata, `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` อ่านหนึ่งหน้า และ `DeleteFileAsync` ลบไฟล์ เมื่อ `HasMore` เป็น true ใช้ `LastId` เป็น `After` ของหน้าถัดไป และรองรับ `Descending` ด้วย ไม่มี endpoint ดาวน์โหลดเนื้อหาไฟล์ที่ประกาศไว้ Chat UI มี Flash และ V4 Pro และใช้แค็ตตาล็อกปัจจุบันสำหรับเขียนคำถามใหม่ ค่าเก่าที่บันทึกเป็น `DeepSeekChat` จะเปลี่ยนเป็น Flash โดยคง ID โมเดลที่กำหนดเอง

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("เปรียบเทียบการปรับใช้แบบทยอยและ blue-green รวมถึงความเสี่ยงในการย้อนกลับ");

await using var run = await deepseek
    .CreateRequest("ตรวจสอบสมมติฐานในการเปรียบเทียบนั้น")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

`ThinkingEnabled` ยังคงเป็น `false` โดยค่าเริ่มต้น `WithDeepSeekReasoning(...)` เปิดการใช้เหตุผลและตั้ง `ReasoningEffort` ถาวร (`Auto`, `Low`, `High`, `Max`) โดย `Auto` นี้ละ effort เพื่อใช้ค่าเริ่มต้นผู้ให้บริการ `High` ส่วน `WithReasoning(...)` ร่วมมีผลต่อหนึ่งคำขอเชิงตรรกะรวมรอบเครื่องมือ: `None` ปิด, `Minimal`/`Low` → `Low`, `Medium`/`High`/`XHigh` → `High`, `Max` → `Max` ค่า `Auto` ร่วมรักษาการตั้งค่าเดิม การเพิ่มเหตุผลอาจเพิ่มเวลาและโทเคน การเปลี่ยนเฉพาะ `ReasoningEffort` ไม่ได้เปิดการใช้เหตุผล

ลงทะเบียนฟังก์ชันภายในด้วย `WithFunction(...)` เพื่อให้โมเดลค้นข้อมูลหรือทำงานผ่านโค้ดของคุณ เครื่องมือใช้ได้ทั้งเปิดและปิดเหตุผล Chat Completions ปฏิเสธการบังคับ/กำหนดเครื่องมือว่าต้องเรียกเมื่อเปิดเหตุผล จึงให้ใช้การเลือกอัตโนมัติสำหรับการส่งแบบนี้ เมื่อตั้ง `UseResponsesApi = true` จะระบุฟังก์ชันด้วย `ForceFunctionName` ได้แม้เปิดเหตุผล โดยอะแดปเตอร์ส่ง `type` และ `name` ตรงใน `tool_choice` ของ Responses ทั้งนี้ไม่ได้เปิดเครื่องมืออะซิงโครนัสเนทีฟ อะแดปเตอร์เก็บ `reasoning_content` และ ID การเรียกไว้สำหรับรอบถัดไป Run และ streaming แสดง `StreamingContentType.Reasoning` เมื่อเปิด `StreamOptions.WithReasoning()` ซึ่งเป็นเพียงการสังเกต ไม่ได้เปิดการใช้เหตุผลเอง สถิติรวมแคชและเหตุผลเมื่อผู้ให้บริการรายงาน การกู้คืนบริบทอัตโนมัติใช้ลูป streaming ร่วม หากเครื่องมือต้องใช้ประวัติการให้เหตุผลดั้งเดิมก่อนหน้า จะปิดกั้นการย่ออัตโนมัติเพื่อรักษาประวัติและส่งข้อผิดพลาดบริบทเกินต่อไป

ส่งกราฟหรือภาพหน้าจอผ่านชนิดข้อความที่มีอยู่:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("อธิบายแนวโน้มของกราฟและระบุป้ายกำกับที่ไม่ชัดเจน"),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` รับไบต์ JPEG, PNG, GIF, WebP หรือ URL HTTP(S) สาธารณะที่ผู้ให้บริการดาวน์โหลด ตัวอย่างใช้ข้อความผู้ใช้ API ปัจจุบันรับภาพในข้อความเครื่องมือด้วย แต่ handler ที่ลงทะเบียนยังคืนข้อความผ่านสัญญาผลลัพธ์ร่วม หากสร้างข้อความภาพ `ActorRole.Function` เอง ต้องใส่ ID การเรียกที่ตรงกันใน `MessageMetadataKeys.FunctionId` (`tool_call_id` บนเครือข่าย) ดูขีดจำกัดขนาดและผลรวมในคู่มือภาพล่าสุด ยังไม่รองรับการสร้างภาพ

ทั้งสองมีบริบท 1M และเอาต์พุตสูงสุด 384K (`393216`) โทเคน งบเริ่มต้นยังเป็น 8,000 เมื่อใช้เหตุผลจะละ temperature/penalty และใช้ `top_p` อย่างน้อย 0.95 เมื่อปิดจะละ `top_p` Responses ใช้ API เอาต์พุตมีชนิดเดิมสำหรับ JSON schema เนทีฟ ไม่รองรับงานเบื้องหลัง `store`/`previous_response_id` บนเซิร์ฟเวอร์ การค้นหาที่โฮสต์ `CachePreservation.Required` เครื่องมืออะซิงโครนัสเนทีฟ `SteerAsync` หรือการสร้างภาพ ส่วน RAG ภายในและรอบเครื่องมือปกติยังใช้ได้

`V4Flash`, `Chat`, `Reasoner` ยังคงเป็นค่าคงที่ obsolete ที่แจ้งเตือนและรักษา wire ID เดิม ผู้ให้บริการเปลี่ยนเส้นทาง alias ที่เลิกใช้ `deepseek-v4-flash` ไป V4.1 Flash ชั่วคราว ไลบรารีไม่ได้เขียนค่าคงที่ใหม่ โค้ดใหม่ควรเลือก `Flash` ส่วน `UseReasonerModel()` เลือก Flash พร้อมเหตุผล `High`

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

ใช้ Perplexity เมื่อคำตอบต้องอาศัยข้อมูลล่าสุดและมีแหล่งอ้างอิงให้ผู้อ่านตรวจสอบ `PerplexityService` เรียก Agent API ส่วนการค้นหาและ embedding แบบแยกใช้สร้างการดึงเอกสารให้โมเดลตอบคำถามที่คุณเลือกได้

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("เปรียบเทียบวิธีรีไซเคิลแบตเตอรี่ล่าสุดและระบุแหล่งอ้างอิง");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

ดู [คู่มือ Perplexity](perplexity.md) สำหรับ preset การวิจัย ฟังก์ชันในแอป เครื่องมือที่ผู้ให้บริการรัน และงานเบื้องหลังที่ใช้เวลานาน โดยยังใช้ API completion, streaming, Run และ citation ที่คุ้นเคยได้

รุ่นนี้ย้ายบริการไปยัง `/v1/agent` โดย `AIModels.Perplexity.Sonar` จะเลือก `perplexity/sonar` ผู้ให้บริการประกาศปิด endpoint Sonar เดิมในวันที่ 27 กันยายน 2026 การเชื่อมต่อ Sonar เดิมจึงต้องย้ายระบบ [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

ติดตั้ง package แยก:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

Model ที่ใช้ได้: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` และ variant อื่น ๆ

เลือก endpoint ที่รองรับด้วย `EndpointPlatform` เมื่อสร้าง service:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[สร้างตัวเลือกโมเดลจากคำนิยามการรองรับร่วมกัน](model-capabilities.md).
