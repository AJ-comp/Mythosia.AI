# 토큰 사용량

토큰 사용량은 모델 요청에서 입력, 출력, 캐시, 추론에 얼마나 많은 토큰이 쓰였는지를 보여줍니다. Mythosia.AI에서는 스트리밍 이벤트의 `TokenUsage`로 이 값을 받을 수 있습니다.

특히 대화가 한 번의 LLM 호출로 끝나지 않을 때 중요합니다. 일반 답변은 보통 한 라운드로 끝나지만, agent나 function calling은 모델 호출, 함수 실행, 함수 결과를 포함한 다음 모델 호출로 이어질 수 있습니다. 그래서 토큰 사용량에는 두 가지 기준이 있습니다.

- `RoundUsage`는 방금 끝난 LLM 라운드 1회의 사용량입니다.
- `Completion.Usage`는 전체 스트리밍 실행의 누적 사용량입니다.

## 라운드란 무엇인가요?

"라운드"란 모델에 요청을 보내고 응답을 받는 한 번의 왕복을 말합니다. 일반 채팅 메시지라면 전체 상호작용이 딱 한 라운드로 끝납니다.

Function calling이나 agent를 사용하면 라운드가 자동으로 늘어납니다. 구체적인 예를 들어보겠습니다. 사용자가 *"지금 서울 날씨 알려줘"*라고 물었습니다.

**1라운드 — 도구 결정**

앱이 사용자 메시지를 모델에 전달합니다. 모델은 현재 날씨를 알 방법이 없으므로 직접 답하지 않고 함수 호출 요청으로 응답합니다. *"`GetWeather("Seoul")`을 호출해 주세요"* — 여기서 모델 응답이 끝납니다.

**라운드 사이**

앱이 `GetWeather("Seoul")`을 실행하고 결과 `"15°C, 흐림"`을 받습니다.

**2라운드 — 최종 답변**

앱이 함수 결과를 새 메시지로 모델에 다시 전달합니다. 이제 모델은 필요한 정보를 모두 갖췄으므로 사용자에게 전달할 최종 답변을 작성합니다. *"현재 서울은 15°C이고 흐립니다."*

사용자 메시지 하나에 LLM 라운드가 두 번 발생했습니다. 만약 모델이 다른 도구를 한 번 더 호출해야 했다면 3라운드가 되었을 것이고, 이런 식으로 라운드가 늘어납니다.

`RoundUsage`는 각 라운드가 끝날 때마다 발생하며 해당 라운드의 토큰 수만 담습니다. `Completion.Usage`는 모든 라운드가 끝난 뒤 한 번만 발생하며 전체 라운드의 합계를 담습니다.

## 왜 필요한가요

토큰 사용량은 목적에 따라 다르게 쓰입니다.

채팅 UI의 컨텍스트 토큰 미터에는 마지막 `RoundUsage.Usage.TotalTokens`가 가장 잘 맞습니다. 이 값은 “지금 대화 상태가 다음 LLM 호출의 입력으로 들어가면 어느 정도 크기인가”에 가장 가깝습니다.

로그, 진단, 비용 분석에는 `Completion.Usage.TotalTokens`를 쓰면 됩니다. function calling이나 agent 흐름에서 여러 라운드가 발생해도 전체 실행의 누적값으로 남습니다.

성능 튜닝에는 캐시와 추론 관련 필드가 도움이 됩니다. 입력 캐시가 실제로 쓰였는지, reasoning 모델이 내부 추론에 얼마나 썼는지 확인할 수 있습니다.

## 이벤트 모델

| 이벤트 | 의미 | 주로 쓰는 곳 |
|---|---|---|
| `StreamingContentType.RoundUsage` | 방금 끝난 LLM 라운드의 사용량 | UI 토큰 미터, 라운드별 디버깅 |
| `StreamingContentType.Completion` | 최종 스트림 이벤트와 전체 누적 사용량 | 로그, 진단, 비용 리포트 |

