# 各提供商特性

## OpenAI (OpenAIService)

### 推理强度

GPT-5.x 和 o3 系列模型支持推理强度控制。通过设置级别在速度和深度之间取舍：

```csharp
using Mythosia.AI.Models;

// GPT-5.6：Sol 是旗舰模型；Terra 和 Luna 是更经济的选择。
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4 系列
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 系列
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### 文本转语音

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "你好，世界！",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### 语音转文本（转录）

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "zh"  // 可选，ISO-639-1
);
```

### 图像生成

```csharp
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "夜晚的未来城市",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### Token 计数（原生 API）

`GetInputTokenCountAsync` 在所有提供商上均可使用（参见[文本生成](completions.md#token-计数)）。Anthropic 的实现调用官方 `messages/count_tokens` 端点，返回**精确**的 Token 数量而非本地估算：

```csharp
uint tokens = await service.GetInputTokenCountAsync("你的提示词");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### 思考深度

控制 Gemini 进行多少内部推理：

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// 选项：Disabled, Low, Medium, High
```

更高级别产生更深入的响应，但会增加延迟和 Token 使用量。

---

## xAI (XAIService)

### 推理模式

```csharp
using Mythosia.AI.Models;

service.ReasoningEffort = GrokReasoning.High;
// 选项：Auto, None, Low, Medium, High（取决于模型）
```

---

## Perplexity (PerplexityService)

### 带引用的网络搜索

Sonar 模型可以搜索网络并在响应中返回来源引用：

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "核聚变的最新进展有哪些？",
    domainFilter: new[] { "nature.com", "science.org" },  // 可选
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"来源：{citation.Url}");
}
```

---

## 阿里巴巴 / 通义千问 (QwenService)

安装独立包：

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

可用模型：`QwenMax`、`QwenPlus`、`QwenTurbo`、`Qwen3` 及其变体。

创建服务时，使用 `EndpointPlatform` 选择兼容端点：

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
