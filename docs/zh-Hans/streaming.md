# 流式输出

默认回调流适配器在提前退出时取消并等待内部生成任务。Run 取消、超时、观察回调失败和 `DisposeAsync` 都等待供应商清理结束后才完成 `Result` 并释放运行锁；不响应取消的任务可能延迟结束。观察与清理异常一并保留。`ContextRecoveryMaxRetries` 使用请求捕获的值。仅停止 `run.StreamAsync()` 观察仍不会取消 Run。 成功 SSE 响应的正文流获取还存在单独的[取消限制](#sse-acquisition-cancellation-limitation)。

> Claude Sonnet 5.5: 需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0。[配置与迁移](providers.md#claude-sonnet-55)

Adaptive 模式可用 `ClaudeThinkingDisplay.Updates` 获取工具进度，或用 `Summarized` 获取推理摘要。读取 `StreamingContentType.Reasoning`，普通完成后读取 `LastThinkingContent`。Adaptive 辅助方法的默认 display 参数为 `Summarized`，与未设置时不同。`between_tools` 自动返回工具进度；不保证固定通知间隔。

> Grok 4.7: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择、推理与处理速度](providers.md#grok-47)

需要同时获取完整答案、用量和来源时，使用 `await run.Result` 返回的 `AIRunResult`，字符串位于 `result.Text`，无需读取流。这是Mythosia.AI 8.0.0 的 API 变更；`GetCompletionAsync` 与 `StructuredStreamRun<T>.Result` 的返回类型保持不变。 [Run 结果与迁移](execution-api-transition.md#run-result).


如需分离每个请求的设置并派生多个版本，请使用[请求构建器](request-building.md)。先调用`CreateRequest(...)`，再连接`With...`。服务属性和服务上的fluent方法保持原有行为。

逐段显示到达的文本，让用户不必等长答案全部生成后才能阅读。如果还需要停止按钮和工具状态，可通过 `StartRunAsync` 启动任务并读取 `run.StreamAsync()`。[Run 使用指南](execution-api-transition.md)提供回调和取消的示例。

```csharp
await using var run = await service.StartRunAsync(
    "总结文档。",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

**Claude 错误响应：** 在流式请求或 Run 中，即使 HTTP 错误正文的读取停滞，取消和请求策略超时仍会生效。Run 清理完成后，可在同一服务上启动下一个 Run。调用方取消抛出 `OperationCanceledException`，策略超时抛出 `AIServiceException`。取消控制本地传输和配合取消的清理过程，不保证提供商停止处理或计费。

**Claude 响应清理：** Claude 流式请求和 Run 会等待已获取的 HTTP 响应正文完成异步清理，包括需要异步释放的自定义流。之后释放响应或内容时发生的异常不会覆盖成功完成、原始读取错误或取消的结果；仍会尝试释放原始响应和内容。

**HTTP 超时：** 对于使用公共流式轮次处理路径的文本、内容或回调流式请求以及 Run，在调用方取消和请求策略超时均未触发时，可识别的 `HttpClient.Timeout`（内部包含 `TimeoutException` 的 `TaskCanceledException`）会转换为 `AIServiceException`。`InnerException` 保留原始传输异常，因此 `run.Result` 会以保留超时原因的故障状态完成。调用方取消、策略超时和其他传输取消的现有行为保持不变。

<a id="sse-acquisition-cancellation-limitation"></a>

## 已知限制：成功 SSE 响应的正文流获取

对于 HTTP 200 SSE，如果自定义处理程序使用缓冲正文的 `HttpContent` 包装器，`ReadAsStreamAsync` 可能在获取正文流和开始清理之前停滞。调用方取消和请求策略超时后，`run.Result` 仍可能保持未完成、响应未释放、服务的运行锁未解除，直到获取完成；下一个 Run 会因已有运行而被拒绝。此问题尚未修复，与清理缓慢不同。默认 `SocketsHttpHandler` 通过了已测试的场景；相同包装器下的 HTTP 错误正文取消也通过了验证。请使用普通流式内容，避免使用缓冲正文的包装器。Claude 原生网页搜索还存在单独的[续接限制](providers.md#claude-native-continuation-limitation)。

接收输入的服务和 RAG StreamAsync 在 v8 中仍公开。新的执行控制使用 StartRunAsync；run.StreamAsync() 只观察已经启动的 run。

## 基本流式输出

使用 `StreamAsync` 可在生成过程中逐步接收文本。

```csharp
await foreach (var token in service.StreamAsync("讲个故事吧"))
{
    Console.Write(token);
}
```

## 带内容类型的流式输出

`StreamAsync` 可以返回 `StreamingContent` 对象，携带文本及其类型：

```csharp
await foreach (var content in service.StreamAsync("解释一下量子计算", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## 推理过程流式输出

OpenAI、Claude、Gemini、Grok 和 DeepSeek Flash 通过相同流式模式返回提供方推理。先在服务或请求中开启推理，再用 `StreamOptions.WithReasoning()` 观察：

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("求解：2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[思考中] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

Gemini 3.7/3.8 Flash 沿用现有流式输出和 Run 事件。`StreamingContentType.Reasoning` 包含提供商返回的摘要或进度，不保证公开完整的内部推理。`StreamOptions.WithReasoning()` 选择此输出，服务的 `WithReasoning(ReasoningLevel...)` 则控制推理强度。

Grok 4.6 也通过这些事件传递提供商可选的推理摘要。流选项选择可见输出，`WithReasoning(ReasoningLevel...)` 则选择一个任务的推理强度。没有摘要不代表推理已关闭。参阅 [Grok 配置](providers.md#xai-xaiservice)。

DeepSeek Flash 开启推理后通过相同事件返回 `reasoning_content`。`StreamOptions.WithReasoning()` 控制观察；`WithDeepSeekReasoning(...)` 或服务级 `WithReasoning(...)` 控制推理。参阅 [DeepSeek 配置](providers.md#deepseek-deepseekservice)。

## 流式输出 + 结构化输出

实时流式传输文本，完成后获取反序列化的对象：

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// 实时将 Token 输出到界面
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// 流式输出完成后获取完整解析结果
MyDto result = await run.Result;
```

## Token 使用量

流式输出完成时，最后的 `Completion` 事件携带 `TokenUsage` 对象，包含详细的使用指标：

```csharp
await foreach (var content in service.StreamAsync("解释一下量子计算", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\n输入 Token：{content.Usage.InputTokens}");
        Console.WriteLine($"输出 Token：{content.Usage.OutputTokens}");
        Console.WriteLine($"总计 Token：{content.Usage.TotalTokens}");
    }
}
```

### TokenUsage 属性

| 属性 | 说明 |
|------|------|
| `InputTokens` | 输入/提示词的 Token 数 |
| `OutputTokens` | 输出/生成的 Token 数 |
| `TotalTokens` | 输入 + 输出 |
| `CachedInputTokens` | 从缓存中获取的 Token 数（降低成本） |
| `CacheCreationTokens` | 写入缓存的 Token 数（Anthropic） |
| `ReasoningTokens` | 用于内部推理的 Token 数 |
| `CacheHitRatio` | 缓存命中率（0.0–1.0） |
| `VisibleOutputTokens` | 排除推理后的输出 Token 数 |

### 检查缓存效率

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"缓存命中率：{content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"未缓存输入：{content.Usage.NonCachedInputTokens}");
}
```

## StreamOptions 预设

`StreamOptions` 提供预设和流式构建器，用于控制流式输出包含的内容：

```csharp
// 全功能 — 元数据、函数调用、推理
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// 最小开销 — 仅文本，无元数据
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// 函数调用场景
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* 处理 Text、FunctionCall、FunctionResult、Completion */ }
```

自定义组合的流式构建器：

```csharp
var options = new StreamOptions()
    .WithReasoning()       // 包含思维链
    .WithMetadata()        // 在 Completion 中包含模型信息
    .WithFunctionCalls();  // 在流式输出中启用函数调用
```

在 `run.Result` 成功之前，应将已显示的片段视为暂定输出。共用的 OpenAI 兼容流式处理路径和 DeepSeek 流式处理路径会拒绝明确结束后的新文本、推理或工具数据，以及发生变化的结束原因：`run.Result` 会抛出异常，失败轮次不会保存到对话历史，也不会执行该轮次的工具。这种失败处理不会撤销之前的轮次或已在外部执行的操作。 允许最后一个增量与首次结束事件一起到达，也允许随后仅包含用量信息的事件。

## 无状态流式输出（StreamOnceAsync）

在不影响对话历史的情况下进行流式输出 — 相当于 `AskOnceAsync` 的流式版本：

```csharp
await foreach (var chunk in service.StreamOnceAsync("把这段翻译成法语"))
    Console.Write(chunk);
```

也接受 `Message` 以支持多模态输入：

```csharp
var message = MessageBuilder.Create().AddText("描述一下").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## 流式输出前的对话摘要

自动摘要策略不会在流式输出期间触发。请在 `StreamAsync` 之前显式调用 `ApplySummaryPolicyIfNeededAsync`：

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("继续我们的对话...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [让长任务继续运行 / 引用可以指向网页结果或其他提供方来源。偏移量属于单个提供方响应的内容部分，而不是 Run 累积结果。保留 URL 和标题用于显示和核对；返回来源本身并不证明每项生成的主张都正确。](perplexity.md).
