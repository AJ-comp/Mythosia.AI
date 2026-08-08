# 대화 관리

## 대화 기록 동작 방식

`GetCompletionAsync` 또는 `StreamAsync`를 호출할 때마다 서비스의 내부 메시지 목록에 추가됩니다. 따라서 모델은 이전 모든 턴의 컨텍스트를 가집니다.

```csharp
await service.GetCompletionAsync("제가 좋아하는 색은 파란색입니다.");
var reply = await service.GetCompletionAsync("제가 좋아하는 색이 뭔가요?");
// → "당신이 좋아하는 색은 파란색입니다."
```

새로 시작하려면:

```csharp
service.ActivateChat.ClearMessages();
```

## 요약 정책

### 자동 요약이 필요한 이유

대화 기록의 모든 메시지는 매 요청마다 모델에 전송됩니다. 대화가 길어지면 두 가지 문제가 발생합니다:

1. **비용** — 긴 기록은 요청당 더 많은 입력 토큰 비용을 발생시킵니다
2. **컨텍스트 초과** — 기록이 모델의 컨텍스트 윈도우(예: GPT-4o의 128K 토큰)를 초과하면 요청 자체가 실패합니다

오래된 메시지를 수동으로 잘라낼 수 있지만, 모델이 필요로 할 수 있는 맥락이 사라집니다. **`SummaryConversationPolicy`**는 오래된 메시지를 간결한 요약으로 자동 압축하면서 최근 메시지는 원문 그대로 유지하여, 모델이 토큰 비용 없이 전체 대화의 핵심을 파악할 수 있게 합니다.

### 메시지 수로 트리거

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // 기록이 20개를 초과하면 요약
    keepRecentCount: 5  // 최근 5개 메시지는 그대로 유지
);
```

### 토큰 수로 트리거

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // 토큰 사용량이 3000을 초과하면 요약
    keepRecentTokens: 1000  // 최근 1000 토큰에 해당하는 메시지 유지
);
```

### 토큰 + 메시지 수 동시 트리거 (OR 조건)

토큰 한도 또는 메시지 수 중 **하나라도** 초과하면 요약을 트리거합니다:

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // 선택사항, 기본값 triggerTokens / 3
    keepRecentCount: 7       // 선택사항, 기본값 triggerCount / 4
);
```

설정하면 `GetCompletionAsync`에서 자동으로 요약이 발생합니다. 다른 변경은 필요 없습니다.

### 동작 방식

1. 매 완성 호출 전, 정책이 대화가 설정된 임계값을 초과하는지 확인합니다.
2. 트리거되면 오래된 메시지를 Stateless LLM 호출로 간결하게 요약합니다.
3. 요약은 시스템 메시지 접두사로 주입되어 모델이 이전 컨텍스트로 인식합니다.
4. 최근 메시지(`KeepRecentCount` 또는 `KeepRecentTokens`로 제어)는 원문 그대로 유지됩니다.

토큰 기반 트리거 사용 시, 정책은 로컬 추정 대신 **API가 보고한 실제 입력 토큰 수**(마지막 스트리밍 응답에서 제공)를 자동으로 사용하여 정확한 트리거 결정을 보장합니다.

### 스트리밍

`StreamAsync` 중에는 요약이 자동으로 트리거되지 않습니다. 먼저 명시적으로 호출하세요:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("대화를 계속하겠습니다..."))
    Console.Write(chunk.Content);
```

## 요약 저장 및 복원

세션 간에 요약을 유지하여 재시작 후에도 모델이 컨텍스트를 기억하게 합니다:

```csharp
// 저장
string saved = service.ConversationPolicy.CurrentSummary;
// → 데이터베이스, 파일 등에 저장

// 새 세션에서 복원
service.ConversationPolicy.LoadSummary(saved);
```
