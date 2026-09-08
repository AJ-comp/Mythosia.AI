# 진행 중인 AI 작업 제어하기

> 이 API는 `Mythosia.AI` 7.1.0 이상에서 사용할 수 있으며, `Mythosia.AI.Abstractions` 3.1.0 이상이 함께 포함됩니다. RAG 예제는 `Mythosia.AI.Rag` 7.6.0 이상이 필요합니다.

## 작업이 끝나기 전에 제어가 필요한 이유

보고서를 완성하려면 문서를 여러 번 검색하고, API를 조회하고, 내용을 작성하는 과정을 거칠 수 있습니다. 그동안 사용자는 진행 상황을 보거나, 작업을 중단하거나, “올해 자료만 포함해줘”처럼 조건을 추가하고 싶을 수 있습니다. 앱에는 이런 행동을 이미 진행 중인 작업에 연결할 방법이 필요합니다.

Run은 앱에서 보관하며 제어할 수 있는 실행 객체를 제공합니다. 예를 들어 채팅 화면에서 도착한 텍스트를 표시하고, 도구 사용 상태를 보여주고, 중지 버튼을 연결하고, 지원되는 모델에 추가 지시를 보낼 수 있습니다. 이 모든 동작이 같은 실행을 대상으로 합니다.

| 앱에서 필요한 동작 | 사용할 API |
| --- | --- |
| 진행 중인 작업을 제어하지 않고 완성된 답변 받기 | 제네릭·RAG 오버로드를 포함한 `GetCompletionAsync`를 사용합니다. |
| 텍스트를 바로 표시하고 완료 후 누적 결과 받기 | `onText`와 함께 run을 시작하고 `run.Result`를 기다립니다. |
| 도구 사용 상태 표시 또는 비동기 출력 처리 | `run.StreamAsync()`에서 이벤트를 읽습니다. |
| 사용자 요청으로 진행 중인 작업 중지 | 보관한 실행 객체의 `run.Cancel()`을 호출합니다. |
| 작업이 끝나기 전에 조건 추가 | 지원 모델에서 `run.CanSteer`를 확인하고 `run.SteerAsync(...)`를 호출합니다. |

`StartRunAsync`는 모델 작업을 시작하고 `AIRun`을 반환합니다. 출력을 읽지 않아도 작업은 계속 진행됩니다. 같은 객체에서 스트리밍, 누적 결과, 취소, 지원되는 모델의 작업 중 추가 지시를 제어합니다. 제네릭·RAG 오버로드를 포함한 `GetCompletionAsync`는 최종 결과만 필요한 사용자를 위한 공개 편의 API로 유지합니다.

## 콜백으로 간단하게 출력하기

채팅 화면이나 콘솔에서 텍스트가 도착하는 대로 표시하면 사용자는 긴 답변이 작성되는 동안 내용을 읽을 수 있습니다.

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "문서를 참고해서 보고서를 작성해줘.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

