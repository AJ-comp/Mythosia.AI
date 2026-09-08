# Provider-Specific Configuration 아키텍처

> GPT-6 Astra, `AllowAsync`, `StartRunAsync`, 공통 추론·검색 API는 `Mythosia.AI` 7.1.0부터 제공하며, 공통 타입은 `Mythosia.AI.Abstractions` 3.1.0에 포함됩니다.

## 원칙

애플리케이션은 [공통 추론·검색 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/reasoning-and-search.md)로 작업에 필요한 추론량과 내장 검색을 표현할 수 있습니다. `AIRequestFeatures`는 한 논리적 요청에 복사하고 공급자 어댑터가 검증·변환하며, 공급자별 기본값은 서비스에 유지합니다. 캐시 보존 변경의 프로토콜 상태는 추적 중인 대화에 남습니다. `AICitation`은 스트림 관찰과 독립적으로 출처를 보관합니다. 사용자 서비스는 `IAIRequestFeatureService`로 선택적으로 지원하며 `IAIService`에 필수 멤버를 추가하지 않습니다.

| 설정 유형 | 위치 | 예시 |
|-----------|------|------|
| **공통 설정** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 등 |
| **전용 설정** | 각 서비스 클래스 | ThinkingBudget (Gemini), ReasoningEffort (GPT) 등 |
| **함수별 실행 허용** | `FunctionDefinition` | `AllowAsync` (기본값 `false`) |

`AllowAsync`는 호출자가 선택하는 허용 옵션이며, 모델·API의 지원 여부는 서비스가 내부적으로 판단합니다. `FunctionBuilder.WithAsync()`와 `[AiFunction("lookup", "데이터 조회", AllowAsync = true)]`도 같은 옵션을 켭니다. GPT-6 Astra는 Responses에서 이를 사용하고, 미지원 모델은 API 옵션을 생략한 뒤 같은 핸들러의 결과를 기다립니다. 사용자가 지정한 허용 값은 바꾸지 않습니다.

## 현재 구현: 서비스 레벨

전용 설정은 해당 서비스 클래스의 프로퍼티로 관리합니다.

```csharp
// 공통 설정 → ChatBlock
geminiService.ActivateChat.Temperature = 0.7f;
geminiService.ActivateChat.MaxTokens = 4096;

// 전용 설정 → 서비스
geminiService.ThinkingBudget = 1024;
```

### 장점
- ChatBlock이 프로바이더에 대해 완전히 무관심 (깨끗한 분리)
- 기존 OOP 원칙에 부합 (서비스가 자기 전용 설정 관리)
- 서비스 인스턴스 하나에 전용 설정 하나 → 단순한 구조

### 단점
- 하나의 서비스 내 여러 ChatBlock에 동일한 전용 설정 적용됨

## ChatBlock 레벨로 이동이 필요한 경우

만약 향후 **ChatBlock별로 전용 설정을 독립적으로 유지해야 하는 요구사항**이 생기면, ChatBlock 내에 Lazy 프로퍼티로 전용 설정 클래스를 추가하는 방식으로 마이그레이션합니다.

```csharp
// 예시 (현재는 미구현)
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// 사용
chatBlock.Gemini.ThinkingBudget = 1024;
```

### 이 방식이 필요한 시나리오
- 하나의 서비스 인스턴스에서 ChatBlock A와 B가 서로 다른 ThinkingBudget을 사용해야 할 때
- 현실적으로 이런 케이스는 거의 없으므로, 현재는 서비스 레벨을 유지

## 결정 이력

- **2026-02-12**: 최초 Option B (ChatBlock 레벨)로 구현 후, 서비스 레벨로 롤백. 전용 설정은 서비스에 두는 것이 자연스럽다고 판단.

## 실행 제어를 선택하는 이유

오래 걸리는 작업에서는 진행 상황을 표시하거나 사용자가 중간에 조건을 바꿀 수 있어야 합니다. `StartRunAsync`가 반환하는 `AIRun`으로 해당 작업을 제어하며, 작업 중 추가 지시의 지원 여부는 provider가 결정합니다. 모델 설정은 서비스에서 실행 전에 구성하고, 추가 지시 전에는 `run.CanSteer`를 확인합니다. 사용 상황과 예제·취소·호환성은 [Run 사용 안내](../../../../../docs/ko/execution-api-transition.md)를 참고하세요.
