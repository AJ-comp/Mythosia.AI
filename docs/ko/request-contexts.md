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
