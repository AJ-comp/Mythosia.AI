# 各提供商特性

> `CreateRequest`示例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和公共请求功能的旧7.1版本不包含构建器；旧包可继续使用原有服务重载。

<a id="image-options-migration"></a>
处理模式及返回的信息取决于供应商、模型和 API。使用[公共速度选项](request-building.md#inference-speed)并检查 capability，区分请求 Fast 与实际应用 Fast。

## 图片选项类型迁移

通过枚举自动补全选择质量和格式，并区分精确像素与分辨率档位。这能减少字符串拼写错误，避免指定的像素尺寸被悄悄转换成其他分辨率。

这是Mythosia.AI 8.0.0 的破坏性变更：`Quality`、`Background`、`OutputFormat`改为枚举，`Size`改为`ImageSize`，请求中单独的`AspectRatio`属性移除。`OutputFormat`默认值改为`ImageOutputFormat.Auto`。生成和编辑方法保持不变。

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)`请求精确尺寸。`Preset(resolution, aspectRatio)`指定分辨率档位和比例，由提供方决定实际像素尺寸。没有尺寸要求时使用`ImageSize.Auto`。只有应用接受近似尺寸时，才应将像素请求迁移为预设。

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

未定义的枚举值和提供方或模型不支持的组合会在HTTP请求前拒绝。枚举成员并不代表所有模型都支持。Google质量仅支持`ImageQuality.Auto`，xAI支持`Auto`、`Low`、`Medium`。

`EditImagesAsync`返回`Task`后即可复用输入缓冲区。已开始的请求会独立保留图像字节，包括OpenAI的遮罩字节；之后修改原始`ImageInput.Data`数组不会改变上传内容。

为避免将中断的输出保存为完整图像，Google生成和编辑要求所有返回的候选结果都以`finishReason: STOP`结束。只要有一个被阻止、未完成或缺少该结束状态，整个调用就会抛出`AIServiceException`。 内联base64数据或图像MIME信息缺失、无效时，整个调用也会失败，不会猜测为PNG。这些检查不验证实际文件字节是否与声明的图像格式一致。

## OpenAI (OpenAIService)

> GPT-6 Astra 支持和异步工具调用从 `Mythosia.AI` 7.1.0 开始提供，共享类型包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

天气查询较慢时，模型仍可先介绍不依赖天气结果的通用旅行用品。模型原生异步工具调用用于在这种等待期间继续独立工作；依赖查询结果的判断仍应等结果返回后再进行。

通过 `FunctionDefinition.AllowAsync = true` 或 `FunctionBuilder.WithAsync()`，可选择允许 GPT-6 Astra / Sol / Luna 在 Responses API 中异步调用工具。默认值为 `false`；不支持的模型仍等待同一个处理器的结果。示例和请求生命周期见[函数调用指南](function-calling.md)。

要跨提供商设置推理级别，并使用最新信息或已索引文档作为依据，请参阅[推理与搜索指南](reasoning-and-search.md)，其中列明了模型支持、缓存保留及组合限制。

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna（尚未发布）

复杂的编程、工具调用和代理任务可选择 GPT-6 Sol；需要低成本大量处理文本或图像输入时可选择 Luna。它们沿用现有的完整响应、流式响应和 Run API，切换模型不需要改变应用的调用流程。

> 这是尚未发布的新增功能，需要配套的 core 和 abstractions 构建。已发布的 Mythosia.AI 8.0.0 / Abstractions 4.0.0 不包含 `Gpt6Sol`、`Gpt6Luna` 或 `Gpt6Reasoning.None`。现有 Astra 功能的最低版本和服务默认模型不变。

使用 `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) 或 `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`) 选择模型。两者支持文本、图像输入及文本输出，上下文为 1,050,000 token，输入上限 922,000，输出上限 128,000。输入、推理与输出的总量仍须符合上下文限制。`MaxTokens` 设置请求的输出预算，而非上下文大小。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto` 对应 `Medium`。Sol/Luna 支持 `None`、`Low`、`Medium`、`High`、`XHigh`、`Max`，不支持 `Minimal`。请求级别可用 `WithReasoning(ReasoningLevel.None)`，`WithGpt6Parameters` 可用 `Gpt6Reasoning.None`。仅 Sol/Luna 的 `None` 会发送 `Temperature` / `TopP`，开启推理时省略它们。Astra 始终需要推理并省略采样参数。`AIRequestProfile.DisableReasoning` 在 Sol/Luna 中使用 `None`，在 Astra 中使用 Standard 模式的 `Low`，并省略推理摘要。

`Gpt6ReasoningMode.Standard` 和 `.Pro` 使用同一个所选模型 ID。三个 GPT-6 模型都支持 Responses 工具调用、可选异步工具、WebSocket Run 的追加指令，以及 Standard 单代理模式下保留缓存的推理变更。追加指令前请检查 `run.CanSteer`；接收成功不会撤回已有输出。`WithSpeed(InferenceSpeed.Fast)` 独立于推理强度请求付费 Fast 处理；通过 `result.Processing` 检查实际模式。账户权限和服务器降级处理不由本地能力检查保证。

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### 推理强度

GPT-6 Astra / Sol / Luna 和 GPT-5.1–5.6 支持调整推理强度，以平衡响应速度和分析深度：

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6：Sol 是旗舰模型；Terra 和 Luna 是更经济的选择。
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4 系列
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 系列
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra 默认使用 Responses API，函数调用也必须使用此 API。`Auto` 对应库默认值 `Medium`，不支持 `None` 和 `Minimal`。`AIRequestProfile.DisableReasoning = true` 会在 `Standard` 模式下将推理强度设为 `Low` 并省略推理摘要。选择 `Gpt6ReasoningMode.Pro` 即可通过同一个模型 ID `gpt-6-astra` 使用 Pro 模式。

### 文本转语音

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "你好，世界！",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### 语音转文本（转录）

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "zh"  // 可选，ISO-639-1
);
```

