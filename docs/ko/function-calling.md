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

## 클래스에서 일괄 등록

`[AiFunction]` 어트리뷰트가 붙은 메서드를 한 번에 등록합니다:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // [AiFunction] 인스턴스 메서드를 스캔

// 정적 메서드의 경우
service.WithStaticFunctions<MyTools>();  // [AiFunction] 정적 메서드를 스캔
```

## 비동기 함수 핸들러

모든 `WithFunction` 오버로드에 `Func<..., Task<string>>`을 받는 `WithFunctionAsync` 대응 메서드가 있습니다:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "외부 API에서 데이터를 가져옵니다",
    ("url", "가져올 URL", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

동기 버전과 동일하게 0~3개 파라미터를 지원합니다.

## 함수 일시 비활성화

등록을 제거하지 않고 단일 요청에서 함수 호출을 비활성화합니다:

```csharp
// 확장 메서드 — 함수 비활성화 상태로 결과 반환
string answer = await service.AskWithoutFunctionsAsync("직접 답변해주세요");

// 또는 속성 토글
service.WithoutFunctions();  // FunctionsDisabled = true 설정
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
