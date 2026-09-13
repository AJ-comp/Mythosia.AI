# 各提供商特性

> `CreateRequest`示例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和公共请求功能的旧7.1版本不包含构建器；旧包可继续使用原有服务重载。

<a id="image-options-migration"></a>
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

通过 `FunctionDefinition.AllowAsync = true` 或 `FunctionBuilder.WithAsync()`，可选择允许 GPT-6 Astra 在 Responses API 中异步调用工具。默认值为 `false`；不支持的模型仍等待同一个处理器的结果。示例和请求生命周期见[函数调用指南](function-calling.md)。

要跨提供商设置推理级别，并使用最新信息或已索引文档作为依据，请参阅[推理与搜索指南](reasoning-and-search.md)，其中列明了模型支持、缓存保留及组合限制。

### 推理强度

GPT-6 Astra 和 GPT-5.1–5.6 支持调整推理强度，以平衡响应速度和分析深度：

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

---

## xAI (XAIService)

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

Google使用`ImageSize.Auto`或模型支持的`ImageResolution.Auto`、`FiveTwelve`、`OneK`、`TwoK`、`FourK`的`Preset`。输出支持`ImageOutputFormat.Auto`或显式`Jpeg`，拒绝`Png`/`WebP`。Google和xAI拒绝`Pixels`，OpenAI支持`Auto`/`Pixels`并拒绝`Preset`。参见[迁移示例](#image-options-migration)。

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

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

`ImageContent` 接收 JPEG、PNG、GIF、WebP 字节或由提供方获取的公开 HTTP(S) URL。示例使用用户消息。当前 API 也接受工具消息中的图像，但注册函数处理器仍通过共用结果契约返回文本。手动构造 `ActorRole.Function` 图像消息时，须通过 `MessageMetadataKeys.FunctionId` 提供匹配的调用 ID（wire 中的 `tool_call_id`）。图像大小和总量限制请参阅最新官方视觉指南。未集成 `file_id`、Files API 或图像生成。

提供方标示上下文1M、输出最多384K (`393216`)令牌；库默认请求预算仍为8,000。推理模式省略 temperature/penalty，`top_p` 至少0.95；非推理模式省略 `top_p`。适配器使用 Chat Completions，未集成 Responses、托管搜索、`CachePreservation.Required`、原生异步工具或 `SteerAsync`。本地 RAG 和普通工具轮次仍可使用。

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
