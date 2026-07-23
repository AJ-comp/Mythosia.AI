# Conversation Summary Mechanism

## 개요

긴 대화에서 토큰 비용과 컨텍스트 윈도우 제한을 관리하기 위한 자동 요약 메커니즘입니다.
`SummaryConversationPolicy`가 트리거 조건을 감지하면, 오래된 메시지를 요약 텍스트로 압축하고 최근 메시지만 유지합니다.

## 핵심 설계 원칙

1. **요약 타이밍**: 트리거 기반 요약은 함수 호출 체인(라운드) 중간이 아닌, 모든 라운드가 완료된 후에만 발동. 예외는 컨텍스트 초과 복구 하나뿐이며, 거기서도 지금 질문과 앞 라운드 결과는 보존한다 ([컨텍스트 초과 복구](#컨텍스트-초과-복구-트리거-요약과-다른-길) 참조)
2. **요약 정책은 API 제약을 모름**: `GetMessagesToSummarize`는 규칙대로만 자름. User-first 같은 API 제약은 각 프로바이더가 처리
3. **원본 불변**: 요약 텍스트는 `CurrentSummary`에 저장되어 시스템 프롬프트에 주입. API 요청용 메시지 리스트는 복사본

## 트리거 조건

```csharp
// 메시지 개수 기반
var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10, keepRecentCount: 4);

// 토큰 수 기반 (API 반환 실제 토큰 사용)
var policy = SummaryConversationPolicy.ByToken(triggerTokens: 8000, keepRecentTokens: 2000);

// 둘 다 (OR 조건)
var policy = SummaryConversationPolicy.ByBoth(triggerTokens: 8000, triggerCount: 20, ...);
```

토큰 기반 트리거는 API가 반환하는 공식 `InputTokens` 값(`LastKnownInputTokens`)을 우선 사용합니다.
값이 없을 때만 로컬 추정치(`EstimateTokens`)로 폴백합니다.

## 전체 흐름 (함수 호출 포함)

### 설정

```
triggerCount=3, keepRecentCount=2
함수: get_user_id, get_user_details
```

### 1단계: 사용자 질문

```
StreamAsync(User("john_doe 정보 알려줘"))

ActivateChat.Messages:
  [0] User: "john_doe 정보 알려줘"
```

### 2단계: Round 0 — 첫 번째 함수 호출

LLM이 `get_user_id("john_doe")` 호출 결정. 함수 실행 후:

```
ActivateChat.Messages:
  [0] User: "john_doe 정보 알려줘"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
```

`hasFunctionResult = true` → 다음 라운드

### 3단계: Round 1 — 두 번째 함수 호출

LLM이 `get_user_details("user_123")` 호출. 함수 실행 후:

```
ActivateChat.Messages:
  [0] User: "john_doe 정보 알려줘"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, name: Test User, email: test@example.com}"
```

`hasFunctionResult = true` → 다음 라운드

### 4단계: Round 2 — 최종 텍스트 응답

LLM이 함수 결과를 종합하여 텍스트 응답 생성:

```
ActivateChat.Messages:
  [0] User: "john_doe 정보 알려줘"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, ...}"
  [5] Assistant: "john_doe의 정보는 다음과 같습니다..."
```

`hasFunctionResult = false` → 모든 라운드 완료

### 5단계: 스트리밍 종료 후 요약 발동

```
ShouldSummarize: 6개 > triggerCount(3) → 트리거!

GetMessagesToSummarize:
  keepFromIndex = 6 - 2 = 4
  요약 대상: [0]~[3] (User, Asst(FC), Func, Asst(FC))
  유지 대상: [4]~[5] (Function, Assistant)
```

요약 생성 후 메시지 삭제:

```
CurrentSummary = "사용자가 john_doe 정보를 요청. get_user_id→user_123, get_user_details로 상세 조회함"

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "john_doe의 정보는 다음과 같습니다..."

SystemMessage:
  "You are a helpful assistant.

  [Previous conversation summary]
  사용자가 john_doe 정보를 요청. get_user_id→user_123, get_user_details로 상세 조회함"
```

### 6단계: 다음 사용자 질문

```
StreamAsync(User("이메일도 알려줘"))

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "john_doe의 정보는 다음과 같습니다..."
  [2] User: "이메일도 알려줘"
```

API 요청 빌드 시 `EnsureUserFirstMessage` 적용:

```
messages[0] = Function → User 아님 → 합성 User 삽입

API에 전송되는 메시지:
  [0] User: "(Continuing from previous conversation context)"  ← 합성
  [1] Function: "{id: user_123, ...}"
  [2] Assistant: "john_doe의 정보는 다음과 같습니다..."
  [3] User: "이메일도 알려줘"
```

## User-First 제약 처리

일부 API(Gemini, Claude)는 메시지 배열이 반드시 User 역할로 시작해야 합니다.
요약 트리밍 후 첫 메시지가 Assistant/Function일 수 있으므로, 해당 프로바이더의 요청 빌더에서 처리합니다.

```csharp
// AIService.cs
protected static void EnsureUserFirstMessage(List<Message> messages)
{
    if (messages.Count == 0) return;
    if (messages[0].Role == ActorRole.User) return;
    messages.Insert(0, new Message(ActorRole.User,
        "(Continuing from previous conversation context)"));
}
```

- **적용 대상**: Gemini, Claude (요청 빌더 4곳)
- **비적용**: OpenAI, Grok, DeepSeek, Sonar, Qwen (User-first 제약 없음)
- **원본 불변**: `GetLatestMessages().ToList()`로 복사본에만 적용

## 요약 타이밍이 라운드 완료 후인 이유

```
❌ 라운드 중간 요약:
  Round 0: FC 호출 → 결과 저장
  Round 1: [여기서 요약 발동] → FC 결과 삭제됨! → LLM이 맥락 잃음

✓ 라운드 완료 후 요약:
  Round 0: FC 호출 → 결과 저장
  Round 1: FC 호출 → 결과 저장
  Round 2: LLM이 모든 FC 결과로 텍스트 생성 (완료)
  [여기서 요약 발동] → 다음 턴을 위한 정리, 현재 응답에 영향 없음
```

## 컨텍스트 초과 복구: 트리거 요약과 다른 길

트리거 요약은 "다음 턴을 위한 정리"라 라운드 중간에 돌 이유가 없습니다. 하지만 **라운드 3에서 서버가
"컨텍스트 넘쳤다"고 거절**하면 이야기가 다릅니다. 턴이 끝날 때까지 기다릴 수가 없습니다 — 지금 줄이지
않으면 이 턴은 실패로 끝나니까요.

그래서 복구 압축은 라운드 안에서 돕니다. 위 ❌ 가 걱정한 "FC 결과 삭제"는 자르는 지점을 **마지막 User
메시지**까지만 당기는 클램프로 막습니다.

```
Round 0: FC 호출 → 결과 저장
Round 1: FC 호출 → 결과 저장
Round 2: [서버가 400 거절]
         → 지금 질문 '이전'의 옛 대화만 요약해서 접음
         → 지금 질문 + Round 0·1 의 FC 결과는 그대로 유지
         → Round 2 만 다시 실행 (Round 0·1 은 재실행 안 함 = 도구 중복 실행 없음)
```

옛 대화가 없어서 접을 게 없으면 **요약 요청조차 보내지 않고** 즉시 포기합니다. 억지로 지워봐야 요청은
안 줄고 이력만 잃기 때문입니다. 멈추는 장치는 셋입니다:

| 사유 | 뜻 |
|---|---|
| `nothing-to-cut` | 지금 질문 앞에 자를 게 없음 |
| `window-clipped` | 자를 구간이 이미 `MaxMessageCount` 창 밖 — 지워도 요청이 안 줄어듦 |
| `retries-exhausted` | `ContextRecoveryMaxRetries` 소진 |

세 경우 모두 **요약 호출도 삭제도 하지 않고** 원래 에러를 그대로 올립니다.

> **비스트리밍은 다릅니다.** 재시도가 공급자의 라운드 루프를 0번부터 다시 돌리므로, 이미 실행된 도구가
> 있으면 두 번 실행됩니다. 그래서 그 경우에는 복구하지 않고 `tool-side-effects` 사유로 멈춥니다.
> 라운드 단위 재생은 스트리밍 경로에만 있습니다.