`RoundUsage.Usage`는 누적값이 아닙니다. 예를 들어 1라운드가 10,100 토큰, 2라운드가 14,000 토큰을 썼다면 최종 `Completion.Usage.TotalTokens`는 24,100이 될 수 있지만, 마지막 `RoundUsage.Usage.TotalTokens`는 그대로 14,000입니다.

`RoundUsage`에는 다음 정보도 같이 들어갑니다.

| 속성 | 의미 |
|---|---|
| `RoundIndex` | 1부터 시작하는 LLM 라운드 번호 |
| `IsFinalRound` | 현재 라운드가 스트림의 마지막 LLM 라운드이면 `true` |

토큰 사용량은 provider가 usage 데이터를 반환할 때 emit됩니다. usage 이벤트를 받기 위해 `IncludeMetadata = true`를 켤 필요는 없습니다.

## 최종 누적 사용량 읽기

전체 스트리밍 요청의 누적 사용량이 필요하면 `Completion.Usage`를 읽습니다.

```csharp
await foreach (var chunk in service.StreamAsync("양자 컴퓨팅을 설명해 주세요", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

단일 LLM 라운드에서는 이 값이 라운드 사용량과 거의 비슷합니다. agent 실행에서는 모든 LLM 라운드를 합친 값입니다.

## UI 토큰 미터

컨텍스트 크기 미터에는 가장 최근의 `RoundUsage`를 사용하세요.

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

마지막 모델 라운드는 함수 결과까지 반영된 최신 대화 상태를 보고 실행됩니다. 그래서 채팅 UI에서는 마지막 `RoundUsage.TotalTokens`가 응답 직후 컨텍스트 크기를 가장 잘 나타냅니다.

## Function Calling과 Agent

function calling 흐름에서는 모델이 여러 번 실행될 수 있습니다. UI에서는 매번 `RoundUsage`를 받고 마지막 값을 유지하고, 실행 전체의 누적값은 마지막 `Completion.Usage`에서 읽으면 됩니다.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function result: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.TotalTokens}");
Console.WriteLine($"Run total: {cumulative?.TotalTokens}");
```

## 캐시와 추론 필드

provider가 제공하는 경우 `TokenUsage`에는 캐시와 추론 관련 값도 들어갑니다.

```csharp
if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
{
    var usage = chunk.Usage;

    Console.WriteLine($"Cached input: {usage.CachedInputTokens}");
    Console.WriteLine($"Cache created: {usage.CacheCreationTokens}");
    Console.WriteLine($"Reasoning:     {usage.ReasoningTokens}");
    Console.WriteLine($"Visible output:{usage.VisibleOutputTokens}");
}
```

| 속성 | 의미 |
|---|---|
| `InputTokens` | 프롬프트/입력 토큰 |
| `OutputTokens` | 모델이 생성한 출력 토큰 |
| `TotalTokens` | 해당 이벤트 범위의 입력 + 출력 |
| `CachedInputTokens` | 캐시에서 재사용된 입력 토큰 |
| `CacheCreationTokens` | 캐시에 새로 기록된 토큰 |
| `ReasoningTokens` | 숨겨진 내부 추론에 사용된 토큰 |
| `VisibleOutputTokens` | 추론 토큰을 제외한 실제 출력 토큰 |

## Provider별 주의점

provider마다 usage 데이터를 붙여 주는 스트림 chunk가 다릅니다. Mythosia.AI는 이를 `RoundUsage`와 최종 `Completion.Usage` 이벤트로 정규화해서 전달합니다.

Gemini가 가장 까다로운 편입니다. usage가 text나 status chunk에 붙어 올 수 있고, function call chunk 뒤늦게 도착할 수도 있습니다. 라이브러리는 다음 라운드로 넘어가기 전에 스트림을 끝까지 읽어 그 usage를 수집합니다.

소비자 코드는 provider별 metadata를 직접 파싱하기보다 정규화된 `RoundUsage`와 `Completion.Usage`를 읽는 쪽을 권장합니다.
