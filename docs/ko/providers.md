# 프로바이더별 기능

## OpenAI (OpenAIService)

### 추론 수준

GPT-5.x 및 o3 시리즈 모델은 추론 수준 제어를 지원합니다. 속도와 깊이를 트레이드오프하는 수준을 설정합니다:

```csharp
using Mythosia.AI.Models;

// GPT-5.4 시리즈
service.Model = AIModels.OpenAI.Gpt5_4;
service.ReasoningLevel = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 시리즈
service.Model = AIModels.OpenAI.Gpt5_2;
service.ReasoningLevel = Gpt5_2Reasoning.Medium;

// o3
service.Model = AIModels.OpenAI.O3;
service.ReasoningLevel = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### 텍스트 음성 변환 (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "안녕하세요!",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### 음성 텍스트 변환 (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "ko"  // 선택사항, ISO-639-1
);
```

### 이미지 생성

```csharp
// 이미지를 바이트로 받기
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "밤의 미래 도시",
    size: "1024x1024"
);

// 이미지를 URL로 받기
string imageUrl = await service.GenerateImageUrlAsync(
    prompt: "밤의 미래 도시",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### 토큰 계산

요청을 보내기 전에 토큰 사용량을 추정합니다:

```csharp
// 특정 프롬프트의 토큰 수 계산
uint tokens = await service.GetInputTokenCountAsync("프롬프트 내용");

// 현재 대화의 토큰 수 계산
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### 사고 수준

Gemini가 수행하는 내부 추론의 양을 제어합니다:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// 옵션: Disabled, Low, Medium, High
```

높은 수준일수록 더 철저한 응답을 생성하지만 지연 시간과 토큰 사용량이 증가합니다.

---

## xAI (XAIService)

### 추론 모드

```csharp
using Mythosia.AI.Models;

service.ReasoningMode = GrokReasoning.High;
// 옵션: Off, Low, High
```

---

## Perplexity (PerplexityService)

### 인용과 함께 웹 검색

Sonar 모델은 웹을 검색하고 응답과 함께 출처를 반환할 수 있습니다:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "핵융합 에너지의 최신 발전 동향은?",
    domainFilter: new[] { "nature.com", "science.org" },  // 선택사항
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"출처: {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

별도 패키지를 설치합니다:

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

사용 가능한 모델: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` 및 변형 모델들.

`EndpointPlatform` 속성으로 Alibaba Cloud와 호환 엔드포인트 간에 전환할 수 있습니다:

```csharp
service.EndpointPlatform = EndpointPlatform.AlibabaCloud;
```
