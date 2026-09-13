# Perplexity：有来源的回答、搜索与嵌入

当回答需要基于最新信息，并让读者能够核对来源时，可以使用 Perplexity。`PerplexityService` 调用 Agent API；独立搜索和嵌入则用于为自行选择的回答模型构建检索能力。

## 先选择需要完成的工作

生成最新答案、获取网页列表和为自有文档索引生成向量，是三种不同的工作。请选择负责该工作的组件，而不是每次检索都调用回答模型。

| 需求 | 组件 |
| --- | --- |
| 带来源的研究答案 | `PerplexityService` |
| 供其他模型或界面使用的网页列表 | `PerplexitySearchClient` |
| 普通 RAG 中独立段落的向量 | `PerplexityEmbeddingProvider` |
| 考虑同一文档相邻分块关系的向量 | `PerplexityContextualizedEmbeddingProvider` |

安装 `Mythosia.AI`；嵌入示例还需要 `Mythosia.AI.Rag`。提供 API 密钥和由应用管理的 `HttpClient`。示例中的 `apiKey`、`httpClient`、`cancellationToken` 来自应用。

## 使用 Agent 预设回答

预设组合了模型、指令、工具、推理投入和预算。简单查询使用 `Fast`，日常研究使用 `Low`，多步比较使用 `Medium`，深入研究使用 `High` / `XHigh`。`WideResearch` 用于广泛调查；预计耗时较长的任务建议使用后台执行。这些值是预设，而不是模型 ID。

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "比较最新的电池回收方法，并注明来源。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

仅需答案时使用 `GetCompletionAsync`，已有流式代码使用服务的 `StreamAsync`，需要观察或取消运行时使用 `StartRunAsync`。`(await run.Result).Text` 累积输出的回答文本。不读取引用事件也会在 `run.Citations` 和 `LastCitations` 中保留来源。推理事件仅包含提供方公开的内容，并取决于所选模型。

`AIRunResult.RequestedModel` 是启动时捕获的、实际请求中发送的单一显式模型，包括提供方模型覆盖设置。如果通过预设、配置文件或服务器模型路由选择模型，没有发送单一模型字段，则为 `null`（例如 Perplexity 的 `Models` 列表）。它与实际响应模型 `Model` 相互独立。

## 控制研究和工具

`WithPerplexityOptions(...)` 设置服务的持续配置，每个逻辑请求都会捕获副本。共用 `WithReasoning(...)` 和 `WithWebSearch(...)` 作用于下一个逻辑请求，包括客户端工具轮次和类型化输出修复。内部 RAG 查询改写不会继承最终回答的搜索设置。

可用 `UsePreset(...)` 快速选择预设。预设/配置自行选择模型，`ModelOverride` 可明确替换。`DisableWebSearch` 仅移除适配器默认工具，不保证关闭预设内置搜索。根据模型可用 `Minimal`、`Low`、`Medium`、`High`、`XHigh`、`Max`；`None` 和直接 Sonar 的显式推理设置会被拒绝。内部 `DisableReasoning` 使用较低的可用级别或省略设置，不保证完全关闭推理。

| 设置 | 用途 |
| --- | --- |
| `Preset` / `ModelOverride` | 选择研究配置，或通过 provider/model ID 明确覆盖模型。 |
| `MaxSteps` | 限制提供方的托管循环；0 使用提供方默认值。它与限制客户端函数后续请求的 `WithMaxRounds` 不同。 |
| `ReasoningEffort` | 调整推理投入。`Auto` 省略覆盖值，支持的级别由实际模型决定。 |
| `DisableWebSearch` / `Tools` | 控制适配器默认网页工具和显式选择的托管工具。 |
| `Models` | 按优先级指定 1～5 个备用模型，覆盖单个模型设置。所有候选模型都必须兼容请求的功能。 |
| `Profile` | 使用服务器保存的配置，可固定版本；不能与 `Preset` 同时使用。 |
| `ServiceTier` | 请求默认、flex 或 priority 处理。提供方可能忽略模型不支持的服务级别。 |
| `Skills` | 提供内置、内联或已上传的自定义技能。自定义资源属于 Perplexity 账户。 |
| `LanguagePreference` / `PromptCacheKey` | 设置回答语言或缓存路由提示；提示不保证缓存命中。 |
| `PreviousResponseId` / `Store` | 续接已完成的提供方响应，或控制可查询性。续接时使用 `StatelessMode`，仅发送新一轮输入。`Store = false` 不会关闭提供方的持久化。 |

`PerplexityHostedTool` 接受支持的 `Type` 和文档定义的 JSON 兼容 `Parameters`：`web_search`、`fetch_url`、`finance_search`、`people_search`、`sandbox`、`mcp`。MCP 服务器和托管连接器通过提供方运行，凭据、权限和账户资源必须与目标连接一致。应用函数仍通过 `Functions` / 函数构建器注册。托管步骤和本地处理器由不同主体执行。

