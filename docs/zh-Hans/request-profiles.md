# AIRequestProfile

## 概述

`AIRequestProfile` 可以**仅对单次请求**覆盖生成参数 — 温度、最大 Token 数、无状态模式、函数调用等。服务的全局设置不受影响。

## 它解决了什么问题

假设你有一个为创意对话配置的聊天机器人：

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("你是一个创意写作助手。");
```

现在你的 RAG 管道需要用低温度、无历史的方式改写用户查询。**没有** `AIRequestProfile` 时，你需要这样做：

```csharp
// ❌ 没有 AIRequestProfile — 手动管理状态
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("改写这个查询：...");

// 恢复所有设置 — 容易遗忘，非线程安全
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

这种方式冗长、易出错，且在**多线程场景下会出问题**（如 Web 服务器同时处理多个用户）。如果恢复前抛出异常，服务将处于错误状态。

**有了** `AIRequestProfile`，一行搞定：

```csharp
// ✅ 有 AIRequestProfile — 简洁且安全
var rewritten = await service.GetCompletionAsync("改写这个查询：...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

全局设置不受影响。无需清理。线程安全。

## 可用属性

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // 覆盖温度
    MaxTokens = 256,          // 覆盖最大输出 Token 数
    Stateless = true,         // 不将此次交互加入对话历史
    DisableFunctions = true,  // 此次请求跳过函数调用
    DisableReasoning = true   // 此次请求跳过推理/思维链
};

var response = await service.GetCompletionAsync("你的提示词", profile);
```

所有属性均为可选 — 只设置需要覆盖的项。未设置的属性使用服务的当前值。

## 预定义配置

针对常见场景提供了内置配置，无需手动设置属性：

```csharp
// 查询改写：低温度，小 Token 预算，无状态
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// 摘要：稍高温度，适中 Token 数
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## 实际用例

### RAG 管道中的内部查询改写

```csharp
// 主服务配置为面向用户的对话
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// 用不同设置改写查询 — 服务保持不变
var betterQuery = await service.GetCompletionAsync(
    $"改写为搜索查询：{userQuery}",
    RequestProfiles.QueryRewrite);

// 继续正常对话 — 仍然是 Temperature 0.7，MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### 对特定步骤禁用函数

```csharp
// 服务已注册函数
service.WithFunction("search_web", "搜索网络", ...);

// 仅此次调用跳过函数调用 — 直接回答
var directAnswer = await service.GetCompletionAsync(
    "2 + 2 等于多少？",
    new AIRequestProfile { DisableFunctions = true });
```

## 与 AIRequestContext 组合

两者可以同时传入，实现最大程度的控制：

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\n请简洁作答。" }
);
```

详见 [AIRequestContext](request-contexts.md) 了解如何向请求中注入内容。
