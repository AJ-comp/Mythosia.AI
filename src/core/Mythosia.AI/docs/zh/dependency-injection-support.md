# [To-Be] 消费者API改善

> **核心目标**: 外部使用必须整洁优雅。模型切换必须一行搞定。

## As-Is — 当前的不便

```csharp
// 每个提供商都需要知道具体的服务类型，还要手动管理HttpClient
var httpClient = new HttpClient();
var gpt = new OpenAIService("sk-...", httpClient);
var response = await gpt.GetCompletionAsync("hello");

// 切换模型？→ 需要重新创建服务实例
var httpClient2 = new HttpClient();
var claude = new AnthropicService("sk-ant-...", httpClient2);
```

## To-Be — 理想的消费者体验

### 1. 一行注册

```csharp
services.AddMythosiaAI(o =>
{
    o.AddOpenAI("sk-...");
    o.AddAnthropic("sk-ant-...");
    o.AddGoogle("AIza...");
});
```

### 2. 基于模型使用 — 无需知道提供商

```csharp
public class ChatController(IAIServiceFactory ai)
{
    public async Task<string> Ask(string prompt)
    {
        // 只需指定模型，提供商自动决定
        var service = ai.Create(AIModel.Gpt4oMini);
        return await service.GetCompletionAsync(prompt);
    }
}
```

### 3. 模型切换只需一行

```csharp
// GPT → Claude 切换
var service = ai.Create(AIModel.Claude4Sonnet);

// 对话历史也可以直接继承
var service = ai.Create(AIModel.Claude4Sonnet).CopyFrom(previousService);
```

### 4. 流式输出同样的模式

```csharp
var service = ai.Create(AIModel.Gpt4oMini);

await foreach (var chunk in service.StreamAsync("explain quantum computing"))
{
    Console.Write(chunk);
}
```

## 设计原则

| 原则 | 说明 |
|------|------|
| **提供商无关** | 消费者只需知道 `AIModel` enum |
| **HttpClient透明** | 内部使用 `IHttpClientFactory`，不向消费者暴露 |
| **向后兼容** | `new OpenAIService(key, httpClient)` 方式仍然有效 |
| **配置分离** | API密钥在注册时，模型选择在使用时 |
