# Provider-Specific Features

> `CreateRequest` examples require Mythosia.AI 8.0.0 / Abstractions 4.0.0; they are not available in the earlier 7.1 release that introduced Run and common request features. Earlier packages can keep their existing service overloads.

<a id="image-options-migration"></a>
## Typed image options: migration

Choose quality and file formats with completion-friendly enums, and distinguish exact pixels from a resolution class. This makes mistakes visible in code and prevents a requested pixel size from silently becoming a different resolution.

This is a breaking change in Mythosia.AI 8.0.0: `Quality`, `Background`, and `OutputFormat` are enums; `Size` is `ImageSize`; the separate request `AspectRatio` property is removed. `OutputFormat` now defaults to `ImageOutputFormat.Auto`. The generation and editing methods remain the same.

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

`Pixels(width, height)` requests exact dimensions. `Preset(resolution, aspectRatio)` requests a resolution class and framing; the provider determines the output pixels. Use `ImageSize.Auto` when no size constraint is needed. Do not convert a pixel request to a preset unless approximate dimensions are acceptable to your application.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

Undefined enum values and unsupported provider/model combinations fail before HTTP. An enum member being available does not mean every model supports it. Google supports only `ImageQuality.Auto`; xAI supports `Auto`, `Low`, and `Medium`.

You can reuse input buffers after `EditImagesAsync` returns its `Task`: the started request keeps its own image data, including OpenAI mask bytes. Later changes to the original `ImageInput.Data` arrays do not alter the upload.

To avoid saving interrupted output as a finished image, Google generation and editing require every returned candidate to end with `finishReason: STOP`. If any candidate is blocked, incomplete, or missing that terminal status, the entire call throws `AIServiceException`. Missing or malformed inline base64 data or image MIME metadata also fails the entire call; no PNG type is assumed. These checks do not verify that the file bytes match the declared image format.

## OpenAI (OpenAIService)

> GPT-6 Astra support and async tool calling are available from `Mythosia.AI` 7.1.0, with shared types in `Mythosia.AI.Abstractions` 3.1.0.

To switch effort between drafting and review, or answer from web/document sources, use the [common reasoning and search API](reasoning-and-search.md). It covers OpenAI, Anthropic and Google where supported, including cache-preserving changes and retained citations. The settings below remain available for provider-specific control.

Astra's async tool calling is useful when the model can explain or prepare something independently while an external lookup runs, such as giving general packing advice while checking the weather.

