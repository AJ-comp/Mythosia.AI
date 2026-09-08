# 프로바이더별 기능

## OpenAI (OpenAIService)

> GPT-6 Astra 지원과 비동기 도구 호출은 `Mythosia.AI` 7.1.0부터 제공하며, 공통 타입은 `Mythosia.AI.Abstractions` 3.1.0에 포함됩니다.

초안과 검토 단계의 추론량을 바꾸거나 웹·문서를 근거로 답하려면 [공통 추론·검색 API](reasoning-and-search.md)를 사용합니다. 지원 범위 안에서 OpenAI·Anthropic·Google에 같은 호출 방식을 적용하며, 캐시 보존 변경과 출처 수집도 제공합니다. 아래의 공급자별 세부 설정도 계속 사용할 수 있습니다.

외부 도구의 결과를 기다리는 동안에도 독립적인 설명이나 준비 작업을 진행하고 싶을 때 Astra의 비동기 도구 호출을 사용할 수 있습니다. 예를 들어 날씨 조회와 일반 여행 준비물 안내를 함께 처리하는 상황입니다.

`FunctionDefinition.AllowAsync = true` 또는 `FunctionBuilder.WithAsync()`로 GPT-6 Astra의 Responses API에서 비동기 도구 호출을 선택적으로 허용합니다. 기본값은 `false`이며, 미지원 모델에서는 같은 핸들러의 결과를 기다립니다. 예제와 요청 수명은 [함수 호출 가이드](function-calling.md)를 참고하세요.

### 추론 수준

속도와 분석 깊이 사이의 균형을 조절합니다:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol은 최상위 모델이며, Terra와 Luna는 비용을 낮춘 선택지입니다.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4 시리즈
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 시리즈
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

GPT-6 Astra는 기본적으로 Responses API를 사용하며, 함수 호출에도 이 API가 필요합니다. `Auto`는 라이브러리 기본값인 `Medium`으로 적용되며 `None`과 `Minimal`은 지원하지 않습니다. `AIRequestProfile.DisableReasoning = true`는 `Standard` 모드에서 추론 수준을 `Low`로 낮추고 추론 요약을 생략합니다. `Gpt6ReasoningMode.Pro`를 선택하면 같은 모델 ID `gpt-6-astra`로 Pro 모드를 사용합니다.

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
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "밤의 미래 도시",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### 토큰 계산 (네이티브 API)

`GetInputTokenCountAsync`는 모든 프로바이더에서 사용 가능합니다([기본 완성](completions.md#토큰-계산) 참조). Anthropic 구현은 공식 `messages/count_tokens` 엔드포인트를 호출하여 로컬 추정 대신 **정확한** 토큰 수를 반환합니다:

```csharp
uint tokens = await service.GetInputTokenCountAsync("프롬프트 내용");
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

service.ReasoningEffort = GrokReasoning.High;
// 옵션: Auto, None, Low, Medium, High (모델별 상이)
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

서비스를 생성할 때 `EndpointPlatform`으로 호환 엔드포인트를 선택합니다:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
