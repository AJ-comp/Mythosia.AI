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
service.ClearMessages();
```

## 요약 정책

긴 대화는 토큰을 소비하고 결국 모델의 컨텍스트 한도를 초과합니다. `SummaryConversationPolicy`는 임계값에 도달하면 오래된 메시지를 자동으로 요약합니다.

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

설정하면 `GetCompletionAsync`에서 자동으로 요약이 발생합니다. 다른 변경은 필요 없습니다.

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
