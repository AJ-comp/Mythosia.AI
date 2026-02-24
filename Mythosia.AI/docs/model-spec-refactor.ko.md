# 모델 메타 정보(ModelSpec) 도입 예정안

## 배경

- 현재 `AIService.Model`은 `string`으로 유지됩니다.
- 서비스별로 문자열 비교를 통해 모델 기능/토큰 한계를 판단합니다.
- 모델 추가는 빠르지만, 로직이 분산되어 유지보수 비용이 늘어나는 문제가 있습니다.

## 목표

- **유연성**: 미등록/커스텀 모델 문자열을 그대로 수용
- **중앙화**: known 모델의 메타 정보를 한 곳에서 관리
- **호환성**: 기존 API/동작과의 브레이킹 최소화

## 제안 구조 (하이브리드)

- `Model`은 **string 유지**
- **ModelSpec(메타 정보)**를 optional로 도입
- known 모델만 `ModelSpec`으로 resolve
- unknown 문자열은 **기존 로직 그대로** 처리

### ModelSpec 예시 범위 (예상)

- ModelId (string)
- Provider
- MaxOutputTokens (또는 MaxTokensLimit)
- Capabilities
  - Vision 지원 여부
  - Reasoning/Thinking 지원 여부
  - Function Calling 지원 여부
- 기본 파라미터 (필요 시): DefaultMaxTokens 등

> 참고: 전용 설정(ReasoningEffort, ThinkingBudget 등)은 **서비스 클래스에 유지**합니다.

## 해석 규칙

1. `Model`이 설정되면 **ModelSpecRegistry**에서 known 모델인지 시도
2. **resolved** → ModelSpec 기반으로 capability/limit 적용
3. **not resolved** → 기존 서비스별 문자열 파싱 로직 사용
4. 최종적으로는 **unknown 문자열도 정상 동작**해야 함

## 기대 효과

- known 모델의 메타가 중앙화되어 **유지보수성 향상**
- 커스텀 모델 대응 **유연성 유지**
- 단계적 마이그레이션 가능

## 마이그레이션 초안

1. `ModelSpec`/`ModelSpecRegistry` 스켈레톤 도입
2. known 모델만 등록 (필요 범위부터)
3. 서비스별 문자열 파싱 로직에 **Spec 우선 적용 + fallback** 추가
4. 테스트 보강 (known 모델 메타, unknown 문자열 경로)

## 논의 포인트

- unknown 모델에 대한 사용자 정의 메타 제공 방식(옵션)
- alias(별칭) 모델 처리 정책
- Spec에 포함할 최소 메타 범위
