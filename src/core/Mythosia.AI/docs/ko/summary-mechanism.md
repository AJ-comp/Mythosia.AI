# Conversation Summary Mechanism

## 개요

긴 대화에서 토큰 비용과 컨텍스트 윈도우 제한을 관리하기 위한 자동 요약 메커니즘입니다.
`SummaryConversationPolicy`가 트리거 조건을 감지하면, 오래된 메시지를 요약 텍스트로 압축하고 최근 메시지만 유지합니다.

## 핵심 설계 원칙

1. **요약 타이밍**: 함수 호출 체인(라운드) 중간이 아닌, 모든 라운드가 완료된 후에만 요약 발동
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
