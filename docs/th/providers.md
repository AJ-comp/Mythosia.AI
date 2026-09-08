# ความสามารถเฉพาะของแต่ละ Provider

## OpenAI (OpenAIService)

> การรองรับ GPT-6 Astra และการเรียกเครื่องมือแบบอะซิงโครนัสเริ่มใน `Mythosia.AI` 7.1.0 โดยชนิดข้อมูลร่วมอยู่ใน `Mythosia.AI.Abstractions` 3.1.0

หากเครื่องมือใช้เวลานานในการดึงข้อมูล GPT-6 Astra สามารถอธิบายหรือทำส่วนอื่นของงานที่ไม่ขึ้นกับผลนั้นต่อได้ระหว่างรอ ใช้ `FunctionDefinition.AllowAsync = true` หรือ `FunctionBuilder.WithAsync()` เพื่อเลือกอนุญาตการเรียกเครื่องมือแบบอะซิงโครนัสของ GPT-6 Astra ผ่าน Responses ค่าเริ่มต้นคือ `false` ส่วนโมเดลที่ไม่รองรับจะรอผลจาก handler เดิม ดูตัวอย่างและอายุของคำขอใน[คู่มือการเรียกฟังก์ชัน](function-calling.md)

ดูวิธีตั้งระดับการให้เหตุผลข้ามผู้ให้บริการและใช้ข้อมูลใหม่หรือเอกสารที่จัดทำดัชนีแล้วใน[คู่มือการให้เหตุผลและการค้นหา](reasoning-and-search.md) ซึ่งระบุโมเดลที่รองรับ การรักษาแคช และข้อจำกัดการใช้ตัวเลือกร่วมกัน

### ระดับ Reasoning

GPT-6 Astra / GPT-5.x และ o3 series รองรับการควบคุมระดับ reasoning เพื่อปรับสมดุลระหว่างความเร็วและความลึก:

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

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
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

### สร้างภาพ

```csharp
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "เมืองแห่งอนาคตในยามค่ำคืน",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### การนับ Token (Native API)

`GetInputTokenCountAsync` ใช้ได้กับทุก provider (ดู [การสร้างข้อความ](completions.md#การนบ-token)) การ implement ของ Anthropic เรียก endpoint `messages/count_tokens` โดยตรง คืนค่า **จำนวน token ที่แม่นยำ** ไม่ใช่การประมาณ:

```csharp
uint tokens = await service.GetInputTokenCountAsync("prompt ของคุณ");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### ระดับการคิด

ควบคุมความลึกของ reasoning ภายในของ Gemini:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// ตัวเลือก: Disabled, Low, Medium, High
```

ระดับที่สูงขึ้นให้คำตอบที่ละเอียดกว่า แต่เพิ่ม latency และ token

---

## xAI (XAIService)

### Reasoning Mode

```csharp
using Mythosia.AI.Models;

service.ReasoningEffort = GrokReasoning.High;
// ตัวเลือก: Auto, None, Low, Medium, High (ขึ้นอยู่กับโมเดล)
```

---

## Perplexity (PerplexityService)

### ค้นหาเว็บพร้อม Citation

Sonar model สามารถค้นเว็บและส่ง citation แหล่งที่มาพร้อมกับคำตอบ:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "ความก้าวหน้าล่าสุดด้านพลังงานฟิวชันคืออะไร?",
    domainFilter: new[] { "nature.com", "science.org" },  // optional
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"แหล่งอ้างอิง: {citation.Url}");
}
```

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
