# AIRequestContext

## 개요

`AIRequestContext`는 **모델이 보는 내용을 단일 요청에 대해서만 변경**합니다 — 추가 지시사항 주입, 참고 문서 추가, 또는 사용자 메시지의 완전한 교체 — 서비스의 시스템 메시지나 대화 기록을 영구적으로 변경하지 않으면서.

## 기존 방식의 한계

관련 문서를 검색한 뒤 프롬프트에 포함해야 하는 RAG 파이프라인을 생각해 보세요. `AIRequestContext` **없이** 하면 시스템 메시지를 직접 수정해야 합니다:

```csharp
// ❌ AIRequestContext 없이 — 시스템 메시지를 오염시킴
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\n다음 컨텍스트를 사용해 답변하세요:\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// 복원 — 하지만 이 컨텍스트는 대화 기록에도 이미 남아있음
service.SystemMessage = originalSystem;
```

이 방식의 문제점:

- 검색된 컨텍스트가 **대화 기록에 누출**됩니다 — 이후 요청에서도 계속 보입니다
- 시스템 메시지를 복원해도 기록 오염은 되돌릴 수 없습니다
- 멀티유저 웹앱에서 공유 상태를 변경하면 경쟁 조건이 발생합니다

`AIRequestContext`를 **사용하면** 주입은 정확히 한 요청에만 적용됩니다:

```csharp
// ✅ AIRequestContext 사용 — 깔끔하고, 범위가 한정되며, 부작용 없음
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\n다음 컨텍스트를 사용해 답변하세요:\n{retrievedDocs}"
    });
```

시스템 메시지는 이 한 번의 호출에서만 수정됩니다. 다음 요청은 원래 시스템 메시지를 봅니다. 정리가 필요 없습니다.

## 사용 가능한 속성

### SystemMessagePrefix

이 요청에 한해 시스템 메시지 앞에 텍스트를 추가합니다:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "오늘 날짜는 2026-03-31입니다.\n"
};

var response = await service.GetCompletionAsync("오늘 무슨 요일인가요?", context);
```

**사용 시점:** 요청마다 변하는 동적 메타데이터(날짜, 사용자 시간대, 세션 정보)를 주입할 때.

### SystemMessageSuffix

이 요청에 한해 시스템 메시지 뒤에 텍스트를 추가합니다:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\n항상 한국어로 답변하세요."
};

var response = await service.GetCompletionAsync("Hello!", context);
```

**사용 시점:** 요청별 행동 지시, RAG 컨텍스트, 또는 언어 선호를 추가할 때.

### AdditionalMessages

이 요청에 한해 대화에 추가 메시지를 삽입합니다 — 참고 문서나 few-shot 예제 주입에 유용합니다:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("참고 문서: 환불 정책은 30일 이내 반품을 허용합니다.").Build()
    }
};

var response = await service.GetCompletionAsync("환불 대상인가요?", context);
```

**사용 시점:** 대화 기록에 남기지 않아야 할 참고 자료, few-shot 예제, 또는 보조 컨텍스트를 제공할 때.

### RequestMessageOverride

이 요청의 사용자 메시지를 완전히 교체합니다. 원래 프롬프트는 무시됩니다:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"다음 컨텍스트를 기반으로 질문에 답하세요.\n\n컨텍스트: {docs}\n\n질문: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context);
```

**사용 시점:** 미들웨어 레이어(RAG, 쿼리 재작성)가 모델에 보내기 전에 프롬프트를 완전히 재구성해야 하지만, 원래 사용자 입력은 대화 기록에 유지하고 싶을 때.

> **💡 참고:** `.WithRag()`를 사용하면 RAG 파이프라인이 이 속성을 자동으로 활용합니다. 내부 동작 원리는 [파이프라인 커스터마이징 — 내부 동작 원리](rag-pipeline.md#내부-동작-원리)를 참조하세요.

## 적용 전후 비교

### 시나리오: 날짜 주입과 검색된 컨텍스트를 포함하는 RAG

**AIRequestContext 없이:**

```csharp
// ❌ 지저분하고, 상태를 변경하며, 오류가 발생하기 쉬움
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\n오늘: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\n컨텍스트:\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // few-shot 예제 제거
```

**AIRequestContext 사용:**

```csharp
// ✅ 깔끔하고, 상태 변경 없으며, 부작용 없음
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"오늘: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\n컨텍스트:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
        }
    });
```

## AIRequestProfile과 결합

단일 요청에 대한 최대한의 제어를 위해 둘 다 함께 전달할 수 있습니다:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n컨텍스트:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("예제: ...").Build()
        }
    }
);
```

생성 파라미터 오버라이드에 대한 자세한 내용은 [AIRequestProfile](request-profiles.md)을 참조하세요.

## `SystemMessageProvider`로 자동 주입

### 이 기능이 해결하는 문제

일반적인 채팅 앱은 같은 베이스라인(오늘 날짜, 활성 폴더, 세션 정보 등)을 필요로 하는 LLM 진입점이 여러 개 있습니다. `SystemMessageProvider` **없이**는 모든 호출 지점에서 그 컨텍스트를 매번 만들어 전달하는 것을 기억해야 합니다:

```csharp
// ❌ SystemMessageProvider 없이 — 모든 진입점에서 주입을 기억해야 함
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. 메인 채팅 응답
var answer = await service.GetCompletionAsync(userMessage,
    new AIRequestContext { SystemMessageSuffix = today });

