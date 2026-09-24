# 2026-09-24 Claude 실패 3건 후속 조사

**확인된 수정 대상은 테스트의 진단 누락이었다. 제품 코드 결함은 이번 조사에서 확인되지 않았다.** Opus 5 대화 기억 테스트는 같은 프롬프트와 설정으로 다시 실행했을 때 통과했다. Opus 5.5 Fast completion/Run은 모두 계정의 Fast 입력 토큰 한도 0으로 실패했다. 최초 거절 원인은 여전히 미확정이며, 재실행 통과를 제품 버그 수정으로 해석하지 않는다.

이 기록은 [전체 API 검증](2026-09-24-live-api.md)의 Claude 관련 실패만 다시 조사한 결과다. 해당 문서의 20:26 KST 전체 집계는 당시의 스냅샷으로 유지하며, 아래의 재검증을 그 실행에서 처음부터 통과한 것으로 합산하지 않는다. 다른 공급자를 다시 호출하지 않았다.

## 결과

| 대상 | 최초 결과 | 후속 실제 검증 | 판정 |
| --- | --- | --- | --- |
| `Claude_Opus5.ContextManagementTest` | `stop_reason=refusal` | 통과. 6건 모두 HTTP 200, `end_turn` | 최초 거절 미재현. 당시 세부 거절 사유는 로그에 없어 확정 불가 |
| Opus 5.5 Fast completion | HTTP 429, 상세 본문 누락 | HTTP 429, Fast 입력 토큰 한도 0/분 | 계정 제한 확인 |
| Opus 5.5 Fast Run | HTTP 429, Fast 입력 토큰 한도 0/분 | 같은 한도로 HTTP 429 | 계정 제한 재확인 |

실제 테스트는 **3건 중 1건 통과·2건 실패·건너뜀 0건**이다. 실패를 성공 또는 skip으로 바꾸지 않았다. 별도 관련 단위테스트는 **70/70 통과**, 그중 이번에 추가한 Opus 5·5.5의 문맥 보존/거절 경로가 **4건**이다. 기존 공통 속도 계약 테스트에는 Google의 mock 검증도 포함되며, 추가 외부 API 호출은 아니다. 격리 빌드는 경고·오류가 없었다.

## 대화 기억 테스트

원래 테스트와 동일하게 `Remember number 1`부터 `Remember number 5`까지 보내고, `GetCompletionWithContextAsync("What numbers did I mention?", contextMessages: 5)`를 호출했다. 프롬프트·모델·토큰 예산·추론 설정·기존 성공 조건은 변경하지 않았다.

실제 전송·응답에서 다음을 확인했다.

- 여섯 요청 모두 `api.anthropic.com/v1/messages`, 모델 `claude-opus-5`, `stream: false`, `max_tokens: 8192`, `thinking.type: disabled`였다.
- 요청의 메시지 수는 **1 → 3 → 5 → 7 → 9 → 11**이었다. 모든 역할은 user/assistant 순서를 유지했고, 각 assistant 본문은 앞선 실제 응답과 일치했다.
- `contextMessages: 5`는 기존 이력을 자르는 설정이 아니다. 최근 5개 메시지를 새 user 질문에 텍스트로 덧붙이며 기존 전체 이력도 유지한다. 이 경로에서 처음 메시지가 assistant로 바뀌거나 이력이 유실되지 않았다.
- 여섯 응답 모두 HTTP 200과 `stop_reason: end_turn`이었고 `stop_details`는 null이었다. 마지막 응답은 **1, 2, 3, 4, 5**를 정확히 열거했다.

최초 실행의 테스트는 예외 메시지만 출력한 뒤 `Assert.Fail`로 바꾸어 `ErrorDetails`와 원본 스택을 잃었다. 당시 요청별 상세 응답이 없어 최초 거절의 분류나 이유를 소급 확인할 수 없다. 이번 재검증에서 문맥 유지 경로는 정상이었지만, 처음 왜 거절했는지 또는 다시 거절할 가능성이 없는지는 입증되지 않았다.

