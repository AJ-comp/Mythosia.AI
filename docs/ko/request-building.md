# 요청마다 설정을 독립적으로 관리하기

문서 요약에는 낮은 Temperature가 필요하고 창작 초안에는 높은 값이 필요할 수 있습니다. 초안을 준비했다고 이미 준비한 요약 요청의 설정까지 바뀌어서는 안 됩니다. 호출마다 다른 설정이 필요하거나, 공통 요청에서 여러 변형을 만들 때 `CreateRequest`를 사용하세요.

완성된 답변과 사용량·출처를 함께 받아야 한다면 `await run.Result`가 반환하는 `AIRunResult`를 사용하세요. 문자열은 `result.Text`에 있으며 스트림을 읽지 않아도 결과를 모읍니다. Mythosia.AI 8.0.0의 API 변경이며 `GetCompletionAsync`와 타입 지정 `StructuredStreamRun<T>.Result`의 반환형은 유지합니다. [Run 결과와 전환 안내](execution-api-transition.md#run-result).

완성된 답변과 중지 버튼만 필요하면 `GetCompletionAsync`에 `cancellationToken`을 전달하세요. 진행 이벤트나 지원 모델의 추가 지시에는 Run을 사용합니다. [일반 응답 취소](completions.md#completion-cancellation)를 참고하세요.

> `CreateRequest` 예제는 Mythosia.AI 8.0.0 / Abstractions 4.0.0의 기능입니다. Run과 공통 요청 기능이 처음 추가된 이전 7.1 버전에는 빌더가 없습니다. 이전 패키지에서는 기존 서비스 오버로드를 사용하세요.

## Before: 서비스 하나의 설정을 공유

기존 서비스의 `WithTemperature`는 서비스 설정을 변경하고 같은 인스턴스를 반환합니다. 아래 두 변수는 같은 서비스를 가리키므로 나중에 지정한 값이 둘 다에 적용됩니다. 이 기존 메서드들은 서비스 기본값을 설정하는 용도로 계속 사용할 수 있습니다.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("이 문서를 설명해줘."); // 0.8
```

## After: 독립적인 요청에서 분기

`CreateRequest`는 서비스 기본값을 가져와 보관합니다. 빌더의 모든 `With...`는 원본을 변경하지 않고 새 빌더를 반환합니다. 실행할 때도 해당 요청에 보관된 설정을 사용하며, 서비스 기본값을 잠시 덮어쓰는 방식으로 적용하지 않습니다.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("이 문서를 설명해줘.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// 0.2 적용. creative와 서비스 기본값은 그대로 유지됩니다.
```

반환된 빌더를 사용해야 합니다. `basis.WithTemperature(0.2f);`만 호출하고 반환값을 버리면 `basis`는 바뀌지 않습니다.

빌더는 값을 조용히 보정하지 않고 검증합니다. `WithTemperature`는 0–2, `WithTopP`는 0–1, 패널티는 −2–2이며 NaN과 무한대는 거부합니다. 토큰 수, 라운드 수, 동시 처리 수, 지정한 제한 시간은 양수여야 합니다. 잘못된 입력은 `ArgumentException` / `ArgumentOutOfRangeException`을 발생시킵니다. 기존 서비스의 Temperature 메서드는 범위를 보정하는 동작을 유지합니다.

## 각 객체의 역할

`AIService`는 제공자 연결, 기본값, 기존 대화 상태를 관리합니다. 공개 타입 `Mythosia.AI.Builders.AIRequestBuilder`가 fluent 사용법을 제공하고, 내부 타입 `AIRequest`가 확정된 입력과 설정을 실행부에 전달합니다. 사용자가 `Build()`를 호출할 필요는 없습니다. `AIRequest`가 답변으로 반환되는 것도 아닙니다. `GetCompletionAsync()`는 `Task<string>`, `StartRunAsync()`는 `Task<AIRun>`을 반환합니다.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## 같은 설정으로 제어 가능한 실행 시작하기

완성된 답변만 필요하면 `GetCompletionAsync()`를 사용합니다. 진행 상황 표시, 지원되는 실행의 중간 지시가 필요하면 `StartRunAsync()`를 사용합니다. 입력은 `CreateRequest`에 전달하며 빌더의 실행 메서드에 다시 전달하지 않습니다. `run.StreamAsync()`는 그 실행을 관찰하고, `run.SteerAsync(...)`의 모델별 지원 조건은 그대로 유지됩니다.

```csharp
await using var run = await service
    .CreateRequest("이 문서를 설명해줘.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

로컬 도구는 `Task<T>` / `ValueTask<T>`로 객체를 반환하고 라이브러리가 주입하는 `CancellationToken`을 받을 수 있습니다. `run.Cancel()`이나 시작 토큰의 취소는 이를 사용하는 도구에도 전달되며, 스트림 읽기만 멈추는 것은 실행 취소가 아닙니다. 예외는 실패로 기록합니다. 취소 시 대기 중인 호출은 건너뛰고, 토큰을 무시한 채 이미 실행 중인 도구는 정리 과정에서 완료를 기다립니다. [도구 반환값·오류·취소 안내](function-calling.md#tool-execution-contract)를 참고하세요.

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## 기존 프로파일과 컨텍스트 재사용

`WithProfile`은 기존 `AIRequestProfile`을, `WithContext`는 `AIRequestContext`를 복사해 보관합니다. 이후 원본 객체를 변경해도 준비된 요청에는 반영되지 않습니다. 샘플링, 시스템 지시, 무상태 모드, 함수 호출 정책, 지원되는 추론·웹 검색·파일 검색을 빌더로 설정할 수 있습니다. 제공자별 기능 검증은 계속 적용되므로 빌더를 사용한다고 미지원 옵션을 사용할 수 있게 되지는 않습니다.

`WithFunctions(params FunctionDefinition[])`는 복사한 함수 정의를 요청에 추가합니다. `Mythosia.AI.Extensions`를 사용하면 `WithFunctions(toolInstance)`와 `WithStaticFunctions<T>()`로 기존 특성 기반 함수도 등록할 수 있습니다. 서비스 기본 등록은 `CreateRequest` 전에, 요청에만 적용할 등록은 그 뒤에 합니다. 기존 서비스에 대기 중인 다음 호출용 기능·정책 옵션은 `CreateRequest`가 가져와 소비합니다. 이 옵션을 재사용하려면 반환된 빌더를 재사용하세요.

```csharp
var request = service
    .CreateRequest("이 질문을 검색에 적합하게 다시 작성해줘.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\n원래 의미를 유지해줘."
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## 복사되는 설정과 계속 공유되는 상태

공통 설정과 제공자 기본값은 `CreateRequest` 호출 시 보관합니다. 이후 서비스 기본값이 바뀌어도 준비한 요청은 바뀌지 않습니다. 제공한 기본 메시지 콘텐츠, 지원되는 옵션 컬렉션, 프로파일, 컨텍스트, 정책은 복사합니다. 함수 핸들러, 동적 컨텍스트 콜백, 사용자 정의 메시지 콘텐츠는 참조를 유지합니다. 사용자 정의 콘텐츠는 변경하지 않아야 하며, 델리게이트는 외부 상태를 읽을 수 있다는 점을 고려하세요. 동적 컨텍스트 콜백 자체는 실행 시 평가됩니다.

캡처가 끝나면 원본 `JsonDocument`를 해제하거나 `JsonNode`를 수정해도 요청 메타데이터와 함수 호출 인자에 보관된 JSON 값은 바뀌지 않습니다. 실행마다 별도 복사본을 사용합니다. 도구 스키마의 `Items`가 순환하거나 중첩이 64단계를 넘으면 캡처 시점(`CreateRequest` 또는 `WithFunctions`)에 `ArgumentException`이 발생합니다. 잘못된 스키마가 프로세스 스택을 소진하기 전에 정상적인 오류로 처리하기 위한 제한입니다.

복사해도 배열의 차원과 시작 인덱스, 표준 `Dictionary<,>`·`SortedDictionary<,>`·`SortedList<,>`의 키 비교 규칙은 유지합니다. 따라서 대소문자를 구분하지 않던 키 조회가 요청 안에서 갑자기 달라지지 않습니다. 비어 있는 `default(JsonElement)` 값(`Undefined`)도 그대로 보존합니다. 알 수 없는 사용자 정의 메타데이터 객체는 참조를 유지하므로, 소유자가 변경하지 않거나 접근을 조율해야 합니다.

표준 `ReadOnlyCollection<T>`와 `ReadOnlyDictionary<TKey, TValue>`도 타입이 지정된 배열이나 사전 안에서 원래 타입을 유지합니다. 지원되는 내부 컬렉션을 복사할 때 읽기 전용 보기, 공유 참조, 순환 참조를 보존합니다. `Hashtable`과 비제네릭 `SortedList`의 키 비교 규칙도 유지합니다.

빌더마다 새 대화가 생기는 것은 아닙니다. 실행 시점에 서비스가 활성화한 대화의 기록을 사용하며, 빌더를 만들 때 기록까지 고정하지 않습니다. 상태를 유지하는 호출은 같은 대화 기록에 결과를 추가합니다. 기록을 읽거나 누적하지 않으려면 `WithStatelessMode()`를 사용하세요. 기존의 서비스당 실행 하나 제한도 유지됩니다. 요청 설정의 독립성이 같은 서비스의 병렬 실행까지 보장하지는 않습니다. 독립적인 대화를 동시에 실행하려면 별도 서비스를 사용하세요.

## 기존 호출부와 확장 기능

`GetCompletionAsync`와 기존 서비스 진입점은 계속 지원합니다. `BeginMessage()` / `MessageChain`은 기존의 변경 가능한 메시지 작성 방식을 유지하고 실행부는 새 요청 경로를 사용합니다. 설정을 분기하거나 재사용하려면 `CreateRequest`를 사용하세요. 빌더 API는 `AIService`와 해당 제공자 구현에 있으며 `IAIService`에 필수 멤버를 추가하지 않습니다. 추상화 인터페이스나 RAG 래퍼만 사용하는 코드는 기존 프로파일·컨텍스트와 실행 API를 계속 사용합니다.

[공통 지원 정의로 모델별 기능 선택지 구성하기](model-capabilities.md).
