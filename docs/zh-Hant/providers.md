# 各供應商特性

## OpenAI (OpenAIService)

### 推理強度

GPT-5.x 和 o3 系列模型支援推理強度控制：

```csharp
using Mythosia.AI.Models;

service.Model = AIModels.OpenAI.Gpt5_4;
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

service.Model = AIModels.OpenAI.Gpt5_2;
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

service.Model = AIModels.OpenAI.O3;
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### 文字轉語音

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "你好，世界！",
    voice: "alloy",
    model: "tts-1"
);
await File.WriteAllBytesAsync("output.mp3", audio);
```

### 語音轉文字（轉錄）

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");
string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "zh"
);
```

### 圖像生成

```csharp
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "夜晚的未來城市",
    size: "1024x1024"
);

string imageUrl = await service.GenerateImageUrlAsync(
    prompt: "夜晚的未來城市",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### Token 計數（原生 API）

Anthropic 的實作呼叫官方 `messages/count_tokens` 端點，回傳**精確**的 Token 數量：

```csharp
uint tokens = await service.GetInputTokenCountAsync("你的提示詞");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### 思考深度

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// 選項：Disabled, Low, Medium, High
```

---

## xAI (XAIService)

### 推理模式

```csharp
using Mythosia.AI.Models;

service.ReasoningMode = GrokReasoning.High;
// 選項：Off, Low, High
```

---

## Perplexity (PerplexityService)

### 帶引用的網路搜尋

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "核融合的最新進展有哪些？",
    domainFilter: new[] { "nature.com", "science.org" },
    recencyFilter: "week"
);

Console.WriteLine(result.Content);
foreach (var citation in result.Citations)
    Console.WriteLine($"來源：{citation.Url}");
```

---

## 阿里巴巴 / 通義千問 (QwenService)

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

可用模型：`QwenMax`、`QwenPlus`、`QwenTurbo`、`Qwen3` 及其變體。

```csharp
service.EndpointPlatform = EndpointPlatform.AlibabaCloud;
```
