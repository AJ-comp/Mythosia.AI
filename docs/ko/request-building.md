# 요청마다 설정을 독립적으로 관리하기

> Claude Sonnet 5.5: Mythosia.AI 8.2.0 / Abstractions 4.2.0이 필요합니다. [설정과 마이그레이션](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: Mythosia.AI 8.2.0 / Abstractions 4.2.0이 필요합니다. [모델 선택과 전환](providers.md#gpt-61-sol)

> Grok 4.7: Mythosia.AI 8.1.0 / Abstractions 4.1.0이 필요합니다. [모델 선택·추론·처리 속도](providers.md#grok-47)

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

Claude는 모델, 요청 목적, 추론과 thinking 바인딩 설정을 함께 결정합니다. `RequestProfiles.Summarization`이나 `RequestProfiles.QueryRewrite` 같은 보조 프로파일에서는 `DisableReasoning = true`이고 목적이 `Default`가 아니며 실제 요청이 무상태일 때만 상속한 바인딩을 생략합니다. 보존할 대화 접두부가 없는 요청에서 상속한 정책 때문에 추론이 다시 켜지거나 요청이 거부되는 것을 막습니다. 추론을 끌 수 있는 모델은 비활성화하고, 항상 추론하는 Opus 5.5·Fable 5.1·Mythos 5.1은 `Low`와 읽을 수 있는 thinking 생략을 사용합니다. Sonnet 5.5는 high effort의 `between_tools`를 사용합니다.

완료, 스트리밍, 구조화 출력, Run은 같은 순서로 요청을 준비합니다. 설정을 보관하고 실제 프로파일 처리를 한 번 적용한 뒤, 최종 공통 옵션과 공급자 네이티브 옵션을 검증합니다. 자동 요약, 새 입력의 이력 추가, 통신 연결보다 먼저 수행하므로 사용자 정의 공급자의 프로파일 재정의도 실제 검증 대상에 반영됩니다. Claude의 실제 사용되는 수동 `ThinkingBudget`이 모델 출력 한도 이상이면 이 단계에서 거부하며, 유효한 프로파일과 공통 추론 설정의 우선순위는 유지합니다.

애플리케이션 호출은 독립된 논리 요청을 시작합니다. `SystemMessageProvider`나 도구 콜백의 일반 중첩 호출, 같은 `AIRequestProfile`·`Message`를 재사용하는 호출도 마찬가지이며, 객체 재사용이 실행 재사용을 뜻하지는 않습니다. 프레임워크 위임, 도구 실행 단계, 재시도, 형식 수정은 원래 요청을 이어가며 프로파일은 한 번 적용합니다. 일반 자식 요청은 자체 옵션과 서비스 기본값을 가져오고 빌더는 보관한 설정을 사용합니다. 성공·실패·취소 후에는 부모 실행 상태를 복원합니다. 프레임워크 호출을 전달하는 공급자 재정의에는 [아래 어댑터 규칙](#provider-request-adapters)을 적용합니다.

내장 공급자는 기본 입력 콘텐츠의 자체 복사본을 보관합니다. 같은 `Message`로 다시 호출해도 해당 호출의 컨텍스트와 턴 지시를 적용하고 이미 수락된 이력은 바꾸지 않습니다. 사용자 정의 콘텐츠와 지원되지 않는 메타데이터 객체는 소유자가 관리해야 합니다. 같은 대화를 동시에 호출해도 안전하다는 뜻은 아닙니다.

`StartRunAsync`가 반환된 뒤에도 Run은 최종 설정의 자체 복사본을 유지합니다. 호출 측 프로파일을 복원해도 실행 중인 Run의 설정은 바뀌지 않으며, 실행용 프로파일 훅은 여전히 한 번만 호출됩니다.

무상태 보조 요청은 별도 대화를 사용하고 부모의 출력 스키마, 호스팅 도구, 일회성 옵션을 상속하지 않습니다. 이 격리가 네이티브 옵션 검증을 생략하지는 않으며 OpenAI·Perplexity Run에도 같은 원칙을 적용합니다. 부모 설정, 메시지, `CurrentSummary`, 요청 관측 정보는 보존합니다. 상태를 유지하는 요청은 바인딩과 대화 검증을 유지합니다. 기존 공개 API는 바뀌지 않습니다.

라이브러리가 자동으로 생성하는 대화 요약은 부모의 `SystemMessageProvider` 콜백과 요청 컨텍스트도 제외합니다. 상속한 `RequestMessageOverride`가 내부 요약 프롬프트를 바꾸지 못하게 하기 위한 것입니다. 애플리케이션이 직접 텍스트 요약을 요청하는 경우를 포함한 일반 요청은 동적 컨텍스트를 정상적으로 적용합니다.

무상태 요청은 부모 대화의 자동 요약도 실행하지 않습니다. 기존 `GetCompletionAsync(string, profile)` 오버로드에서도 `Message` 오버로드와 요청 빌더처럼 부모의 `CurrentSummary`와 메시지를 그대로 유지합니다. 상태를 유지하는 요청은 기존 자동 요약 동작을 유지합니다.

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## 사용자 정의 공급자에서 요청 전달하기

프레임워크가 공급자의 가상 어댑터 메서드를 호출하면, 재정의 내부에서 처음 호출하는 대응 base 진입점은 준비된 요청을 이어갑니다. 입력 `Message`를 교체해도 보관한 빌더 옵션과 적용한 프로파일은 유지합니다. 기본 콜백 스트리밍 어댑터도 같은 준비된 요청을 이어갑니다.

어댑터가 다른 `AIRequestProfile`을 전달하면, 같은 값은 두 번 적용하지 않고 변경된 값은 보관한 설정을 기준으로 이전 프로파일 계층을 교체합니다. 자동 요약·전송 전에 다시 검증하므로 무상태로 변경한 요청이 먼저 부모 대화를 요약하지 않습니다. OpenAI 무상태 보조 요청은 관련 없는 보존 이력 검사를 건너뛰며, 부모의 상태 유지 요청에 대한 보호는 해제하지 않습니다.

전달할 프로파일을 교체해도 요청 안에서 나중에 추가·제거·수정한 도구, 정책 변경, 명시적으로 다시 지정한 설정은 유지됩니다. 같은 스칼라 값을 다시 지정한 경우도 포함합니다. 다른 프로파일 필드가 바뀌었다는 이유만으로 어댑터가 제거한 도구를 복원하지 않으며, 서비스 기본값을 다시 읽지도 않습니다.

라이브러리가 내부 구조를 알 수 없는 사용자 정의 설정 객체는 내부 필드를 직접 바꾸기보다 `SetExecutionSetting`으로 값을 교체하세요. 프로파일 변경을 추적하기 위해 임의의 애플리케이션 객체를 탐색하거나 그 객체의 직렬화기를 실행하지는 않습니다.

Claude 압축은 보관된 `RequestMessageOverride`·`AdditionalMessages`의 도구 호출과 결과 관계를 서버 도구까지 보호합니다. Mythos 5.1의 바인딩된 thinking 접두부도 보호합니다. 병렬 도구의 레거시 기록은 일반 이력과 추가 메시지에서 한 번만 묶어 전송하며, 각 기록의 소유 관계를 유지합니다.

원래 요청을 전달하기 전에 같은 base 진입점으로 별개의 보조 작업을 호출하면 프레임워크는 그것이 원래 요청의 연속인지 구분할 수 없습니다. 이때는 protected `BeginIndependentRequestScope()`로 보조 호출과 `await`를 함께 감싸세요. 스트리밍은 전체 열거가 끝날 때까지 범위를 유지해야 합니다. 보조 요청은 서비스 기본값으로 시작하고, 범위 해제 시 바깥 요청의 설정·기능·컨텍스트와 대기 중인 위임을 복원합니다. 컨텍스트나 도구 콜백에서 하는 일반 중첩 호출은 이미 독립적이므로 이 범위가 필요하지 않습니다.

예를 들어 구체 공급자의 하위 클래스는 텍스트를 재작성한 뒤 전달할 수 있습니다.

```csharp
public override async Task<string> GetCompletionAsync(
    Message message, AIRequestProfile? profile = null,
    AIRequestContext? context = null, CancellationToken cancellationToken = default)
{
    string rewritten;
    using (BeginIndependentRequestScope())
    {
        rewritten = await base.GetCompletionAsync(
            new Message(ActorRole.User, message.Content),
            RequestProfiles.QueryRewrite,
            cancellationToken: cancellationToken);
    }

    var replacement = new Message(message.Role, rewritten);
    return await base.GetCompletionAsync(replacement, profile, context, cancellationToken);
}
```

이 범위는 요청 실행 상태를 분리하며 대화 이력을 격리하거나 서비스 동시 사용을 허용하지는 않습니다. 예제는 무상태 `QueryRewrite` 프로파일로 보조 작업이 부모 대화에 들어가지 않게 합니다.

Claude의 대화 압축 검사는 보존된 실제 전송 이력을 확인하며, `AIRequestContext.AdditionalMessages`로 추가한 서명된 thinking도 포함합니다. 기본 thinking 바인딩은 자동·명시적 요약 압축에서 이 접두부를 보호합니다. 지원되는 경우 `ClaudeThinkingPrefixMismatchBehavior.DropBlock`을 명시하면 압축할 수 있으며, 다른 대화 제약은 계속 적용됩니다.

## 복사되는 설정과 계속 공유되는 상태

공통 설정과 제공자 기본값은 `CreateRequest` 호출 시 보관합니다. 이후 서비스 기본값이 바뀌어도 준비한 요청은 바뀌지 않습니다. 제공한 기본 메시지 콘텐츠, 지원되는 옵션 컬렉션, 프로파일, 컨텍스트, 정책은 복사합니다. 함수 핸들러, 동적 컨텍스트 콜백, 사용자 정의 메시지 콘텐츠는 참조를 유지합니다. 사용자 정의 콘텐츠는 변경하지 않아야 하며, 델리게이트는 외부 상태를 읽을 수 있다는 점을 고려하세요. 동적 컨텍스트 콜백 자체는 실행 시 평가됩니다.

캡처가 끝나면 원본 `JsonDocument`를 해제하거나 `JsonNode`를 수정해도 요청 메타데이터와 함수 호출 인자에 보관된 JSON 값은 바뀌지 않습니다. 실행마다 별도 복사본을 사용합니다. 도구 스키마의 `Items`가 순환하거나 중첩이 64단계를 넘으면 캡처 시점(`CreateRequest` 또는 `WithFunctions`)에 `ArgumentException`이 발생합니다. 잘못된 스키마가 프로세스 스택을 소진하기 전에 정상적인 오류로 처리하기 위한 제한입니다.

복사해도 배열의 차원과 시작 인덱스, 표준 `Dictionary<,>`·`SortedDictionary<,>`·`SortedList<,>`의 키 비교 규칙은 유지합니다. 따라서 대소문자를 구분하지 않던 키 조회가 요청 안에서 갑자기 달라지지 않습니다. 비어 있는 `default(JsonElement)` 값(`Undefined`)도 그대로 보존합니다. 알 수 없는 사용자 정의 메타데이터 객체는 참조를 유지하므로, 소유자가 변경하지 않거나 접근을 조율해야 합니다.

표준 `ReadOnlyCollection<T>`와 `ReadOnlyDictionary<TKey, TValue>`도 타입이 지정된 배열이나 사전 안에서 원래 타입을 유지합니다. 지원되는 내부 컬렉션을 복사할 때 읽기 전용 보기, 공유 참조, 순환 참조를 보존합니다. `Hashtable`과 비제네릭 `SortedList`의 키 비교 규칙도 유지합니다.

빌더마다 새 대화가 생기는 것은 아닙니다. 실행 시점에 서비스가 활성화한 대화의 기록을 사용하며, 빌더를 만들 때 기록까지 고정하지 않습니다. 상태를 유지하는 호출은 같은 대화 기록에 결과를 추가합니다. 기록을 읽거나 누적하지 않으려면 `WithStatelessMode()`를 사용하세요. 기존의 서비스당 실행 하나 제한도 유지됩니다. 요청 설정의 독립성이 같은 서비스의 병렬 실행까지 보장하지는 않습니다. 독립적인 대화를 동시에 실행하려면 별도 서비스를 사용하세요.

## 기존 호출부와 확장 기능

`GetCompletionAsync`와 기존 서비스 진입점은 계속 지원합니다. `BeginMessage()` / `MessageChain`은 기존의 변경 가능한 메시지 작성 방식을 유지하고 실행부는 새 요청 경로를 사용합니다. 설정을 분기하거나 재사용하려면 `CreateRequest`를 사용하세요. 빌더 API는 `AIService`와 해당 제공자 구현에 있으며 `IAIService`에 필수 멤버를 추가하지 않습니다. 추상화 인터페이스나 RAG 래퍼만 사용하는 코드는 기존 프로파일·컨텍스트와 실행 API를 계속 사용합니다.

[공통 지원 정의로 모델별 기능 선택지 구성하기](model-capabilities.md).

<a id="inference-speed"></a>

## 작업에 맞게 처리 속도 선택하기

고객이 화면에서 답을 기다리는 요청에는 유료 저지연 처리를 쓰고, 백그라운드 보고서는 일반 처리로 실행할 수 있습니다. `WithSpeed`는 모델과 추론 수준을 유지한 채 처리 모드를 선택합니다. Mythosia.AI 8.1.0 / Abstractions 4.1.0이 필요합니다.

`ProviderDefault`는 별도 덮어쓰기 없이 기존 서비스·공급자 설정을 따릅니다. 프로젝트 기본값이 이미 Fast일 수도 있습니다. `Standard`는 일반 처리를 명시적으로 요청합니다. `Fast`는 공급자의 유료 저지연 처리를 요청하므로 추가 비용이 발생할 수 있습니다. 반환된 빌더를 사용하세요. 아래 세 요청은 각자 설정을 가지며 원본 요청을 바꾸지 않습니다.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

선택지를 보여주기 전에 `GetSpeedSupport(InferenceSpeed.Fast)`를 확인합니다. `StandardSpeed`와 `FastSpeed`도 Supported·Unsupported·Unknown을 구분합니다. 로컬 Supported 결과가 계정 권한·서버 용량·지연시간을 보장하지는 않습니다. 미지원·불명인 Standard/Fast 요청은 실패하며 모델이나 추론 수준을 조용히 바꾸지 않습니다. 기존 경로를 유지하려면 `ProviderDefault`를 사용하세요.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing`은 스트림을 읽지 않아도 불변 `AIProcessingInfo` 기록을 보존합니다. `RequestIndex`는 1부터 시작하는 공급자 추론 시도 번호이며 서버 continuation도 포함합니다. 도구 라운드 수나 HTTP 요청 수와는 다릅니다. 도구 후속 호출·재시도·형식 복구로 기록이 늘어날 수 있습니다. 실패한 시도를 포함해 서버가 인식 가능한 모드를 보고하지 않으면 `AppliedSpeed`는 null입니다. `RawAppliedMode`와 `ResponseId`에는 공급자가 보고한 원본 값이 남습니다. `IsDowngraded`는 Fast를 요청했는데 Standard 적용이 명시적으로 보고된 경우에만 true입니다. false만으로 Fast 적용을 확정하지 마세요.

일반 completion에서는 호출 직후 `AIService.LastProcessing`을 확인합니다. 다음 논리적 요청이 실행되면 이 뷰는 교체되며, 이미 받은 기록은 불변입니다. 서비스 확장 메서드는 다음 논리적 요청과 그 도구 왕복에 적용되며 영구 기본값을 바꾸지 않습니다. 보조 요약·내부 질의 재작성·내부 프로필은 본 요청의 속도 덮어쓰기를 상속하거나 관측 기록에 섞이지 않습니다.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

이 값은 공급자의 처리 모드이며 실제 초당 토큰 수 측정값이 아닙니다. OpenAI·xAI·Google 서버가 일반 처리로 낮출 수 있으며, Mythosia가 다른 속도로 자동 재호출하지는 않습니다. Anthropic fast mode는 권한이 있는 계정의 직접 Claude API에서 지원하며, 속도를 바꾸면 프롬프트 캐시 재사용이 무효화될 수 있습니다. Gemini Developer API priority는 Tier 2/3 자격이 필요합니다. 공통 메서드와 별도로 공급자·모델·API의 지원 및 요금 조건을 확인하세요. 이미지 생성·임베딩·네이티브 Batch API에는 이 설정을 적용하지 않습니다. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

`IAIService`로 참조한다면 `Mythosia.AI.Extensions`의 `GetLastProcessing()`을 사용하세요. 선택적 `IAIProcessingInfoService`를 읽으며 진단 기능이 없으면 빈 목록을 반환합니다. `IAIService`에 필수 멤버를 추가하지 않습니다. RAG에서는 `RagEnabledService.WithSpeed(...)`가 검색 후 작성하는 다음 답변에 적용되고 `LastProcessing`은 해당 답변의 처리 정보를 보여줍니다. 내부 질의 재작성은 분리하며 Run 결과에서도 같은 `Processing`을 읽습니다.

이번 구현의 Fast 지원 목록은 아래와 같이 명시되어 있습니다. Standard 지원은 `GetSpeedSupport(InferenceSpeed.Standard)`로 별도로 확인하세요. 목록 밖 모델·서드파티 엔드포인트·OpenAI 호환 공급자가 유료 처리 기능을 자동으로 상속하지 않습니다.

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — Sonnet 5를 포함한 나머지 등록 Claude 모델 | `speed`와 fast-mode beta 생략 | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

나머지 Claude 모델의 Standard는 기존 일반 요청을 사용합니다. 서버가 처리 정보를 보고하지 않으면 `AppliedSpeed`는 null로 유지하며, 요청값만 보고 Standard가 적용되었다고 추정하지 않습니다.
