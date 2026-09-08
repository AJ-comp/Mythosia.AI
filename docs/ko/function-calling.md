# 함수 호출

## 함수 호출이 필요한 이유

LLM은 텍스트만 생성할 수 있습니다 — 날씨를 확인하거나, 데이터베이스를 쿼리하거나, API를 호출하는 것은 스스로 할 수 없습니다. 함수 호출 **없이** 하면 모델의 의도를 수동으로 파싱해야 합니다:

```csharp
// ❌ 함수 호출 없이 — 수동 의도 파싱
var reply = await service.GetCompletionAsync("서울 날씨 어때?");
// reply = "날씨 정보를 확인하려면 날씨 서비스를 조회해야 합니다."

// 사용자가 날씨를 원하는지 파악하고, "서울"을 추출하고, API를 직접 호출해야 함
if (reply.Contains("날씨"))
{
    var city = ExtractCity(reply); // 취약한 정규식이나 키워드 매칭
    var weather = await weatherApi.GetAsync(city);
    // 이제 날씨 데이터를 주입해서 다시 요청...
}
```

이 방식은 취약하고, 확장되지 않으며, 가능한 모든 사용자 의도를 미리 예측해야 합니다. 함수 호출을 **사용하면** 모델이 **언제** 코드를 호출하고 **어떤 인자**를 전달할지 스스로 결정합니다:

```csharp
// ✅ 함수 호출 사용 — 모델이 의도 파악 + 인자 추출을 처리
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "특정 위치의 현재 날씨를 가져옵니다",
        ("location", "도시와 국가", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("서울 날씨 어때?");
// 모델이 get_weather("서울, 한국")을 호출하고, 결과를 받아 자연스럽게 답변합니다.
```

개발자는 코드가 **무엇을** 할 수 있는지 정의하고, 모델은 **언제** 그리고 **어떻게** 사용할지를 판단합니다.

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
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "상품 카탈로그를 검색합니다")]
    public string SearchProducts(
        [AiParameter("검색 쿼리", required: true)] string query,
        [AiParameter("최대 결과 수")] int limit = 5)
    {
        // ... 구현
        return JsonSerializer.Serialize(results);
    }
}
```

그런 다음 등록합니다:

```csharp
service.WithFunctions(new ProductFunctions());
```

## 함수 호출 정책

모델이 함수를 호출할 수 있는 시점을 제어합니다:

```csharp
using Mythosia.AI.Models.Functions;

// 모델이 판단 (기본값)
service.FunctionCallMode = FunctionCallMode.Auto;

// 항상 함수를 호출하도록 강제
service.ForceFunctionName = "search_products";

// 함수 호출 비활성화
service.FunctionCallMode = FunctionCallMode.None;
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
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("현재 주가를 반환합니다")
    .AddParameter("ticker", "string", "주식 티커 심볼", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

## 모델의 비동기 도구 호출

외부 조회에 몇 초가 걸리더라도, 모델이 그 결과와 무관한 설명까지 기다릴 필요는 없는 경우가 있습니다. 예를 들어 날씨를 조회하는 동안 일반적인 여행 준비물을 먼저 안내할 수 있습니다. 비동기 도구 호출은 이런 독립적인 작업을 진행하고, 조회가 끝나면 결과를 이어서 반영할 수 있게 합니다. 결과에 의존하는 판단은 실제 도구 결과를 받은 뒤에 해야 합니다.

GPT-6 Astra 지원과 비동기 도구 호출은 `Mythosia.AI` 7.1.0부터 제공하며, 공통 타입은 `Mythosia.AI.Abstractions` 3.1.0에 포함됩니다.

`FunctionDefinition.AllowAsync`의 기본값은 `false`입니다. 함수가 실행되는 동안 모델이 다른 작업을 계속해도 되는 경우에만 `true`로 설정하거나 `FunctionBuilder.WithAsync()`를 호출하세요. `WithAsync(false)`로 다시 끌 수 있습니다. 같은 함수 정의와 핸들러를 여러 공급자에서 그대로 재사용합니다.

어트리뷰트 등록에서도 `[AiFunction("lookup", "데이터 조회", AllowAsync = true)]`로 같은 옵션을 지정할 수 있습니다.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("서울의 예시 날씨를 반환합니다")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "서울의 예시 날씨를 조회해줘. 기다리는 동안 여행 준비물 세 가지를 설명해줘.");
```

Mythosia는 GPT-6 Astra의 Responses API에서 `async: true`를 전송합니다. 미지원 모델과 API에서는 이 필드를 생략하고 같은 핸들러의 결과를 기다립니다. 사용자가 지정한 `AllowAsync` 값은 바꾸지 않습니다. 실제 호출도 공급자가 비동기로 표시해야 하므로(`FunctionCall.IsAsync`), 옵션을 켰다고 비동기 실행이 보장되는 것은 아닙니다.

`WithFunctionAsync`는 .NET 비동기 핸들러를 등록하는 메서드이고, `FunctionExecutionMode.Parallel`은 로컬 핸들러의 실행 방식을 제어합니다. 둘 다 이 옵션을 자동으로 켜지 않습니다. `AllowAsync`는 함수 결과가 나오기 전에 모델이 작업을 계속하도록 허용하는 별도 설정입니다. `FunctionExecutionMode`는 일반 호출에 계속 적용됩니다. 허용한 비동기 작업은 `Sequential` 모드에서도 겹쳐 실행될 수 있으며, 별도 작업 풀 전체가 `MaxConcurrency` 제한을 공유합니다.

실행 중인 도구 작업은 이를 시작한 `GetCompletionAsync`, 기존 `service.StreamAsync`, 또는 `StartRunAsync` 실행 안에서 관리하며, 결과는 원래 호출 ID에 연결해 전달합니다. 성공한 완료 반환과 Run의 `Result`는 대기 중인 결과를 처리한 뒤 완료됩니다. [Run 사용 안내](execution-api-transition.md)처럼 `AIRun`으로 실행을 제어해도 도구 작업이 해당 실행과 분리되어 남지는 않습니다.

비동기 도구를 사용한 `GetCompletionAsync`는 요청 종료 후 중간에 생성한 독립적인 설명과 최종 답변을 순서대로 누적해 반환합니다. `StreamAsync`는 각 라운드의 텍스트가 도착하는 대로 전달합니다.

핸들러는 취소 토큰을 받지 않으므로 실행 취소·시간 초과·오류가 발생하면 이미 시작한 핸들러의 완료를 기다려 정리합니다. 기존 입력형 `service.StreamAsync`의 조기 해제도 실행을 정리하지만, `run.StreamAsync()` 읽기 중단은 관찰만 끝냅니다. Run 실행을 중지하려면 `run.Cancel()`을 호출하거나 run을 해제하세요. 프로토콜은 [공식 API 안내](https://developers.openai.com/api/docs/guides/async-tool-calling)를 참고하세요.

스트리밍은 완전한 함수 호출과 유효한 응답 경계를 확인한 뒤 핸들러를 시작하며, 비동기 작업이 실행되는 동안 다음 모델 라운드를 진행할 수 있습니다. 미완성 호출 이벤트만으로 함수를 실행하지 않습니다. 새 호출 없이 모델이 응답했는데 대기 중인 작업이 남아 있으면 결과를 기다린 뒤 이어갑니다. 미완료 호출이 기록에서 빠지지 않도록 대기 중에는 컨텍스트 초과 시 자동 요약·재시도를 수행하지 않습니다.
