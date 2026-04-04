# 구조화된 출력

## 구조화된 출력이 필요한 이유

LLM은 기본적으로 자유 형식 텍스트를 반환합니다. 애플리케이션이 응답을 **프로그래밍적으로 처리**해야 하는 경우 — 데이터베이스에 저장, 다른 API에 전달, 또는 타입이 지정된 UI에 렌더링 — 텍스트를 직접 파싱해야 합니다. 이는 모델이 표현을 바꾸면 깨지는 취약한 정규식이나 `string.Contains` 체크로 이어집니다.

구조화된 출력은 모델에게 C# 타입의 스키마에 맞는 JSON을 반환하도록 지시하여 이를 해결합니다. Mythosia.AI가 스키마 생성, 프롬프트 주입, 역직렬화를 자동으로 처리합니다 — 모델이 생성할 수 있는 경미한 포맷 오류에 대한 **자동 JSON 복구**도 포함됩니다.

### 이런 경우에 유용합니다

- 비정형 텍스트에서 엔티티, 분류, 구조화된 데이터 추출
- AI 생성 콘텐츠로 타입이 지정된 API 응답 구축
- 특정 데이터 형태를 기대하는 다운스트림 파이프라인에 AI 출력 전달
- 모델로부터 **신뢰할 수 있는, 기계 판독 가능한** 출력이 필요한 모든 시나리오

## 기존 방식의 한계

모델의 응답에서 날씨 데이터를 추출해야 한다고 가정합니다. 구조화된 출력 **없이** 하면:

```csharp
// ❌ 구조화된 출력 없이 — 취약한 수동 파싱
var text = await service.GetCompletionAsync("서울의 날씨는 어떤가요?");
// text = "서울의 날씨는 맑음이며 기온은 22도입니다."

// 이제 직접 파싱해야 합니다...
var city = "서울"; // 하드코딩? 정규식?
var tempMatch = Regex.Match(text, @"(\d+)도");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// 모델이 "22도" 대신 "이십이 도"라고 하면? 💥
```

모델이 표현을 바꿀 때마다 깨집니다. 구조화된 출력을 **사용하면**:

```csharp
// ✅ 구조화된 출력 사용 — 타입 안전, 자동
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "서울의 날씨는 어떤가요?");

Console.WriteLine(result.City);         // 서울
Console.WriteLine(result.Condition);    // 맑음
Console.WriteLine(result.TemperatureC); // 22
```

모델이 C# 타입에 맞는 JSON을 반환하도록 지시됩니다. Mythosia.AI가 자동으로 역직렬화합니다. 모델이 약간 잘못된 JSON(누락된 쉼표, 후행 텍스트)을 생성해도 내장된 **자동 복구**가 역직렬화 전에 수정합니다 — 수동 오류 처리가 필요 없습니다.

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
