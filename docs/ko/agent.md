# 에이전트 (ReAct 루프)

## 에이전트 루프가 필요한 이유

일반 함수 호출도 모델의 한 응답에서 요청된 **여러 함수를 순서가 보장된 배치로** 실행하고 다음 도구 라운드로 이어갈 수 있습니다. Agent API는 이 메커니즘을 명시적인 **단계 제한**이 있는 목표 지향 ReAct 루프로 묶어, 최종 답변이 나올 때까지 각 배치 결과를 모델에 다시 전달합니다:

- "상위 3개 AI 기업을 조사하고 주가를 비교해 줘" — 여러 번의 웹 검색과 주가 조회가 필요
- "관련 정책을 찾고, 주문 상태를 확인한 다음, 환불 대상인지 알려줘" — 다른 도구들을 논리적 순서로 연결해야 함
- 첫 번째 결과가 부족하면 모델이 검색을 **재시도하거나 개선**해야 할 수도 있음

`GetCompletionAsync`와 `StartRunAsync`도 공통 모델·도구 반복 실행을 처리합니다. 기존 에이전트 편의 함수가 추가하는 것은 호출별 라운드 제한과 에이전트 전용 오류 변환이며, 별도의 계획 엔진이나 실행 엔진을 만드는 것은 아닙니다.

## 진행 상황을 제어하며 실행하기

여러 도구를 거치는 작업을 화면에 표시하거나 중단하려면 `StartRunAsync`로 실행 객체를 보관합니다. 지원 모델에서는 작업 중 추가 지시도 보낼 수 있습니다. 자세한 선택 기준은 [Run 사용 안내](execution-api-transition.md)를 참고하세요.

```csharp
// 작업을 시작하기 전에 service에 함수를 등록합니다.
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "정책을 찾고 주문 상태를 확인해서 결과를 설명해줘.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

## 기존 에이전트 API 호환

`RunAgentAsync`와 `RunAgentStreamAsync`는 `[Obsolete]` 경고와 함께 기존 동작을 유지합니다. 아래는 기존 호출을 유지·이관할 때 참고할 예제입니다.

함수를 등록한 후 목표와 함께 `RunAgentAsync`를 호출합니다:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "웹에서 정보를 검색합니다",
        ("query", "검색 쿼리", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "현재 주가를 가져옵니다",
        ("ticker", "주식 티커 심볼", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "상위 3개 AI 기업의 현재 주가는 얼마인가요?",
    maxSteps: 10
);

Console.WriteLine(result);
```

모델은 필요에 따라 함수를 호출하고, 결과를 관찰하고, 최종 텍스트 응답을 생성할 때까지 다음 단계를 결정합니다.

## maxSteps

`maxSteps`는 LLM→함수 호출 라운드 수를 제한합니다. 한도 내에 완료되지 않으면 `AgentMaxStepsExceededException`이 발생합니다:

```csharp
try
{
    string result = await service.RunAgentAsync("조사하고 요약해 주세요...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponse에 모델이 지금까지 생성한 내용이 담겨 있습니다
    Console.WriteLine($"조기 종료: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

에이전트 루프의 라운드별 동작을 제어합니다:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// 기존 RunAgentAsync는 DefaultPolicy와 명시적인 maxSteps 인자를 사용합니다.
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "조사하고 요약해 주세요...", maxSteps: 15);
```

미리 정의된 정책:

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // 낮은 타임아웃, 적은 라운드 — 빠른 작업용
var fastResult = await service.RunAgentAsync(
    "조사하고 요약해 주세요...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // 높은 타임아웃, 많은 라운드 — 심층 연구용
var complexResult = await service.RunAgentAsync(
    "조사하고 요약해 주세요...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## 호출별 요청 컨텍스트

`RunAgentAsync`와 `RunAgentStreamAsync`는 선택적 `AIRequestContext`를 받아 동적 system message prefix/suffix, 참조 문서, 또는 목표 메시지 교체를 **단일 에이전트 실행 범위**로 한정하여 주입할 수 있습니다 — 서비스의 system message나 대화 기록을 영구 변경하지 않습니다.

```csharp
string result = await service.RunAgentAsync(
    goal: "환불 정책을 찾아서 주문 #1234가 대상이 되는지 확인해줘.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"오늘 날짜는 {DateTime.UtcNow:yyyy-MM-dd}입니다.\n",
        SystemMessageSuffix = "\n항상 참조한 정책 조항을 인용하세요."
    });
```

스트리밍 버전도 동일한 파라미터를 받습니다:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "상위 3개 AI 기업의 주가를 조사해줘.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"사용자 타임존: {userTz}\n"
    }))
{
    // 콘텐츠 처리
}
```

`AIRequestContext`는 `AsyncLocal`로 전파되지만 서비스의 대화 기록과 실행 정책까지 동시 사용에 안전해지는 것은 아닙니다. 독립적인 동시 작업에는 별도 서비스 인스턴스를 사용하세요.

사용 가능한 속성 전체 목록은 [AIRequestContext](request-contexts.md) 문서를 참고하세요 (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Mythosia.AI v6.3.0 이상에서 사용 가능합니다.

## 동작 방식

각 단계:

1. LLM이 목표 + 대화 기록 + 함수 정의를 받음
2. LLM이 함수를 호출하면 → 실행하고 결과를 기록에 추가
3. LLM이 텍스트 응답을 반환하면 → 루프 종료, 응답 반환
4. 단계 수가 `maxSteps`에 도달하면 → `AgentMaxStepsExceededException` 발생
