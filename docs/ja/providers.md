# プロバイダー固有機能

## OpenAI (OpenAIService)

### 推論レベル

応答速度と分析の深さのバランスを調整します:

```csharp
using Mythosia.AI.Models;

// GPT-5.6: Sol は最上位モデルで、Terra と Luna は低コストの選択肢です。
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4シリーズ
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2シリーズ
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### テキスト音声変換 (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "こんにちは！",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### 音声テキスト変換 (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "ja"  // オプション、ISO-639-1
);
```

### 画像生成

```csharp
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "夜の未来都市",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### トークンカウント（ネイティブAPI）

`GetInputTokenCountAsync`はすべてのプロバイダーで利用可能です（[基本的な補完](completions.md#トークンカウント)参照）。Anthropicの実装は公式の`messages/count_tokens`エンドポイントを呼び出し、ローカル推定の代わりに**正確な**トークン数を返します:

```csharp
uint tokens = await service.GetInputTokenCountAsync("プロンプト内容");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### 思考レベル

Geminiが実行する内部推論の量を制御します:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// オプション: Disabled, Low, Medium, High
```

高いレベルほどより徹底したレスポンスを生成しますが、遅延とトークン使用量が増加します。

---

## xAI (XAIService)

### 推論モード

```csharp
using Mythosia.AI.Models;

service.ReasoningEffort = GrokReasoning.High;
// オプション: Auto, None, Low, Medium, High（モデル依存）
```

---

## Perplexity (PerplexityService)

### 引用付きウェブ検索

Sonarモデルはウェブを検索してレスポンスと共にソースの引用を返すことができます:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "核融合エネルギーの最新動向は？",
    domainFilter: new[] { "nature.com", "science.org" },  // オプション
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"出典: {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

別パッケージをインストールします:

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

利用可能なモデル: `QwenMax`、`QwenPlus`、`QwenTurbo`、`Qwen3`およびバリアント。

サービスの作成時に `EndpointPlatform` で互換エンドポイントを選択します:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
