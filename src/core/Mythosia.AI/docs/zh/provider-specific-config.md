# 提供商特有配置架构

需要同时获取完整答案、用量和来源时，使用 `await run.Result` 返回的 `AIRunResult`，字符串位于 `result.Text`，无需读取流。这是 Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0 的 API 变更；`GetCompletionAsync` 与 `StructuredStreamRun<T>.Result` 的返回类型保持不变。 [Run 结果与迁移](../../../../../docs/zh-Hans/execution-api-transition.md#run-result).


如需分离每个请求的设置并派生多个版本，请使用[请求构建器](../../../../../docs/zh-Hans/request-building.md)。先调用`CreateRequest(...)`，再连接`With...`。服务属性和服务上的fluent方法保持原有行为。

> [Claude Fable 5.1](../../../../../docs/zh-Hans/fable-5-1.md) 的进度更新、单轮指令和 thinking 绑定诊断从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 开始提供。Mythos 5.1 需要邀请访问，两个模型都拒绝强制工具选择。

> GPT-6 Astra、`AllowAsync`、`StartRunAsync` 和通用推理与搜索 API 从 `Mythosia.AI` 7.1.0 开始提供，共享类型包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

## 原则

应用可以通过[通用推理与搜索 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/reasoning-and-search.md)表达任务所需的推理深度和托管检索。`AIRequestFeatures` 为每个逻辑请求复制，由提供商适配器校验并转换；提供商专有默认设置仍保留在服务上。保留缓存的变更状态存储在受跟踪的会话中。`AICitation` 独立于流的读取保留来源。自定义服务通过可选的 `IAIRequestFeatureService` 提供支持，不向 `IAIService` 添加必需成员。

| 配置类型 | 位置 | 示例 |
|----------|------|------|
| **通用配置** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **提供商特有** | 各服务类 | ThinkingLevel/ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |
| **每个函数的执行许可** | `FunctionDefinition` | `AllowAsync`（默认为 `false`） |

`AllowAsync` 是调用方选择的许可，模型和 API 是否支持则由服务在内部判断。`FunctionBuilder.WithAsync()` 和 `[AiFunction("lookup", "查询数据", AllowAsync = true)]` 也可开启同一许可。GPT-6 Astra 通过 Responses 使用此选项；不支持的模型会省略 API 选项并等待同一个处理器的结果，不会修改已设置的许可。

## 当前实现: 服务级别

提供商特有配置作为各服务类的属性进行管理。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;

geminiService.ChangeModel(AIModels.Google.Gemini3_8Flash);

// 通用配置 → ChatBlock
geminiService.ActivateChat.MaxTokens = 4096;

// 提供商特有配置 → 服务
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;
```

初步检查可使用 `Low`，复杂审查可使用 `High`；更多推理可能增加延迟和 token 用量。两种模型都支持 `Low`、`Medium`、`High`，不支持 `Minimal` 或 `None`。`GeminiThinkingLevel.Auto` 不发送覆盖值，3.8 的提供商默认值为 `Medium`。`ThinkingLevel` 设置服务基线，`WithReasoning(...)` 只覆盖一个逻辑请求。适配器不发送这两种模型的 `temperature`、`topP`、`topK`。提供商上限为输入 1,048,576 token、输出 65,536 token。

### Grok 4.6

快速起草时可以降低推理强度；对于回答质量比响应速度更重要的复杂审查，可以投入更多推理。要使用新增的 `XHigh` 级别，请显式选择 Grok 4.6。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("比较滚动部署与蓝绿部署，包括故障恢复步骤。")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 支持 `Low`、`Medium`、`High` 和 `XHigh` (`GrokReasoning.XHigh`)。`Auto` 省略 `reasoning_effort`，使用提供商的默认值 `High`；不能用 `None` 关闭推理。更多推理可能增加延迟和令牌用量。为保持兼容，`XAIService` 默认模型仍为 Grok 4.5。4.5 支持 `Low` 至 `High`，4.3 支持 `None` 至 `High`；在这些旧模型上请求 `XHigh` 会在发送前被拒绝。

`WithGrokReasoning(...)` 和现有的 `WithGrokParameters(...)` 设置服务的默认推理配置。在 Grok 4.6 上，通用 `WithReasoning(...)` 只覆盖一个逻辑请求，包括其工具轮次和结构化输出修复，之后恢复默认配置。对于这一始终推理的模型，内部 `DisableReasoning` 配置使用 `Low`。通用选项尚未集成 xAI 的缓存保留更新及托管网页、文件搜索。


### Grok Imagine Image 2.0

需要把商品描述变成视觉草稿，或组合不同照片中的主体与背景时，可以使用图像生成和编辑。`XAIService` 与 OpenAI、Google 共用 `IImageGenerationService`，从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 起支持。

独立的默认图像模型为 `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`)。图像请求不会更改所选聊天模型，也不会追加到聊天记录中。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "日出时的玻璃展亭，宽幅构图",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

每个 `GeneratedImage.Data` 包含解码后的图像字节；请根据 `MediaType` 选择文件扩展名。适配器请求内联 base64 输出，不下载提供方托管的图像 URL。`Count` 支持1–10张输出；编辑支持1–5张 JPEG、PNG 或 WebP 参考图。

xAI仅支持新的共用默认值`ImageOutputFormat.Auto`。它无法选择输出编码，因此显式`Jpeg`、`Png`、`WebP`会在发送前拒绝。请按`GeneratedImage.MediaType`选择扩展名，库不会转码。质量支持`ImageQuality.Auto`、`Low`、`Medium`，背景仅支持`ImageBackground.Auto`；不支持显式压缩或单独的`Mask`。

Google使用`ImageSize.Auto`或模型支持的`ImageResolution.Auto`、`FiveTwelve`、`OneK`、`TwoK`、`FourK`的`Preset`。输出支持`ImageOutputFormat.Auto`或显式`Jpeg`，拒绝`Png`/`WebP`。Google和xAI拒绝`Pixels`，OpenAI支持`Auto`/`Pixels`并拒绝`Preset`。参见[迁移示例](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/providers.md#image-options-migration)。

### DeepSeek Flash

需要快速回答后深入审查，或解释图表、截图时，可使用 DeepSeek Flash。`AIModels.DeepSeek.Flash` (`deepseek-flash`) 选择2026年9月10日发布、原生支持视觉理解的 V4.1 Flash。沿用补全、流式、Run、函数调用和 RAG API，从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 起支持。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("比较滚动部署与蓝绿部署，并分析回滚风险。");

await using var run = await deepseek
    .CreateRequest("检查上述比较中的假设。")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

库的 `ThinkingEnabled` 默认仍为 `false`。`WithDeepSeekReasoning(...)` 开启推理并设置持续生效的 `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`)；原生 `Auto` 省略 effort，使用提供方默认 `High`。共用 `WithReasoning(...)` 仅覆盖一个逻辑请求及工具轮次：`None` 关闭推理，`Minimal`/`Low` 映射到 `Low`，`Medium`/`High`/`XHigh` 映射到 `High`，`Max` 映射到 `Max`。共用 `Auto` 保留当前默认配置。增加推理可能提高响应时间和令牌用量。 仅修改 `ReasoningEffort` 属性不会开启推理。

通过 `WithFunction(...)` 注册本地函数，让模型通过应用代码查询数据或执行操作。推理与非推理均支持工具，但推理模式拒绝强制/必选工具，应使用自动选择。适配器保留原生 `reasoning_content` 和调用 ID，供后续工具轮次重放。Run 与原有流式 API 在启用 `StreamOptions.WithReasoning()` 后通过 `StreamingContentType.Reasoning` 输出推理；观察选项本身不会开启推理。用量包含提供方报告的缓存和推理令牌。 自动上下文恢复使用共用流式循环。工具需要此前的原生推理历史时，为保留历史会阻止自动压缩，并传递超限错误。

提供方标示上下文1M、输出最多384K (`393216`)令牌；库默认请求预算仍为8,000。推理模式省略 temperature/penalty，`top_p` 至少0.95；非推理模式省略 `top_p`。适配器使用 Chat Completions，未集成 Responses、托管搜索、`CachePreservation.Required`、原生异步工具或 `SteerAsync`。本地 RAG 和普通工具轮次仍可使用。

### Perplexity Agent API

当回答需要基于最新信息，并让读者能够核对来源时，可以使用 Perplexity。`PerplexityService` 调用 Agent API；独立搜索和嵌入则用于为自行选择的回答模型构建检索能力。

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low,
        MaxSteps = 8
    });
string answer = await service.GetCompletionAsync("比较最新的电池回收方法，并注明来源。");
```

`WithPerplexityOptions(...)` 设置服务的持续配置，每个逻辑请求都会捕获副本。共用 `WithReasoning(...)` 和 `WithWebSearch(...)` 作用于下一个逻辑请求，包括客户端工具轮次和类型化输出修复。内部 RAG 查询改写不会继承最终回答的搜索设置。

可用 `UsePreset(...)` 快速选择预设。预设/配置自行选择模型，`ModelOverride` 可明确替换。`DisableWebSearch` 仅移除适配器默认工具，不保证关闭预设内置搜索。根据模型可用 `Minimal`、`Low`、`Medium`、`High`、`XHigh`、`Max`；`None` 和直接 Sonar 的显式推理设置会被拒绝。内部 `DisableReasoning` 使用较低的可用级别或省略设置，不保证完全关闭推理。

`PerplexityHostedTools.WebSearch`、`FetchUrl`、`Sandbox`、`FinanceSearch`、`PeopleSearch`、`Mcp`、`Connector` 可创建工具配置。MCP 不暂停等待批准，需要时用 `allowedTools` 限制。Connector 是提供方预览功能，引用已连接的集成。

`StartBackgroundAsync` 捕获输入但不追加历史，并拒绝启用的本地函数或 `Store = false`。`GetResponseAsync` 查询一次；`WaitForCompletionAsync` 轮询到终止状态。保存 `Id` 与 `LastSequenceNumber`，用 `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` 重连。`CancelAsync` 取消远程任务；取消查询/读取令牌只停止该客户端操作。`LastResponse` 包含文本、状态、用量、引用与 `OutputJson`，使用答案前请检查终止状态。

工具、推理、图像和模式兼容性由所选模型决定。共用 `WithFileSearch` 不是 Perplexity 向量存储适配器。沙箱生成文件、上传附件和远程 MCP 数据是不同资源，不会自动变成共用文件搜索存储。

[Perplexity Agent API、搜索与嵌入](../../../../../docs/zh-Hans/perplexity.md).


### 优点
- ChatBlock对提供商完全无关（干净的分离）
- 符合OOP原则（服务管理自己的特有配置）
- 一个服务实例一套特有配置 → 简单结构

### 缺点
- 一个服务内的多个ChatBlock共享相同的特有配置

## 需要迁移到ChatBlock级别的情况

如果未来出现 **每个ChatBlock需要独立维护特有配置的需求**，通过在ChatBlock内添加延迟初始化的配置类进行迁移。

```csharp
// 示例（当前未实现）
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// 使用
chatBlock.Gemini.ThinkingBudget = 1024;
```

### 需要此方式的场景
- 一个服务实例中ChatBlock A和B需要使用不同的ThinkingBudget
- 实际上这种情况非常罕见，因此目前维持服务级别

## 决策日志

- **2026-02-12**: 最初以Option B（ChatBlock级别）实现后，回滚到服务级别。判断特有配置放在服务中更自然。

## 何时需要控制执行中的任务

耗时较长的任务需要向用户展示进度，也可能需要在执行过程中调整要求。通过 `StartRunAsync` 返回的 `AIRun` 可以控制该任务，而执行中追加指令的支持情况由提供商决定。模型设置仍在服务上配置，并应在启动前完成；发送追加指令前检查 `run.CanSteer`。使用场景、示例、取消和兼容性说明请参阅 [Run 使用指南](../../../../../docs/zh-Hans/execution-api-transition.md)。

## 自定义提供商实现

公开服务属性在执行期间仍表示默认值。自定义`AIService`子类构建发送数据时应使用`RequestTemperature`、`RequestTopP`、`RequestMaxTokens`、`RequestSystemMessage`、`RequestModel`和`RequestFunctions`等protected访问器。直接读取`Temperature`会得到服务默认值，忽略构建器覆盖。在`CaptureRequestSettings`中先调用base实现，再保存额外的提供商默认值，并复制可变集合。通过`RequestSetting<T>`读取自定义值。若提供商捕获独立的选项对象，请在`CloneProviderRequestOptions`中复制。绕过base执行的现有override入口应进入`BeginRequestSettingsScope()`并保留现有功能作用域。这是实现扩展契约，不会为`IAIService`新增必需成员。

自定义工具执行仍可重写原有的protected virtual `ProcessFunctionCallAsync(FunctionCall)`。使用protected `FunctionCancellationToken`向I/O或`HandlerWithCancellation`传递执行取消。直接调用旧`Handler`委托会使用`CancellationToken.None`；默认执行器已选择支持取消的路径。参见[工具约定](../../../../../docs/zh-Hans/function-calling.md#tool-execution-contract)。

只需完整答案和停止按钮时，将 `cancellationToken` 传给 `GetCompletionAsync`。进度事件或受支持的中途追加指令使用 Run。参阅[取消回答](../../../../../docs/zh-Hans/completions.md#completion-cancellation)。

此取消契约包含在 Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0 中。不传令牌的调用和现有 profile/context 位置参数在源代码层面仍有效，但使用方需要重新构建。自定义 `IAIService` 实现必须在两个完成方法签名末尾添加并传递 `CancellationToken cancellationToken = default`。继承 `AIService` 的自定义供应商保留现有 `GetCompletionAsync(Message)` override，并将受保护的 `RequestCancellationToken` 传给传输层。构建器和 Run 本身不需要这次接口修改。 如果子类重写了已修改的字符串/profile/context 完成调用、图像辅助方法或 `RunAgentAsync` 等 public virtual 重载，也必须追加并传递新的 `CancellationToken`；仅接收单个 `Message` 的供应商 override 保留原签名。直接绑定到已修改签名的方法组委托可能需要改成显式传入或省略令牌的 lambda。

[用共享支持定义构建模型功能选项](../../../../../docs/zh-Hans/model-capabilities.md).

如果自定义提供程序的配置会改变原生模式标志，请重写 `ApplyCapabilityRequestProfile(AIRequestProfile)`，并仅通过 `SetExecutionSetting(...)` 应用解析支持信息所需的标志。默认钩子不执行任何操作。构建器已捕获公共配置覆盖值；查询不会调用 `ApplyRequestProfile` 或 `ApplyProviderSpecificRequestProfile`。此钩子不得执行验证、回调、序列化、预算预留，或修改服务及调用方拥有的状态。临时设置会在查询结束后恢复，重写方法抛出异常时也一样。
