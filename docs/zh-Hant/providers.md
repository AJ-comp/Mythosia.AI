# 各供應商特性

## OpenAI (OpenAIService)

> GPT-6 Astra 支援與非同步工具呼叫從 `Mythosia.AI` 7.1.0 開始提供，共用型別包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

天氣查詢較慢時，模型仍可先介紹不依賴天氣結果的一般旅行用品。模型原生非同步工具呼叫用於在這種等待期間繼續獨立工作；依賴查詢結果的判斷仍應等結果傳回後再進行。

透過 `FunctionDefinition.AllowAsync = true` 或 `FunctionBuilder.WithAsync()`，可選擇允許 GPT-6 Astra 在 Responses API 中非同步呼叫工具。預設值為 `false`；不支援的模型仍等待同一個處理常式的結果。範例與請求生命週期請參見[函式呼叫指南](function-calling.md)。

若要跨供應商設定推理等級，並使用最新資訊或已索引文件作為依據，請參閱[推理與搜尋指南](reasoning-and-search.md)，其中列出模型支援、快取保留及組合限制。

### 推理強度

GPT-6 Astra / GPT-5.x 和 o3 系列模型支援推理強度控制：

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6：Sol 是旗艦模型；Terra 與 Luna 是更經濟的選擇。
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

GPT-6 Astra 預設使用 Responses API，函式呼叫也必須使用此 API。`Auto` 對應函式庫預設值 `Medium`，不支援 `None` 和 `Minimal`。`AIRequestProfile.DisableReasoning = true` 會在 `Standard` 模式下將推理強度設為 `Low` 並省略推理摘要。選擇 `Gpt6ReasoningMode.Pro` 即可透過同一個模型 ID `gpt-6-astra` 使用 Pro 模式。

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
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "夜晚的未來城市",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
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

service.ReasoningEffort = GrokReasoning.High;
// 選項：Auto, None, Low, Medium, High（依模型而異）
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

建立服務時，使用 `EndpointPlatform` 選擇相容端點：

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
