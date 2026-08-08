# 对话管理

## 对话历史的工作方式

每次调用 `GetCompletionAsync` 或 `StreamAsync` 都会追加到服务的内部消息列表。这意味着模型拥有所有前序轮次的上下文。

```csharp
await service.GetCompletionAsync("我最喜欢的颜色是蓝色。");
var reply = await service.GetCompletionAsync("我最喜欢的颜色是什么？");
// → "你最喜欢的颜色是蓝色。"
```

要重新开始：

```csharp
service.ActivateChat.ClearMessages();
```

## 摘要策略

### 为什么需要自动摘要？

每条对话历史消息都会在每次请求时发送给模型。随着对话增长，会产生两个问题：

1. **成本** — 更长的历史意味着每次请求计费更多输入 Token
2. **上下文溢出** — 一旦历史超过模型的上下文窗口（如 GPT-4o 的 128K Token），请求将直接失败

你可以手动截断旧消息，但这会丢失模型可能需要的上下文。**`SummaryConversationPolicy`** 通过自动将旧消息压缩成简要摘要来解决这个问题，同时保留最近消息的原文 — 模型既能掌握完整对话要旨，又无需承担 Token 成本。

### 按消息数触发

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // 历史超过 20 条消息时触发摘要
    keepRecentCount: 5  // 保留最近 5 条消息原文
);
```

### 按 Token 数触发

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // Token 使用量超过 3000 时触发摘要
    keepRecentTokens: 1000  // 保留最近 1000 Token 的消息
);
```

### 同时按两者触发（OR 条件）

当 Token 限制**或**消息数任一超出时触发摘要：

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // 可选，默认为 triggerTokens / 3
    keepRecentCount: 7       // 可选，默认为 triggerCount / 4
);
```

设置后，摘要在 `GetCompletionAsync` 时自动触发，无需其他修改。

### 工作原理

1. 每次生成前，策略检查对话是否超过配置的阈值。
2. 如果触发，旧消息通过无状态 LLM 调用被压缩为简要文本。
3. 摘要作为系统消息前缀注入 — 模型将其视为先前的上下文。
4. 最近的消息（由 `KeepRecentCount` 或 `KeepRecentTokens` 控制）保持原样。

使用基于 Token 的触发器时，策略会自动使用 API 报告的**实际输入 Token 数**（来自上一次流式响应），而非本地估算，确保触发决策的准确性。

### 流式输出

摘要不会在 `StreamAsync` 期间自动触发。请先显式调用：

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("继续我们的对话..."))
    Console.Write(chunk.Content);
```

## 保存与恢复摘要

持久化摘要以便在会话重启后保留上下文：

```csharp
// 保存
string saved = service.ConversationPolicy.CurrentSummary;
// → 存入数据库、文件等

// 在新会话中恢复
service.ConversationPolicy.LoadSummary(saved);
```
