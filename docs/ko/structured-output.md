# 구조화된 출력

구조화된 출력은 모델의 응답을 C# 타입으로 직접 역직렬화합니다. Mythosia.AI에는 자동 JSON 복구 기능이 내장되어 있어 모델의 경미한 포맷 오류를 투명하게 처리합니다.

## 기본 사용법

`GetCompletionAsync`에 타입 파라미터를 전달합니다:

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "서울의 날씨는 어떤가요?");

Console.WriteLine(result.City);         // 서울
Console.WriteLine(result.Condition);    // 맑음
Console.WriteLine(result.TemperatureC); // 22
```

## 컬렉션

컬렉션 타입도 래퍼 DTO 없이 바로 사용할 수 있습니다:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "이 텍스트에서 모든 사람과 조직을 추출해 주세요: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## 스트리밍 + 구조화된 출력

실시간으로 텍스트를 스트리밍하면서 최종 역직렬화된 객체도 받습니다:

```csharp
var run = service.BeginStream("제품 요약을 생성해 주세요").As<ProductDto>();

// 실시간 출력
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// 최종 파싱된 결과
ProductDto product = await run.Result;
```

## 구조화된 출력 정책

모델이 구조화된 출력을 생성하는 엄격도를 제어합니다:

```csharp
using Mythosia.AI.Models;

// 기본값: 스키마에 맞는 JSON을 반환하도록 요청
service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;

// 완화: 모델에 더 많은 자유를 주고 자동 복구에 의존
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient;
```
