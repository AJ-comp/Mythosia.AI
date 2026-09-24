# 使用 Run 控制正在执行的 AI 任务

> GPT-6 Sol/Luna: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择与版本要求](providers.md#gpt-6-sol-luna)

只需完整答案和停止按钮时，将 `cancellationToken` 传给 `GetCompletionAsync`。进度事件或受支持的中途追加指令使用 Run。参阅[取消回答](completions.md#completion-cancellation)。

如需分离每个请求的设置并派生多个版本，请使用[请求构建器](request-building.md)。先调用`CreateRequest(...)`，再连接`With...`。服务属性和服务上的fluent方法保持原有行为。

> 需要同时获取完整答案、用量和来源时，使用 `await run.Result` 返回的 `AIRunResult`，字符串位于 `result.Text`，无需读取流。这是Mythosia.AI 8.0.0 的 API 变更；`GetCompletionAsync` 与 `StructuredStreamRun<T>.Result` 的返回类型保持不变。 [Run 结果与迁移](#run-result).

> `CreateRequest`示例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和公共请求功能的旧7.1版本不包含构建器；旧包可继续使用原有服务重载。

对等待时间敏感的请求可选择[处理速度](request-building.md#inference-speed)。`WithSpeed` 保持模型和推理级别，`Processing` 显示供应商实际应用的模式。Fast 是受支持组合上的付费选项。

## 为什么需要在执行过程中控制任务？

一份报告可能需要多次文档检索、API 调用和写作才能完成。在这个过程中，用户可能想查看进度、停止工作，或者补充“只包含今年的数据”这样的要求。应用需要把这些操作关联到正在执行的任务。

Run 为任务提供一个可保存在应用中的句柄。例如，聊天界面可以显示陆续到达的文本、提示工具正在运行、把停止按钮连接到取消操作，并在模型支持时发送追加指令。这些操作都针对同一次执行。

| 应用的需求 | 使用方式 |
| --- | --- |
| 接收完整答案，并可选择取消 | `GetCompletionAsync(..., cancellationToken: token)` |
| 实时显示文本，并在完成后获取汇总文本 | 通过 `onText` 启动 Run，然后等待 `run.Result`。 |
| 显示工具活动，或等待异步输出处理 | 读取 `run.StreamAsync()` 的事件。 |
| 让用户停止正在进行的工作 | 使用保存的句柄调用 `run.Cancel()`。 |
| 在任务完成前补充要求 | 检查 `run.CanSteer`，在支持的模型上调用 `run.SteerAsync(...)`。 |

`StartRunAsync` 启动一个模型任务并返回 `AIRun`。无论是否观察输出，任务都会继续执行。通过同一个句柄可以读取流、获取累积结果、取消执行，以及在支持的模型上发送轮次中的追加指令。`GetCompletionAsync`（包括泛型和 RAG 重载）仍是面向完整结果调用者的公开便捷 API。

<a id="run-result"></a>

## 一次获取答案、用量和来源

即使界面只显示完整答案，也可能需要保存令牌用量和来源。以前 `run.Result` 只返回字符串，用量需要自行收集流事件，来源则从 Run 单独获取。`AIRunResult` 会汇总这些信息，无需读取流。

Before — 旧 Run 契约

```csharp
string answer = await run.Result;
```

After — Mythosia.AI 8.0.0

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("分析这些文档。")
    .StartRunAsync();

AIRunResult result = await run.Result;
string answer = result.Text;
int? totalTokens = result.Usage?.TotalTokens;
var citations = result.Citations;
string? requestedModel = result.RequestedModel;
string? actualModel = result.Model;
var finishReason = result.FinishReason;
```

实时显示文本时也使用同一结果。回调、`run.StreamAsync()`、`SteerAsync` 和取消操作的职责不变。

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("分析这些文档。")
    .StartRunAsync(onText: text => Console.Write(text));

AIRunResult result = await run.Result;
Console.WriteLine($"\nTokens: {result.Usage?.TotalTokens}");
```

完成结果是快照。`Usage` 和引用对象以副本提供，修改返回的对象不会影响已保存结果。释放 Run 后仍可读取。流过滤、停止观察或观察缓冲区溢出不会丢失结果数据。

`Usage` 对库中各模型轮次报告的用量分别累加一次，不会重复计算轮次事件和最终合计。完全没有报告时为 `null`，不会估算缺失信息。不包括独立辅助摘要请求，因此不等于全部账单或账户用量。 如果只有部分轮次报告用量，则仅合计这些轮次，并不保证包含所有轮次的用量。

即使输入、输出明细不完整，也会保留提供方明确报告的 `TotalTokens`；跨轮汇总会累加这些已报告的总数。

令牌计数仍使用 `Int32`。跨轮总和超过 `Int32.MaxValue` 时，流或 Run 会以 `OverflowException` 失败，而不会返回溢出后的错误值。清理仍会完成；即使最终汇总失败，`run.Result` 也不会一直处于等待状态。

`run.StreamAsync()` 返回的序列只能枚举一次。重复或并发枚举同一序列会抛出 `InvalidOperationException`；原有读取与执行仍相互独立。

`Provider` 为适配器名称，`RequestedModel` 是启动时捕获的、实际请求中发送的单一显式模型，包括提供方模型覆盖设置。如果通过预设、配置文件或服务器模型路由选择模型，没有发送单一模型字段，则为 `null`（例如 Perplexity 的 `Models` 列表）。它与实际响应模型 `Model` 相互独立。 `Model` 是提供方在最后一轮响应中报告的实际模型 ID，缺失时为 `null`，不会用请求模型代替。`RoundCount` 是库的 LLM 轮次数，不是单个工具调用次数或托管代理的内部步骤数。不报告轮次的自定义提供方得到 `0`。

`FinishReason` 使用 `AIFinishReason`（`Unknown`、`Stop`、`MaxTokens`、`ToolCalls`、`ContentFilter`、`Other`）；`RawFinishReason` 保留提供方原始结束值。缺失时为 `Unknown`/`null`。这些字段仅描述成功结果。轮次超限等原有错误仍使 `Result` 失败；用户取消仍抛出 `OperationCanceledException`，不会替换为成功的取消结果。

`Text` 保持原有语义：按顺序连接所有输出文本，包括工具轮次中的中间内容及追加指示前的文本。结果等待执行和清理完成。`Citations` 为完成后的来源快照，执行中仍可读取 `run.Citations`。引用偏移保留原始内容片段的坐标，不是拼接文本中的位置。

<a id="run-result-migration"></a>

**主版本迁移：** `AIRun.Result` 从 `Task<string>` 改为 `Task<AIRunResult>`。只需字符串时读取 `(await run.Result).Text`。自定义 `AIRun` 必须更新重写并构造 `AIRunResult`，使用方须重新编译。结果类型位于 `Mythosia.AI.Models.Runs`；`TokenUsage` 和 `AIFinishReason` 位于 `Mythosia.AI.Models.Streaming`。`GetCompletionAsync` 仍返回 `Task<string>`，`StructuredStreamRun<T>.Result` 仍返回 `Task<T>`。这些示例要求Mythosia.AI 8.0.0，不适用于最初的 7.1/3.1 Run 包。

## 通过回调显示文本

在聊天界面或控制台中，从第一段文本到达时就开始显示，可以让用户在长答案生成过程中逐步阅读。

```csharp
await using var run = await service
    .CreateRequest("阅读文档并撰写报告。")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

本地工具可以通过`Task<T>` / `ValueTask<T>`返回对象，并接收库注入的`CancellationToken`。`run.Cancel()`或启动令牌的取消会传递给配合取消的工具，仅停止流读取则不会。异常会记录为失败；取消时跳过排队调用，清理仍会等待已启动且忽略令牌的工具。参见[结果、错误与取消](function-calling.md#tool-execution-contract)。

`onText` 是在任务开始前注册的可选 `Action<string>`。它按顺序接收文本，不负责执行工具。只需要结果时可以省略。回调抛出异常会取消 Run，并使 `Result` 以异常结束。不要向 `onText` 传入 `async` lambda：它会变成 `async void`，Run 无法等待其工作或捕获其异步错误。异步输出处理应使用事件流。回调不会自动切换到 UI 线程。

`(await run.Result).Text` 是 Run 的所有文本事件按顺序连接而成的字符串，包括工具调用之间的中间文本和追加指令前已经生成的文本。它不会发起第二次模型请求，也不是重新改写的答案。如果现有完整结果的返回语义更合适，可以继续使用 `GetCompletionAsync`。

## 读取文本、工具和用量事件

任务检索文档或调用业务 API 时，仅有文本可能无法解释等待的原因。通过带类型的事件，可以同时显示工具活动和答案，并记录提供商返回的用量信息。

```csharp
await using var run = await service.StartRunAsync(
    "检索文档并解释结果。",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[正在调用工具]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[已收到工具结果]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[总 token 数：{item.Usage.TotalTokens}]");
            break;
    }
}

string answer = (await run.Result).Text;
```

`run.StreamAsync()` 接收可选的观察取消令牌，不接收提示词。它观察的是 `StartRunAsync` 已经启动的任务。注册的函数处理器由库内部执行，不要因收到展示事件而再次执行同一个工具。文本显示选项也不会禁用 Run 中注册的工具。

启动时的回调和 `run.StreamAsync()` 可以同时观察同一个 Run，事件流支持一个读取者。例如，用 `onText` 显示文本，并只在流中处理工具事件，可以避免重复显示。即使配置了回调，也最多缓存 1,024 个未读事件。只要未超出容量，较晚开始读取时仍可从头获取缓存事件；超出上限后，流的观察会明确失败，但回调、任务执行和 `Result` 会继续。不要把流当作无限重放日志。等待 `Result` 不要求先读完事件流。

搜索网页或文档的 Run 也可以显示回答来源。[推理与搜索指南](reasoning-and-search.md)提供了 `WithWebSearch`、`WithFileSearch`、`run.Citations` 和引用事件的示例。

## 异步处理输出

需要异步处理输出时，应在读取循环中等待操作，而不是使用异步的 `onText` 回调：

```csharp
using var writer = new StreamWriter("report.txt");
await using var run = await service.StartRunAsync(
    "撰写报告。", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = (await run.Result).Text;
```

## 取消和释放资源

- 跳出 `await foreach`，或取消仅传给 `run.StreamAsync(token)` 的令牌，只会停止观察，任务继续执行。
- `run.Cancel()`、传给 `StartRunAsync` 的令牌，以及释放仍在运行的 Run，都会取消执行。
- `await using` 确保 `DisposeAsync()` 等待生产任务和提供商清理完成。不支持取消的工具可能需要一段时间才能结束；释放资源不会撤销已完成的操作。
- 同一个服务只允许一个活动的 `StartRunAsync` 任务，重叠启动会被拒绝。独立并发任务应使用不同服务；Run 活动期间不要混用旧调用或修改服务设置。

Run 在后台执行前捕获输入和待应用的单次请求策略。内置文本、图像、音频内容以及媒体字节数组会被复制。自定义 `MessageContent` 子类保留原实例，因此在 Run 结束前不得修改。

捕获的 `FunctionCallingPolicy.TimeoutSeconds` 为 Run 准备阶段及所有模型/工具轮次设置同一个总期限。超时会报告 `AIServiceException`；用户取消会使结果进入取消状态。清理仍会等待不支持取消的处理器结束。

## 在任务执行中追加指令

假设用户开始生成项目计划后，才发现必须把工期控制在两周内。追加指令（steering）允许应用在模型仍在工作时提交这个新要求，适合较长任务中发现的修正和范围调整。任务结束后的新问题，应像平常一样启动下一次请求。

```csharp
await using var run = await service.StartRunAsync(
    "起草项目计划。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// 在 Run 活动期间，从 UI 的追加指令处理器调用此函数。
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("此 Run 不支持追加指令。");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = (await run.Result).Text;
```

GPT-6 Astra / Sol / Luna 通过 Responses WebSocket 连接支持轮次中的追加指令。其他提供商及不支持的模型仍可使用普通 Run，但 `CanSteer` 为 `false`，追加指令会明确报告不支持，而不会默默创建普通的下一轮。`CanSteer` 不保证稍后调用时 Run 仍处于活动状态。

GPT-6 的 Run 会建立专用 socket。传入的 `HttpClient` 及其消息处理器继续服务于 HTTP 调用，不会拦截此 socket。自定义传输可以重写 `OpenAIService.ConnectRunWebSocketAsync`。

`SteerAsync` 成功表示服务器已将输入接受到队列中，不表示模型已经应用该指令。继续通过同一个 Run 观察后续执行或等待结果。已经发送的文本和已完成的操作不会撤销，也不会仅因提交了追加指令而取消已启动的工具。库在同一连接上处理继续执行和工具结果关联。参见 OpenAI 的[轮次中追加指令指南](https://developers.openai.com/api/docs/guides/steering)和 [WebSocket 模式](https://developers.openai.com/api/docs/guides/websocket-mode)。队列中的输入属于当前连接，不应假设断开后仍会保留；不要盲目重发已被接受的指令。

## 使用工具的任务与旧 Agent 方法

“检查退款政策和这个订单的状态”这类问题需要多个信息源。注册文档检索和订单查询工具后，由模型选择必要的调用。轮数限制约束模型在必须结束或报告错误前，能够继续请求工具的范围。

普通函数调用已经支持多轮模型/工具交互。`StartRunAsync` 使用相同的已注册函数和执行策略，不需要独立的 Agent 模式、规划器或 `WithAgentic` 开关。

`RunAgentAsync` 和 `RunAgentStreamAsync` 仍可调用，但现在带有 `[Obsolete]` 警告。迁移期间保留现有签名、默认 `maxSteps = 10` 和旧有的步数超限错误行为。新调用请使用：

```csharp
await using var run = await service
    .CreateRequest("查找政策、检查订单并说明结果。")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

通用 `FunctionCallingPolicy.MaxRounds` 默认值为 20；如果需要保留旧 Agent 上限，请明确指定 10。`WithMaxRounds` 配置单次请求的策略覆盖，不修改 `DefaultPolicy`，应在任务开始前设置。旧 Agent 方法则复制当前默认策略，再应用该次调用的 `maxSteps`。新 Run 使用通用执行错误约定，不保证转换为旧的 `AgentMaxStepsExceededException`/`PartialResponse`。如果依赖这一约定，应先迁移异常处理，再替换旧调用。

## RAG、MCP 和包边界

- `RagEnabledService.StartRunAsync` 支持字符串或 `Message` 输入、`onText`、每次查询的 `RagQueryOptions`、`streamOptions` 和取消。它在底层 Run 启动前执行检索，保留图像/音频内容及元数据，将原始输入保留在对话历史中，并通过请求上下文发送增强文本。增强内容绑定到原始用户问题，因此不会用原来的 RAG 提示词覆盖后续工具结果或追加指令。向返回的 Run 追加指令会更新模型的任务，但不会自动重新执行 RAG 检索。
- `WithAgenticRag` 继续注册检索工具。通过 `StartRunAsync` 使用该工具时，模型可按需发起后续检索。`WithMcpServerAsync` 的 MCP 注册方式也不变。共享 MCP 连接与使用它的 Run 应分别释放。
- `IAIRunService` 是 `Mythosia.AI.Abstractions` 中的可选能力接口，`IAIService` 没有新增必需成员。自定义服务必须实现 `IAIRunService` 才能支持从 RAG 启动 Run；不支持的服务会在 RAG 索引开始前被拒绝。
- RAG 保持对 Abstractions 的依赖，单独打包的提供商保留公开的 completion 重写方法和可访问的提供商扩展点。此变更不会弃用向量存储、文档加载器或服务器管理 API。

## 迁移到 Mythosia.AI 8

| API | 当前状态 |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | 继续公开并受支持，包括接口、提供商和 RAG 变体。 |
| `StartRunAsync` / `AIRun` | 通用的执行与控制 API。 |
| `RunAgentAsync` / `RunAgentStreamAsync` | 带弃用警告；为兼容保留现有行为。 |
| 接收输入的 `service.StreamAsync` 和 RAG `StreamAsync` | 接收输入的服务和 RAG StreamAsync 在 v8 中仍公开。新的执行控制使用 StartRunAsync；run.StreamAsync() 只观察已经启动的 run。 |
| `run.StreamAsync()` | 观察已存在任务的输出，不接收请求输入。 |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | 保留现有泛型流式 API；其仅输出的 `Stream()` 不是旧服务请求方法。 |

[迁移到 Mythosia.AI 8](v8-migration.md).

Perplexity: [让长任务继续运行](perplexity.md).

[用共享支持定义构建模型功能选项](model-capabilities.md).
