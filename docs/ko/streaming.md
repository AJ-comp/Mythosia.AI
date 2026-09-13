# 스트리밍

완성된 답변과 사용량·출처를 함께 받아야 한다면 `await run.Result`가 반환하는 `AIRunResult`를 사용하세요. 문자열은 `result.Text`에 있으며 스트림을 읽지 않아도 결과를 모읍니다. Mythosia.AI 8.0.0의 API 변경이며 `GetCompletionAsync`와 타입 지정 `StructuredStreamRun<T>.Result`의 반환형은 유지합니다. [Run 결과와 전환 안내](execution-api-transition.md#run-result).


요청마다 설정을 분리하고 공통 요청에서 여러 변형을 만들려면 [요청 빌더](request-building.md)를 사용하세요. `CreateRequest(...)` 다음에 `With...`를 연결합니다. 서비스에 직접 지정하는 속성과 fluent 메서드는 기존 동작을 유지합니다.

긴 답변이 완성될 때까지 기다리면 사용자는 작업이 진행 중인지 알기 어렵습니다. 스트리밍으로 도착한 텍스트부터 표시하고, `StartRunAsync`가 반환한 실행 객체로 누적 결과와 중지를 함께 관리할 수 있습니다. 도구 이벤트와 추가 지시까지 필요한 경우에는 [Run 사용 안내](execution-api-transition.md)를 참고하세요.

```csharp
await using var run = await service.StartRunAsync(
    "긴 문서를 요약해줘.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

## 기존 입력형 스트리밍 API

입력을 받는 서비스·RAG StreamAsync는 v8에서도 공개로 유지합니다. 새 실행 제어에는 StartRunAsync를 사용하고 run.StreamAsync()는 이미 시작한 run의 출력만 관찰합니다.

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
await foreach (var content in service.StreamAsync("양자 컴퓨팅을 설명해 주세요", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## 추론 스트리밍

OpenAI, Claude, Gemini, Grok, DeepSeek Flash는 같은 스트리밍 패턴으로 제공자 추론을 반환합니다. 서비스 또는 요청에서 추론을 켠 뒤 `StreamOptions.WithReasoning()`으로 관찰하세요:

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

Gemini 3.7/3.8 Flash도 기존 스트리밍과 Run 이벤트를 사용합니다. `StreamingContentType.Reasoning`에는 공급자가 반환한 요약이나 진행 안내가 담기며, 전체 내부 추론의 공개를 보장하지 않습니다. `StreamOptions.WithReasoning()`은 이 출력을 선택하고, 서비스의 `WithReasoning(ReasoningLevel...)`은 추론 강도를 설정합니다.

Grok 4.6도 공급자가 선택적으로 제공하는 추론 요약을 같은 이벤트로 전달합니다. 스트림 옵션은 표시할 출력을 선택하고 `WithReasoning(ReasoningLevel...)`은 한 작업의 추론 수준을 정합니다. 요약이 없다고 추론이 꺼진 것은 아닙니다. [Grok 설정](providers.md#xai-xaiservice)을 참고하세요.

DeepSeek Flash는 추론을 켠 뒤 같은 이벤트로 `reasoning_content`를 제공합니다. `StreamOptions.WithReasoning()`은 관찰 여부를, `WithDeepSeekReasoning(...)` 또는 서비스의 `WithReasoning(...)`은 추론 동작을 제어합니다. [DeepSeek 설정](providers.md#deepseek-deepseekservice)을 참고하세요.

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
await foreach (var content in service.StreamAsync("양자 컴퓨팅을 설명해 주세요", StreamOptions.Default))
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
| `OutputTokens` | 출력 응답의 토큰 수 |
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

화면에 표시한 조각은 `run.Result`가 성공할 때까지 잠정적인 출력으로 취급하세요. 공통 OpenAI 호환 스트리밍 경로와 DeepSeek 스트리밍 경로에서는 명시적 종료 뒤에 새 텍스트·추론·도구 데이터가 오거나 종료 이유가 바뀌면 실패 처리합니다. 이때 `run.Result`는 예외를 던지고, 실패한 라운드는 대화 기록에 저장하지 않으며 해당 라운드의 도구도 실행하지 않습니다. 이 실패 처리는 이전 라운드나 이미 외부에서 실행된 동작을 되돌리지 않습니다. 첫 종료 이벤트에 마지막 조각이 함께 오거나 이후 사용량 정보만 오는 경우는 허용합니다.

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

await foreach (var chunk in service.StreamAsync("대화를 이어가겠습니다...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

## 스트리밍 진단 (Streaming Diagnostics)

스트리밍 도중 SSE 연결이 끊어지거나 응답이 비정상적으로 종료되는 경우, 어느 시점에서 무엇이 잘못됐는지를 추적하기 위한 진단 훅을 제공합니다. 자체 호스팅 vLLM, 사내 프록시, 불안정한 네트워크 환경에서 특히 유용합니다.

### 등록 방법

서비스에 한 번만 등록하면 이후 모든 `StreamAsync` 호출에 자동으로 적용됩니다. `WithRag` 와 동일한 빌더 패턴입니다.

```csharp
using Mythosia.AI.Extensions;

service.WithStreamDiagnostics(d => d
    .OnRawLine(line => logger.LogDebug("SSE: {Line}", line))
    .OnComplete(diag => logger.LogInformation("Stream finished: {Diag}", diag)));

// 이후 모든 스트리밍 호출에 자동 적용
await foreach (var chunk in service.StreamAsync(message, StreamOptions.Default))
    Console.Write(chunk.Content);
```

각 `On*` 메서드는 독립적입니다. 필요한 것만 호출하면 됩니다.

```csharp
// raw 라인 트레이스만 켜기
service.WithStreamDiagnostics(d => d.OnRawLine(line => logger.LogDebug("SSE: {Line}", line)));

// 콜백을 모두 비우기 (해제)
service.WithStreamDiagnostics(_ => { });
```

> **프로바이더 전환 시 (`CopyFrom`)**: 등록한 콜백은 `CopyFrom` 호출 시 새 인스턴스에 자동 인계됩니다. 외부 sink(`logger`, `metrics`)를 가리키는 콜백은 그대로 동작합니다. 단, 콜백이 service 인스턴스 자체를 closure로 캡처한 경우(예: `line => Log(service.Provider, line)`) 새 인스턴스에서도 원본 service를 참조하므로 주의하세요. 외부 자원만 캡처하는 패턴이 안전합니다.

### 사용 가능한 콜백

| 메서드 | 호출 시점 | 용도 |
|---|---|---|
| `OnRawLine(Action<string>)` | 모든 SSE 라인 수신 시 | Debug 트레이스 — 죽기 직전 마지막 라인이 짤렸는지, 비표준 포맷인지 확인 |
| `OnComplete(Action<StreamDiagnostics>)` | 스트림 종료 시 (정상/예외 모두) 1회 | 텔레메트리 — 라인 수, 누적 길이, 경과 시간을 telemetry로 보내기 |

### 예외 발생 시 진단 정보 추출

SSE 읽기 중 `IOException`이나 전송 오류가 발생하면 라이브러리는 `StreamReadException`으로 wrap해서 throw합니다. `Diagnostics` 속성에서 실패 시점의 상태를 그대로 확인할 수 있습니다 — 이 경로는 `WithStreamDiagnostics` 등록 여부와 무관하게 항상 동작합니다.

```csharp
try
{
    await foreach (var chunk in service.StreamAsync(message, StreamOptions.Default))
        Console.Write(chunk.Content);
}
catch (StreamReadException ex)
{
    logger.LogError(ex,
        "Stream died after {Lines} lines, {Chars} chars. Last raw line: {Line}",
        ex.Diagnostics.LinesRead,
        ex.Diagnostics.AccumulatedTextLength,
        ex.Diagnostics.LastRawLine);

    // ex.InnerException 에 원인 예외(IOException 등)가 들어 있습니다.
}
```

### `StreamDiagnostics` 필드

| 필드 | 의미 |
|---|---|
| `LinesRead` | 수신한 SSE 라인 총 개수 (빈 줄, 주석 포함) |
| `DataLinesProcessed` | 청크 파서가 콘텐츠로 받아들인 라인 수 |
| `ParseFailures` | JSON 파싱에 실패한 라인 수 (조용히 스킵된 항목 포함) |
| `AccumulatedTextLength` | 누적된 어시스턴트 텍스트의 총 문자 수 |
| `LastRawLine` | 가장 최근에 수신한 raw SSE 라인 — 스트림이 라인 중간에 끊긴 경우 짤린 라인을 그대로 노출 |
| `Elapsed` | 스트림 읽기에 소요된 시간 |

### 자체 호스팅 백엔드 진단 시나리오

vLLM, ollama 등 자체 호스팅 환경에서 "turn 1은 정상인데 turn 2부터 간헐 실패" 같은 패턴을 만나셨다면 다음 절차를 권장드립니다.

1. `WithStreamDiagnostics(d => d.OnRawLine(...))` 로 Debug 레벨 트레이스 등록 후 재현
2. `StreamReadException` 발생 시 `Diagnostics.LastRawLine`과 `ex.InnerException.GetType().FullName`을 로그
3. 서버 로그(200 OK인데 응답 끊김)와 클라이언트의 마지막 수신 라인을 비교

이 정보가 있으면 "서버는 정상 송출을 마쳤는데 클라이언트가 라인 중간에 끊겼다"는 식으로 원인 위치를 빠르게 좁힐 수 있습니다.

Perplexity: [오래 걸리는 작업 계속 실행하기 / 인용은 웹 검색 결과나 다른 제공자 출처를 나타낼 수 있습니다. 위치 값은 제공자 응답의 개별 콘텐츠 기준이며 Run 누적 결과의 위치가 아닙니다. 표시와 확인에 URL·제목을 사용하세요. 출처가 반환됐다는 사실만으로 생성된 모든 주장이 검증되는 것은 아닙니다.](perplexity.md).
