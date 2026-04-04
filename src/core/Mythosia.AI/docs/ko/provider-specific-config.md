# Provider-Specific Configuration 아키텍처

## 원칙

| 설정 유형 | 위치 | 예시 |
|-----------|------|------|
| **공통 설정** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 등 |
| **전용 설정** | 각 서비스 클래스 | ThinkingBudget (Gemini), ReasoningEffort (GPT) 등 |

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
