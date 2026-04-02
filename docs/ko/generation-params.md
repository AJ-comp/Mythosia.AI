# 생성 파라미터

## 공통 속성

모든 AI 서비스 인스턴스는 다음 속성을 제공합니다:

```csharp
service.Temperature = 0.7f;        // 무작위성 [0, 2]. 낮을수록 결정론적
service.TopP = 1.0f;               // 핵 샘플링 임계값
service.MaxTokens = 1024;          // 최대 출력 토큰 수
service.FrequencyPenalty = 0.0f;   // 반복 토큰 패널티
service.PresencePenalty = 0.0f;    // 이미 등장한 토큰 패널티
service.MaxMessageCount = 20;      // 대화 윈도우 크기
```

## 플루언트 확장 메서드

`this`를 반환하므로 체이닝이 가능합니다:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("당신은 도움이 되는 어시스턴트입니다.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| 메서드 | 설명 |
|--------|------|
| `.WithSystemMessage(string)` | 시스템 프롬프트 설정 |
| `.WithTemperature(float)` | [0, 2] 범위로 제한 |
| `.WithMaxTokens(uint)` | 최대 출력 토큰 수 |
| `.WithStatelessMode(bool)` | 대화 기록 누적 비활성화 |

## 무상태 모드

활성화하면 각 요청이 독립적입니다 — 대화 기록이 전송되거나 저장되지 않습니다:

```csharp
service.StatelessMode = true;

// 동일한 효과:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

기록 오버헤드가 필요 없는 단발성 쿼리에 유용합니다.

## 단발성 쿼리

대화 기록에 영향을 주거나 사용하지 않고 단일 쿼리를 실행합니다:

```csharp
// 텍스트 프롬프트
string response = await service.AskOnceAsync("2+2는 무엇인가요?");

// 메시지 (멀티모달)
string response = await service.AskOnceAsync(message);

// 파일 경로의 이미지
string response = await service.AskOnceWithImageAsync("설명해 주세요", "photo.jpg");
```

## 모델 전환

대화 기록을 유지하면서 세션 중간에 모델을 변경합니다:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// 또는 확장 메서드로 — 기록을 초기화하고 새로 시작:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## 여러 대화 관리

단일 서비스 인스턴스가 여러 독립적인 대화 스레드를 가질 수 있습니다:

```csharp
// 새 대화 블록 시작
var chat1 = service.AddNewChat();

// 다른 블록으로 전환
service.SetActivateChat(chat2Id);

// 모든 블록 접근
var allChats = service.ChatRequests;
```

## 대화 상태 조회

마지막 AI 응답 또는 현재 세션의 간략한 요약을 가져옵니다:

```csharp
// 마지막 AI 응답 가져오기 (없으면 null)
string? lastReply = service.GetLastAssistantResponse();

// 현재 서비스 상태의 텍스트 요약
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: You are a helpful assistant.
```

## 서비스 설정 복사

대화 기록 없이 다른 서비스 인스턴스의 모든 설정을 복제합니다:

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
