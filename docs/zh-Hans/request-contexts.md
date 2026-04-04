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