`TranscribeAudioAsync` 使用 `gpt-transcribe`，公开签名保持不变。

### 图像生成

#### GPT Image 2.5

需要快速制作视觉草稿时选择 Flare，需要精确执行细节编辑指令时选择 Sunburst。两者都通过现有的 `IImageGenerationService` 支持图像生成和编辑；选择图像模型不会改变聊天模型。

| 模型 | 适用场景 |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | 快速、高质量的日常图像生成。 |
| `AIModels.OpenAI.GptImage2_5Sunburst` | 重视编辑精度的图像生成与修改。 |

在 `ImageGenerationRequest.Model` 或继承它的 `ImageEditRequest.Model` 中明确指定。OpenAI 默认仍为 `AIModels.OpenAI.GptImage2`。别名为 `gpt-image-2.5-flare` 和 `gpt-image-2.5-sunburst`；要固定到 2026 年 9 月 8 日的快照，请使用 `GptImage2_5Flare_260908` 或 `GptImage2_5Sunburst_260908`，对应 ID 以 `-2026-09-08` 结尾。

先用 Flare 生成草稿：

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "日出时的玻璃亭，建筑概念图",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

再用 Sunburst 编辑生成的图像：

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "保持亭子的设计，移除周围景物，并将背景改为透明。",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

这两个模型及其快照的 `Quality` 支持 `Auto`、`Low`、`Medium`、`High`、`XHigh`、`Max`。草稿可用较低质量，最终素材可比较较高等级。`OutputFormat` 支持 `Auto` / `Png`、`Jpeg`、`WebP`；`OutputCompression` 仅适用于 JPEG/WebP，范围为 0–100。`Transparent` 背景需要 PNG/WebP，`Count` 为 1–10。

`Size`使用`ImageSize.Auto`或`ImageSize.Pixels(width, height)`：两边为16的倍数，比例1:3–3:1，单边最多3840像素，总面积655360–8294400像素。超过2560×1440的尺寸属于实验性功能。OpenAI拒绝`Preset`。

编辑接受 1–16 张非空 JPEG/PNG/WebP 参考图像，每张小于 50 MiB。可选蒙版须为小于 50 MiB 的 PNG/WebP，与第一张参考图像的格式和像素尺寸一致，并有 alpha 通道。库验证 MIME 类型和字节长度，供应商验证像素尺寸和 alpha。

这些示例使用现有 Image API 的字节返回与 multipart 编辑路径。本次集成不公开 Responses `image_generation` 工具、部分图像流式输出或 `input_fidelity`。请读取结果中的 `GeneratedImage.Data` 和 `MediaType`。

