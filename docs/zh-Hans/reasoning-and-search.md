# 选择推理强度，并获得附带来源的回答

> 这些 API 需要 `Mythosia.AI` 7.1.0 或更高版本，其中包含 `Mythosia.AI.Abstractions` 3.1.0 或更高版本。RAG 示例需要 `Mythosia.AI.Rag` 7.6.0 或更高版本。

## 为什么需要这些设置？

不同阶段需要不同的帮助。起草时可能希望快速得到答案，检查其假设时则值得投入更多推理。关于今天发生的事情，需要最新信息；关于产品的问题，需要描述该产品的文档。仅仅提高推理强度，并不会让模型获得这两类来源。

使用通用 Fluent API 表达下一个任务的需求。所选提供商会把支持的选项转换为其原生 API 请求。应用可以继续使用 `GetCompletionAsync` 获取完整答案，也可以用 `StartRunAsync` 显示进度并控制同一个任务。

| 任务需要 | 配置方式 |
| --- | --- |
| 快速起草，然后仔细审查 | `WithReasoning(...)` |
| 在保留符合条件的对话缓存前缀的同时调整推理强度 | `WithReasoning(..., cache: CachePreservation.Required)` |
| 获取网络上的最新信息 | `WithWebSearch()` |
| 根据提供商已建立索引的文档回答 | `WithFileSearch(store)` |

示例假设服务已初始化，并使用支持相应功能的模型。请导入 `Mythosia.AI.Extensions` 和 `Mythosia.AI.Models`；流事件还需要 `Mythosia.AI.Models.Streaming`。

## 从快速起草转向仔细审查

可以在拟定提纲时减少推理，然后在同一段对话中检查复杂细节：

```csharp
string outline = await service
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("拟定迁移计划的提纲。");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("审查该计划中的故障场景与恢复步骤。");
```

`ReasoningLevel` 表达所请求的级别，并非固定的令牌预算，也不保证回答质量。每个模型接受的级别范围不同。`Auto` 保留提供商已配置的行为或默认行为，不表示自动替换不受支持的级别。对于提供令牌预算而非命名级别的模型，原有的提供商专用预算属性仍然可用。

