# [To-Be] 소비자 API 개선

> **핵심 목표**: 외부에서 깔끔하고 우아하게 사용할 수 있어야 한다. 모델 전환이 한 줄이어야 한다.

## As-Is ? 현재의 불편함

```csharp
// 프로바이더마다 서비스 타입을 알아야 하고, HttpClient를 직접 관리해야 함
var httpClient = new HttpClient();
var gpt = new ChatGptService("sk-...", httpClient);
var response = await gpt.GetCompletionAsync("hello");

// 모델 전환? → 서비스 새로 생성해야 함
var httpClient2 = new HttpClient();
var claude = new ClaudeService("sk-ant-...", httpClient2);
```

## To-Be ? 이상적인 소비자 경험

### 1. 한 줄 등록

```csharp
services.AddMythosiaAI(o =>
{
    o.AddOpenAI("sk-...");
    o.AddAnthropic("sk-ant-...");
    o.AddGoogle("AIza...");
});
```

### 2. 모델 기반 사용 ? 프로바이더를 몰라도 됨

```csharp
public class ChatController(IAIServiceFactory ai)
{
    public async Task<string> Ask(string prompt)
    {
        // 모델만 지정하면 프로바이더는 자동 결정
        var service = ai.Create(AIModel.Gpt4oMini);
        return await service.GetCompletionAsync(prompt);
    }
}
```

### 3. 모델 전환이 한 줄

```csharp
// GPT → Claude 전환
var service = ai.Create(AIModel.Claude4Sonnet);

// 대화 이력도 그대로 이어가기
var service = ai.Create(AIModel.Claude4Sonnet).CopyFrom(previousService);
```

### 4. 스트리밍도 동일한 패턴

```csharp
var service = ai.Create(AIModel.Gpt4oMini);

await foreach (var chunk in service.StreamAsync("explain quantum computing"))
{
    Console.Write(chunk);
}
```

## 설계 원칙

| 원칙 | 설명 |
|------|------|
| **프로바이더 무관** | 소비자는 `AIModel` enum만 알면 됨 |
| **HttpClient 투명** | `IHttpClientFactory`를 내부에서 사용, 소비자에게 노출하지 않음 |
| **기존 호환** | `new ChatGptService(key, httpClient)` 방식도 계속 동작 |
| **설정 분리** | API 키는 등록 시, 모델 선택은 사용 시 |