// 2. 제목 생성기 (나중에 추가됨)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 3. 요약기 (더 나중에 추가됨)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 4. Agent 호출 — 깜빡하기 쉬움! 컴파일러가 경고해주지 않음
var agentResult = await service.RunAgentAsync(goal);  // ← 날짜 누락, 조용한 버그
```

이 방식의 문제점:

- 같은 컨텍스트 빌드 조각이 모든 호출 지점에 **중복**됩니다
- 새 진입점(위의 `RunAgentAsync`)을 **누락하기 쉬우며** 컴파일 시점 체크가 없습니다
- LLM 호출을 추가하는 모든 새 기능이 이 관례를 기억해야 합니다
- 테스트에서도 각 호출 지점마다 컨텍스트 설정을 복제해야 합니다

`SystemMessageProvider`를 사용하면 베이스라인을 **한 번만 등록**하고 모든 외부 호출이 자동으로 받아갑니다:

```csharp
// ✅ SystemMessageProvider 사용 — 한 번 등록하고 모든 곳에 적용됨
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// 아래 모두 자동으로 베이스라인을 받습니다 — per-call 보일러플레이트 없음
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← 이것도 베이스라인 받음

// 스트리밍 엔트리 포인트도 동일 — 같은 베이스라인, per-call 보일러플레이트 없음
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### 동작 방식

`WithSystemMessageProvider` 플루언트 헬퍼로 콜백을 한 번 등록합니다. 모든 외부 호출(`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`)이 자동으로 이를 호출해 베이스라인 컨텍스트를 만듭니다:

```csharp
// 보통 서비스 생성 / DI 설정 시점에 등록
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### IO를 수반하는 provider를 위한 async 오버로드

베이스라인 컨텍스트가 DB, 캐시, HTTP 호출에서 오는 경우 async 오버로드를 사용하세요. provider가 `.Result` / `.GetAwaiter().GetResult()`로 블로킹할 필요가 없습니다. 오버로드 분기는 람다 arity로 자동 — sync는 인자 없음, async는 `CancellationToken` 하나:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

비스트리밍 경로(`GetCompletionAsync`, `RunAgentAsync`)는 설계상 취소를 지원하지 않습니다 — 시그니처에 `CancellationToken`을 받지 않으며 provider에는 항상 `CancellationToken.None`이 전달됩니다. Provider에서 취소가 필요한 경우(예: 오래 걸리는 DB 쿼리)에는 호출자의 토큰을 provider 콜백까지 전파하는 스트리밍 경로(`StreamAsync`, `RunAgentStreamAsync`)를 사용하세요.

### 명시적 per-call 컨텍스트와의 병합

등록된 provider가 있고 호출 시 명시적 `AIRequestContext`도 함께 전달되면 두 컨텍스트는 필드 단위로 병합됩니다:

| 필드 | 병합 규칙 |
|---|---|
| `SystemMessagePrefix` | 명시적 값이 non-null이면 그것이 우선, 아니면 provider |
| `SystemMessageSuffix` | 명시적 값이 non-null이면 그것이 우선, 아니면 provider |
| `RequestMessageOverride` | 명시적 값이 non-null이면 그것이 우선, 아니면 provider |
| `AdditionalMessages` | 연결(provider가 먼저, 그 다음 명시적) |

근거: 일반적인 케이스는 "provider가 베이스라인을 제공하고, 특정 호출이 스칼라 필드 하나를 교체하거나 메시지를 추가하고 싶다"입니다 — 필드 단위 override는 예상치 못한 연결 없이 의미를 예측 가능하게 유지합니다.

### 호출당 invocation

Provider는 **요청당 한 번** 호출되므로 반환 값은 그 순간의 상태(타임스탬프, 세션 등)를 반영할 수 있습니다. `null` 반환은 no-op이며, 해당 호출에 대해 `SystemMessageProvider`를 설정하지 않은 것과 동일합니다.

> Mythosia.AI v6.3.0+에서 사용 가능.