在长对话中，修改请求顶层的推理设置可能使可复用的提示前缀失效。对于支持的模型，可以要求使用提供商的机制，在保留该前缀的同时更改推理强度：

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("重新检查上一个回答中的假设。");
```

`Required` 约定的是变更的发送方式。它**不保证**缓存命中、免费令牌或更低延迟；提供商的缓存资格、保留时间及计费规则仍然适用。不支持的模型会在发送请求前抛出 `NotSupportedException`。请使用同一段受跟踪的对话、同一模型和同一端点，不要截断或重新排列包含这些更新的历史。要改变这些条件，请开始新对话。在要求保留前缀期间，自动压缩会被阻止。

被接受的缓存保留推理设置会成为对话的有效设置，直到下一次显式变更。普通的 `WithReasoning(level)` 仅应用于对应的逻辑请求，不会悄悄替换这一持续设置。变更发生在**两次模型响应之间**，不会改变已经在生成的响应的推理强度，也不同于 `run.SteerAsync`；后者用于向支持该功能的运行中任务发送补充指令。

## 回答需要最新信息的问题

如果答案需要使用模型训练数据之外的信息，可以启用原生网页搜索：

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("搜索最新的版本发布公告，并注明来源。");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

这一托管工具由提供商执行，无须注册或执行本地函数处理程序。启用搜索意味着模型可以使用它；模型也可能判断某个提示不需要搜索。只有提供商返回来源时，才能获取相应引用。

OpenAI 和 Anthropic 也接受 `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`。Google 的集成工具不提供这一允许列表，因此带域名限制的请求会被拒绝，而不会退而搜索整个网络。

## 从提供商已建立索引的文档中回答

如果应用已经维护提供商托管的文档索引，可以使用该存储为回答提供依据，无须自行实现检索轮次：

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .WithFileSearch(documents)
    .GetCompletionAsync("搜索我们的政策文档。取消服务的期限是多久？");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

对于 Google，请将 `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` 与 Google 服务一起使用。存储属于特定的提供商、账户和部署环境，不能将 OpenAI 的存储 ID 传给 Google。使用前，请通过提供商 API 或控制台创建存储、上传文档并建立索引。本 API 仅搜索已有存储，不上传本地文件。

托管文件搜索与库的 [RAG 流水线](rag.md) 满足不同的部署需求。索引已由提供商管理时，可以选择托管搜索；应用需要控制加载器、分割、嵌入、检索或向量存储时，应选择 RAG。`RagEnabledService` 也会将 `WithReasoning`、`WithWebSearch` 和 `WithFileSearch` 转发到最终回答，其内部查询重写不会继承这些选项。RAG 检索引用仍在 `RagProcessedQuery` 上，与提供商返回的 `AICitation` 来源分开保存。

## 显示进度并保留来源

在 `StartRunAsync` 之前也可以使用相同选项。文本回调可用于更新界面，Run 则为完整答案保留来源：

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "搜索近期公告，并比较其中的变更。",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

即使不读取流、只观察文本，或输出观察缓冲区已满，`run.Citations` 仍然可用。它包含运行期间从提供商收集的来源引用，包括中间响应。`service.LastCitations`（通过 `IAIService` 使用时为 `GetLastCitations()`）描述最近一次逻辑请求；显示多个答案时，请保留对应的 Run，或复制其引用快照。

如需在来源事件到达时处理它们，请只使用一个事件读取器：

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "搜索并解释最新变更。", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\n来源: {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

提供商未返回值的引用字段可以为 null。`ResponseId`、`OutputIndex` 和 `ContentIndex` 标识原始响应及内容片段。`StartIndex` 和 `EndIndex` 保留提供商在局部内容中的偏移量及索引规则，**不是**拼接后的 `run.Result` 中的位置。不要直接把它们当作完整答案的索引来放置引用。

## 检查提供商支持范围与请求作用域

| 已集成的提供商 | 命名推理级别 | 保留缓存的变更 | 网页搜索 | 文件搜索 |
| --- | --- | --- | --- | --- |
| OpenAI | 支持的推理模型；级别因模型而异 | GPT-6 Astra Standard，单代理模式 | 支持的 Responses 模型 | 支持的 Responses 模型及已有向量存储 |
| Anthropic | 具有原生 effort 控制的模型 | 支持的 Opus 5 / Fable 5.1 / Mythos 5.1，使用提供商测试版功能 | 支持的 Claude 模型 | 没有原生存储适配器；请使用 RAG |
| Google | Gemini 3 的级别；Gemini 2.5 保留提供商专用预算 | 不支持 | 支持的 Gemini 文本模型 | 支持的 Gemini 文本模型及已有文件搜索存储 |
| 其他服务 | 原有提供商专用设置仍然可用；这些通用选项需要适配器 | 本组适配器不支持 | 没有通用适配器 | 没有通用适配器 |

模型、级别、传输方式及组合检查均在发送请求之前进行。尤其要注意，**Google 网页搜索与文件搜索不能在同一个请求中组合使用**。库不会悄悄移除功能、降低推理级别、忽略域名限制，或切换到外部搜索服务。在提供商支持时，原生工具可与注册的客户端函数共存；Run 的工具轮次仍遵循函数策略与 `WithMaxRounds`。

Fluent 方法保留服务的具体类型，并复制输入选项。非 null 的组件会合并到下一次逻辑请求中，覆盖其工具轮次及结构化输出修复调用，随后被消费。搜索不会自动用于后续无关调用；需要时，请再次添加 `WithWebSearch` 或 `WithFileSearch`。已启动的 Run 会保留捕获的设置。与其他可变服务配置一样，请勿在请求运行时修改同一服务的设置，或在该服务上启动重叠请求。

自定义 `IAIService` 实现仍然兼容。实现可以通过 `IAIRequestFeatureService` 选择提供这些功能；在没有这一能力的实现上调用相关辅助方法，会显式抛出异常。现有完成、流式及提供商专用配置 API 仍然可用。取消、观察和补充指令的用法请参阅 [Run 控制](execution-api-transition.md)。

提供商协议：[OpenAI 推理变更](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation)、[OpenAI 工具](https://developers.openai.com/api/docs/guides/tools)、[Anthropic effort 变更](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation)、[Anthropic 网页搜索](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool)、[Google Search 信息依据](https://ai.google.dev/gemini-api/docs/google-search)、[Google File Search](https://ai.google.dev/gemini-api/docs/file-search)。