参阅官方[图像指南](https://developers.openai.com/api/docs/guides/image-generation)、[Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) 和 [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare) 模型页面。

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) 的进度更新、单轮指令和 thinking 绑定诊断从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 开始提供。Mythos 5.1 需要邀请访问，两个模型都拒绝强制工具选择。

<a id="claude-opus-55"></a>

### Claude Opus 5.5：显示长时间工具任务的进度

需要多轮工具调用的代码审查或文档调查可以使用 Opus 5.5。仍然使用现有 completion 和 Run API，但默认不显示进度，保留推理时也需要注意历史变更。此支持属于尚未发布的工作区新增功能，不包含在已发布的 8.0.0 / 4.0.0 包中。

`ClaudeOpus5_5` 选择 `claude-opus-5-5`，支持文本和图像输入、文本输出，提供 1M 上下文和最多 128K 输出 token。2026-09-24 核实的标准输入/输出价格为每百万 token $4/$20；特殊模式和工具另行计费。 [官方模型信息](https://platform.claude.com/docs/en/models/opus-5-5/overview).

未更改服务设置时，`Auto` 使用 `Medium` effort，并省略可读推理。Adaptive thinking 始终启用。可显式选择 `Low`、`Medium`、`High`、`XHigh` 或 `Max`；公共 `ReasoningLevel.None` 和 `Minimal` 会被拒绝。服务默认模型保持不变。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

示例请求 `Updates` 并观察 `StreamingContentType.Reasoning`。`Summarized` 显示推理摘要，`Omitted` 隐藏显示。`WithAdaptiveThinkingParameters(effort)` 的显示参数仍默认为 `Summarized`，与未设置的服务不同。普通 completion 完成后读取 `LastThinkingContent`。不保证按固定间隔产生进度。

旧的正数 `ThinkingBudget` 映射为 high/xhigh/max effort，不是精确 token 预算；零或负数也无法关闭推理。禁用推理的 profile 会使用 low effort 并省略可读推理。`MaxTokens` 包括隐藏推理和回答，因此迁移时应重新评估输出限制和成本。

Mythosia 在会话轮次和工具结果之间保留签名 thinking 块，包括内容为空的块。请继续使用同一个服务和会话；若要保留推理，不要重写此前消息、system 或 tools。可使用 `WithTurnInstruction`、`WithConversationInstruction` 和 `CachePreservation.Required`。通过 `WithThinkingBinding` 选择 `Error` / `DropBlock`，并用 `LastInputTransformations` 查看报告的丢弃；Drop 表示推理被丢弃。[历史指南](fable-5-1.md)说明共享控制，默认值及模型兼容性则遵循 Opus 5.5 的规则。

不要设置 `ForceFunctionName`；支持普通工具选择和 `FunctionsDisabled`。拒绝 assistant prefill，并省略 sampling 参数。Opus 5.5 无法读取 Fable/Mythos 的 thinking，但 Claude API 上的 Fable 5.1 和 Mythos 5.1 可读取 Opus 5.5 的 thinking。切换模型可能丢失此前推理。此新增功能未开放 原生 computer toolset、task budget、对话中工具变更、服务器压缩或自动服务器 fallback。 [迁移要求](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [原生功能范围](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

在 Opus 5.5 中直接修改已保存的 assistant 响应内容，会在 HTTP 请求前引发 `InvalidOperationException`；`DropBlock` 也不允许重写签名响应。请通过新的用户输入提交更正，或开始新会话。对之前 user/system 内容的修改则遵循供应商的前缀绑定策略。

Opus 5.5 fast mode 可通过 [WithSpeed](request-building.md#inference-speed) 在有权限的直接 Claude API 上使用。它保持所选推理级别并请求额外收费的模式。

### Token 计数（原生 API）

`GetInputTokenCountAsync` 在所有提供商上均可使用（参见[文本生成](completions.md#token-计数)）。Anthropic 的实现调用官方 `messages/count_tokens` 端点，返回**精确**的 Token 数量而非本地估算：

```csharp
uint tokens = await service.GetInputTokenCountAsync("你的提示词");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

审查长文档或执行多轮工具任务时，可以通过现有 Google 适配器选择 Gemini 3.7 Flash 或 3.8 Flash。支持从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 开始，服务默认模型仍为 Gemini 3.6 Flash。

### 思考深度

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("比较滚动部署和蓝绿部署，包括回滚风险。")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

初步检查可使用 `Low`，复杂审查可使用 `High`；更多推理可能增加延迟和 token 用量。两种模型都支持 `Low`、`Medium`、`High`，不支持 `Minimal` 或 `None`。`GeminiThinkingLevel.Auto` 不发送覆盖值，3.8 的提供商默认值为 `Medium`。`ThinkingLevel` 设置服务基线，`WithReasoning(...)` 只覆盖一个逻辑请求。适配器不发送这两种模型的 `temperature`、`topP`、`topK`。提供商上限为输入 1,048,576 token、输出 65,536 token。 [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### Google 图像模型的分辨率和宽高比

| 模型 | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

10种标准比例为 `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9`。14种比例集合另加 `1:4`, `4:1`, `1:8`, `8:1`。所有模型也允许 `ImageAspectRatio.Auto`。

使用 `ImageSize.Auto` 或 `ImageSize.Preset(resolution, aspectRatio)`。`Auto` 省略对应选择器。`GetImageCapabilities(model)` 与 `GenerateImagesAsync` / `EditImagesAsync` 使用相同的模型专属选项。不支持的显式值会在 HTTP 前抛出 `NotSupportedException`，不会调整尺寸或发送替代请求。未知的自定义模型 ID 保持 `Unknown`，通过提供方通用选项验证后原样传递。

Flash-Lite 的[模型页面](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image)和指南正文指定 1K，但[指南表格](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size)也有 512 列。在验证这一差异前，库保守地仅允许 1K；这不表示已实测服务器会拒绝 512。

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

需要先快速起草、再仔细审查代码或文档时，可以选择 Grok 4.7，并按请求调整推理强度。继续使用现有的普通响应、流式响应、Run、本地工具、结构化输出和图像输入 API。`grok-4.7` 接受文本和图像输入，返回文本，上下文窗口为 500,000 token。此集成需要匹配的未发布 core 与 abstractions 构建，已发布的 8.0.0 / 4.0.0 软件包不包含它。服务默认模型仍为 Grok 4.5。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

支持 `Low`、`Medium`、`High` 和 `XHigh`。原生 `GrokReasoning.Auto` 省略 `reasoning_effort`，采用提供方默认的 `High`；通用 `ReasoningLevel.Auto` 也会省略该字段，在本次请求中使用提供方默认值 `High`。`None`、`Minimal` 和 `Max` 会在发送前被拒绝。`WithReasoning(...)` 对整个逻辑请求生效，包括工具轮次和结构化输出修复；`WithGrokReasoning(...)` 设置服务基础值。内部 `DisableReasoning` 配置使用 `Low`。可选的推理摘要并非完整的内部推理过程。

`WithSpeed(InferenceSpeed.Standard)` 发送 `service_tier: "default"`；`Fast` 在受支持的 xAI 端点发送 `"priority"`，可能增加费用。`ProviderDefault` 不覆盖设置。服务器可能降级为普通处理，请通过 `result.Processing` 查看报告的实际等级。这是 `grok-4.7` 的优先处理，并非 Cursor/Grok Build 专用的独立“Grok 4.7 Fast”变体；该变体没有公开 API 模型 ID。

`GetCapabilities()` 在本地描述所选请求的支持情况，不检查账户权限。此集成使用 Chat Completions。本次未连接 Responses 专用的加密推理、托管 Web/X 搜索、原生异步工具、保留缓存的更新以及 `SteerAsync`。客户端函数继续使用现有的本地工具循环；`run.CanSteer` 为 false。

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### 为任务选择推理强度

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

启用 `new StreamOptions().WithReasoning()` 观察选项后，Grok 4.6 可能通过 `StreamingContentType.Reasoning` 返回提供商生成的推理摘要。摘要是可选输出，不代表完整内部推理。Run 使用同样的观察选项；更改流显示设置不会改变请求的推理强度。

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

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

编辑时，按提示词引用的顺序传入现有图像的字节：

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "将图1中的主体放入图2的场景。",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

每个 `GeneratedImage.Data` 包含解码后的图像字节；请根据 `MediaType` 选择文件扩展名。适配器请求内联 base64 输出，不下载提供方托管的图像 URL。`Count` 支持1–10张输出；编辑支持1–5张 JPEG、PNG 或 WebP 参考图。

xAI使用`ImageSize.Auto`或`ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`，分辨率支持`Auto`、`OneK`、`TwoK`，比例须为模型支持的值。由于不能请求精确尺寸，`Pixels(...)`会被拒绝。

xAI仅支持新的共用默认值`ImageOutputFormat.Auto`。它无法选择输出编码，因此显式`Jpeg`、`Png`、`WebP`会在发送前拒绝。请按`GeneratedImage.MediaType`选择扩展名，库不会转码。质量支持`ImageQuality.Auto`、`Low`、`Medium`，背景仅支持`ImageBackground.Auto`；不支持显式压缩或单独的`Mask`。

Google 使用 `ImageSize.Auto` 或模型专属分辨率和比例的 `Preset`。[Google 模型专属图像选项](#google-image-options).输出支持`ImageOutputFormat.Auto`或显式`Jpeg`，拒绝`Png`/`WebP`。Google和xAI拒绝`Pixels`，OpenAI支持`Auto`/`Pixels`并拒绝`Preset`。参见[迁移示例](#image-options-migration)。

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

需要快速回答后深入审查，或解释图表、截图时，可使用 DeepSeek Flash。`AIModels.DeepSeek.Flash` (`deepseek-flash`) 选择2026年9月10日发布、原生支持视觉理解的 V4.1 Flash。沿用补全、流式、Run、函数调用和 RAG API，从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 起支持。

> 已发布的 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 已包含 Flash 基础支持。`AIModels.DeepSeek.V4Pro`、`UseResponsesApi`、Files API 和 `DeepSeekImageFileContent` 是源码中尚未发布的新增功能，需要从源码构建相互匹配的核心与抽象包；上述已发布包不包含这些功能。[未发布的更新说明](../../src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased)。

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

图表或截图可通过现有消息类型传入图像字节：

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("解释这张图表的趋势，并指出不清楚的标签。"),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` 接收 JPEG、PNG、GIF、WebP 字节或由提供方获取的公开 HTTP(S) URL。示例使用用户消息。当前 API 也接受工具消息中的图像，但注册函数处理器仍通过共用结果契约返回文本。手动构造 `ActorRole.Function` 图像消息时，须通过 `MessageMetadataKeys.FunctionId` 提供匹配的调用 ID（wire 中的 `tool_call_id`）。图像大小和总量限制请参阅最新官方视觉指南。 仍不支持图像生成。

两种模型均提供 1M 上下文和最多 384K (`393216`) 输出令牌，默认请求预算仍为 8,000。推理模式省略 temperature/penalty，`top_p` 至少 0.95；非推理模式省略 `top_p`。Responses 的原生 JSON schema 使用现有类型化输出 API。不支持后台执行、服务器 `store`/`previous_response_id`、托管搜索、`CachePreservation.Required`、原生异步工具、`SteerAsync` 或图像生成。本地 RAG 和普通工具轮次仍可使用。

`V4Flash`、`Chat`、`Reasoner` 保留原始 wire ID，标记为仅警告的 obsolete 常量。提供方临时将已退役的 `deepseek-v4-flash` 路由到 V4.1 Flash；库不会改写常量。新代码请显式选择 `Flash`。`UseReasonerModel()` 选择 Flash 并启用 `High` 推理。

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

当回答需要基于最新信息，并让读者能够核对来源时，可以使用 Perplexity。`PerplexityService` 调用 Agent API；独立搜索和嵌入则用于为自行选择的回答模型构建检索能力。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("比较最新的电池回收方法，并注明来源。");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

研究预设、本地函数与托管工具连接，以及长时间后台任务的管理，请参阅 [Perplexity 指南](perplexity.md)。可以继续使用现有的 completion、流式输出、Run 和引用 API。

此版本将服务迁移至 `/v1/agent`。`AIModels.Perplexity.Sonar` 现在选择 `perplexity/sonar`。提供方已宣布旧 Sonar 端点将于 2026 年 9 月 27 日停用，因此现有 Sonar 集成需要迁移。 [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## 阿里巴巴 / 通义千问 (QwenService)

安装独立包：

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

可用模型：`QwenMax`、`QwenPlus`、`QwenTurbo`、`Qwen3` 及其变体。

创建服务时，使用 `EndpointPlatform` 选择兼容端点：

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[用共享支持定义构建模型功能选项](model-capabilities.md).
