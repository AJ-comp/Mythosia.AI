# 선택한 모델에 맞는 기능 선택지 보여주기

> Grok 4.7: Mythosia.AI 8.1.0 / Abstractions 4.1.0이 필요합니다. [모델 선택·추론·처리 속도](providers.md#grok-47)

> GPT-6 Sol/Luna: Mythosia.AI 8.1.0 / Abstractions 4.1.0이 필요합니다. [모델 선택과 필요 버전](providers.md#gpt-6-sol-luna)

[Claude Opus 5.5](providers.md#claude-opus-55)의 capability는 `XHigh`를 포함한 `Low`부터 `Max`까지를 제공하며 `None`과 `Minimal`은 미지원입니다. `ThinkingToggle`은 미지원, `MaxOutputTokens`는 128000입니다. 표시를 숨겨도 추론이 꺼지는 것은 아닙니다. Mythosia.AI 8.1.0 / Abstractions 4.1.0이 필요합니다.

채팅 화면의 추론·검색·도구·이미지 선택지는 현재 연결한 모델에 맞아야 합니다. 앱마다 모델 이름 목록을 따로 관리하면 라이브러리의 지원 규칙을 중복해서 만들게 되고, 제공자·API 방식·배포가 바뀔 때 서로 달라질 수 있습니다. 기능 스냅샷을 조회하면 화면과 실제 요청 검증이 같은 모델 정의를 사용할 수 있습니다.

이 API는 Mythosia.AI 8.0.0의 기능입니다. 스냅샷은 라이브러리가 알고 있는 지원 정보를 담은 불변 객체이며, 계정이나 서버에 실시간으로 조회한 결과가 아닙니다. 타입은 `Mythosia.AI.Models.Capabilities`에 있습니다.

사용자가 기다리는 시간을 줄여야 하는 요청에는 [처리 속도](request-building.md#inference-speed)를 선택할 수 있습니다. `WithSpeed`는 모델과 추론 수준을 유지하고, `Processing`은 공급자가 실제 적용한 모드를 보여줍니다. Fast는 지원 조합에서 사용하는 유료 옵션입니다.

## Before / After

Before: 앱이 지원 모델 목록을 직접 관리합니다. 아래 목록은 라이브러리 API가 아니라 앱이 별도로 작성해야 하는 코드입니다.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: 구성한 요청의 지원 정보를 보고 옵션을 선택합니다. 마지막 완료 호출에서 실제 모델에 요청하며, 지원 정보 조회 자체는 API를 호출하지 않습니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("문서 내용을 설명해줘.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport`는 `Supported`, `Unsupported`, `Unknown`을 구분합니다. 사용자 정의 배포나 서버가 선택하는 모델처럼 판단할 정보가 부족하면 `Unknown`이며, 미지원이라는 뜻은 아닙니다. 예제는 지원이 확인된 경우에만 추가 추론을 켭니다. 알 수 없는 경우에는 기본 설정 유지나 요청 시도 허용 등 앱의 정책을 선택하세요.

`request.GetCapabilities()`는 빌더에 캡처된 모델·제공자 옵션·프로파일을 읽습니다. `service.GetCapabilities()`는 다음 호출용 옵션을 소비하지 않고 서비스 기본 설정을 살펴봅니다. 둘 다 HTTP 호출, 컨텍스트 콜백이나 실행 검증기 호출, 대화 기록 변경, 작업 시작을 하지 않습니다. 반환되는 목록도 읽기 전용 스냅샷입니다. 서비스 조회는 다음 호출에 대기 중인 기능 설정도 함께 살펴보며, 실제 요청에서 사용할 수 있도록 그대로 남겨 둡니다. 조회는 함수 기본값이나 호스팅 도구 매개변수를 직렬화하지 않으며, 실행용 프로파일 준비나 토큰 예산 예약도 수행하지 않습니다.

조회 결과는 연결이 지원할 수 있는 기능이며, 현재 어떤 옵션을 켰는지를 뜻하지 않습니다. 모델 이름뿐 아니라 제공자·API 프로토콜·모드도 영향을 줍니다. 모델 정보는 제공자별 재정의와 Qwen/Ollama ID 변환을 반영한 실제 전송 모델을 기준으로 하며, 단일 모델이 선택되지 않으면 `null`일 수 있습니다. 샘플 Chat UI는 모델 목록에만 의존하지 않고, 현재 연결과 등록된 도구를 포함한 실제 설정을 기준으로 선택지를 갱신합니다. 추론 모드나 도구 유무에 따라 샘플링 지원이 달라질 수 있으므로 설정을 바꾸면 다시 조회하세요. 알 수 없는 지원 상태는 미지원과 구분해 표시합니다.

| API | 의미 |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | 공통 `WithReasoning` 설정의 지원 여부와 수준입니다. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | 제공자 고유 추론 설정과 예산 선택지입니다. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | 스트리밍, 도구, 제공자 고유 비동기 도구와 작업 중 추가 지시입니다. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | 호스팅 검색, 캐시를 유지하는 추론 변경, 이미지 입력과 구조화 출력입니다. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | 샘플링 설정의 지원 여부와 알려진 최대 출력 토큰 수이며, 한도는 nullable입니다. |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | Mythosia.AI 8.1.0 / Abstractions 4.1.0: 처리 모드의 Supported/Unsupported/Unknown. 계정 권한은 별도 확인합니다. |
| `Provider`, `Model` | 제공자와 실제 전송 모델의 식별 정보이며, 알 수 없으면 null일 수 있습니다. |

공통 추론과 제공자 고유 추론은 구분합니다. `ReasoningLevels`는 공통 `WithReasoning`에 넣을 값이고 `NativeReasoningLevels`는 제공자 고유 설정입니다. `ThinkingBudgetPresets`는 UI에서 제시할 만한 예산 선택지이며 모든 허용 예산이나 전체 숫자 범위를 나열한 것은 아닙니다. `AsyncFunctionCalling`은 제공자 고유 비동기 도구 실행을 뜻하며, 로컬 함수가 단순히 `Task`를 반환하거나 병렬로 실행된다는 뜻이 아닙니다. `StructuredOutput`은 프롬프트·복구 방식을 포함한 공통 타입 지정 출력 API의 지원을 뜻하며, 제공자의 네이티브 제약 디코딩을 보장하지 않습니다. 두 추론 수준 목록은 모두 `ReasoningLevel`을 사용하고 예산 프리셋은 정수 값입니다.

스냅샷은 계정 접근 권한이나 실제 서버 준비 상태를 보장하지 않으며 잘못 조합한 옵션을 유효하게 만들지도 않습니다. 실행 단계의 기존 검증과 오류는 유지합니다. 추가 지시를 보내기 전에는 실제 실행의 `run.CanSteer`를 확인하세요. 모델이 지원한다는 사실만으로 run이 아직 진행 중이라는 보장은 없습니다.

## 이미지 생성은 별도로 조회하기

이미지 생성 모델은 채팅 모델과 독립적으로 선택합니다. 특정 이미지 모델은 `service.GetImageCapabilities(imageModel)`로 조회하고, 인자를 생략하면 제공자의 기본 이미지 모델을 조회합니다. 채팅 요청 빌더로 이미지 생성 모델을 선택하지 않습니다. `Generation`, `Editing`, `Mask`를 보고 화면에 표시할 이미지 동작을 정할 수 있습니다.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions`, `AspectRatios`는 타입이 지정된 읽기 전용 선택지 목록입니다. `MaxImages`, `MaxInputImages`는 알려진 한도이며 알 수 없으면 null입니다. 목록에 있는 값이라고 모든 조합이 허용되는 것은 아닙니다. 기존 크기·형식·품질·마스크·모델 검증은 계속 적용합니다. 사용자 정의 또는 알 수 없는 이미지 모델을 미지원으로 단정하지 않습니다.

Google의 `Resolutions`와 `AspectRatios`는 선택한 이미지 모델에 따라 달라지며 생성·편집 검증에도 적용됩니다. Flash-Lite의 보수적인 1K 정책을 포함한 [모델별 표](providers.md#google-image-options)를 참고하세요. 지원하지 않는 명시적 값은 HTTP 전에 거절하며 사용자 정의 미확인 모델은 `Unknown` 지원 정보와 공급자 공통 옵션 검증을 유지합니다.

사용자 정의 `AIService`가 신뢰할 수 있는 지원 정의를 제공할 수 있다면 protected `ResolveRequestCapabilities()`를 재정의합니다. 기본값은 `AIModelCapabilities.Unknown`입니다. 모델 목록에 없다는 이유만으로 사용자 배포를 미지원으로 바꾸면 안 됩니다. `IAIService`에는 새 필수 멤버를 추가하지 않으며 조회 메서드는 `AIService`와 요청 빌더에서 제공합니다.

사용자 정의 제공자의 프로파일이 고유 모드 플래그를 바꾼다면 `ApplyCapabilityRequestProfile(AIRequestProfile)`를 재정의하고, 조회에 필요한 플래그만 `SetExecutionSetting(...)`으로 적용하세요. 기본 훅은 아무 작업도 하지 않습니다. 공통 프로파일 설정은 빌더에 이미 캡처되어 있으며, 조회 중에는 `ApplyRequestProfile`이나 `ApplyProviderSpecificRequestProfile`을 호출하지 않습니다. 이 훅에서 검증·콜백·직렬화·예산 예약을 수행하거나 서비스 및 호출자 소유 상태를 변경하면 안 됩니다. 임시 설정은 조회가 끝나거나 재정의한 훅에서 예외가 발생해도 복원됩니다.

[요청 설정](request-building.md) · [제공자·이미지 옵션](providers.md) · [Run 제어](execution-api-transition.md)
