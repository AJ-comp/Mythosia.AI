# 함수 호출

함수 호출을 사용하면 모델이 정보를 가져오거나 작업을 수행할 때 내 C# 코드를 호출할 수 있습니다.

## 빠른 예제

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "특정 위치의 현재 날씨를 가져옵니다",
        ("location", "도시와 국가", required: true),
        (string location) => $"{location}의 날씨는 맑음, 22°C입니다"
    );

var response = await service.GetCompletionAsync("서울 날씨가 어때요?");
// 모델이 get_weather("Seoul, Korea")를 호출하고 결과를 반영합니다.
```

## 어트리뷰트로 함수 정의

복잡한 함수에는 `[AiFunction]`과 `[AiParameter]` 어트리뷰트를 사용합니다:

```csharp
using Mythosia.AI.Attributes;

[AiFunction("search_products", "상품 카탈로그를 검색합니다")]
public static string SearchProducts(
    [AiParameter("검색 쿼리", required: true)] string query,
    [AiParameter("최대 결과 수")] int limit = 5)
{
    // ... 구현
    return JsonSerializer.Serialize(results);
}
```

그런 다음 등록합니다:

```csharp
service.AddFunction(SearchProducts);
```

## 함수 호출 정책

모델이 함수를 호출할 수 있는 시점을 제어합니다:

```csharp
using Mythosia.AI.Models.Functions;

// 모델이 판단 (기본값)
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// 항상 함수를 호출하도록 강제
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// 함수 호출 비활성화
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
```

## FunctionBuilder 사용

함수 정의를 프로그래매틱하게 구성합니다:

```csharp
using Mythosia.AI.Builders;

var fn = FunctionBuilder
    .Create("get_stock_price", "현재 주가를 반환합니다")
    .AddParameter("ticker", "주식 티커 심볼", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