string answer = await run.Result;
```

`onText`는 작업 시작 전에 등록되는 선택적 `Action<string>` 콜백입니다. 텍스트가 순서대로 도착하며 도구 실행은 라이브러리가 처리합니다. 결과만 필요하면 콜백을 생략합니다. 콜백에서 예외가 발생하면 실행을 취소하고 `Result`에도 예외를 전달합니다. `onText`에 `async` 람다를 전달하면 run이 작업 완료와 오류를 기다릴 수 없는 `async void`가 되므로 사용하지 마세요. 비동기 출력 처리는 이벤트 스트림에서 수행합니다. 콜백을 UI 스레드로 자동 전환하지는 않습니다.

`Result`는 실행 중 전달된 텍스트 이벤트를 연결한 결과입니다. 도구 호출 사이의 중간 출력과 추가 지시 이전의 출력도 포함하며, 답변을 새로 생성하는 두 번째 요청이 아닙니다. 기존 완료 API의 결과 의미가 필요한 경우에는 계속 `GetCompletionAsync`를 사용할 수 있습니다.

## 텍스트·도구·사용량 이벤트 읽기

최신 웹 정보나 공급자가 관리하는 문서가 필요한 답변에는 Run 시작 전에 [추론·검색 옵션](reasoning-and-search.md)을 붙입니다. 출처 이벤트는 `StreamingContentType.Citation`으로 전달되며, 이벤트 리더 없이도 `run.Citations`에 출처가 남습니다.

문서를 검색하거나 업무 API를 조회할 때는 텍스트만으로 기다리는 이유가 드러나지 않을 수 있습니다. 이벤트 종류를 구분해서 읽으면 도구 사용 상태를 답변과 함께 표시하고, provider가 제공하는 사용량도 기록할 수 있습니다.

```csharp
await using var run = await service.StartRunAsync(
    "관련 문서를 검색하고 결과를 설명해줘.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[도구 호출 중]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[도구 결과 도착]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[전체 토큰: {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = await run.Result;
```

`run.StreamAsync()`는 선택적인 관찰 취소 토큰만 받고 새 질문은 받지 않습니다. 이미 `StartRunAsync`로 시작한 작업의 출력을 읽습니다. 등록한 함수는 라이브러리가 실행하므로 도구 호출 이벤트를 받았다고 다시 실행하면 안 됩니다. 텍스트만 표시하도록 선택해도 run의 도구 실행은 꺼지지 않습니다.

시작 콜백과 `run.StreamAsync()`는 같은 run을 함께 관찰할 수 있으며 이벤트 스트림의 독자는 하나입니다. 예를 들어 `onText`에서 텍스트를 표시하고 스트림에서는 도구 이벤트만 처리하면 텍스트를 두 번 표시하지 않습니다. 콜백이 있어도 읽지 않은 이벤트는 최대 1,024개를 보관합니다. 이 범위 안에서 늦게 읽기 시작하면 처음부터 받을 수 있지만, 한도를 넘으면 스트림 관찰만 명시적으로 실패하고 콜백·작업·`Result`는 계속 진행됩니다. 무제한 재생 기록으로 사용하지 마세요. `Result`를 받기 위해 이벤트 스트림을 끝까지 읽을 필요는 없습니다.

## 비동기 출력

비동기 출력은 `onText` 대신 스트림을 읽는 반복문 안에서 `await`합니다.

```csharp
using var writer = new StreamWriter("report.txt");
await using var run = await service.StartRunAsync(
    "보고서를 작성해줘.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = await run.Result;
```

## 취소와 해제

- `await foreach`를 중단하거나 `run.StreamAsync(token)`에만 전달한 토큰을 취소하면 출력 관찰만 중단하고 작업은 계속합니다.
- `run.Cancel()`, `StartRunAsync`에 전달한 토큰, 실행 중인 run의 해제는 작업을 취소합니다.
- `await using`의 `DisposeAsync()`는 출력 생성 작업과 provider 정리를 기다립니다. 취소를 지원하지 않는 도구는 완료까지 시간이 걸릴 수 있으며 해제가 완료된 행동을 되돌리지는 않습니다.
- 서비스 하나에서는 `StartRunAsync` 작업 하나만 동시에 실행할 수 있습니다. 겹치는 시작은 거부합니다. 독립적인 동시 작업에는 별도 서비스를 사용하고, run 실행 중에는 기존 API를 혼용하거나 서비스 설정을 변경하지 마세요.

run은 백그라운드 실행 전에 입력과 호출별 정책을 복사합니다. 기본 텍스트·이미지·오디오 콘텐츠와 미디어 바이트 배열도 복사합니다. 사용자 정의 `MessageContent` 하위 타입은 원래 객체를 유지하므로 run이 끝날 때까지 변경하지 않아야 합니다.

시작 시 복사한 `FunctionCallingPolicy.TimeoutSeconds`는 run 준비부터 모든 모델·도구 라운드까지 하나의 제한 시간으로 적용합니다. 시간 초과는 `AIServiceException`으로 알리고, 사용자 취소는 결과를 취소 상태로 만듭니다. 취소를 지원하지 않는 핸들러의 정리는 완료될 때까지 기다립니다.

## 작업 중 추가 지시 보내기

사용자가 프로젝트 계획 작성을 요청한 뒤 “일정은 2주 안으로 맞춰줘”라는 조건을 떠올렸다고 가정해 보겠습니다. 추가 지시 기능은 모델이 아직 작업 중일 때 그 조건을 전달할 수 있게 합니다. 오래 걸리는 작업을 보면서 오류를 정정하거나 범위를 조정할 때 유용합니다. 작업이 끝난 뒤 새 질문을 하려면 평소처럼 다음 요청을 시작합니다.

```csharp
await using var run = await service.StartRunAsync(
    "프로젝트 계획을 작성해줘.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// run 실행 중 UI의 추가 지시 처리기에서 호출합니다.
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("추가 지시를 지원하지 않는 실행입니다.");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = await run.Result;
```

작업 중 추가 지시는 Responses WebSocket 연결을 사용하는 GPT-6 Astra에서 지원합니다. 다른 provider와 미지원 모델도 일반 run은 사용할 수 있지만 `CanSteer`는 false이고, 추가 지시를 일반적인 다음 대화로 바꾸어 보내지 않고 지원 불가를 알립니다. `CanSteer`가 true라고 나중에 호출하는 시점까지 작업이 실행 중이라는 보장은 없습니다.

Astra run은 전용 소켓을 엽니다. 전달한 `HttpClient`와 메시지 핸들러는 기존 HTTP 호출에 사용되며 이 소켓을 가로채지 않습니다. 사용자 정의 전송이 필요하면 `OpenAIService.ConnectRunWebSocketAsync`를 재정의할 수 있습니다.

`SteerAsync` 성공은 서버가 입력을 대기열에 접수했다는 뜻이며 모델이 이미 반영했다는 뜻은 아닙니다. 이어지는 응답까지 같은 run의 출력이나 결과를 기다립니다. 이미 전달한 텍스트와 완료한 행동은 되돌리지 않으며 추가 지시 자체가 실행 중인 도구를 취소하지도 않습니다. 라이브러리가 같은 연결에서 후속 응답과 도구 결과 연결을 처리합니다. OpenAI의 [추가 지시 안내](https://developers.openai.com/api/docs/guides/steering)와 [WebSocket 안내](https://developers.openai.com/api/docs/guides/websocket-mode)를 참고하세요. 연결이 끊겼을 때 대기 중인 지시가 보존되었다고 가정하거나 접수한 지시를 확인 없이 재전송하면 안 됩니다.

## 도구 작업과 기존 에이전트 메서드

“환불 정책과 이 주문의 상태를 확인해줘” 같은 질문에는 여러 자료가 필요합니다. 문서 검색과 주문 조회 도구를 등록하면 모델이 필요한 호출을 선택합니다. 라운드 제한을 두면 도구를 계속 요청할 수 있는 횟수를 제한하고, 완료하거나 오류를 알리도록 할 수 있습니다.

일반 함수 호출도 모델과 도구를 여러 라운드에 걸쳐 실행합니다. `StartRunAsync`는 같은 등록 함수와 실행 정책을 사용하며, 별도 에이전트 모드·계획 엔진·`WithAgentic` 스위치는 필요하지 않습니다.

`RunAgentAsync`와 `RunAgentStreamAsync`는 계속 호출할 수 있지만 `[Obsolete]` 경고를 표시합니다. 전환 기간에는 기존 시그니처, 기본 `maxSteps = 10`, 기존 한도 초과 오류 동작을 보존합니다. 새 호출부는 다음과 같이 작성합니다.

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "정책을 찾고 주문 상태를 확인해서 결과를 설명해줘.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

일반 `FunctionCallingPolicy.MaxRounds` 기본값은 20이므로 기존 에이전트의 기본 한도를 유지하려면 10을 지정합니다. `WithMaxRounds`는 다음 호출 한 번에 적용할 정책을 설정하며 `DefaultPolicy`는 바꾸지 않습니다. 실행 전에 구성하세요. 기존 에이전트 메서드는 기본 정책을 복제하여 호출별 `maxSteps`를 적용합니다. 새 run은 공통 실행 오류 계약을 사용하며 기존 `AgentMaxStepsExceededException`과 `PartialResponse` 변환을 보장하지 않습니다. 해당 계약에 의존한다면 오류 처리까지 이관한 후 기존 호출을 교체하세요.

## RAG·MCP와 패키지 경계

- `RagEnabledService.StartRunAsync`는 문자열 또는 `Message`, `onText`, 호출별 `RagQueryOptions`, `streamOptions`, 취소 토큰을 받습니다. 내부 run을 시작하기 전에 검색하고, 이미지·오디오·메타데이터와 대화 기록의 원본 입력을 보존하면서 문맥을 보강한 텍스트를 요청 컨텍스트로 전달합니다. 문맥 보강은 원본 사용자 질문에만 적용하므로 이후 도구 결과와 추가 지시를 원래 RAG 프롬프트로 덮어쓰지 않습니다. 반환된 run의 추가 지시는 모델에 전달되며 RAG 검색을 자동으로 다시 실행하지 않습니다.
- `WithAgenticRag`는 검색 도구 등록 기능으로 유지합니다. 등록한 도구를 `StartRunAsync`에서 사용하면 모델이 필요에 따라 후속 검색을 요청할 수 있습니다. `WithMcpServerAsync`를 통한 MCP 도구 등록도 유지하며 공유 MCP 연결은 run과 별도로 해제합니다.
- `IAIRunService`는 `Mythosia.AI.Abstractions`의 선택적 기능 계약입니다. `IAIService`에 새 필수 멤버를 추가하지 않습니다. 사용자 서비스가 RAG run 시작을 지원하려면 `IAIRunService`를 구현해야 하며, 미지원 서비스는 RAG 인덱싱 전에 거부합니다.
- RAG의 Abstractions 의존성과 별도 provider 패키지의 공개 생성 재정의·접근 가능한 확장 지점은 유지합니다. 벡터 저장소·문서 로더·서버 관리 API는 이번 변경의 폐기 대상이 아닙니다.

## 호환성과 다음 메이저 전환

| API | 현재 상태 |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | 인터페이스·provider·RAG 버전을 포함해 공개로 유지합니다. |
| `StartRunAsync` / `AIRun` | 공통 실행과 제어 API입니다. |
| `RunAgentAsync` / `RunAgentStreamAsync` | 경고를 표시하며 기존 동작은 호환용으로 유지합니다. |
| 입력을 받는 `service.StreamAsync`와 RAG `StreamAsync` | 이번 마이너에서 유지하고 다음 메이저에서 공개 API에서 제외할 예정입니다. |
| `run.StreamAsync()` | 새 입력 없이 이미 시작한 작업의 출력을 읽습니다. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | 기존 타입 지정 스트리밍 API를 유지합니다. 출력 전용 `Stream()`은 기존 서비스 요청 메서드와 다릅니다. |

다음 메이저에서 공개 스트리밍 진입점을 바꾸어도 실행 구현과 필요한 provider 확장 지점은 보존합니다. public 메서드를 private이나 protected로 바꾸면 구현을 남겨도 외부 소스·바이너리 호출자에게는 호환성이 깨지는 변경입니다. 메시지 체인, 일회성 호출, 요약, 질의 재작성, 재순위화는 기존 실행 메서드를 사용한다는 이유만으로 폐기하지 않습니다.
