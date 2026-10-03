# 提供商特有配置架构

> GPT-6.1 Sol: 需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0。[模型选择与迁移](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/providers.md#gpt-61-sol)

> GPT-6 Sol/Luna 从 Mythosia.AI 8.1.0 / Abstractions 4.1.0 开始提供。

需要同时获取完整答案、用量和来源时，使用 `await run.Result` 返回的 `AIRunResult`，字符串位于 `result.Text`，无需读取流。这是 Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0 的 API 变更；`GetCompletionAsync` 与 `StructuredStreamRun<T>.Result` 的返回类型保持不变。 [Run 结果与迁移](../../../../../docs/zh-Hans/execution-api-transition.md#run-result).


如需分离每个请求的设置并派生多个版本，请使用[请求构建器](../../../../../docs/zh-Hans/request-building.md)。先调用`CreateRequest(...)`，再连接`With...`。服务属性和服务上的fluent方法保持原有行为。

> [Claude Fable 5.1](../../../../../docs/zh-Hans/fable-5-1.md) 的进度更新、单轮指令和 thinking 绑定诊断从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 开始提供。Mythos 5.1 需要邀请访问，两个模型都拒绝强制工具选择。

> GPT-6 Astra、`AllowAsync`、`StartRunAsync` 和通用推理与搜索 API 从 `Mythosia.AI` 7.1.0 开始提供，共享类型包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

<a id="claude-sonnet-55"></a>

## Claude Sonnet 5.5

`AIModels.Anthropic.ClaudeSonnet5_5` (`claude-sonnet-5-5`) 支持文本和图像输入、文本输出，具有 1M 上下文窗口和最多 128K 输出 token。需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0；现有默认模型及模型标识符保持不变。

未修改设置时使用 adaptive 推理、`High` effort，并省略可读推理。Adaptive 支持 `Low`、`Medium`、`High`、`XHigh` 和 `Max`，拒绝 `Minimal`。`MaxTokens` 包含推理和回答。采样参数不会发送。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeSonnet5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await claude.CreateRequest("Review the plan using the registered tools.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(options: StreamOptions.FullOptions);
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

Adaptive 模式可用 `ClaudeThinkingDisplay.Updates` 获取工具进度，或用 `Summarized` 获取推理摘要。读取 `StreamingContentType.Reasoning`，普通完成后读取 `LastThinkingContent`。Adaptive 辅助方法的默认 display 参数为 `Summarized`，与未设置时不同。`between_tools` 自动返回工具进度；不保证固定通知间隔。

`ReasoningLevel.None`、禁用的旧版 `ThinkingBudget` 或 `AIRequestProfile.DisableReasoning` 会选择 high effort 的 `between_tools`，关闭预先推理，但工具进度仍可能作为 thinking 块返回。`WithBetweenToolsThinking(...)` 接受 `Auto`（high）、`Low`、`Medium` 或 `High`，拒绝 `XHigh` 和 `Max`。发送的 thinking 对象只有 `type`，没有 display、budget 或 binding。此模式不支持按消息更改 effort 或 `CachePreservation.Required`。公共 `WithReasoning(Low...Max)` 会切回 adaptive，`Auto` 则遵循所选提供者模式。

```csharp
claude.StartNewConversation(AIModels.Anthropic.ClaudeSonnet5_5);
claude.WithBetweenToolsThinking(ClaudeReasoningEffort.Low);
string quick = await claude.CreateRequest("Use the registered tools to check the status.")
    .GetCompletionAsync();

// A separate conversation using request-scoped high-effort between_tools.
claude.StartNewConversation(AIModels.Anthropic.ClaudeSonnet5_5);
string next = await claude.CreateRequest("Give me the latest status.")
    .WithReasoning(ReasoningLevel.None)
    .GetCompletionAsync();
```

Claude 会统一确定模型、请求用途、推理和 thinking 绑定设置。对于 `RequestProfiles.Summarization` 或 `RequestProfiles.QueryRewrite` 等辅助配置，仅当 `DisableReasoning = true`、用途不是 `Default` 且实际请求无状态时，才省略继承的绑定策略。这样可防止没有会话前缀需要保留的请求因继承策略而重新启用推理或被拒绝。允许关闭推理的模型会关闭推理；始终推理的 Opus 5.5、Fable 5.1 和 Mythos 5.1 使用 `Low` 并省略可读 thinking，Sonnet 5.5 则使用 high effort 的 `between_tools`。

完成、流式、结构化输出和 Run 使用同一请求准备流程：捕获设置，执行一次实际的配置处理，然后验证最终的通用选项和提供者原生选项。这些步骤先于自动摘要、追加新输入和建立传输连接，因此自定义提供者的配置覆盖会参与实际验证。Claude 实际使用的手动 `ThinkingBudget` 若达到或超过模型输出上限，也会在此阶段被拒绝；有效配置和通用推理设置的优先级保持不变。

应用发起的调用会启动独立的逻辑请求，包括从 `SystemMessageProvider` 或工具回调发起的普通调用，以及复用同一个 `AIRequestProfile`、`Message` 的调用。对象复用不代表执行复用。框架内部委派、工具轮次、重试和格式修复会延续原请求，其配置仅应用一次。普通子请求读取自己的选项和服务默认值，构建器保留已捕获的设置。成功、失败或取消后均恢复父请求的执行状态。转发框架调用的提供者重写方法遵循[提供者适配器规则](../../../../../docs/zh-Hans/request-building.md#provider-request-adapters)。 框架调用虚拟提供者适配器时，对相应基类入口的第一次调用会延续已准备的请求，即使替换了输入 `Message` 也不例外。如果在转发前通过同一个基类入口执行无关的辅助调用，请用 `BeginIndependentRequestScope()` 包住该调用及其 `await`；流式调用的作用域须覆盖整个枚举过程。

内置提供者保存内置输入内容的独立副本。复用 `Message` 发起新调用时，会应用本次的上下文和轮次指令，不会改写已接受的历史。自定义内容和不支持的元数据对象仍由所有者管理。这不保证同一会话的并发调用安全。

`StartRunAsync` 返回后，Run 仍持有最终设置的独立副本。恢复调用方的配置不会改变正在运行的 Run，执行用的配置钩子仍只调用一次。

无状态辅助请求使用独立会话，不继承父请求的输出模式、托管工具或一次性选项。隔离不会跳过原生选项验证，OpenAI 和 Perplexity 的 Run 也遵循此规则。父请求的设置、消息、`CurrentSummary` 和观测信息保持不变。有状态请求继续执行绑定与会话检查。现有公共 API 不变。

库自动生成的会话摘要也会排除父请求的 `SystemMessageProvider` 回调和请求上下文，防止继承的 `RequestMessageOverride` 替换内部摘要提示词。应用主动发起的请求仍正常应用动态上下文，包括明确要求模型总结文本的请求。

无状态请求也会跳过父会话的自动摘要。现有 `GetCompletionAsync(string, profile)` 重载与 `Message` 重载和请求构建器行为一致，保留父会话的 `CurrentSummary` 和消息。有状态请求继续使用原有的自动摘要行为。

`(ClaudeReasoningEffort)1234` 等未定义的 `ClaudeReasoningEffort` 值会在实际推理配置使用该原生 effort 时于本地被拒绝。被拒绝的请求不会触发父会话的自动摘要，也不会将未发送的输入保留在历史记录中。有效的显式通用推理设置或配置覆盖仍按原有优先级覆盖原生基线设置。

`ClaudeThinkingMode`: `Auto` / `Adaptive` / `BetweenTools`; `AnthropicService.ThinkingMode`.

请只向历史末尾追加内容。带签名的 thinking 块（包括空块与 `progress_updates` 元数据）会在轮次和工具结果间保留。修改已保存的 assistant 响应会在本地被拒绝，`ClaudeThinkingPrefixMismatchBehavior.DropBlock` 也不例外。旧 user/system/tool 前缀修改不会自动在本地阻止，而是交给 Anthropic 的绑定策略处理。Adaptive 中的 `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` 请求提供者严格验证，无效前缀可能产生 HTTP 400；`DropBlock` 允许提供者丢弃受影响的推理，null 使用提供者默认策略。通过 `LastInputTransformations` 查看报告的丢弃。`between_tools` 不支持绑定控制。通过 `WithTurnInstruction` / `WithConversationInstruction` 添加新指令。Adaptive 的 `CachePreservation.Required` 也不能保证编辑旧消息是安全的。

继续使用现有完成、流式、结构化输出、图像、本地函数、网页搜索和普通 Run API。不要设置 `ForceFunctionName`；强制工具选择（`any` / `tool`）和 assistant prefill 在 HTTP 前被拒绝，自动选择与 `FunctionsDisabled` 仍可用。不支持 `Fast`、原生异步工具或 Run steering。此集成未提供 computer toolset、advisor 工具、原生压缩、对话内工具更改或自动服务器 fallback。切换模型或账户可能丢弃绑定的推理；请求成功并不代表推理已保留。

Mythosia.AI 8.2.0 的已知限制：Sonnet 5.5 和 Opus 5.5 会将以尚未执行的 `server_tool_use` 结尾的有效 `pause_turn` 响应误判为 assistant prefill，并在第二次 HTTP 请求前拒绝继续。以已完成的服务器工具结果结尾的续传已通过现有检查。请参阅[原生续传限制](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/providers.md#claude-native-continuation-limitation)。

公共结构化输出 API 使用模式提示、反序列化和修复重试，不发送原生 `output_config.format` 模式约束。

[官方模型信息](https://platform.claude.com/docs/en/models/sonnet-5-5/overview) · [迁移](https://platform.claude.com/docs/en/models/sonnet-5-5/migration-guide) · [提供者变更](https://platform.claude.com/docs/en/models/sonnet-5-5/whats-new-sonnet-5-5).


## Claude token 计算

无参数的 `GetInputTokenCountAsync()` 复用支持工具的完成消息序列化，即使当前工具已禁用，也会保留 assistant 的 `tool_use` 和 user 的 `tool_result` 块。导入的旧版并行工具记录若共享同一个 `OriginalContent` 批次，该批次只会序列化一次，并保留带签名的内容和历史指令。它包含当前启用的工具定义及其 `tool_choice`，省略仅用于生成的字段，并保留 token 计算专用的 thinking 限制。`GetInputTokenCountAsync(string prompt)` 继续仅计算独立提示词，不包含已存储的会话历史或工具定义。

普通完成、保留 thinking 的后续请求和 token 计算使用同一工具历史转换。禁用或移除函数后发送的普通请求也会将历史调用及结果保留为原生工具块。导入的 `FunctionSource` 元数据在调用和结果两侧均支持含义相同的已定义 enum、整数、字符串及 JSON 表示。同一并行 assistant 批次的重复记录只发送一次，不改变工具顺序、签名或提供者字段；仅改变元数据表示不会使已接受的前缀重复。

Claude 在序列化、token 计算和摘要压缩保护中读取同一份权威 assistant 内容，涵盖原生内容、类型化批次及旧版 `Message.Metadata[OriginalContent]`。因此，无论导入表示如何，带签名 thinking 的前缀都受到相同保护。这只说明本地历史保留，不保证实际服务器接受签名。

## 原则

应用可以通过[通用推理与搜索 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/reasoning-and-search.md)表达任务所需的推理深度和托管检索。`AIRequestFeatures` 为每个逻辑请求复制，由提供商适配器校验并转换；提供商专有默认设置仍保留在服务上。保留缓存的变更状态存储在受跟踪的会话中。`AICitation` 独立于流的读取保留来源。自定义服务通过可选的 `IAIRequestFeatureService` 提供支持，不向 `IAIService` 添加必需成员。

| 配置类型 | 位置 | 示例 |
|----------|------|------|
| **通用配置** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **提供商特有** | 各服务类 | ThinkingLevel/ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |
| **每个函数的执行许可** | `FunctionDefinition` | `AllowAsync`（默认为 `false`） |

`AllowAsync` 是调用方选择的许可，模型和 API 是否支持则由服务在内部判断。`FunctionBuilder.WithAsync()` 和 `[AiFunction("lookup", "查询数据", AllowAsync = true)]` 也可开启同一许可。GPT-6.1 Sol / GPT-6 Astra / Sol / Luna 通过 Responses 使用此选项；不支持的模型会省略 API 选项并等待同一个处理器的结果，不会修改已设置的许可。

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

Google 使用 `ImageSize.Auto` 或模型专属分辨率和比例的 `Preset`。[Google 模型专属图像选项](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/providers.md#google-image-options).输出支持`ImageOutputFormat.Auto`或显式`Jpeg`，拒绝`Png`/`WebP`。Google和xAI拒绝`Pixels`，OpenAI支持`Auto`/`Pixels`并拒绝`Preset`。参见[迁移示例](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/providers.md#image-options-migration)。

Google 的 `Resolutions` 和 `AspectRatios` 取决于所选图像模型，也用于生成和编辑验证。请参阅[模型专属表格](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/providers.md#google-image-options)，其中包含 Flash-Lite 的保守 1K 策略。不支持的显式值在 HTTP 前拒绝；未知自定义模型保持 `Unknown` 和提供方通用选项验证。

### DeepSeek Flash

需要快速回答后深入审查，或解释图表、截图时，可使用 DeepSeek Flash。`AIModels.DeepSeek.Flash` (`deepseek-flash`) 选择2026年9月10日发布、原生支持视觉理解的 V4.1 Flash。沿用补全、流式、Run、函数调用和 RAG API，从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 起支持。

> 已发布的 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 已包含 Flash 基础支持。`AIModels.DeepSeek.V4Pro`、`UseResponsesApi`、Files API 和 `DeepSeekImageFileContent` 是源码中尚未发布的新增功能，需要从源码构建相互匹配的核心与抽象包；上述已发布包不包含这些功能。[未发布的更新说明](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased)。

纯文本任务可选择 `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813)。默认模型 Flash 支持图像，两者均提供 Low/High/Max 推理和相同输出上限。若要通过现有补全、流式、Run 和本地函数 API 使用 Responses，请在创建请求前设置 `UseResponsesApi = true`。默认仍为 `false`，以保留现有应用的 Chat Completions 行为；设置会固定到该请求及后续工具轮次。Responses 重发完整对话和原始推理历史，不依赖服务器保存的响应 ID。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

同一图像需要用于多个问题或对话时，可先上传一次。`UploadFileAsync` 接受路径，或调用方拥有的流和文件名；purpose 固定为 `user_data`。JPEG、PNG、GIF、WebP 上传上限为 64 MiB。`DeepSeekImageFileContent` 在两种传输方式的 Flash 中引用该图像，不是 PDF 或文档输入，V4 Pro 会拒绝。省略过期时间表示永久保留，`expiresAfterSeconds` 范围为 3600–2592000 秒。请在所有引用它的对话结束后再删除文件。

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync` 查询元数据；`ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` 获取一页；`DeleteFileAsync` 删除文件。`HasMore` 为 true 时将返回的 `LastId` 用作下一页 `After`，也可使用 `Descending`。官方文档未列出文件内容下载端点。Chat UI 提供 Flash 和 V4 Pro，重写模型选择器使用当前目录；旧保存值 `DeepSeekChat` 迁移为 Flash，任意模型 ID 保持不变。

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

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

通过 `WithFunction(...)` 注册本地函数，让模型通过应用代码查询数据或执行操作。推理与非推理均支持工具。Chat Completions 在推理时拒绝强制/必选工具，应使用自动选择。设置 `UseResponsesApi = true` 后，推理时也可通过 `ForceFunctionName` 指定函数；适配器将 `type` 和 `name` 直接放在 Responses 的 `tool_choice` 中。这不会启用原生异步工具。适配器保留原生 `reasoning_content` 和调用 ID，供后续工具轮次重放。Run 与原有流式 API 在启用 `StreamOptions.WithReasoning()` 后通过 `StreamingContentType.Reasoning` 输出推理；观察选项本身不会开启推理。用量包含提供方报告的缓存和推理令牌。 自动上下文恢复使用共用流式循环。工具需要此前的原生推理历史时，为保留历史会阻止自动压缩，并传递超限错误。

两种模型均提供 1M 上下文和最多 384K (`393216`) 输出令牌，默认请求预算仍为 8,000。推理模式省略 temperature/penalty，`top_p` 至少 0.95；非推理模式省略 `top_p`。Responses 的原生 JSON schema 使用现有类型化输出 API。不支持后台执行、服务器 `store`/`previous_response_id`、托管搜索、`CachePreservation.Required`、原生异步工具、`SteerAsync` 或图像生成。本地 RAG 和普通工具轮次仍可使用。

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

Perplexity 在应用配置后，使用同一最终请求计划进行验证和序列化。已抑制的父请求工具或推理设置不会导致辅助 Sonar 请求被拒绝；实际生效的选项仍会验证。预设和回退列表仍保留各自的模型选择。

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

`ApplyRequestProfile` 和 `ApplyProviderSpecificRequestProfile` 每个逻辑请求执行一次，再验证最终设置。应用发起的普通调用是独立请求，在上下文或工具回调中也如此。框架调用虚拟提供者适配器时，对相应基类入口的第一次调用会延续已准备的请求及其设置，即使替换了输入也不例外。在转发前通过同一个基类入口执行无关的辅助调用时，须使用 `BeginIndependentRequestScope()`；参阅[提供者适配器规则](../../../../../docs/zh-Hans/request-building.md#provider-request-adapters)。自定义提供者可在 `BeginRequestFeaturesScope` 之后调用新增的 protected 钩子 `ResolveRequestMessage(message)`，并保存返回的输入副本。现有提供者覆盖保持兼容。功能查询仍使用独立且无副作用的钩子。

如果自定义提供程序的配置会改变原生模式标志，请重写 `ApplyCapabilityRequestProfile(AIRequestProfile)`，并仅通过 `SetExecutionSetting(...)` 应用解析支持信息所需的标志。默认钩子不执行任何操作。构建器已捕获公共配置覆盖值；查询不会调用 `ApplyRequestProfile` 或 `ApplyProviderSpecificRequestProfile`。此钩子不得执行验证、回调、序列化、预算预留，或修改服务及调用方拥有的状态。临时设置会在查询结束后恢复，重写方法抛出异常时也一样。
