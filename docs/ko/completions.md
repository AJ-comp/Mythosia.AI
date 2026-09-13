# 기본 텍스트 생성

요청마다 설정을 분리하고 공통 요청에서 여러 변형을 만들려면 [요청 빌더](request-building.md)를 사용하세요. `CreateRequest(...)` 다음에 `With...`를 연결합니다. 서비스에 직접 지정하는 속성과 fluent 메서드는 기존 동작을 유지합니다.

질문을 보내고 작업이 끝난 뒤 답변을 받아 처리하려면 `GetCompletionAsync`를 사용합니다. 제네릭·RAG 오버로드도 계속 지원합니다. 진행 상황 표시나 작업 중 제어가 필요해지면 [Run 사용 안내](execution-api-transition.md)에서 사용 방식을 선택하세요.

<a id="completion-cancellation"></a>

## 더 이상 필요하지 않은 답변 취소하기

사용자가 화면을 닫거나 중지 버튼을 누르면, 또는 앱이 정한 대기 시간이 지나면 답변이 더 이상 필요하지 않을 수 있습니다. `CancellationToken`을 전달하면 우리 쪽 통신과 작업을 중단하고 불필요한 도구 호출과 다음 모델 호출을 막을 수 있습니다. 완성된 답변은 계속 `GetCompletionAsync`로 받으며, 취소만 필요하다면 Run을 만들 필요가 없습니다.

### Before: 호출자가 취소 신호를 전달하지 않음

```csharp
string answer = await service.CreateRequest("이 문서를 요약해줘.")
    .GetCompletionAsync();
```

### After: 사용자 요청 또는 30초 뒤 취소

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("이 문서를 요약해줘.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("취소되었습니다.");
}
```

호출이 진행되는 동안 토큰 소스를 보관하고, 중지 버튼이나 화면 닫기 이벤트에서 `cancellation.Cancel()`을 호출하세요. 예제는 30초 뒤 취소도 예약합니다. 호출자 취소는 정리가 끝난 뒤 `OperationCanceledException`으로 전달됩니다. `CancellationTokenSource`로 정한 대기 시간도 호출자 취소에 해당하며, 기존 `FunctionCallingPolicy.TimeoutSeconds` 정책은 기존 시간 초과 오류 동작을 유지합니다.

서비스의 문자열·`Message` 오버로드, 제네릭 완료 호출, 요청 빌더, `MessageChain.SendAsync` / `SendOnceAsync`에서 토큰을 받을 수 있습니다. 토큰을 생략한 기존 호출도 유지됩니다. 다음은 같은 기능을 쓰는 다른 진입점입니다.

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "이 문서를 요약해줘.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "제목과 저자를 JSON으로 반환해줘.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("이 문서를 요약해줘.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("이 문장을 번역해줘.")
    .SendOnceAsync(cancellationToken: token);
```

토큰은 요청 준비, HTTP 전송·응답 읽기, 취소에 협조하는 로컬 도구와 다음 모델 호출까지 전달됩니다. 취소를 확인하면 대기 중인 도구와 다음 라운드를 건너뜁니다. 기록한 도구 호출과 결과가 짝을 이루도록 정리하므로, 토큰을 무시한 채 이미 실행 중인 도구는 정리를 지연시킬 수 있습니다. 완료한 행동을 되돌리거나 대화 기록을 지우지는 않습니다. [도구 실행 계약](function-calling.md#tool-execution-contract)을 참고하세요.

공급자 서버의 생성이나 과금 중단까지 보장하지는 않습니다. OpenAI는 일반 Responses 요청의 연결 종료를 취소 방법으로 안내하고, Google은 클라이언트 취소가 서버 요청을 취소하지 않으며 해당 사용량은 과금된다고 명시합니다. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). 백그라운드 작업 자체는 명시적으로 `CancelAsync()`로 취소합니다. `WaitForCompletionAsync(cancellationToken: ...)`의 취소는 대기만 중단합니다. 일반 완료 요청을 백그라운드 실행으로 바꾸지는 않습니다. [Perplexity 백그라운드 작업](perplexity.md)을 참고하세요.

<a id="completion-cancellation-migration"></a>

이 취소 기능은 Mythosia.AI 8.0.0에 포함됩니다. 토큰을 생략한 앱 호출과 기존 profile/context 위치 인자 호출은 소스 수준에서 유지되지만, 사용하는 패키지는 다시 빌드해야 합니다. 사용자 정의 `IAIService` 구현은 두 완료 메서드의 마지막에 `CancellationToken cancellationToken = default`를 추가하고 전달해야 합니다. `AIService`를 상속한 사용자 제공자는 기존 `GetCompletionAsync(Message)` override를 유지하며 protected `RequestCancellationToken`을 통신에 전달해야 합니다. 빌더와 Run 자체에는 이 인터페이스 변경이 필요하지 않았습니다. 문자열·profile/context 완료 호출, 이미지 편의 메서드, `RunAgentAsync` 등 변경된 public virtual 오버로드를 재정의한 하위 클래스도 새 `CancellationToken` 매개변수를 추가하고 전달해야 합니다. 기존 시그니처를 유지하는 것은 `Message` 하나를 받는 제공자 override입니다. 변경된 메서드를 델리게이트에 직접 대입한 코드는 토큰을 전달하거나 생략하는 명시적 람다로 바꿔야 할 수 있습니다.

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

차트·스크린샷 분석, 로컬 함수 호출, 빠른 답변 뒤의 깊은 검토에는 [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash)를 사용할 수 있습니다. 추론은 기본적으로 꺼져 있으며 `WithDeepSeekReasoning(...)` 또는 요청별 `WithReasoning(...)`으로 켭니다.

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

Perplexity: [Agent 프리셋으로 답변하기 / 출처, 이미지와 구조화된 답변](perplexity.md).
