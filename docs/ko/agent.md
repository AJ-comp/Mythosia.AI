# 에이전트 (ReAct 루프)

## 에이전트 루프가 필요한 이유

일반 함수 호출에서는 모델이 요청당 **한 번**의 함수를 호출하고, 실행한 뒤 대화가 계속됩니다. 하지만 실제 많은 작업은 모델이 자율적으로 계획하고 실행해야 하는 **여러 단계**를 필요로 합니다:

- "상위 3개 AI 기업을 조사하고 주가를 비교해 줘" — 여러 번의 웹 검색과 주가 조회가 필요
- "관련 정책을 찾고, 주문 상태를 확인한 다음, 환불 대상인지 알려줘" — 다른 도구들을 논리적 순서로 연결해야 함
- 첫 번째 결과가 부족하면 모델이 검색을 **재시도하거나 개선**해야 할 수도 있음

이 오케스트레이션 루프를 직접 작성하는 것은 번거롭고 오류가 발생하기 쉽습니다. **에이전트 루프**(ReAct 패턴: 추론 → 행동 → 관찰 → 반복)가 이를 자동으로 처리합니다 — 모델이 최종 답변에 도달할 때까지 각 단계에서 다음 행동을 스스로 결정합니다.

## 기본 사용법

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// 또는 확장 메서드로:
service.WithMaxRounds(15).WithTimeout(60);
```

미리 정의된 정책:

```csharp
service.WithFastPolicy();    // 낮은 타임아웃, 적은 라운드 — 빠른 작업용
service.WithComplexPolicy(); // 높은 타임아웃, 많은 라운드 — 심층 연구용
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

컨텍스트는 `AsyncLocal`을 통해 전파되므로, 동일한 서비스 인스턴스에서 동시에 실행되는 여러 에이전트 호출이 서로 간섭하지 않습니다.

사용 가능한 속성 전체 목록은 [AIRequestContext](request-contexts.md) 문서를 참고하세요 (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Mythosia.AI v6.3.0 이상에서 사용 가능합니다.

## 동작 방식

각 단계:

1. LLM이 목표 + 대화 기록 + 함수 정의를 받음
2. LLM이 함수를 호출하면 → 실행하고 결과를 기록에 추가
3. LLM이 텍스트 응답을 반환하면 → 루프 종료, 응답 반환
4. 단계 수가 `maxSteps`에 도달하면 → `AgentMaxStepsExceededException` 발생
