# 观察 Claude Fable 5.1 的长时间任务

[Claude Opus 5.5](providers.md#claude-opus-55) 从 Mythosia.AI 8.1.0 / Abstractions 4.1.0 起可用：推理始终启用，默认 effort 为 medium，并省略显示。可读进度需显式请求；默认值和模型绑定规则与 Fable 5.1 不同。

> Fable 5.1 控制需要 `Mythosia.AI` 8.0.0 和 `Mythosia.AI.Abstractions` 4.0.0 或更高版本。现有 Run、推理/搜索和 GPT-6 Astra API 的最低版本仍为 7.1.0 / 3.1.0。

## 为什么需要这些控制？

文档调查可能经过多次搜索和工具调用才能给出答案。应用可能需要显示进度、要求只在当前轮次执行某项检查，或在修改早期会话后继续工作。Fable 5.1 为这些情况提供了控制，但复用已保留的思考时，会话历史本身也是请求契约的一部分。

使用 [Run API](execution-api-transition.md) 观察和取消任务，使用[通用推理与搜索选项](reasoning-and-search.md)选择推理深度与信息来源，再用下面的 Claude 专用设置控制进度和历史处理。模型的原生能力并不意味着 Mythosia 已公开所有供应商 API。

## 明确选择模型和推理深度

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` 选择 `claude-fable-5-1`。`ClaudeMythos5_1` 选择 `claude-mythos-5-1`，需要 Project Glasswing 访问权限。现有 Fable 5 和 Mythos 5 常量仍保留。两个 5.1 模型接受文本和图像输入并输出文本，上下文窗口为 1M token，最大输出为 128K token。[模型概述](https://platform.claude.com/docs/en/models/fable-5-1/overview)。

模型原生默认 effort 为 `high`，而 Mythosia 的 `ClaudeReasoningEffort.Auto` 保留现有 `ThinkingBudget` 映射：启用的预算默认对应 `High`，达到 32,768 时为 `XHigh`，达到 100,000 时为 `Max`。关闭推理的请求使用低 adaptive effort 并省略可读思考。如果需要 `High`，请明确选择；`Auto` 不意味着库始终省略 effort 并完全交给模型默认值。

## 在工具调用之间显示进度

`ClaudeThinkingDisplay.Updates` 请求可读进度，同时隐藏推理。`Summarized` 还包含推理摘要，`Omitted` 则省略可读 thinking 块。只有模型生成更新时才会返回，因此不保证固定间隔的状态通知。[进度更新](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta)。

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

更新使用现有 `StreamingContentType.Reasoning` 事件；通过 `StreamOptions.FullOptions` 或 `StreamOptions.Default.WithReasoning()` 开启观察。非流式调用结束后可读取 `service.LastThinkingContent`。进度文字与最终答案分开，不会公开原始思维链。

## 不要为单轮变更改写早期历史

Fable 5.1 的 thinking 块绑定于生成它的系统提示、工具和早期消息。保留后续 thinking 的同时改写这些输入，可能使其失效。只要求当前轮次先检查客服政策时，可以追加轮次限定指令并将其留在历史中；后续用户消息出现后，该指令停止生效。这样不必重复修改顶层系统提示。修改 effort 和追加轮次指令是两种独立控制。

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

两个辅助方法都会为下一个逻辑请求捕获指令。Mythosia 保留早期消息，在用户输入或工具结果之后追加系统消息。`WithTurnInstruction` 使用 `clear_at: "next_user_message"`；同一个逻辑请求内，每次工具结果轮次之后都会重新追加该指令，使其生效到本次请求结束。`WithConversationInstruction` 会在后续轮次持续生效。必须在启动任务前配置；两者都不是 `run.SteerAsync`，也不会向已运行的响应注入指令。

要在请求之间调整 effort 并保留可复用的缓存前缀，使用 `Mythosia.AI.Extensions` 中的 `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)`。库发送消息级 effort 更新并保留其历史。支持组合见[通用指南](reasoning-and-search.md)。在 5.1 中，`AIRequestContext` 的请求级系统前缀/后缀会变为追加的轮次指令，不会改写早期系统提示。

Fable 5.1 可以读取较早 Claude 模型的 thinking，但较早模型不能读取 Fable 5.1 的 thinking。Mythos 5.1 具有相同的 5.1 能力，但不强制执行 Fable 的前缀绑定检查。遇到历史修改、模型切换或思考块丢弃时，应观察这些变化，不能假设推理保持不变。[迁移指南](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)。

## 诊断有意进行的历史修改

`ThinkingPrefixMismatchBehavior = null` 将检查交给供应商的账号策略。`WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` 明确要求服务器验证。用户对历史、`SystemMessage` 或工具的修改仍会发送给 Anthropic；使用 `Error` 时，前缀不匹配由供应商返回 400。重复发送相同的无效请求无法修复它。

如果应用有意修改早期内容，并接受丢失受影响的推理，可以选择 `DropBlock`。Mythosia 会将该控制发送给 Anthropic，不会在请求前静默剥离 thinking。

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` 提供供应商报告的 `Type`、`Path`、`Reason`，以及用于归属的 `ResponseId` 和 `Model`。`prefix_binding_mismatch` 表示前缀改变，`model_binding_mismatch` 表示目标模型无法读取该 thinking。丢弃不是修复推理。需要保留时应保持对话记录完整；需要重置时则开始新会话。

Mythosia 保留传输历史，避免内部 RAG/context 处理造成意外变化。普通 Fable 5.1 会话在默认/`Error` 路径下会阻止自动本地压缩。`DropBlock` 允许压缩，但可能丢弃推理，也不保证缓存命中。独立的 `CachePreservation.Required` 选项仍保留更严格的历史保护。 `WithWebSearch()` 等通用选项会在请求后消耗。下一轮省略它们会改变原生 tools 数组，可能造成前缀不匹配。要保留历史，应再次应用相同的工具/搜索设置；有意改变时使用 `DropBlock` 或新会话。这些选项不会自动延续到下个请求。

保留的传输快照属于服务及其 `ChatBlock`。仅把 `ChatBlock` 复制到新服务，不会转移早期 RAG/context 或轮次 system 快照。需要保留推理时应继续使用同一服务和会话；如果只搬移了原始历史，应开始新会话，不要假设保留状态也已迁移。

## 使用普通工具选择

Fable 5.1 和 Mythos 5.1 拒绝强制工具选择。不要设置 `ForceFunctionName`，而是在请求中说明何时使用已注册工具。不允许当前轮次调用工具时仍可使用 `FunctionsDisabled`。需要类型化响应时，应使用现有结构化输出 API，不要仅为获取 JSON 而强制调用函数。

## 了解哪些变化由服务器负责

| 原生选项 | 所需 Anthropic beta |
| --- | --- |
| 消息级 effort | `mid-conversation-output-config-2026-07-01` |
| 轮次限定系统消息 | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Thinking 绑定控制与 `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia 在开启相应受支持设置时添加所需请求头。开启一个 beta 不代表开启其他所有 beta。此集成不新增服务器端 compaction、原生工具添加/删除块或自动模型 fallback。

两个模型都要求满足供应商适用的 30 天数据保留安排；ZDR 需要 Anthropic 明确授权。Adaptive thinking 始终启用，不支持手动 `budget_tokens` 或禁用推理，也不发送自定义采样参数。账号访问和保留安排是服务器要求。[迁移要求](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)。

文本水印、受支持媒体的来源信息以及缓存读取价格由 Anthropic 应用，无需增加 Mythosia 请求选项。此集成不新增媒体来源创建 API、水印开关或计费控制。参见 [Fable 5.1 的变化](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1)。
