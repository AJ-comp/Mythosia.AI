# 기본 완성

## 단일 턴

가장 간단한 사용법 — 메시지를 보내고 응답을 받습니다:

```csharp
var response = await service.GetCompletionAsync("프랑스의 수도는 어디인가요?");
Console.WriteLine(response); // 파리
```

## 시스템 프롬프트

모델에 역할이나 지침을 부여하는 시스템 프롬프트를 설정합니다:

```csharp
service.SystemPrompt = "당신은 간결한 어시스턴트입니다. 한 문장으로 답하세요.";

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
service.ClearMessages();
```

## 메시지 직접 구성

`MessageBuilder`를 사용해 메시지를 명시적으로 만들 수 있습니다:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("이 텍스트를 요약해 주세요: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 멀티모달 (이미지 입력)

비전을 지원하는 프로바이더는 텍스트와 함께 이미지 콘텐츠를 받을 수 있습니다:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("이 다이어그램은 무엇을 보여주나요?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 출력 길이와 온도 제어

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // 낮을수록 결정론적
```
