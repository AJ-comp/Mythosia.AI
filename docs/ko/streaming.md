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

## 토큰 사용량

스트리밍이 완료되면 마지막 `Completion` 이벤트에 상세한 사용량 지표를 담은 `TokenUsage` 객체가 포함됩니다:

```csharp
await foreach (var content in service.StreamAsync("양자 컴퓨팅을 설명해 주세요"))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\n입력 토큰:  {content.Usage.InputTokens}");
        Console.WriteLine($"출력 토큰: {content.Usage.OutputTokens}");
        Console.WriteLine($"총 토큰:   {content.Usage.TotalTokens}");
    }
}
```

### TokenUsage 속성

| 속성 | 설명 |
|---|---|
| `InputTokens` | 입력/프롬프트의 토큰 수 |
| `OutputTokens` | 출력/완성의 토큰 수 |
| `TotalTokens` | 입력 + 출력 |
| `CachedInputTokens` | 캐시에서 제공된 토큰 (비용 절감) |
| `CacheCreationTokens` | 캐시에 기록된 토큰 (Anthropic) |
| `ReasoningTokens` | 내부 추론에 사용된 토큰 |
| `CacheHitRatio` | 캐시 적중률 (0.0–1.0) |
| `VisibleOutputTokens` | 추론을 제외한 출력 토큰 |

### 캐시 효율 확인

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"캐시 적중률: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"비캐시 입력: {content.Usage.NonCachedInputTokens}");
}
```

## StreamOptions 프리셋

`StreamOptions`는 스트림이 반환하는 내용을 제어하는 프리셋과 Fluent 빌더를 제공합니다:

```csharp
// 전체 기능 — 메타데이터, 함수 호출, 추론
await foreach (var c in service.StreamAsync("프롬프트", StreamOptions.FullOptions))
    Console.Write(c.Content);

// 최소 오버헤드 — 텍스트만, 메타데이터 없음
await foreach (var c in service.StreamAsync("프롬프트", StreamOptions.Minimal))
    Console.Write(c.Content);

// 함수 호출 시나리오
await foreach (var c in service.StreamAsync("프롬프트", StreamOptions.WithFunctions))
{ /* Text, FunctionCall, FunctionResult, Completion 처리 */ }
```

커스텀 조합을 위한 Fluent 빌더:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // 사고 과정 포함
    .WithMetadata()        // Completion에 모델 정보 포함
    .WithFunctionCalls();  // 스트림 중 함수 호출 활성화
```

## Stateless 스트리밍 (StreamOnceAsync)

대화 기록에 영향 없이 응답을 스트리밍합니다 — `AskOnceAsync`의 스트리밍 버전입니다:

```csharp
await foreach (var chunk in service.StreamOnceAsync("이것을 프랑스어로 번역해주세요"))
    Console.Write(chunk);
```

멀티모달 입력을 위한 `Message` 오버로드도 지원합니다:

```csharp
var message = MessageBuilder.Create().AddText("이것을 설명해주세요").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## 스트리밍 전 대화 요약

자동 요약 정책은 스트리밍 중에는 트리거되지 않습니다. `StreamAsync` 호출 전에 명시적으로 호출하세요:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("대화를 이어가겠습니다..."))
    Console.Write(chunk.Content);
```
