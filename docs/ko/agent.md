# 에이전트 (ReAct 루프)

에이전트 루프는 모델이 루프를 직접 작성하지 않아도 함수를 반복 호출하고 결과를 반영해 최종 답변에 도달할 때까지 자율적으로 목표를 추구하게 합니다.

## 기본 사용법

함수를 등록한 후 목표와 함께 `RunAgentAsync`를 호출합니다:

```csharp
var service = new ChatGptService(apiKey, http)
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

## 동작 방식

각 단계:
1. LLM이 목표 + 대화 기록 + 함수 정의를 받음
2. LLM이 함수를 호출하면 → 실행하고 결과를 기록에 추가
3. LLM이 텍스트 응답을 반환하면 → 루프 종료, 응답 반환
4. 단계 수가 `maxSteps`에 도달하면 → `AgentMaxStepsExceededException` 발생
