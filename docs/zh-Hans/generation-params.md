# 生成参数

## 通用属性

所有 AI 服务实例都暴露以下属性：

```csharp
service.Temperature = 0.7f;        // 随机性 [0, 2]，越低越确定
service.TopP = 1.0f;               // 核采样阈值
service.MaxTokens = 1024;          // 最大输出 Token 数
service.FrequencyPenalty = 0.0f;   // 对重复 Token 的惩罚
service.PresencePenalty = 0.0f;    // 对已出现 Token 的惩罚
service.MaxMessageCount = 20;      // 对话窗口大小（已弃用 — 将在 v7.0 中移除）
```

> **已弃用：** `MaxMessageCount`（基于消息数量的滑动窗口）已过时，将在 v7.0 中移除 — 上下文管理将改为仅通过 `ConversationPolicy` 基于 Token 进行。在移除之前，该窗口保证永远不会丢弃最近一条用户消息，因此智能体式工具运行不会丢失其正在处理的查询。

## 流式扩展方法

返回 `this` 以支持链式调用：

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("你是一个有用的助手。")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| 方法 | 说明 |
|------|------|
| `.WithSystemMessage(string)` | 设置系统提示词 |
| `.WithTemperature(float)` | 限制在 [0, 2] 范围内 |
| `.WithMaxTokens(uint)` | 最大输出 Token 数 |
| `.WithStatelessMode(bool)` | 禁用对话历史累积 |

## 无状态模式

启用后，每次请求独立 — 不发送也不存储对话历史：

```csharp
service.StatelessMode = true;

// 等价写法：
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

适用于不需要历史上下文的一次性查询。

## 一次性查询

以下扩展方法执行单次查询，不影响也不使用对话历史：

```csharp
// 文本提示
string response = await service.AskOnceAsync("2+2 等于多少？");

// 消息（多模态）
string response = await service.AskOnceAsync(message);

// 从文件路径加载图像
string response = await service.AskOnceWithImageAsync("描述一下这张图", "photo.jpg");
```

## 切换模型

在保留对话历史的前提下切换模型：

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// 或使用扩展方法 — 清除历史并重新开始：
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## 管理多个对话

单个服务实例可以管理多个独立的对话线程：

```csharp
// 创建新的对话块
var chat1 = service.AddNewChat();

// 切换到另一个对话块
service.SetActivateChat(chat2Id);

// 访问所有对话块
var allChats = service.ChatRequests;
```

## 查看对话状态

获取最后一条助手响应或当前会话的快速摘要：

```csharp
// 获取最后一条助手消息（若没有则返回 null）
string? lastReply = service.GetLastAssistantResponse();

// 获取当前服务状态的文字摘要
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: 你是一个有用的助手。
```

## 复制服务配置

从另一个服务实例克隆所有设置（不包括对话历史）：

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