`FunctionDefinition.AllowAsync = true` or `FunctionBuilder.WithAsync()` enables opt-in async function calls on GPT-6 Astra through Responses. The default is `false`; unsupported models keep the same handler and wait for its result. See [async tool calling](function-calling.md#async-tool-calling) for examples and request-lifetime details.

### Reasoning Effort

GPT-6 Astra and GPT-5.1–5.6 models support reasoning effort control. Set the level to trade off speed and depth:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: the alias routes to Sol; Terra lowers cost; Luna targets efficient volume.
service.ChangeModel(AIModels.OpenAI.Gpt5_6);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4 series
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 series
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra uses the Responses API by default; function calling requires it. `Auto` resolves to the library default, `Medium`, and `None`/`Minimal` are unavailable. `AIRequestProfile.DisableReasoning = true` uses `Low` effort in `Standard` mode and omits reasoning summaries. Select `Gpt6ReasoningMode.Pro` for Pro execution with the same `gpt-6-astra` model ID.

### Text-to-Speech

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Hello, world!",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Speech-to-Text (Transcription)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "en"  // optional, ISO-639-1
);
```

`TranscribeAudioAsync` uses `gpt-transcribe`; its public signature is unchanged.

### Image Generation

#### GPT Image 2.5

Use Flare to create visual drafts quickly, or Sunburst when a revision must follow detailed editing instructions. Both models generate and edit images through the existing `IImageGenerationService`; choosing an image model does not change the chat model.

| Model | When to choose it |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | Fast, high-quality everyday image generation. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | Image generation and editing where editing precision matters most. |

Set `ImageGenerationRequest.Model` explicitly, or its inherited property on `ImageEditRequest`. OpenAI's default remains `AIModels.OpenAI.GptImage2`. The aliases are `gpt-image-2.5-flare` and `gpt-image-2.5-sunburst`; to pin the September 8, 2026 snapshots, use `GptImage2_5Flare_260908` or `GptImage2_5Sunburst_260908` (the corresponding IDs end in `-2026-09-08`).

Create a draft with Flare:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "A glass pavilion at sunrise, architectural concept art",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

Then use Sunburst to revise the generated image:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Keep the pavilion design, remove the surroundings, and use a transparent background.",
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

`Quality` accepts `Auto`, `Low`, `Medium`, `High`, `XHigh`, and `Max` on these two models and their snapshots. Use lower quality for drafts and compare higher levels for final assets. `OutputFormat` accepts `Auto` / `Png`, `Jpeg`, or `WebP`; `OutputCompression` is 0–100 for JPEG/WebP only. A `Transparent` background requires PNG/WebP. `Count` is 1–10.

`Size` uses `ImageSize.Auto` or `ImageSize.Pixels(width, height)`: dimensions must be multiples of 16, ratio 1:3–3:1, each edge at most 3840, and area 655360–8294400 pixels. Sizes above 2560×1440 are experimental. OpenAI rejects `Preset`.

Editing accepts 1–16 nonempty JPEG/PNG/WebP reference images, each smaller than 50 MiB. An optional mask must be PNG/WebP, smaller than 50 MiB, and match the first reference's format and dimensions with an alpha channel. The library checks MIME type and byte length; the provider checks pixel dimensions and alpha.

These examples use the Image API's existing byte-based generation and multipart editing paths. Responses `image_generation` tools, partial-image streaming, and `input_fidelity` are not exposed by this integration. Read `GeneratedImage.Data` and `MediaType` from the result.

See the official [image guide](https://developers.openai.com/api/docs/guides/image-generation), [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst), and [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare) model pages.

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) adds progress updates, turn-scoped instructions, and thinking-binding diagnostics from `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 requires invitation access. Both reject forced tool choice.

### Token Counting (Native API)

