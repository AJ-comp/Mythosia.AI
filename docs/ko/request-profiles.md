# AIRequestProfile

## 개요

`AIRequestProfile`은 생성 파라미터 — Temperature, MaxTokens, Stateless 모드, 함수 호출 — 를 **단일 요청에 대해서만** 오버라이드합니다. 서비스의 전역 설정은 그대로 유지됩니다.

## 기존 방식의 한계

창의적인 대화용으로 설정된 챗봇이 있다고 가정합니다:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("당신은 창의적인 글쓰기 도우미입니다.");
```

이제 RAG 파이프라인에서 사용자의 쿼리를 낮은 Temperature로, 기록 없이 재작성해야 합니다. `AIRequestProfile` **없이** 하면 이렇게 됩니다:

```csharp
// ❌ AIRequestProfile 없이 — 수동 상태 관리
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("이 쿼리를 재작성해 주세요: ...");

// 모두 복원 — 까먹기 쉽고, 스레드 안전하지 않음
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

이 방식은 장황하고, 오류가 발생하기 쉬우며, **멀티스레드 시나리오에서 깨집니다** (예: 동시 사용자를 처리하는 웹 서버). 복원 전에 예외가 발생하면 서비스가 손상된 상태로 남습니다.

`AIRequestProfile`을 **사용하면** 한 줄입니다:

```csharp
// ✅ AIRequestProfile 사용 — 깔끔하고 안전
var rewritten = await service.GetCompletionAsync("이 쿼리를 재작성해 주세요: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

서비스의 전역 설정은 전혀 건드리지 않습니다. 정리도 필요 없습니다. 스레드 안전합니다.

## 사용 가능한 속성

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Temperature 오버라이드
    MaxTokens = 256,          // 최대 출력 토큰 오버라이드
    Stateless = true,         // 이 교환을 대화 기록에 추가하지 않음
    DisableFunctions = true,  // 이 요청에서 함수 호출 건너뜀
    DisableReasoning = true   // 이 요청에서 추론/사고 과정 건너뜀
};

var response = await service.GetCompletionAsync("프롬프트", profile);
```

모든 속성은 선택 사항입니다 — 오버라이드할 것만 설정하세요. 설정하지 않은 것은 서비스의 현재 값을 사용합니다.

## 미리 정의된 프로필

일반적인 시나리오를 위해 속성을 수동으로 설정하지 않아도 되는 내장 프로필이 제공됩니다:

```csharp
// 쿼리 재작성: 낮은 Temperature, 작은 토큰 예산, Stateless
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// 요약: 약간 높은 Temperature, 적당한 토큰
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## 실제 사용 예제

### RAG 파이프라인에서 내부 쿼리 재작성

```csharp
// 사용자 대화용으로 설정된 메인 서비스
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// 다른 설정으로 쿼리 재작성 — 서비스는 변경되지 않음
var betterQuery = await service.GetCompletionAsync(
    $"검색을 위해 재작성해 주세요: {userQuery}",
    RequestProfiles.QueryRewrite);

// 일반 대화 계속 — 여전히 Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### 특정 단계에서 함수 비활성화

```csharp
// 서비스에 함수가 등록된 상태
service.WithFunction("search_web", "웹 검색", ...);

// 이 한 번의 호출에서만 함수 호출 건너뛰기 — 직접 답변만
var directAnswer = await service.GetCompletionAsync(
    "2 + 2는 뭔가요?",
    new AIRequestProfile { DisableFunctions = true });
```

## AIRequestContext와 결합

최대한의 제어를 위해 둘 다 함께 전달할 수 있습니다:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\n간결하게 답하세요." }
);
```

요청에 콘텐츠 주입에 대한 자세한 내용은 [AIRequestContext](request-contexts.md)를 참조하세요.