`PerplexityHostedTools.WebSearch`、`FetchUrl`、`Sandbox`、`FinanceSearch`、`PeopleSearch`、`Mcp`、`Connector` 可创建工具配置。MCP 不暂停等待批准，需要时用 `allowedTools` 限制。Connector 是提供方预览功能，引用已连接的集成。

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "阅读项目文档并比较相关能力。");
```

工具、推理、图像和模式兼容性由所选模型决定。共用 `WithFileSearch` 不是 Perplexity 向量存储适配器。沙箱生成文件、上传附件和远程 MCP 数据是不同资源，不会自动变成共用文件搜索存储。

## 来源、图像与结构化答案

应用需要 JSON 字段时，使用类型化 completion 或类型化流式输出。适配器发送原生模式并保留现有修复流程。原生响应项和工具标识会保留用于后续请求，请勿随意删除或重排协议历史。图像通过 `Message` 和 `ImageContent` 输入 JPEG/PNG/WebP/GIF 字节或 HTTPS URL，具体支持取决于模型；这不是图像生成请求。

原始响应记录保存在历史元数据中，但后续请求只重放允许的 `message`、`function_call` 和 `function_call_output` 输入项；如需延续提供商端的完整托管执行状态，请使用 `PreviousResponseId`。

引用可以指向网页结果或其他提供方来源。偏移量属于单个提供方响应的内容部分，而不是 Run 累积结果。保留 URL 和标题用于显示和核对；返回来源本身并不证明每项生成的主张都正确。

## 让长任务继续运行

研究需要在客户端暂时断线后继续，或需要稍后凭 ID 查询时，使用提供方后台执行。本地 `AIRun` 控制当前客户端运行，后台响应具有独立的服务器生命周期。停止读取流只会停止观察。要停止远程工作，必须明确取消提供方任务。

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "比较最新的电池回收方法，并注明来源。", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` 捕获输入但不追加历史，并拒绝启用的本地函数或 `Store = false`。`GetResponseAsync` 查询一次；`WaitForCompletionAsync` 轮询到终止状态。保存 `Id` 与 `LastSequenceNumber`，用 `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` 重连。`CancelAsync` 取消远程任务；取消查询/读取令牌只停止该客户端操作。`LastResponse` 包含文本、状态、用量、引用与 `OutputJson`，使用答案前请检查终止状态。

用句柄的 `ListFilesAsync` 和 `DownloadFileAsync(fileId)` 读取沙箱输出。服务也提供 `GetAgentResponseAsync`、`GetResponseFilesAsync`、`GetResponseFileContentAsync`。它们读取响应产物，不创建或搜索向量存储。

内置 Office 技能请使用本指南的后台路径：先调用 `StartBackgroundAsync`，再使用 `WaitForCompletionAsync` / `GetResponseAsync` 和文件方法。此类响应的内部工具记录可能无法与普通本地函数调用区分。

后台提交、查询、取消和流重连不会启用执行中的 `SteerAsync` 或原生异步客户端工具。重连继续观察已有响应，不会重新提交原任务。请保存提供方响应 ID 和游标。

## 仅搜索，不生成答案

需要自行排序、在界面展示或交给其他 LLM 的网页时，使用 `PerplexitySearchClient`。它不调用回答模型，也不修改 `PerplexityService` 的对话历史。

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "电池回收方法",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` 接受单个或多个查询，可指定网页/人物搜索、国家、域名、语言、发布/更新日期范围及新近程度。`ContentSize` 与显式 `MaxTokens` / `MaxTokensPerPage` 二选一。结果包含顺序、标题、URL、摘要和提供方日期；顺序是返回位置，不是相关性得分。

`ContentSize` 仅适用于 Web 搜索。People 搜索必须省略；客户端会在发送前拒绝该组合。

## 为自有文档索引使用向量

标准嵌入独立处理各段落，并实现 `IEmbeddingProvider`，因此可接入现有构建器。上下文嵌入保留相邻分块顺序和文档分组；为防止把无关文档展平成单一输入，使用单独的 API。

| 模型常量 | 提供方 ID | 默认维度 |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("购买后 30 天内可以退货。", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("购买的商品可以在多久内退货？");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "购买后 30 天内可以退货。", "申请退款时请保留收据。" },
    new[] { "标准配送需要三天。", "工作日可使用快速配送。" }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "购买的商品可以在多久内退货？", cancellationToken);
```

索引文档和查询应使用相同的模型、维度和编码。`GetQueryEmbeddingAsync` 将单个查询作为独立文档发给同一个上下文模型。上下文结果保留文档和分块顺序，不会自动接入接收平面输入的 RAG 构建器。

float API 解码提供方的 base64 signed-int8 向量，并为向量相似度进行归一化。显式 binary API 返回压缩位并使用汉明距离，不会把二进制静默当作浮点坐标。0.6B 的完整维度是 1024，4B 是 2560；可选降维遵守提供方限制。批量、文档长度、总令牌和账户速率限制仍然适用。

二进制方法包括 `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync`，以及上下文的 `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`。`PerplexityBinaryEmbedding` 提供 `Dimensions`、返回副本的 `ToArray()` 和越小越相似的 `HammingDistance`。二进制维度须为8的倍数。标准批量最多512个文本，上下文批量最多512个文档、16,000个分块。每文本/文档32K及合计120K令牌由提供方检查。

## 迁移现有 Sonar 代码

此版本在提供方宣布的 2026 年 9 月 27 日端点停用前主动移除旧 Sonar 适配器。`PerplexityService` 改为调用 `/v1/agent`，`AIModels.Perplexity.Sonar` 改为 `perplexity/sonar`。旧 Sonar 专用搜索方法和响应类型已移除。请使用共用 completion/Run/引用、Agent 预设，以及独立检索用的 `PerplexitySearchClient`。

推荐映射为 Sonar → `Fast`、Sonar Pro → `Low`、Sonar Reasoning Pro → `Medium`、Sonar Deep Research → `High`。这是工作流迁移，不保证文本、费用或模型行为相同。动态预设可能随提供方更新而变化；需要固定时请明确指定模型或带版本的配置。

此适配器不支持原生 steering、原生异步客户端工具和 `CachePreservation.Required`。Router/Gateway API 不在本次集成范围。可用组合取决于提供方、模型和账户，本指南不声称所有组合都已通过付费真实调用测试。

Profile、custom skill 和 connector 需要账户中预先注册的资源。请求结构已通过单元测试，但尚未验证使用这些资源的实际 API 成功调用。

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
