# 文本生成

## 单轮对话

最简单的用法 — 发送消息，获取响应：

```csharp
var response = await service.GetCompletionAsync("法国的首都是哪里？");
Console.WriteLine(response); // 巴黎
```

## 系统提示词

通过系统提示词为模型设定角色或指令：

```csharp
service.SystemMessage = "你是一个简洁的助手，请用一句话回答。";

var response = await service.GetCompletionAsync("解释一下递归。");
```

## 多轮对话

消息会自动累积。每次调用 `GetCompletionAsync` 都会追加到对话历史中：

```csharp
await service.GetCompletionAsync("我叫小明。");
var response = await service.GetCompletionAsync("我叫什么名字？");
// → "你叫小明。"
```

清除对话历史：

```csharp
service.ActivateChat.ClearMessages();
```

## 手动构建消息

使用 `MessageBuilder` 显式构造消息：

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("请总结这段文字：...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 多模态（图像输入）

支持视觉能力的提供商可以同时接收图像和文本：

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("这张图展示了什么？")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## 快速提问（静态 API）

无需构建服务实例的一次性查询，使用静态方法 `QuickAskAsync`。提供商会根据模型名称自动识别：

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "法国的首都是哪里？",
    model: AIModels.OpenAI.Gpt4oMini  // 默认值
);
```

带图像的版本：

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "描述这张图片",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4_1
);
```

## 图像快捷方法

无需 `MessageBuilder` 即可分析图像 — 服务会自动读取文件并识别 MIME 类型：

```csharp
// 从文件路径
var response = await service.GetCompletionWithImageAsync(
    "这张图展示了什么？", "diagram.png");

// 从 URL
var response = await service.GetCompletionWithImageUrlAsync(
    "描述这张照片", "https://example.com/photo.jpg");
```

## 重试上一条消息

移除上一条助手响应，重新发送最后一条用户消息：

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

当上一条响应不理想时，可用此方法让模型重新生成。

## Token 计数

在发送请求前估算 Token 用量。所有提供商均支持：

```csharp
// 统计当前对话历史的 Token 数
uint conversationTokens = await service.GetInputTokenCountAsync();

// 统计特定提示词的 Token 数
uint promptTokens = await service.GetInputTokenCountAsync("你的提示词");
```

OpenAI 及大多数提供商使用本地 TikToken 估算。Anthropic 和 Google 会调用原生 Token 计数 API 以获取精确结果。

## 流式消息链

`BeginMessage()` 提供流式 API，可在一条链中构建并发送消息 — 包括文本、图像、流式输出及策略配置：

```csharp
// 文本 + 图像 → 发送
string response = await service.BeginMessage()
    .AddText("这张图展示了什么？")
    .AddImage("diagram.png")
    .SendAsync();

// 一次性查询（不保留对话历史）
string answer = await service.BeginMessage()
    .AddText("把这段翻译成英文")
    .SendOnceAsync();

// 流式输出
await service.BeginMessage()
    .AddText("写一首关于春天的诗")
    .StreamAsync(chunk => Console.Write(chunk));

// 自定义超时和策略
string result = await service.BeginMessage()
    .AddText("分析这张图片")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` 也支持 `IAsyncEnumerable`：

```csharp
await foreach (var chunk in service.BeginMessage().AddText("讲个故事吧").StreamAsync())
    Console.Write(chunk);
```

## 控制输出长度和温度

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // 越低越确定
```
