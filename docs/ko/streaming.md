# 스트리밍

## 기본 스트리밍

`StreamAsync`를 사용해 토큰이 생성되는 즉시 수신합니다:

```csharp
await foreach (var token in service.StreamAsync("이야기를 들려주세요"))
{
    Console.Write(token);
}
```

## 콘텐츠 타입을 포함한 스트리밍

`StreamAsync`는 텍스트와 타입 정보를 함께 담은 `StreamingContent` 객체를 반환할 수 있습니다:

```csharp
await foreach (var content in service.StreamAsync("양자 컴퓨팅을 설명해 주세요"))
{
    Console.Write(content.Content);
}
```

## 추론 스트리밍

추론 기능이 있는 모든 프로바이더(OpenAI, Claude, Gemini, Grok, DeepSeek)는 동일한 패턴을 공유합니다. 추론을 활성화한 `StreamOptions`를 전달합니다:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("풀어 주세요: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[생각 중] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

`StreamingContentType.Reasoning`은 모델의 내부 추론 과정을 담고, `StreamingContentType.Text`는 최종 답변을 담습니다.

## 구조화된 출력과 함께 스트리밍

실시간으로 텍스트를 스트리밍하면서 완료 후 역직렬화된 객체를 받습니다:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// 토큰이 도착하는 대로 UI에 스트리밍
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// 스트리밍 완료 후 파싱된 결과 가져오기
MyDto result = await run.Result;
```

## 스트리밍 전 대화 요약

자동 요약 정책은 스트리밍 중에는 트리거되지 않습니다. `StreamAsync` 호출 전에 명시적으로 호출하세요:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("대화를 이어가겠습니다..."))
    Console.Write(chunk.Content);
```
