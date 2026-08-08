# 开始使用

## 安装

安装核心包：

```bash
dotnet add package Mythosia.AI
```

如果需要使用流式 LINQ 操作符（如 `ToListAsync`），还需安装：

```bash
dotnet add package System.Linq.Async
```

## 第一次生成文本

选择一个提供商，使用 API Key 和 `HttpClient` 创建服务实例：

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

调用 `GetCompletionAsync`：

```csharp
var response = await service.GetCompletionAsync("你好！");
Console.WriteLine(response);
```

## 选择模型

每个服务都有合理的默认模型，也可以显式指定：

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

所有可用模型常量请参阅 [API 参考](../../api/Mythosia.AI.Models.AIModels.yml)。

## 后续步骤

- [文本生成](completions.md) — 系统提示词、对话历史、多模态
- [流式输出](streaming.md) — 逐 Token 输出与推理过程流式传输
- [函数调用](function-calling.md) — 让模型调用你的代码
- [结构化输出](structured-output.md) — 将响应反序列化为 C# 类型