`GetInputTokenCountAsync` is available on all providers (see [Basic Completions](completions.md#token-counting)). Anthropic's implementation calls the official `messages/count_tokens` endpoint, returning **exact** token counts rather than local estimation:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Your prompt here");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

For long document reviews and tasks with repeated tool calls, select Gemini 3.7 Flash or 3.8 Flash through the existing Google adapter. Support starts with `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0; the service default remains Gemini 3.6 Flash.

### Thinking Level

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
    .CreateRequest("Compare rolling and blue-green deployments, including rollback risks.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Use `Low` for a lighter first pass and `High` for a more demanding review; more reasoning can increase latency and token use. Both models accept `Low`, `Medium`, and `High`, but not `Minimal` or `None`. `GeminiThinkingLevel.Auto` omits the override; 3.8's provider default is `Medium`. `ThinkingLevel` sets the service baseline, while `WithReasoning(...)` overrides one logical request. The adapter omits `temperature`, `topP`, and `topK` for both models. Their provider limits are 1,048,576 input and 65,536 output tokens. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

Earlier Gemini models retain their settings. Gemini 3 thinking is always on, and Pro models reject `Minimal`. Gemini 2.5 uses `ThinkingBudget` (`-1` dynamic, `0` off on Flash/Lite, and at least `128` on Pro). Gemini 3.6 Flash and 3.5 Flash-Lite also omit legacy sampling fields; all Gemini 3 models omit `candidateCount`.

### Safety Thresholds

Safety settings are omitted by default so Google can apply its current defaults. Configure only the categories your application owns:

```csharp
gemini.HarassmentSafetyThreshold = GeminiSafetyThreshold.BlockMediumAndAbove;
gemini.HateSpeechSafetyThreshold = GeminiSafetyThreshold.BlockOnlyHigh;
gemini.SexuallyExplicitSafetyThreshold = GeminiSafetyThreshold.Off;
gemini.DangerousContentSafetyThreshold = GeminiSafetyThreshold.ProviderDefault;
```

### Image Generation and Editing

`GoogleAIService` also implements `IImageGenerationService`. Its independent default image model is `AIModels.Google.Images.Gemini3_1FlashImage`; the Flash-Lite and Pro image IDs are available as explicit request overrides.

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Google;

IImageGenerationService images = new GoogleAIService(apiKey, httpClient);

var result = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A clean product photo on a neutral background",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Jpeg
});
```

Gemini supports reference-image editing through `EditImagesAsync`, but it does not expose a separate mask parameter or a guaranteed multi-image count parameter.
Google accepts `ImageSize.Auto` or `Preset` with `ImageResolution.Auto`, `FiveTwelve`, `OneK`, `TwoK`, or `FourK`, subject to model support. It accepts `ImageOutputFormat.Auto` or explicit `Jpeg`, and rejects `Png`/`WebP`. Google and xAI reject `Pixels`; OpenAI accepts `Auto`/`Pixels` and rejects `Preset`. See [migration examples](#image-options-migration).
Google accepts `ImageOutputFormat.Auto` for provider-selected output or `ImageOutputFormat.Jpeg` for its explicit JPEG selector. Explicit `Png` and `WebP` are rejected before sending because those selectors are unavailable. Always use `GeneratedImage.MediaType` as the format of returned bytes.

---

## xAI (XAIService)

### Choose effort for the task

Use a lower effort for a quick first pass, then spend more reasoning on difficult checks where answer quality matters more than response time. Select Grok 4.6 explicitly to use its additional `XHigh` level.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("Compare rolling and blue-green deployment, including failure recovery.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 accepts `Low`, `Medium`, `High`, and `XHigh` (`GrokReasoning.XHigh`). `Auto` omits `reasoning_effort`, leaving the provider's `High` default; `None` cannot disable this model's reasoning. More effort can increase latency and token use. `XAIService` still defaults to Grok 4.5 for compatibility; 4.5 accepts `Low` through `High`, and 4.3 accepts `None` through `High`. The adapter rejects `XHigh` on those earlier models before sending.

`WithGrokReasoning(...)` and the existing `WithGrokParameters(...)` set the service baseline. On Grok 4.6, common `WithReasoning(...)` overrides one logical request, including its tool rounds and structured-output repairs, then restores that baseline. Internal `DisableReasoning` profiles use `Low` for this always-reasoning model. Cache-preserving updates and hosted web/file search are not integrated for xAI through these common options.

Grok 4.6 can return provider-generated reasoning summaries as `StreamingContentType.Reasoning` when observation enables `new StreamOptions().WithReasoning()`. Summaries are optional and are not the full internal reasoning. The same observation options work with Run; changing stream observation does not change the requested effort.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

Use image generation to turn a product description into a visual draft, or image editing to combine a subject and a background from reference photos. `XAIService` supports both through the same `IImageGenerationService` used by OpenAI and Google, starting with `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

The independent default image model is `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). Image requests do not change the selected chat model or append to its conversation.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "A glass pavilion at sunrise, wide composition",
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

For an edit, pass existing image bytes in the order referenced by the prompt:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Place the subject from image 1 in the scene from image 2.",
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

Each `GeneratedImage.Data` contains decoded image bytes; use `MediaType` when choosing the file extension. The adapter requests inline base64 output and does not download provider-hosted image URLs. `Count` accepts 1–10 outputs; editing accepts 1–5 JPEG, PNG, or WebP reference images.

For xAI, use `ImageSize.Auto` or `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`; supported resolution classes are `Auto`, `OneK`, and `TwoK`. Ratios must be supported by the selected model. `Pixels(...)` is rejected because exact dimensions cannot be requested.

xAI supports only `ImageOutputFormat.Auto`, the new shared default. It has no output-codec selector and rejects explicit `Jpeg`, `Png`, and `WebP` before sending. Read `GeneratedImage.MediaType` and use the matching extension; the library does not transcode. `Quality` accepts `ImageQuality.Auto`, `Low`, or `Medium`; `Background` must be `ImageBackground.Auto`. Explicit compression and a separate `Mask` are unsupported.

Google accepts `ImageSize.Auto` or `Preset` with `ImageResolution.Auto`, `FiveTwelve`, `OneK`, `TwoK`, or `FourK`, subject to model support. It accepts `ImageOutputFormat.Auto` or explicit `Jpeg`, and rejects `Png`/`WebP`. Google and xAI reject `Pixels`; OpenAI accepts `Auto`/`Pixels` and rejects `Preset`. See [migration examples](#image-options-migration).

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

Use DeepSeek Flash when a task needs a quick answer, a more careful review, or an explanation of a chart or screenshot. `AIModels.DeepSeek.Flash` (`deepseek-flash`) selects V4.1 Flash, released on September 10, 2026, with native visual understanding. The existing completion, streaming, Run, function-calling, and RAG APIs remain the entry points; support starts with `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("Compare rolling and blue-green deployment, including rollback risks.");

await using var run = await deepseek
    .CreateRequest("Review the assumptions in that comparison.")
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

`ThinkingEnabled` remains `false` by default in the library. `WithDeepSeekReasoning(...)` enables thinking and sets the persistent service `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`); native `Auto` omits effort and uses the provider's `High` default. Common `WithReasoning(...)` overrides one logical request and its tool rounds: `None` disables thinking, `Minimal`/`Low` maps to `Low`, `Medium`/`High`/`XHigh` to `High`, and `Max` to `Max`. Common `Auto` keeps the configured baseline. More effort can increase response time and token usage. Setting `ReasoningEffort` alone does not enable thinking.

Register local functions with `WithFunction(...)` to let the model fetch data or act through your code. Tool calls work with or without thinking; with thinking enabled, use automatic tool choice because forced/required selection is rejected. The adapter retains native `reasoning_content` and call IDs for later tool rounds. Run and legacy streaming expose provider reasoning as `StreamingContentType.Reasoning` when `StreamOptions.WithReasoning()` is enabled; this observation option does not itself enable thinking. Token usage includes cache and reasoning counts when reported. Automatic context recovery uses the shared streaming loop. When tools require earlier native reasoning history, automatic compaction is blocked to preserve that history; the overflow error remains visible.

For a chart or screenshot, send image bytes with the existing message types:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart and identify any unclear labels."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` accepts JPEG, PNG, GIF, or WebP bytes, or a public HTTP(S) URL retrieved by the provider. The example uses a user message. The current API also accepts image content in tool messages, but registered function handlers still return text through the shared result contract. Manually supplied `ActorRole.Function` image messages must include the matching call ID through `MessageMetadataKeys.FunctionId` (`tool_call_id` on the wire). See the current official vision guide for image size and aggregate limits. `file_id`, the Files API, and image generation are not integrated.

The provider advertises 1M context and up to 384K (`393216`) output tokens; the library keeps its 8,000-token request default. In thinking mode, unsupported temperature/penalty fields are omitted and `top_p` is at least 0.95; in non-thinking mode, the adapter omits `top_p`. This adapter uses Chat Completions, not the provider's Responses transport. Hosted web/file search, `CachePreservation.Required`, native asynchronous tools, and `SteerAsync` are unsupported; local RAG and ordinary tool rounds remain available.

`V4Flash`, `Chat`, and `Reasoner` remain warning-only obsolete constants with their original wire IDs. The provider temporarily routes the retired `deepseek-v4-flash` alias to V4.1 Flash; the library does not rewrite that constant. Prefer `Flash` explicitly. `UseReasonerModel()` selects Flash with thinking enabled at `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

Use Perplexity when an answer must reflect recent information and readers need sources they can check. `PerplexityService` calls the Agent API, while independent search and embeddings let you build retrieval around your own answer model.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("Compare the latest approaches to battery recycling and cite the sources.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Choose a research preset, connect local functions or hosted tools, and manage longer background tasks in the [Perplexity guide](perplexity.md). The same completion, streaming, Run, and citation APIs apply.

This release moves the service to `/v1/agent`. `AIModels.Perplexity.Sonar` now selects `perplexity/sonar`. The provider announced retirement of the old Sonar endpoints for September 27, 2026; existing Sonar integrations must migrate. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

Install the separate package:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
    .UseMaxModel();
```

Available models include `QwenMax`, `QwenPlus`, `QwenTurbo`, and the size-specific Qwen 3 and Qwen 3.5 constants in `AlibabaModels`.

Choose a compatible endpoint when constructing the service:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[Choose model controls using shared capability definitions](model-capabilities.md).
