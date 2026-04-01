# 요청 프로필 및 컨텍스트

서비스의 전역 상태를 변경하지 않고 단일 요청에 대한 설정을 재정의할 수 있습니다.

## AIRequestProfile

요청별 파라미터 재정의 모음입니다. `GetCompletionAsync` 또는 `StreamAsync`에 전달합니다:

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,
    MaxTokens = 256,
    Stateless = true,        // 이 요청을 기록에 추가하지 않음
    DisableFunctions = true, // 이 요청에서 함수 호출 건너뜀
    DisableReasoning = true  // 이 요청에서 추론 건너뜀
};

var response = await service.GetCompletionAsync("요약해 주세요.", profile);
```

### 미리 정의된 프로필

일반적인 사용 사례를 위한 두 가지 내장 프로필:

```csharp
// 낮은 온도, 작은 토큰 예산, 무상태 — 쿼리 재작성용
var response = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// 약간 높은 온도, 적당한 토큰 — 요약용
var response = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## AIRequestContext

서비스의 시스템 메시지나 기록을 건드리지 않고 단일 요청에 추가 콘텐츠를 주입합니다:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "오늘 날짜는 2026-03-31입니다.\n",
    SystemMessageSuffix = "\n항상 한국어로 답변하세요.",
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("참고 문서: ...").Build()
    }
};

var response = await service.GetCompletionAsync("질문에 답해 주세요.", context);
```

### RequestMessageOverride

이 호출에 한해 요청 메시지를 완전히 교체합니다:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User("검색된 컨텍스트를 기반으로 재구성된 프롬프트...")
        .Build()
};

await service.GetCompletionAsync(originalPrompt, context);
```

## 프로필과 컨텍스트 결합

둘 다 함께 전달할 수 있습니다:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\n간결하게 답하세요." }
);
```
