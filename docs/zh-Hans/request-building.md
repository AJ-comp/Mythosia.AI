# 让每个请求的设置互相独立

> Claude Sonnet 5.5: 需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0。[配置与迁移](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: 需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0。[模型选择与迁移](providers.md#gpt-61-sol)

> Grok 4.7: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择、推理与处理速度](providers.md#grok-47)

文档摘要可能需要较低的Temperature，创意草稿则需要较高的值。准备草稿不应悄悄改变已经准备好的摘要请求。当不同调用需要不同设置，或需要从基础请求派生多个版本时，请使用`CreateRequest`。

需要同时获取完整答案、用量和来源时，使用 `await run.Result` 返回的 `AIRunResult`，字符串位于 `result.Text`，无需读取流。这是Mythosia.AI 8.0.0 的 API 变更；`GetCompletionAsync` 与 `StructuredStreamRun<T>.Result` 的返回类型保持不变。 [Run 结果与迁移](execution-api-transition.md#run-result).

只需完整答案和停止按钮时，将 `cancellationToken` 传给 `GetCompletionAsync`。进度事件或受支持的中途追加指令使用 Run。参阅[取消回答](completions.md#completion-cancellation)。

> `CreateRequest`示例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和公共请求功能的旧7.1版本不包含构建器；旧包可继续使用原有服务重载。

## Before：共享服务设置

现有服务的`WithTemperature`会修改服务并返回同一实例。以下两个变量引用同一个服务，因此后设置的值会作用于两者。这些旧方法仍可用于配置服务默认值。

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("请解释这份文档。"); // 0.8
```

## After：从独立请求派生

`CreateRequest`会保存服务默认值。构建器的每个`With...`都返回新的构建器，不修改原对象。执行时直接使用该请求保存的设置，不会临时覆盖服务默认值。

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("请解释这份文档。");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// 使用0.2；creative和服务默认值保持不变。
```

请使用返回的构建器。仅调用`basis.WithTemperature(0.2f);`并丢弃返回值，不会改变`basis`。

构建器验证输入而不会静默修正：`WithTemperature`范围为0–2，`WithTopP`为0–1，惩罚为−2–2，拒绝NaN和无穷值。Token数、轮数、并发数和指定的超时必须为正。无效输入抛出`ArgumentException` / `ArgumentOutOfRangeException`。旧服务的Temperature方法仍会限制到有效范围。

## 各对象的职责

`AIService`管理提供商连接、默认值和现有会话状态。公开类型`Mythosia.AI.Builders.AIRequestBuilder`提供fluent API，内部类型`AIRequest`将确定的输入和设置传给执行层。用户无需调用`Build()`。`AIRequest`也不是回答结果：`GetCompletionAsync()`返回`Task<string>`，`StartRunAsync()`返回`Task<AIRun>`。

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## 用相同设置启动可控制的运行

只需要完整回答时使用`GetCompletionAsync()`。需要显示进度或向支持的运行追加指令时，使用`StartRunAsync()`。输入传给`CreateRequest`，不再传给构建器的执行方法。`run.StreamAsync()`继续观察该运行，`run.SteerAsync(...)`的模型支持条件保持不变。

```csharp
await using var run = await service
    .CreateRequest("请解释这份文档。")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

本地工具可以通过`Task<T>` / `ValueTask<T>`返回对象，并接收库注入的`CancellationToken`。`run.Cancel()`或启动令牌的取消会传递给配合取消的工具，仅停止流读取则不会。异常会记录为失败；取消时跳过排队调用，清理仍会等待已启动且忽略令牌的工具。参见[结果、错误与取消](function-calling.md#tool-execution-contract)。

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## 复用配置档和上下文

`WithProfile`复制现有的`AIRequestProfile`，`WithContext`复制`AIRequestContext`。之后修改原对象不会影响已准备的请求。构建器可设置采样、系统指令、无状态模式、函数调用策略，以及受支持的推理、网页和文件搜索。提供商能力检查仍然适用，构建器不会使不支持的选项变得可用。

`WithFunctions(params FunctionDefinition[])`向请求添加复制的函数定义。导入`Mythosia.AI.Extensions`后，也可用`WithFunctions(toolInstance)`和`WithStaticFunctions<T>()`注册现有的特性函数。在`CreateRequest`之前注册的是服务默认值，之后注册的仅用于请求。服务待处理的下一次调用功能和策略在`CreateRequest`时被捕获并消费；需要复用时请保留返回的构建器。

```csharp
var request = service
    .CreateRequest("请将这个问题改写为适合搜索的形式。")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\n请保留原意。"
    });

string rewritten = await request.GetCompletionAsync();
```

Claude 会统一确定模型、请求用途、推理和 thinking 绑定设置。对于 `RequestProfiles.Summarization` 或 `RequestProfiles.QueryRewrite` 等辅助配置，仅当 `DisableReasoning = true`、用途不是 `Default` 且实际请求无状态时，才省略继承的绑定策略。这样可防止没有会话前缀需要保留的请求因继承策略而重新启用推理或被拒绝。允许关闭推理的模型会关闭推理；始终推理的 Opus 5.5、Fable 5.1 和 Mythos 5.1 使用 `Low` 并省略可读 thinking，Sonnet 5.5 则使用 high effort 的 `between_tools`。

完成、流式、结构化输出和 Run 使用同一请求准备流程：捕获设置，执行一次实际的配置处理，然后验证最终的通用选项和提供者原生选项。这些步骤先于自动摘要、追加新输入和建立传输连接，因此自定义提供者的配置覆盖会参与实际验证。Claude 实际使用的手动 `ThinkingBudget` 若达到或超过模型输出上限，也会在此阶段被拒绝；有效配置和通用推理设置的优先级保持不变。

应用发起的调用会启动独立的逻辑请求，包括从 `SystemMessageProvider` 或工具回调发起的普通调用，以及复用同一个 `AIRequestProfile`、`Message` 的调用。对象复用不代表执行复用。框架内部委派、工具轮次、重试和格式修复会延续原请求，其配置仅应用一次。普通子请求读取自己的选项和服务默认值，构建器保留已捕获的设置。成功、失败或取消后均恢复父请求的执行状态。转发框架调用的提供者重写方法遵循[下方的适配器规则](#provider-request-adapters)。

内置提供者保存内置输入内容的独立副本。复用 `Message` 发起新调用时，会应用本次的上下文和轮次指令，不会改写已接受的历史。自定义内容和不支持的元数据对象仍由所有者管理。这不保证同一会话的并发调用安全。

`StartRunAsync` 返回后，Run 仍持有最终设置的独立副本。恢复调用方的配置不会改变正在运行的 Run，执行用的配置钩子仍只调用一次。

无状态辅助请求使用独立会话，不继承父请求的输出模式、托管工具或一次性选项。隔离不会跳过原生选项验证，OpenAI 和 Perplexity 的 Run 也遵循此规则。父请求的设置、消息、`CurrentSummary` 和观测信息保持不变。有状态请求继续执行绑定与会话检查。现有公共 API 不变。

库自动生成的会话摘要也会排除父请求的 `SystemMessageProvider` 回调和请求上下文，防止继承的 `RequestMessageOverride` 替换内部摘要提示词。应用主动发起的请求仍正常应用动态上下文，包括明确要求模型总结文本的请求。

无状态请求也会跳过父会话的自动摘要。现有 `GetCompletionAsync(string, profile)` 重载与 `Message` 重载和请求构建器行为一致，保留父会话的 `CurrentSummary` 和消息。有状态请求继续使用原有的自动摘要行为。

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## 在自定义提供者中转发请求

框架调用虚拟提供者适配器时，适配器对相应基类入口的第一次调用会延续已准备的请求，即使重写方法替换了输入 `Message` 也是如此。构建器已捕获的选项和已应用的配置在转发时仍然保留。默认的回调式流适配器也会延续同一个已准备的请求。

适配器可以转发修改后的 `AIRequestProfile`：相同值不会重复应用，变更值基于已捕获的设置替换先前的配置层，并在自动摘要和发送前重新验证。因此，切换到无状态模式不会先摘要父会话。OpenAI 无状态辅助请求不检查无关的保留历史，也不会清除父会话的保护状态。

替换转发的配置文件时，会保留请求内后来添加、删除或修改的工具、策略变更及显式设置赋值，即使重新赋予相同的标量值也一样。更改其他配置字段不会恢复适配器已删除的工具，也不会重新读取服务默认值。

对于库无法了解内部结构的自定义设置对象，适配器应通过 `SetExecutionSetting` 替换其值，而不是修改内部字段。库不会为了跟踪配置变更而检查任意应用对象或调用其序列化器。

Claude 压缩保留 `RequestMessageOverride` 和 `AdditionalMessages` 中的调用／结果依赖，包括服务器工具，并保护 Mythos 5.1 绑定的 thinking 前缀。并行工具的旧格式记录在普通历史和附加消息中都只合并一次，且保留每条记录的归属。

如果在转发之前，另一个无关的辅助调用也使用相同的基类入口，就会产生歧义：框架无法判断它是否是在延续原请求。请使用 protected 方法 `BeginIndependentRequestScope()` 的作用域包住辅助调用及其 `await`；对于流式调用，作用域必须覆盖整个枚举过程。辅助请求从服务默认值开始，释放作用域会恢复外层的设置、功能、上下文及待处理的委派。上下文或工具回调中的普通嵌套调用本身已独立，无需此作用域。

例如，具体提供者的子类可以先改写文本，再转发请求：

```csharp
public override async Task<string> GetCompletionAsync(
    Message message, AIRequestProfile? profile = null,
    AIRequestContext? context = null, CancellationToken cancellationToken = default)
{
    string rewritten;
    using (BeginIndependentRequestScope())
    {
        rewritten = await base.GetCompletionAsync(
            new Message(ActorRole.User, message.Content),
            RequestProfiles.QueryRewrite,
            cancellationToken: cancellationToken);
    }

    var replacement = new Message(message.Role, rewritten);
    return await base.GetCompletionAsync(replacement, profile, context, cancellationToken);
}
```

此作用域隔离请求的执行状态，不会隔离会话历史或使服务支持并发使用。示例使用无状态的 `QueryRewrite` 配置，避免辅助请求加入父会话。

Claude 压缩会检查以规范传输格式保留的历史，包括通过 `AIRequestContext.AdditionalMessages` 加入的带签名 thinking。默认的 thinking 绑定会保护该前缀，防止自动或显式摘要对其进行压缩。显式指定 `ClaudeThinkingPrefixMismatchBehavior.DropBlock` 可在支持时允许压缩；其他会话约束仍然适用。

## 哪些内容被复制，哪些状态仍然共享

公共设置和提供商默认值在调用`CreateRequest`时保存。之后更改服务默认值不会改变已准备的请求。内置消息内容、受支持的选项集合、配置档、上下文和策略都会复制。函数处理程序、动态上下文回调和自定义消息内容保留引用。请勿修改自定义内容，并注意委托仍可读取外部状态。动态上下文回调在执行时运行。

捕获完成后，即使释放原始 `JsonDocument` 或修改原始 `JsonNode`，请求元数据和函数调用参数中保存的 JSON 值也不会改变；每次执行都使用独立副本。工具架构的 `Items` 链存在循环或嵌套超过 64 层时，会在捕获阶段（`CreateRequest` 或 `WithFunctions`）抛出 `ArgumentException`，以便在执行前正常报告无效架构，而不是耗尽进程堆栈。

复制还会保留数组的维数和起始索引，以及标准 `Dictionary<,>`、`SortedDictionary<,>` 和 `SortedList<,>` 的键比较规则。因此，原本不区分大小写的键查找在请求中仍然如此。空的 `default(JsonElement)` 值（`Undefined`）也会原样保留。未知的自定义元数据对象仍保留引用，由其所有者负责保持不变或协调访问。

标准`ReadOnlyCollection<T>`和`ReadOnlyDictionary<TKey, TValue>`在强类型数组或字典中也会保留原始类型。复制支持的底层集合时，会保留只读视图、共享引用和循环引用。`Hashtable`及非泛型`SortedList`也会保留键比较规则。

构建器不是独立会话。它使用执行时服务的活动会话历史，创建时不会冻结历史。有状态调用仍会更新共享会话。不想读取或累积历史时，请使用`WithStatelessMode()`。每个服务只允许一个活动Run的限制保持不变。请求设置独立不代表同一服务支持并行执行；独立会话的并发任务应使用不同服务。

## 现有调用和扩展

`GetCompletionAsync`和现有服务入口继续受支持。`BeginMessage()` / `MessageChain`保留原来的可变消息构建方式，执行层使用新的请求路径。需要分支复用设置时请选择`CreateRequest`。构建器API属于`AIService`及其提供商实现，不会为`IAIService`新增必需成员。仅使用抽象接口或RAG包装器的代码继续使用现有配置档、上下文和执行API。

[用共享支持定义构建模型功能选项](model-capabilities.md).

<a id="inference-speed"></a>

## 按任务选择处理速度

用户正在等待的请求可选择付费低延迟处理，后台报告可使用普通处理。 `WithSpeed` 保持模型和推理级别，只选择处理模式。 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。

`ProviderDefault` 不覆盖现有服务或供应商设置；项目默认值也可能已经是 Fast。`Standard` 显式要求普通处理。`Fast` 请求供应商的付费低延迟模式，可能产生额外费用。请保留返回的构建器：以下三个分支相互独立，不修改原始请求。

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

显示选项前检查 `GetSpeedSupport(InferenceSpeed.Fast)`。`StandardSpeed` 和 `FastSpeed` 同样区分 Supported、Unsupported、Unknown。本地 Supported 不保证账户权限、容量或延迟。显式 Standard/Fast 在不支持或未知时会失败，不会悄悄更改模型或推理级别。使用 `ProviderDefault` 保持原有路径。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` 无需读取流即可保留不可变的 `AIProcessingInfo`。`RequestIndex` 从 1 开始，标识供应商推理尝试，包含服务器 continuation；不等于工具轮次或 HTTP 请求数量；工具后续调用、重试及格式修复可能增加记录。包括失败尝试在内，服务器未报告可识别模式时 `AppliedSpeed` 为 null。`RawAppliedMode` 和 `ResponseId` 保留报告的原始信息。仅在请求 Fast 而明确报告 Standard 时，`IsDowngraded` 才为 true；false 不能证明已应用 Fast。

普通 completion 完成后立即读取 `AIService.LastProcessing`；后续逻辑请求会替换此视图，已获取的记录保持不可变。服务扩展只配置下一个逻辑请求及其工具往返，不设置永久默认值。辅助摘要、内部查询改写和内部 profile 不继承主请求的速度覆盖，也不混入主请求的观测记录。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

这些值是供应商报告的处理模式，不是每秒 token 数的实测值。OpenAI、xAI、Google 可能在服务器端降级；Mythosia 不会自动换速度重试。Anthropic fast mode 需要权限，仅限直接 Claude API，切换速度可能使提示缓存失效。Gemini Developer API priority 需要 Tier 2/3 资格。请另行确认供应商、模型、API 支持及收费；此设置不适用于图像生成、嵌入或原生 Batch API。 [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

通过 `IAIService` 引用时，使用 `Mythosia.AI.Extensions` 的 `GetLastProcessing()`。它读取可选的 `IAIProcessingInfoService`；不支持诊断时返回空列表。`IAIService` 不增加必需成员。RAG 中 `RagEnabledService.WithSpeed(...)` 配置检索后的下一次回答，`LastProcessing` 描述该回答；内部查询改写保持分离。Run 结果提供相同的 `Processing` 记录。

本次实现的 Fast 支持列表如下。请通过 `GetSpeedSupport(InferenceSpeed.Standard)` 单独检查 Standard。列表外模型、第三方端点和 OpenAI 兼容供应商不会自动继承付费处理支持。

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — 其他已知 Claude 模型，包括 Sonnet 5 | 省略 `speed` 和 fast-mode beta | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

这些其他 Claude 模型的 Standard 使用原有普通请求。服务器未报告处理信息时，`AppliedSpeed` 保持 null，不会仅根据请求值推断已应用 Standard。
