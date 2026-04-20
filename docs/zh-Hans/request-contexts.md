# AIRequestContext

## 概述

`AIRequestContext` 可以修改**模型在单次请求中看到的内容** — 注入额外指令、添加参考文档或完全替换用户消息 — 而不会永久改变服务的系统消息或对话历史。

## 它解决了什么问题

假设一个 RAG 管道检索了相关文档并需要将其包含在提示词中。**没有** `AIRequestContext` 时，你需要直接修改系统消息：

```csharp
// ❌ 没有 AIRequestContext — 污染系统消息
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\n请根据以下信息回答：\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// 恢复 — 但这些上下文已经留在对话历史里了
service.SystemMessage = originalSystem;
```

这种方式的问题：

- 检索到的上下文**泄漏到对话历史** — 后续请求仍然能看到
- 恢复系统消息并不能撤销历史污染
- 在多用户 Web 应用中，修改共享状态会导致竞态条件

**有了** `AIRequestContext`，注入仅作用于一次请求：

```csharp
// ✅ 有 AIRequestContext — 简洁、作用域隔离、无副作用
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\n请根据以下信息回答：\n{retrievedDocs}"
    });
```

系统消息仅在本次调用中被修改。下一次请求仍使用原始系统消息。无需清理。

## 可用属性

### SystemMessagePrefix

仅在本次请求中向系统消息前部追加文本：

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "今天的日期是 2026-03-31。\n"
};

var response = await service.GetCompletionAsync("今天是几号？", context);
```

**适用场景：** 注入每次请求都不同的动态元数据（日期、用户时区、会话信息）。

### SystemMessageSuffix

仅在本次请求中向系统消息尾部追加文本：

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\n请始终用中文回答。"
};

var response = await service.GetCompletionAsync("Hello!", context);
```

**适用场景：** 添加每次请求的行为指令、RAG 上下文或语言偏好。

### AdditionalMessages

仅在本次请求中插入额外消息 — 适合注入参考文档或少样本示例：

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("参考资料：退款政策允许 30 天内退货。").Build()
    }
};

var response = await service.GetCompletionAsync("我是否有资格退款？", context);
```

**适用场景：** 提供不应持久化到对话历史中的参考资料、少样本示例或辅助上下文。

### RequestMessageOverride

完全替换本次请求的用户消息。原始提示词被忽略：

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"根据以下上下文回答问题。\n\n上下文：{docs}\n\n问题：{userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context);
```

**适用场景：** 当中间件层（RAG、查询改写）需要在发送给模型前完全重构提示词，同时保留原始用户输入在对话历史中。

> **💡 提示：** 使用 `.WithRag()` 时，RAG 管道会自动利用此属性。详见 [管道自定义 — 内部工作原理](rag-pipeline.md#how-it-works-internally)。

## 前后对比

### 场景：带日期注入和检索上下文的 RAG

**没有 AIRequestContext：**

```csharp
// ❌ 混乱、有状态、易出错
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\n今天：{DateTime.Now:yyyy-MM-dd}"
    + $"\n\n上下文：\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // 移除少样本示例
```

**有 AIRequestContext：**

```csharp
// ✅ 简洁、无状态、无副作用
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"今天：{DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\n上下文：\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
        }
    });
```

## 与 AIRequestProfile 组合

两者可以同时传入，对单次请求实现最大程度的控制：

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n上下文：\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("示例：...").Build()
        }
    }
);
```

详见 [AIRequestProfile](request-profiles.md) 了解如何覆盖生成参数。

## 使用 `SystemMessageProvider` 自动注入

### 此功能解决的问题

典型的聊天应用有多个需要相同基线（今日日期、活动文件夹、会话信息等）的 LLM 入口点。**不使用** `SystemMessageProvider` 时，每个调用点都需要记得构建并传递该上下文：

```csharp
// ❌ 不使用 SystemMessageProvider — 每个入口点都必须记得注入
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. 主聊天响应
var answer = await service.GetCompletionAsync(userMessage,
    new AIRequestContext { SystemMessageSuffix = today });

// 2. 标题生成器（后来添加）
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 3. 摘要器（更晚添加）
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 4. Agent 调用 — 容易忘记！ 编译器不会警告你
var agentResult = await service.RunAgentAsync(goal);  // ← 日期缺失，无声 bug
```

此方式的问题：

- 相同的上下文构建片段在每个调用点**重复**
- 新入口点（上面的 `RunAgentAsync`）**容易遗漏** — 没有编译时检查
- 每个添加 LLM 调用的新功能都必须记住此约定
- 测试也必须在每个调用点复制上下文设置

使用 `SystemMessageProvider`，基线**只需注册一次**，所有外发调用自动接收：

```csharp
// ✅ 使用 SystemMessageProvider — 注册一次，随处生效
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// 以下所有调用都自动接收基线 — 无需每次调用的样板代码
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← 也接收基线

// 流式入口点也一样 — 相同基线，无需每次调用的样板代码
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### 工作原理

通过 `WithSystemMessageProvider` fluent 辅助方法注册一次回调。每个外发调用（`GetCompletionAsync`、`StreamAsync`、`RunAgentAsync`、`RunAgentStreamAsync`）都会自动调用它来构建基线上下文：

```csharp
// 通常在服务构造 / DI 设置时注册
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### 用于 IO 支持的 provider 的异步重载

当基线上下文来自数据库、缓存或 HTTP 调用时,请使用异步重载,以便 provider 无需通过 `.Result` / `.GetAwaiter().GetResult()` 阻塞。根据 lambda arity 自动进行重载解析 — 无参数为 sync,一个 `CancellationToken` 为 async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

非流式路径（`GetCompletionAsync`、`RunAgentAsync`）在设计上不支持取消 — 其签名不接受 `CancellationToken`，始终向 provider 传递 `CancellationToken.None`。如果您的 provider 需要取消（例如长时间运行的 DB 查询），请使用流式路径（`StreamAsync`、`RunAgentStreamAsync`），它们会将调用者的 token 传递到 provider 回调。

### 与显式 per-call 上下文合并

当一个调用既有已注册的 provider**又**传递了显式的 `AIRequestContext` 时,两者按字段合并:

| 字段 | 合并规则 |
|---|---|
| `SystemMessagePrefix` | 显式值非 null 时胜出,否则使用 provider |
| `SystemMessageSuffix` | 显式值非 null 时胜出,否则使用 provider |
| `RequestMessageOverride` | 显式值非 null 时胜出,否则使用 provider |
| `AdditionalMessages` | 拼接(provider 在前,然后是显式) |

原因: 常见场景是"provider 提供基线,特定调用想替换一个标量字段或添加额外消息" — 字段级覆盖使语义可预测,避免意外的拼接。

### 每次调用的 invocation

Provider **每个请求调用一次**,因此返回值可以反映最新状态(时间戳、会话等)。返回 `null` 是 no-op — 相当于该调用未设置 `SystemMessageProvider`。

> 在 Mythosia.AI v6.3.0+ 中可用。
