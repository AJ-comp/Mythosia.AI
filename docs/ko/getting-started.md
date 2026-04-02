# 빠른 시작

## 설치

핵심 패키지를 설치합니다:

```bash
dotnet add package Mythosia.AI
```

LINQ 연산자(예: `ToListAsync`)를 사용한 스트리밍이 필요하다면 추가로 설치합니다:

```bash
dotnet add package System.Linq.Async
```

## 첫 번째 완성 요청

프로바이더를 선택하고 API 키와 `HttpClient`로 서비스 인스턴스를 생성합니다:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

그런 다음 `GetCompletionAsync`를 호출합니다:

```csharp
var response = await service.GetCompletionAsync("안녕하세요!");
Console.WriteLine(response);
```

## 모델 선택

각 서비스는 기본 모델을 사용하지만, 명시적으로 지정할 수도 있습니다:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

사용 가능한 모든 모델 상수는 [API 레퍼런스](../../api/Mythosia.AI.Models.AIModels.yml)를 참고하세요.

## 다음 단계

- [기본 완성](completions.md) — 시스템 프롬프트, 대화 기록, 멀티모달
- [스트리밍](streaming.md) — 토큰 단위 출력 및 추론 스트리밍
- [함수 호출](function-calling.md) — 모델이 내 코드를 호출하게 하기
- [구조화된 출력](structured-output.md) — 응답을 C# 타입으로 역직렬화
