# プロバイダー固有機能

## OpenAI (OpenAIService)

### 推論レベル

GPT-5.xとo3シリーズモデルは推論レベル制御をサポートします。速度と深さのトレードオフレベルを設定します:

```csharp
using Mythosia.AI.Models;

// GPT-5.4シリーズ
service.Model = AIModels.OpenAI.Gpt5_4;
service.ReasoningLevel = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2シリーズ
service.Model = AIModels.OpenAI.Gpt5_2;
service.ReasoningLevel = Gpt5_2Reasoning.Medium;

// o3
service.Model = AIModels.OpenAI.O3;
service.ReasoningLevel = Gpt5Reasoning.High; // Minimal, Low, Medium, High
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
// 画像をバイトで取得
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "夜の未来都市",
    size: "1024x1024"
);

// 画像をURLで取得
string imageUrl = await service.GenerateImageUrlAsync(
    prompt: "夜の未来都市",
    size: "1024x1024"
);
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

service.ReasoningMode = GrokReasoning.High;
// オプション: Off, Low, High
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

`EndpointPlatform`プロパティでAlibaba Cloudと互換エンドポイント間を切り替えられます:

```csharp
service.EndpointPlatform = EndpointPlatform.AlibabaCloud;
```
