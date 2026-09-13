# 文本生成

如需分离每个请求的设置并派生多个版本，请使用[请求构建器](request-building.md)。先调用`CreateRequest(...)`，再连接`With...`。服务属性和服务上的fluent方法保持原有行为。

<a id="completion-cancellation"></a>

## 取消不再需要的回答

用户关闭页面、点击停止，或应用等待超时后，可能不再需要这个回答。传入 `CancellationToken` 可中断客户端的通信与工作，避免多余的工具调用和后续模型调用。需要完整答案时仍可使用 `GetCompletionAsync`；仅需取消时不必创建 Run。

### Before：调用方不传入取消信号

```csharp
string answer = await service.CreateRequest("总结这份文档。")
    .GetCompletionAsync();
```

### After：用户操作或 30 秒后取消

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("总结这份文档。")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("已取消。");
}
```

调用期间保留令牌源，让停止按钮或页面关闭事件调用 `cancellation.Cancel()`。示例也会在 30 秒后请求取消。清理结束后，调用方收到 `OperationCanceledException`。使用 `CancellationTokenSource` 设置的期限也属于调用方取消；现有 `FunctionCallingPolicy.TimeoutSeconds` 保留原有超时错误行为。

服务的字符串与 `Message` 重载、泛型完成方法、请求构建器和 `MessageChain.SendAsync` / `SendOnceAsync` 均接受令牌。省略令牌的现有调用仍可使用。以下是其他调用入口：

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "总结这份文档。", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "以JSON返回标题和作者。", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("总结这份文档。")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("翻译这个句子。")
    .SendOnceAsync(cancellationToken: token);
```

令牌传递到请求准备、HTTP 发送与读取、配合取消的本地工具以及后续模型轮次。检测到取消后，跳过排队工具和后续轮次。清理保持已记录工具调用与结果的配对，因此忽略令牌的已启动工具可能延迟清理。取消不会撤销已完成操作或清空对话历史。参阅[工具执行约定](function-calling.md#tool-execution-contract)。

不保证供应商服务器停止生成或计费。OpenAI 说明普通 Responses 请求可通过断开连接取消；Google 明确说明只取消客户端，相关用量仍计费。[OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal)。后台任务本身需要显式调用 `CancelAsync()`；取消 `WaitForCompletionAsync(cancellationToken: ...)` 只停止等待。普通完成请求不会转换为后台执行。参阅 [Perplexity](perplexity.md)。

<a id="completion-cancellation-migration"></a>

此功能属于Mythosia.AI 8.0.0。不传令牌的调用和现有 profile/context 位置参数在源代码层面仍有效，但使用方需要重新构建。自定义 `IAIService` 实现必须在两个完成方法签名末尾添加并传递 `CancellationToken cancellationToken = default`。继承 `AIService` 的自定义供应商保留现有 `GetCompletionAsync(Message)` override，并将受保护的 `RequestCancellationToken` 传给传输层。构建器和 Run 本身不需要这次接口修改。 如果子类重写了已修改的字符串/profile/context 完成调用、图像辅助方法或 `RunAgentAsync` 等 public virtual 重载，也必须追加并传递新的 `CancellationToken`；仅接收单个 `Message` 的供应商 override 保留原签名。直接绑定到已修改签名的方法组委托可能需要改成显式传入或省略令牌的 lambda。

## 单轮对话

最简单的用法 — 发送消息，获取响应：

```csharp
var response = await service.GetCompletionAsync("法国的首都是哪里？");
Console.WriteLine(response); // 巴黎
```

只需要完整答案时，`GetCompletionAsync` 仍然适用。如需逐段显示、执行中停止或追加指令，请参阅 [Run 使用指南](execution-api-transition.md)。

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

图表和截图分析、本地函数调用、快速回答后的深入审查可使用 [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash)。推理默认关闭，通过 `WithDeepSeekReasoning(...)` 或请求级 `WithReasoning(...)` 开启。

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

Perplexity: [使用 Agent 预设回答 / 来源、图像与结构化答案](perplexity.md).
