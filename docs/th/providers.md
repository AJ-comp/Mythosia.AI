# ความสามารถเฉพาะของแต่ละ Provider

## OpenAI (OpenAIService)

### ระดับ Reasoning

GPT-5.x และ o3 series รองรับการควบคุมระดับ reasoning เพื่อปรับสมดุลระหว่างความเร็วและความลึก:

```csharp
using Mythosia.AI.Models;

// GPT-5.4 series
service.Model = AIModels.OpenAI.Gpt5_4;
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 series
service.Model = AIModels.OpenAI.Gpt5_2;
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.Model = AIModels.OpenAI.O3;
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

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
// รับภาพเป็น bytes
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "เมืองแห่งอนาคตในยามค่ำคืน",
    size: "1024x1024"
);

// รับภาพเป็น URL
string imageUrl = await service.GenerateImageUrlAsync(
    prompt: "เมืองแห่งอนาคตในยามค่ำคืน",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### การนับ Token (Native API)

`GetInputTokenCountAsync` ใช้ได้กับทุก provider (ดู [การสร้างข้อความ](completions.md#token-counting)) การ implement ของ Anthropic เรียก endpoint `messages/count_tokens` โดยตรง คืนค่า **จำนวน token ที่แม่นยำ** ไม่ใช่การประมาณ:

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

service.ReasoningMode = GrokReasoning.High;
// ตัวเลือก: Off, Low, High
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

Property `EndpointPlatform` ให้สลับระหว่าง Alibaba Cloud และ endpoint ที่รองรับ:

```csharp
service.EndpointPlatform = EndpointPlatform.AlibabaCloud;
```