라이브러리의 거절 감지는 `stop_reason=refusal`을 검사하고 `stop_details`를 `AIServiceException.ErrorDetails`에 보존한다. 거절 응답을 정상 답변으로 반환하거나 부분 assistant 응답을 이력에 저장하지 않는다. 새 단위테스트에서도 이 경계와 재시도가 발생하지 않는 것을 확인했다. 공식 문서 역시 거절의 세부 사유를 `stop_details`로 제공하고 부분 결과를 불완전한 것으로 취급하도록 안내한다. [Anthropic 거절 처리 문서](https://platform.claude.com/docs/en/build-with-claude/refusals-and-fallback)

## Fast 모드

현재 구현의 `claude-opus-5-5`, `speed: "fast"`, `anthropic-beta: fast-mode-2026-02-01` 조합은 공식 사양과 일치했다. Fast는 별도 접근 권한과 처리 한도를 사용한다. [Anthropic Fast 문서](https://platform.claude.com/docs/en/build-with-claude/fast-mode)

이번에는 completion과 Run 모두 상세 응답에서 다음을 확인했다.

```text
HTTP 429
error.type = rate_limit_error
0 fast mode input tokens per minute
```

이는 코드에서 입력을 더 짧게 만들어 해결할 수 있는 양의 한도가 아니다. 실제 Fast 성공 호출은 계정에 Fast 접근 권한과 0보다 큰 입력 토큰 한도가 마련되어야 재검증할 수 있다. 계정 설정은 변경하지 않았다. 요청한 Fast를 Standard로 바꾸거나 다른 모델로 전환해 성공으로 처리하지 않았다.

## 변경한 테스트

- `TestCases/AIServiceTestBase.Conversation.cs`: 어느 대화 단계에서 실패했는지와 `ErrorDetails`를 기록하고 원래 예외·스택을 보존한다.
- `Infrastructure/AnthropicContextDiagnosticHandler.cs`: 합성 입력만 사용하는 `ContextManagementTest`에서 요청 역할·본문과 응답의 종료 사유·거절 상세·요청 ID를 기록한다. 인증 헤더, 서명, 응답 thinking 블록은 출력하지 않는다. 응답을 재작성하지 않고, 진단 파싱 실패가 제품 파서의 오류를 가리지 않도록 한다.
- `Providers/Anthropic/AnthropicServiceTests.cs`: 위 테스트에만 핸들러를 적용하고 해당 클라이언트를 정리한다.
- `Providers/Common/InferenceSpeedLiveTests.cs`: Anthropic의 Fast 실패에서도 공급자 상세 본문을 출력한다. API 키는 가리고 원래 예외 및 엄격한 속도 검증을 유지한다.
- `Common/AnthropicContextManagementTests.cs`: Opus 5·5.5에서 정상 이력과 여섯 번째 요청의 거절을 재현하는 단위테스트 4건을 추가했다. 메시지 수·역할·본문·오류 상세와 부분 응답 미저장을 검사한다.

제품 코드·공개 API·패키지 버전은 변경하지 않았다. 실제 검증 뒤 진단 핸들러에 잘못된 JSON 형태를 처리하는 보호 코드를 추가했고 최종 빌드를 확인했다. 이 보호 코드는 정상 응답의 전송·파싱을 바꾸지 않으며 추가 실제 호출은 수행하지 않았다.

## 실행 근거

로컬 `artifacts/test-results/claude-errors-20260924/`에 보관한다. 아래 파일은 추적 문서와 별도 산출물이므로 새 checkout에 자동 포함되지 않는다.

| 파일 | 내용 |
| --- | --- |
| `live/claude-failed-only-diagnostic.trx`, `live.log` | 정확히 3건의 결과, 6개 문맥 요청·응답, 두 Fast 실패 상세 |
| `unit/claude-regression-unit.trx`, `unit.log` | 신규 4건을 포함한 관련 단위테스트 70건 |
| `result-audit.json`, `audit-results.py` | TRX 카운터·원본 이력 재전송·최종 숫자·두 Fast 제한을 독립 대조한 결과 |
| `build.log`, `build-final.log` | 실제 호출 전 격리 빌드와 진단 파서 보호 코드 추가 후 최종 빌드 |

원래 전체 실행의 실패 TRX도 그대로 보존했다. 거절의 최초 원인이 밝혀졌다는 주장이나 Fast 실행 성공 근거는 이 기록에 없다.
