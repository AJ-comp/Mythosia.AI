# 对话摘要机制

## 概述

用于管理长对话中令牌成本和上下文窗口限制的自动摘要机制。
当`SummaryConversationPolicy`检测到触发条件时，将旧消息压缩为摘要文本，仅保留最近的消息。

## 核心设计原则

1. **摘要时机**: 基于触发器的摘要仅在所有函数调用轮次完成后触发，绝不在链式调用中间触发。唯一的例外是上下文超限恢复，且即便如此当前问题与先前轮次的结果也会被保留（参见[上下文超限恢复](#上下文超限恢复与触发式摘要不同的路径)）
2. **摘要策略不感知API约束**: `GetMessagesToSummarize`仅按规则裁剪。User-first等API约束由各提供商处理
3. **原始数据不变**: 摘要文本存储在`CurrentSummary`中并注入系统提示。API请求的消息列表是副本

## 触发条件

```csharp
// 基于消息数量
var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10, keepRecentCount: 4);

// 基于令牌数量（使用API返回的实际令牌数）
var policy = SummaryConversationPolicy.ByToken(triggerTokens: 8000, keepRecentTokens: 2000);

// 两者兼用（OR条件）
var policy = SummaryConversationPolicy.ByBoth(triggerTokens: 8000, triggerCount: 20, ...);
```

基于令牌的触发优先使用API返回的官方`InputTokens`值（`LastKnownInputTokens`）。
仅在没有实际值时回退到本地估算（`EstimateTokens`）。

## 完整流程（包含函数调用）

### 设置

```
triggerCount=3, keepRecentCount=2
函数: get_user_id, get_user_details
```

### 第1步: 用户提问

```
StreamAsync(User("查询john_doe的信息"))

ActivateChat.Messages:
  [0] User: "查询john_doe的信息"
```

### 第2步: Round 0 — 第一次函数调用

LLM决定调用`get_user_id("john_doe")`。执行后:

```
ActivateChat.Messages:
  [0] User: "查询john_doe的信息"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
```

`hasFunctionResult = true` → 下一轮

### 第3步: Round 1 — 第二次函数调用

LLM调用`get_user_details("user_123")`。执行后:

```
ActivateChat.Messages:
  [0] User: "查询john_doe的信息"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, name: Test User, email: test@example.com}"
```

`hasFunctionResult = true` → 下一轮

### 第4步: Round 2 — 最终文本响应

LLM综合函数结果生成文本响应:

```
ActivateChat.Messages:
  [0] User: "查询john_doe的信息"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, ...}"
  [5] Assistant: "john_doe的信息如下..."
```

`hasFunctionResult = false` → 所有轮次完成

### 第5步: 流式传输结束后触发摘要

```
ShouldSummarize: 6条消息 > triggerCount(3) → 触发!

GetMessagesToSummarize:
  keepFromIndex = 6 - 2 = 4
  待摘要: [0]~[3] (User, Asst(FC), Func, Asst(FC))
  待保留: [4]~[5] (Function, Assistant)
```

生成摘要并删除消息后:

```
CurrentSummary = "用户请求john_doe的信息。get_user_id返回user_123，然后通过get_user_details查询详细信息"

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "john_doe的信息如下..."

SystemMessage:
  "You are a helpful assistant.

  [Previous conversation summary]
  用户请求john_doe的信息。get_user_id返回user_123，然后通过get_user_details查询详细信息"
```

### 第6步: 下一个用户提问

```
StreamAsync(User("邮箱也告诉我"))

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "john_doe的信息如下..."
  [2] User: "邮箱也告诉我"
```

构建API请求时应用`EnsureUserFirstMessage`:

```
messages[0] = Function → 不是User → 插入合成User

发送到API的消息:
  [0] User: "(Continuing from previous conversation context)"  ← 合成
  [1] Function: "{id: user_123, ...}"
  [2] Assistant: "john_doe的信息如下..."
  [3] User: "邮箱也告诉我"
```

## User-First约束处理

部分API（Gemini、Claude）要求消息数组必须以User角色开头。
摘要裁剪后首条消息可能是Assistant/Function，因此在各提供商的请求构建器中处理。

```csharp
// AIService.cs
protected static void EnsureUserFirstMessage(List<Message> messages)
{
    if (messages.Count == 0) return;
    if (messages[0].Role == ActorRole.User) return;
    messages.Insert(0, new Message(ActorRole.User,
        "(Continuing from previous conversation context)"));
}
```

- **适用对象**: Gemini、Claude（请求构建器4处）
- **不适用**: OpenAI、Grok、DeepSeek、Sonar、Qwen（无User-first约束）
- **原始数据不变**: 仅应用于通过`GetLatestMessages().ToList()`创建的副本

## 为什么摘要在轮次完成后执行

```
X 轮次中间摘要:
  Round 0: FC调用 → 结果保存
  Round 1: [此处触发摘要] → FC结果被删除！ → LLM丢失上下文

O 完成后摘要:
  Round 0: FC调用 → 结果保存
  Round 1: FC调用 → 结果保存
  Round 2: LLM使用所有FC结果生成文本（完成）
  [此处触发摘要] → 为下一轮做准备，不影响当前响应
```

## 上下文超限恢复：与触发式摘要不同的路径

触发式摘要是"为下一轮做的整理"，没有理由在轮次中间执行。
但如果**服务器在轮次3拒绝并报告"上下文超限"**，情况就不同了 —
无法等到本轮结束，因为不立即缩减，这一轮就会以失败告终。

因此恢复压缩确实在轮次内执行。上面 X 所担心的"FC结果被删除"，
由"切割点最多只能前移到**最后一条User消息**"的钳制来阻止。

```
Round 0: FC调用 → 结果保存
Round 1: FC调用 → 结果保存
Round 2: [服务器以400拒绝]
         → 仅将当前问题"之前"的旧对话摘要折叠
         → 当前问题与 Round 0·1 的FC结果原样保留
         → 仅重放 Round 2（Round 0·1 不重跑 = 工具不会执行两次）
```

若没有可折叠的旧对话，则**连摘要请求都不发出**便立即放弃 —
强行删除既不能缩小请求，又白白损失历史。停止装置有三个：

| 原因 | 含义 |
|---|---|
| `nothing-to-cut` | 当前问题之前没有可切割的内容 |
| `retries-exhausted` | `ContextRecoveryMaxRetries` 已耗尽 |

两种情况都**不调用摘要、不删除任何消息**，原始错误原样上抛。

> **非流式不同。** 重试会让提供方的轮次循环从0重新开始，因此已经执行过的工具会被执行第二次。
> 所以在这种情况下不进行恢复，而是以 `tool-side-effects` 为原因停止。
> 按轮次重放仅存在于流式路径。
