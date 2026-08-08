# 기본 텍스트 생성

## 단발 질문

가장 간단한 사용법입니다 — 메시지를 보내고 응답을 받으면 됩니다:

```csharp
var response = await service.GetCompletionAsync("프랑스의 수도는 어디인가요?");
Console.WriteLine(response); // 파리
```

## 시스템 프롬프트

모델에 역할이나 지침을 부여하는 시스템 프롬프트를 설정합니다:

```csharp
service.SystemMessage = "당신은 간결한 어시스턴트입니다. 한 문장으로 답하세요.";

var response = await service.GetCompletionAsync("재귀를 설명해 주세요.");
```

## 멀티턴 대화

메시지는 자동으로 누적됩니다. `GetCompletionAsync`를 호출할 때마다 대화 기록에 추가됩니다:

```csharp
await service.GetCompletionAsync("제 이름은 앨리스입니다.");
var response = await service.GetCompletionAsync("제 이름이 뭔가요?");
// → "당신의 이름은 앨리스입니다."
```

대화 기록을 초기화하려면:

```csharp
service.ActivateChat.ClearMessages();
```

## 메시지 직접 구성

`MessageBuilder`를 사용해 메시지를 명시적으로 만들 수 있습니다:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("이 텍스트를 요약해 주세요: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 멀티모달 (이미지 입력)

비전을 지원하는 프로바이더는 텍스트와 함께 이미지 콘텐츠를 받을 수 있습니다:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("이 다이어그램은 무엇을 보여주나요?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 빠른 질문 (정적 API)

서비스 인스턴스 생성 없이 한 줄로 질문할 수 있습니다. 모델명에서 프로바이더가 자동 감지됩니다:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "프랑스의 수도는?",
    model: AIModels.OpenAI.Gpt4oMini  // 기본값
);
```

이미지 버전:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "이 이미지를 설명해주세요",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4_1
);
```

## 이미지 편의 메서드

`MessageBuilder` 없이 이미지를 분석합니다 — 파일 읽기와 MIME 타입 판별이 자동으로 처리됩니다:

```csharp
// 파일 경로에서
var response = await service.GetCompletionWithImageAsync(
    "이 다이어그램은 무엇을 보여주나요?", "diagram.png");

// URL에서
var response = await service.GetCompletionWithImageUrlAsync(
    "이 사진을 설명해주세요", "https://example.com/photo.jpg");
```

## 마지막 메시지 재시도

마지막 AI 응답을 제거하고 마지막 사용자 메시지를 다시 전송합니다:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

이전 응답이 만족스럽지 않을 때 모델에게 다시 시도하게 할 수 있습니다.

## 토큰 계산

요청을 보내기 전에 토큰 사용량을 추정합니다. **모든 프로바이더**에서 사용 가능합니다:

```csharp
// 현재 대화 기록의 토큰 수 계산
uint conversationTokens = await service.GetInputTokenCountAsync();

// 특정 프롬프트의 토큰 수 계산
uint promptTokens = await service.GetInputTokenCountAsync("프롬프트 내용");
```

OpenAI 및 대부분의 프로바이더는 로컬 TikToken 기반 추정을 사용합니다. Anthropic과 Google은 정확한 결과를 위해 네이티브 토큰 카운팅 API를 호출합니다.

## Fluent 메시지 체인

`BeginMessage()`는 텍스트, 이미지, 스트리밍, 정책 설정을 하나의 체인으로 빌드하고 전송하는 Fluent API를 제공합니다:

```csharp
// 텍스트 + 이미지 → 전송
string response = await service.BeginMessage()
    .AddText("이 다이어그램은 무엇을 보여주나요?")
    .AddImage("diagram.png")
    .SendAsync();

// 일회성 질문 (대화 기록에 영향 없음)
string answer = await service.BeginMessage()
    .AddText("이것을 한국어로 번역해주세요")
    .SendOnceAsync();

// 스트리밍
await service.BeginMessage()
    .AddText("봄에 대한 시를 써주세요")
    .StreamAsync(chunk => Console.Write(chunk));

// 커스텀 타임아웃과 정책
string result = await service.BeginMessage()
    .AddText("이 이미지를 분석해주세요")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()`는 `IAsyncEnumerable`도 지원합니다:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("이야기를 해주세요").StreamAsync())
    Console.Write(chunk);
```

## 출력 길이와 온도 제어

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // 낮을수록 결정론적
```
