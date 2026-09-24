# Mythosia.AI

Build applications that can switch AI providers while keeping the same completion, streaming and tool workflows. `Mythosia.AI` connects OpenAI, Anthropic, Google, xAI, DeepSeek and Perplexity, with multimodal input, reasoning, hosted search, citations and optional image generation/editing.

Choose premium low-latency processing only for requests that need it, while keeping the same model and reasoning effort. The `WithSpeed(InferenceSpeed.ProviderDefault/Standard/Fast)` API works on immutable request builders and next-request service extensions. Inspect model capabilities, then read `AIRunResult.Processing` or `LastProcessing` to distinguish requested from reported processing; unknown reporting remains unknown. Fast can cost more and is limited by provider/model/API and account access. See [speed selection and examples](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md#inference-speed).

## Current release: 8.1.0

This minor release requires **Mythosia.AI.Abstractions 4.1.0**. It adds GPT-6 Sol/Luna, Claude Opus 5.5, Grok 4.7, DeepSeek V4 Pro and Responses/Files APIs, plus common processing-speed controls. Existing public APIs and service defaults remain. See the [v8.1.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v810).

The v8 API contracts below remain available. When upgrading from 7.x or earlier, follow the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) and rebuild dependent applications and custom providers.

| When you need to... | Use... |
| --- | --- |
| Choose valid image options without remembering string spellings | Typed quality/background/format and `ImageSize.Pixels(...)` / `.Preset(...)`; [image migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#image-options-migration) |
| Prepare independent settings for different requests | Immutable `CreateRequest(...)` builders; [request settings](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md) |
| Return business objects from asynchronous tools and stop cooperative work | `Task<T>` / `ValueTask<T>` results and injected cancellation tokens; [tool contract](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/function-calling.md#tool-execution-contract) |
| Stop waiting when a screen closes or a task is cancelled | `GetCompletionAsync(..., cancellationToken: token)`; [cancellation and implementation migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation) |
| Keep the final answer together with usage and sources | `AIRunResult` from `await run.Result`; [Run result migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result) |
| Show only appropriate model controls before making a request | Local `GetCapabilities()` / `GetImageCapabilities(...)` snapshots; [capability inspection](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/model-capabilities.md) |

Run string callers now read `(await run.Result).Text`. `GetCompletionAsync` and typed `StructuredStreamRun<T>.Result` keep their return types. Cancellation stops cooperative client work; cleanup may wait for tools that ignore cancellation and does not guarantee that the provider stops inference or billing. Request builders isolate settings while conversation history remains shared at execution time.

Provider additions include [Claude Fable/Mythos 5.1](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/fable-5-1.md), Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash, Grok Imagine Image 2.0 and GPT Image 2.5 Sunburst/Flare. [Perplexity](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/perplexity.md) moves to the Agent API with research presets, hosted tools, background tasks and independent Search; removed Sonar-specific APIs require migration. GPT-6 Astra, supported steering and opt-in native asynchronous tools remain available from v7.1.

Reliability fixes preserve request snapshots and reported token totals, complete cleanup on failure, validate stream termination and image payloads, and reject invalid structured-output retry budgets. See the [v8.0.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v800) for exact changes and compatibility limits. For older upgrades, apply the [v7 migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v7-migration.md) as well.

## Grok 4.7

For fast drafts and careful code/document reviews, select `AIModels.xAI.Grok4_7` and choose `Low`, `Medium`, `High` or `XHigh` through the existing request builder. Completion, streaming, Run, local tools, structured output, image input and model capabilities share the same model-specific validation. Native Auto uses the provider’s High default; reasoning cannot be disabled. `WithSpeed(InferenceSpeed.Fast)` selects paid priority processing, not the separate Cursor/Grok Build-only Grok 4.7 Fast variant. Read the reported tier in `AIRunResult.Processing`. This addition requires Mythosia.AI 8.1.0 and Abstractions 4.1.0; the service default remains Grok 4.5. See [usage, limits and transport scope](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#grok-47).

## Claude Opus 5.5

For long coding and document tasks, select `AIModels.Anthropic.ClaudeOpus5_5` through the existing completion, streaming and Run APIs. Untouched settings use medium adaptive effort and omit readable thinking; explicitly choose summarized reasoning or progress updates for your interface. Signed thinking, including empty blocks, is retained across ordinary turns and tool rounds. Forced tools, assistant prefills and reasoning-off requests are unsupported; native server features are not all exposed. This addition is available with Mythosia.AI 8.1.0 and Abstractions 4.1.0. See [Opus 5.5 configuration and migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#claude-opus-55).

## Supported Providers

- **OpenAI** — GPT-6 Astra / Sol / Luna, GPT-5.6 alias / Sol / Terra / Luna, GPT-5.5 / 5.5 Pro, GPT-5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro, GPT-5.3 Codex, GPT-5.2 / 5.2 Pro, GPT-5.1, GPT-4.1 / 4.1 Mini, GPT-4o / 4o Mini
- **Anthropic** — Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus 5.5 / 5 / 4.8 / 4.7 / 4.6 / 4.5, Sonnet 5 / 4.6 / 4.5, Haiku 4.5
- **Google** — Gemini 3.8 Flash, Gemini 3.7 Flash, Gemini 3.6 Flash, Gemini 3.5 Flash/Flash-Lite, Gemini 3.1 Pro Preview/Flash-Lite, Gemini 3 Flash Preview, Gemini 2.5 Pro/Flash/Flash-Lite, Gemini 3.1 Flash Image, Gemini 3.1 Flash-Lite Image, and Gemini 3 Pro Image
- **DeepSeek** — Flash (V4.1 Flash) with image input and text-only V4 Pro; local functions, optional thinking, opt-in Responses, and reusable image uploads
- **xAI** — Grok 4.7, Grok 4.6, Grok 4.5 (default), Grok 4.3, Grok 4.20 (reasoning / non-reasoning), Grok Build
- **Perplexity** — Agent API research presets and `perplexity/sonar`, hosted search, local tools, and source citations

## 📚 Documentation

- **[Getting Started](https://aj-comp.github.io/Mythosia.AI/docs/getting-started.html)** — Installation, provider setup, and first completion
- **[Function Calling](https://aj-comp.github.io/Mythosia.AI/docs/function-calling.html)** — Registration, execution policy, and streaming events
- **[Reasoning and Search](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/reasoning-and-search.md)** — Move from quick drafts to deeper review, search hosted sources, and retain citations through common Fluent options
- **[Claude Fable 5.1](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/fable-5-1.md)** — Observe progress, append turn instructions, and diagnose preserved-thinking changes
- **[Perplexity](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/perplexity.md)** — Produce grounded answers, manage research tasks, or use independent search and embeddings
- **[v8.1.0 Release Notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v810)** — Current changes, compatibility details, and full version history
- **[Relationship to Microsoft.Extensions.AI](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Abstractions#relationship-to-microsoftextensionsai)** — How IAIService and IChatClient differ

> Claude Fable 5 and Claude Mythos 5 require 30-day data retention and cannot use zero-data-retention arrangements. Adaptive thinking is always on; a reasoning-off request is represented by low effort with readable reasoning omitted. Mythos 5 is limited to approved Project Glasswing customers.

> Fable 5.1 and Mythos 5.1 also require the provider's 30-day retention arrangement; ZDR needs explicit Anthropic authorization. They reject forced tool choice. Fable 5.1 validates preserved thinking against the earlier conversation, while Mythos 5.1 does not enforce that prefix check. See the [5.1 migration guidance](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## Installation

```bash
dotnet add package Mythosia.AI
```

For advanced LINQ operations with streams:

```bash
dotnet add package System.Linq.Async
```

For RAG (Retrieval-Augmented Generation) support:

```bash
dotnet add package Mythosia.AI.Rag
```

This adds `.WithRag()` to any `AIService`, enabling document-based context augmentation. See the [Mythosia.AI.Rag README](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/rag/Mythosia.AI.Rag) for full usage details.

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

## Quick Start

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;

// OpenAI GPT
var gptService = new OpenAIService(apiKey, httpClient);
var openAiResponse = await gptService.GetCompletionAsync("Hello!");

// Anthropic Claude
var claudeService = new AnthropicService(apiKey, httpClient);
var claudeResponse = await claudeService.GetCompletionAsync("Hello!");

// Google Gemini
var geminiService = new GoogleAIService(apiKey, httpClient);
geminiService.ChangeModel(AIModels.Google.Gemini3_6Flash);
var geminiResponse = await geminiService.GetCompletionAsync("Hello!");
```

## Control ongoing tasks

A report or tool-assisted investigation may take time. Use a run to display progress, connect a Stop button, and submit additional requirements on a supported model. See the [Run guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md) for choosing between a run and `GetCompletionAsync`.

```csharp
await using var run = await service.StartRunAsync(
    "Read the documents and write a report.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

The same run can expose `run.StreamAsync()` events for tools and usage. `(await run.Result).Text` accumulates all emitted text, including intermediate tool-round and pre-steering output; observing the stream is optional. For supported GPT-6 Astra / Sol / Luna runs, call `run.SteerAsync(...)` while work is active. Success acknowledges queued input; it does not undo earlier output or actions.

## Image Generation and Editing

To create visual drafts from text or revise existing images, use the optional `IImageGenerationService` implemented by `OpenAIService`, `GoogleAIService`, and `XAIService`. OpenAI defaults image requests to `AIModels.OpenAI.GptImage2` (`gpt-image-2`); Google uses `AIModels.Google.Images.Gemini3_1FlashImage` (`gemini-3.1-flash-image`); xAI uses `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`). The image model is independent from the service's chat `Model` and can be overridden per request.

Choose `AIModels.OpenAI.GptImage2_5Flare` for quick visual drafts, or `GptImage2_5Sunburst` when editing precision matters most. Both use the same generation and editing methods. The `GptImage2_5Flare_260908` and `GptImage2_5Sunburst_260908` constants pin the September 8, 2026 snapshots. These models add `ImageQuality.XHigh` / `Max` quality and support transparent PNG/WebP output; see the [GPT Image 2.5 guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#gpt-image-25) for format, size, reference-image, and mask limits. The default remains GPT Image 2.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);

var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "A futuristic city at night",
    Count = 2,
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.High,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("city.png", generated.Images[0].Data);

var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "Add warm interior lighting",
    Quality = ImageQuality.XHigh,
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("building.png"), "image/png", "building.png")
    }
});
```

For Gemini image generation or reference-image editing, construct the capability from the Google provider instead. Gemini accepts one requested output per call and has no separate mask input.

```csharp
using Mythosia.AI.Services.Google;

IImageGenerationService geminiImages = new GoogleAIService(geminiApiKey, httpClient);

var generated = await geminiImages.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A precise architectural facade study",
    Model = AIModels.Google.Images.Gemini3_1FlashImage,
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Jpeg
});
```

Google accepts `ImageOutputFormat.Auto` or explicit `ImageOutputFormat.Jpeg`. Explicit `Png` and `WebP` are rejected before sending; the API has no corresponding selectors. Use each returned `GeneratedImage.MediaType` as the format of `Data`.

Google image presets are model-specific: Flash supports 512/1K/2K/4K, Flash-Lite currently allows 1K, and Pro supports 1K/2K/4K. Flash/Lite offer 14 ratios; Pro offers the 10 standard ratios. All accept `Auto`. Inspect `GetImageCapabilities(model)` before presenting choices; unsupported explicit sizes or ratios fail before HTTP in generation and editing. See the [model matrix and Flash-Lite documentation discrepancy](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#google-image-options).

Grok Imagine Image 2.0 uses the same methods for generation and editing with up to five ordered reference images. Its output codec is provider-selected: keep `ImageOutputFormat.Auto` and select the saved extension from `GeneratedImage.MediaType`.

```csharp
using Mythosia.AI.Services.xAI;

IImageGenerationService grokImages = new XAIService(xaiApiKey, httpClient);
var grokImage = await grokImages.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise, wide composition",
    Model = AIModels.xAI.GrokImagineImage2_0,
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});
var image = grokImage.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

xAI accepts 1–10 outputs, `ImageQuality.Auto` / `Low` / `Medium`, and JPEG/PNG/WebP references. Use `ImageSize.Auto` or `Preset` with `ImageResolution.Auto`, `OneK`, or `TwoK`. Keep `ImageBackground.Auto`; masks and explicit compression are rejected. Only `ImageOutputFormat.Auto` is supported; explicit formats fail before HTTP and no transcoding is added.

The upcoming major API replaces string options with `ImageQuality`, `ImageBackground`, `ImageOutputFormat`, and immutable `ImageSize`. The separate request `AspectRatio` property is removed; pass a ratio to `ImageSize.Preset`. OpenAI accepts exact `Pixels` but rejects `Preset`; Google and xAI accept `Preset` but reject `Pixels` instead of approximating them. The output-format default is now `ImageOutputFormat.Auto`. See [before/after migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#image-options-migration). Image methods remain independent of chat Run/streaming.

The result contains every returned image along with provider/model provenance, an optional request ID, and optional usage. `GeneratedImage.Url` is optional; use `GeneratedImage.Data` when the provider returns inline image bytes.

### Migration from v6.x

| Removed API | Replacement |
| --- | --- |
| `GenerateImageAsync` / `GenerateImageUrlAsync` | `IImageGenerationService.GenerateImagesAsync` and `GeneratedImage.Data` / `Url` |
| `AIService.MaxMessageCount` | Configure `ConversationPolicy`; without one, the full active history is sent |
| `ChatBlock.RemoveFunctionMessages()` | Keep function call/result pairs intact, or explicitly clear/rebuild the conversation with `ClearMessages()` |
| `AIService.ExtractFunctionCall(...)` | Override `ExtractFunctionCalls(...)` and return a `FunctionCallBatch` |
| `CompletionProtocol.ExtractFunctionCall(...)` | Override `ExtractFunctionCalls(...)` and return a `FunctionCallBatch` |
| `ProcessFunctionCallAsync(string, Dictionary<string, object>)` | Override `ProcessFunctionCallAsync(FunctionCall)`; batch scheduling is handled by `ProcessFunctionCallsAsync(...)` |
| `GrokReasoning.Off` | Use `Auto` to omit `reasoning_effort`, or `None` to disable reasoning on Grok 4.3; Grok 4.5, 4.6 and 4.7 cannot disable reasoning |
| `AIModels.xAI.Grok3Mini` / `XAIService.UseMiniModel()` | Select Grok 4.3 when reasoning must be switchable, or explicitly select Grok 4.7 for the latest model and its `XHigh` level |
| Retired OpenAI and Claude snapshot constants | Select a current constant from `AIModels`; see the release notes for the complete removal list |
| Implicit `gpt-image-1` default | Use the independent `IImageGenerationService.DefaultImageModel`, which defaults to GPT Image 2 on OpenAI |

## `AIModels` Catalog

Model selection is now documented around provider-grouped string constants via `AIModels`.

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.ChangeModel(AIModels.Anthropic.ClaudeSonnet4_6);
service.ChangeModel(AIModels.Google.Gemini3_6Flash);
service.ChangeModel(AIModels.xAI.Grok4_7);
```

`AIModels.OpenAI.Gpt6Astra` selects `gpt-6-astra`. Pro execution uses the same model ID with `Gpt6ReasoningMode.Pro`.

`AIModels.OpenAI.Gpt5_6` is the rolling GPT-5.6 alias and currently routes to Sol. Use `Gpt5_6Sol` for an explicit Sol selection, `Gpt5_6Terra` for strong performance at a lower price, or `Gpt5_6Luna` for efficient high-volume workloads.

## Static Quick Helpers

For simple stateless usage, use `AIService` static helpers.

```csharp
var answer = await AIService.QuickAskAsync(apiKey, "Summarize this text.");
var vision = await AIService.QuickAskWithImageAsync(apiKey, "Describe this image.", imagePath);
```

<a id="gpt-6-sol-luna"></a>

## GPT-6 Sol / Luna

Choose GPT-6 Sol for demanding coding, tool use and agent tasks; choose Luna when cost and throughput matter for large volumes of text or image-input work. Both use the existing completion, streaming and Run APIs, so switching models does not require a new application workflow.

> `Gpt6Sol`, `Gpt6Luna` and `Gpt6Reasoning.None` require Mythosia.AI 8.1.0 / Abstractions 4.1.0. Existing Astra support retains its earlier minimum versions; the service default is unchanged.

Use `AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) or `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`). Both accept text and images and produce text, with a 1,050,000-token context window, at most 922,000 input tokens and 128,000 output tokens. Input, reasoning and output must still fit the context budget. `MaxTokens` sets the requested output budget, not the context size.

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

`Auto` resolves to `Medium`. Sol/Luna support `None`, `Low`, `Medium`, `High`, `XHigh` and `Max`; `Minimal` is unsupported. Use `WithReasoning(ReasoningLevel.None)` per request, or `Gpt6Reasoning.None` in `WithGpt6Parameters`. Only Sol/Luna with `None` send `Temperature` / `TopP`; reasoning-enabled requests omit them. Astra always requires reasoning and omits sampling. `AIRequestProfile.DisableReasoning` selects `None` for Sol/Luna and `Low` in Standard mode for Astra, omitting reasoning summaries.

`Gpt6ReasoningMode.Standard` and `.Pro` use the same selected model ID. All three GPT-6 models use Responses for tool calling and support opt-in async tools, steering through a WebSocket Run, and cache-preserving reasoning updates in Standard single-agent mode. Check `run.CanSteer` before steering; acceptance does not retract earlier output. `WithSpeed(InferenceSpeed.Fast)` requests paid Fast processing independently of reasoning effort; inspect `result.Processing` to see the applied tier. Account access and server downgrades remain separate from local capability support.

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

## GPT-6 Astra Configuration

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var astra = new OpenAIService(apiKey, httpClient);
astra.ChangeModel(AIModels.OpenAI.Gpt6Astra);
astra.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium,
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

var answer = await astra.GetCompletionAsync("Explain this design.");
```

On Astra, `Gpt6Reasoning` supports `Auto`, `Low`, `Medium`, `High`, `XHigh`, and `Max`; `Auto` resolves to the library default, `Medium`. The values shown above are the `WithGpt6Parameters()` defaults. Configure them individually through `Gpt6ReasoningEffort`, `Gpt6Verbosity`, `Gpt6ReasoningSummary`, and `Gpt6ReasoningMode`. Select `Gpt6ReasoningMode.Pro` for Pro execution on the same `gpt-6-astra` model ID. `None` is available only on Sol/Luna.

Mythosia routes GPT-6 Astra through the Responses API by default, including completions, streaming, structured outputs, vision input, and function calling. GPT-6 tool calling requires Responses. `Temperature` and `TopP` are omitted automatically, including request-profile overrides. The model supports up to 128,000 output tokens; `MaxTokens` controls the requested budget. See the [official model details](https://developers.openai.com/api/docs/models/gpt-6-astra) and [migration guidance](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-6-astra).

GPT-6 Astra reasoning is always enabled: `None` and `Minimal` are unavailable. `AIRequestProfile.DisableReasoning = true` temporarily uses `Low` effort in `Standard` mode and omits the reasoning summary, then restores the configured settings. Set `Gpt6ReasoningSummary = null` when only the summary should be disabled.

GPT-6 Astra internal summarization and query-rewrite profiles reserve at least 4,096 output tokens to accommodate mandatory reasoning. General request token budgets remain caller-controlled.

To continue independent work during a slow lookup, use GPT-6 Astra / Sol / Luna opt-in async tools through `FunctionDefinition.AllowAsync` or `FunctionBuilder.WithAsync()`. See [Async Tool Calling](#async-tool-calling). To add a requirement while the model is working, start a run, check `run.CanSteer`, and call `run.SteerAsync(...)`; see [control ongoing tasks](#control-ongoing-tasks).

## GPT-5 Family Configuration

Current GPT-5 family models (5.1 / 5.2 / 5.3 Codex / 5.4 / 5.5 / 5.6) support **type-safe reasoning configuration** with per-model enums.

### Reasoning Effort (Per-Model Enums)

Each GPT-5 variant has its own enum to ensure only valid options are available at compile time.

```csharp
var gptService = (OpenAIService)service;

// GPT-5.1: Gpt5_1Reasoning (Auto/None/Low/Medium/High) + Verbosity
gptService.WithGpt5_1Parameters(
    reasoningEffort: Gpt5_1Reasoning.Medium,
    verbosity: Verbosity.Low,
    reasoningSummary: ReasoningSummary.Concise);

// GPT-5.2: Gpt5_2Reasoning (Auto/None/Low/Medium/High/XHigh) + Verbosity
gptService.WithGpt5_2Parameters(
    reasoningEffort: Gpt5_2Reasoning.XHigh,
    verbosity: Verbosity.High);

// GPT-5.3 Codex: Gpt5_3Reasoning (Auto/None/Low/Medium/High/XHigh) + Verbosity
gptService.WithGpt5_3Parameters(
    reasoningEffort: Gpt5_3Reasoning.Medium,
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Concise);

// GPT-5.4 / 5.4 Pro: Gpt5_4Reasoning (Auto/None/Low/Medium/High/XHigh) + Verbosity
gptService.WithGpt5_4Parameters(
    reasoningEffort: Gpt5_4Reasoning.Auto,
    verbosity: Verbosity.High,
    reasoningSummary: ReasoningSummary.Auto);

// GPT-5.5 / 5.5 Pro: Gpt5_5Reasoning (Auto/None/Low/Medium/High/XHigh) + Verbosity
gptService.WithGpt5_5Parameters(
    reasoningEffort: Gpt5_5Reasoning.High,
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Concise);

// GPT-5.6 Sol / Terra / Luna: adds Max effort; Pro is a reasoning mode, not a model ID
gptService.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Max,
    verbosity: Verbosity.High,
    reasoningSummary: ReasoningSummary.Detailed,
    reasoningMode: Gpt5_6ReasoningMode.Pro);
```

`Auto` uses the model-appropriate default (e.g., Medium for GPT-5 and GPT-5.6, None for GPT-5.1/5.2, Medium for GPT-5.2 Pro and GPT-5.3 Codex, None for GPT-5.4, Medium for GPT-5.4 Pro, Medium for GPT-5.5, and High for GPT-5.5 Pro). GPT-5 Pro is forced to High; GPT-5.2/5.4/5.5 Pro clamp unsupported `None`/`Low` values to Medium. GPT-5.6 Pro is selected with `Gpt5_6ReasoningMode.Pro` on the same model ID.

GPT-5.6 and GPT-6 requests use `reasoning.context: "current_turn"` because Mythosia rebuilds conversation history locally instead of relying on `previous_response_id`. During tool calls, the original reasoning and function output items are replayed within the active turn.

For OpenAI Responses API calls, Mythosia consumes output and executes tools only after a top-level `status: "completed"`. Failed, incomplete, refused, malformed, or prematurely ended responses surface as errors; collected function calls are discarded before a handler can run. Function-call requests preserve multimodal message parts and image detail, structured-output `text.format`, forced function selection, and each parameter's declared required/optional contract. Empty, malformed, or non-object function arguments also fail before handler execution.

### Reasoning Summary

All GPT-5 family models support `ReasoningSummary` enum (`Auto` / `Concise` / `Detailed`). Set to `null` to disable.

## Gemini Configuration

### Gemini 3 — ThinkingLevel

For long document reviews or repeated tool calls, choose Gemini 3.7 Flash or 3.8 Flash explicitly. The service default remains Gemini 3.6 Flash.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;

var geminiService = new GoogleAIService(apiKey, httpClient);
geminiService.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await geminiService
    .CreateRequest("Compare rolling and blue-green deployments, including rollback risks.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

`ThinkingLevel` sets the service baseline; `WithReasoning(...)` overrides one logical request. Use `Low` for a lighter first pass and `High` for demanding review; more reasoning can increase latency and token use. Gemini 3.7/3.8 Flash accept `Low`, `Medium`, and `High`, but reject `Minimal` and common `ReasoningLevel.None`. `GeminiThinkingLevel.Auto` omits the override; Gemini 3.8's provider default is `Medium`. Both models have provider limits of 1,048,576 input and 65,536 output tokens.

Gemini 3 thinking cannot be fully disabled. Earlier models retain their own defaults: Medium for Gemini 3.6 Flash and Gemini 3.5 Flash, Minimal for Gemini 3.5 Flash-Lite, and High for the current preview models. Gemini 3 Pro models reject `Minimal`; their floor is `Low`.

Gemini 3.8 Flash, 3.7 Flash, 3.6 Flash, and 3.5 Flash-Lite use the latest request contract, so Mythosia omits legacy `temperature`, `topP`, `topK`, and `candidateCount` fields for those models. Other Gemini 3 models omit `candidateCount` while retaining their supported sampling controls. The new models reuse completion, streaming, Run, function calling, and native web/file search with the existing adapter's limits; this is not support for every Google API capability. See the [Google guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#google-googleaiservice).

### Gemini 2.5 — ThinkingBudget

```csharp
geminiService.ChangeModel(AIModels.Google.Gemini2_5Pro);
geminiService.ThinkingBudget = 8192;  // -1 = dynamic (default), 0 = disable
```

`0` disables thinking only on Gemini 2.5 Flash and Flash-Lite. Gemini 2.5 Pro requires at least 128 thinking tokens.

### Gemini Safety Thresholds

Provider defaults are preserved unless a category is explicitly configured:

```csharp
geminiService.HarassmentSafetyThreshold = GeminiSafetyThreshold.BlockMediumAndAbove;
geminiService.HateSpeechSafetyThreshold = GeminiSafetyThreshold.BlockOnlyHigh;
geminiService.SexuallyExplicitSafetyThreshold = GeminiSafetyThreshold.Off;
geminiService.DangerousContentSafetyThreshold = GeminiSafetyThreshold.ProviderDefault;
```

### Gemini Streaming Reasoning (`includeThoughts`)

When streaming with `StreamOptions.WithReasoning()`, Mythosia.AI requests Gemini thought summaries (`includeThoughts: true`) and emits provider-returned chunks as `StreamingContentType.Reasoning`. This observes exposed reasoning output; it does not guarantee a complete internal reasoning trace or set the model's effort.

```csharp
await foreach (var content in geminiService.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Gemini Thinking] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

## Grok Configuration

### Reasoning Effort

Use lower effort for a quick first pass and more reasoning for difficult checks where answer quality matters more than response time. Select Grok 4.6 explicitly for its additional `XHigh` level.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grokService = new XAIService(apiKey, httpClient);
grokService.ChangeModel(AIModels.xAI.Grok4_6);
grokService.WithGrokReasoning(GrokReasoning.Low);

string review = await grokService
    .CreateRequest("Compare rolling and blue-green deployment, including failure recovery.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

`GrokReasoning` is `Auto`, `None`, `Low`, `Medium`, `High`, or `XHigh`. Grok 4.6 accepts `Low` through `XHigh`; `Auto` omits the parameter and uses the provider's `High` default. It cannot disable reasoning. More effort can increase latency and token use. Grok 4.5 remains the service default and accepts `Low` through `High`; Grok 4.3 accepts `None` through `High`. Unsupported combinations, including `XHigh` on earlier models, fail before sending.

`WithGrokReasoning(...)` and the existing `WithGrokParameters(...)` set the service baseline. Common `WithReasoning(...)` overrides one logical request on Grok 4.6, including its tool rounds and structured-output repairs, then restores the baseline. Internal `DisableReasoning` profiles use `Low` for Grok 4.6. Common cache-preserving updates and hosted search are not integrated for xAI. See the official [Grok 4.6](https://docs.x.ai/developers/grok-4-6) and [reasoning](https://docs.x.ai/developers/model-capabilities/text/reasoning) guides.

### Reasoning Content Streaming

Grok 4.6 can stream provider-generated reasoning summaries when observation enables reasoning output. These summaries are optional and do not expose the complete internal reasoning; changing observation options does not change the requested effort. The same options work with Run:

```csharp
using Mythosia.AI.Models.Streaming;

await using var run = await grokService.StartRunAsync(
    "Compare deployment strategies.", streamOptions: new StreamOptions().WithReasoning());
await foreach (var content in run.StreamAsync())
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Summary] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
string answer = (await run.Result).Text;
```

## DeepSeek Configuration

Use DeepSeek Flash when a task needs a quick answer, a more careful review, or an explanation of a chart or screenshot. `AIModels.DeepSeek.Flash` (`deepseek-flash`) selects V4.1 Flash, released on September 10, 2026, with native visual understanding. The existing completion, streaming, Run, function-calling, and RAG APIs remain the entry points; support starts with `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

> V4 Pro, Responses and Files support below requires Mythosia.AI 8.1.0 / Abstractions 4.1.0. Earlier 8.0.0 / 4.0.0 packages include the Flash integration described above.

Select `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813) for text-only work; Flash remains the default and supports images. Both expose Low/High/Max thinking and the same output ceiling. To use DeepSeek Responses with the existing completion, streaming, Run and local-function APIs, set `UseResponsesApi = true` before creating the request. The default stays `false` so existing applications keep Chat Completions; the choice is captured for the whole request and its tool rounds. Responses resends full conversation and native reasoning history instead of relying on server-stored response IDs.

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

Upload an image once when several questions or conversations need to reuse it. `UploadFileAsync` accepts a path or caller-owned stream plus filename; the upload purpose is `user_data`. JPEG, PNG, GIF and WebP uploads are limited to 64 MiB. `DeepSeekImageFileContent` refers to that uploaded image on Flash through either transport; it is not PDF/document input and V4 Pro rejects it. Omitted expiration keeps the file permanently; `expiresAfterSeconds` accepts 3600–2592000 seconds. Keep the file until every conversation that references it has finished.

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

Use `GetFileAsync` for metadata, `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })` for a page, and `DeleteFileAsync` when the image is no longer needed. Pass the returned `LastId` as `After` while `HasMore` is true; `Descending` is also supported. There is no documented file-content download endpoint. The Chat UI offers Flash and V4 Pro; query rewriting uses the current catalogue and migrates the former saved `DeepSeekChat` label to Flash while preserving arbitrary model IDs.

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

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

Register local functions with `WithFunction(...)` to let the model fetch data or act through your code. Tool calls work with or without thinking. Chat Completions rejects forced/required tool choice while thinking, so use automatic selection there. With `UseResponsesApi = true`, `ForceFunctionName` can select a named function even while thinking; the adapter emits a flat Responses `tool_choice` with `type` and `name`. This does not enable native asynchronous tools. The adapter retains native `reasoning_content` and call IDs for later tool rounds. Run and legacy streaming expose provider reasoning as `StreamingContentType.Reasoning` when `StreamOptions.WithReasoning()` is enabled; this observation option does not itself enable thinking. Token usage includes cache and reasoning counts when reported. Automatic context recovery uses the shared streaming loop. When tools require earlier native reasoning history, automatic compaction is blocked to preserve that history; the overflow error remains visible.

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

`ImageContent` accepts JPEG, PNG, GIF, or WebP bytes, or a public HTTP(S) URL retrieved by the provider. The example uses a user message. The current API also accepts image content in tool messages, but registered function handlers still return text through the shared result contract. Manually supplied `ActorRole.Function` image messages must include the matching call ID through `MessageMetadataKeys.FunctionId` (`tool_call_id` on the wire). See the current official vision guide for image size and aggregate limits. Image generation remains unsupported.

Both models advertise 1M context and up to 384K (`393216`) output tokens; the library keeps its 8,000-token request default. Thinking omits temperature/penalties and uses `top_p` of at least 0.95; non-thinking omits `top_p`. Responses uses existing typed-output APIs for native JSON schema. Background execution, server-side `store`/`previous_response_id`, hosted web/file search, `CachePreservation.Required`, native asynchronous tools, `SteerAsync` and image generation are unsupported. Local RAG and ordinary tool rounds remain available.

`V4Flash`, `Chat`, and `Reasoner` remain warning-only obsolete constants with their original wire IDs. The provider temporarily routes the retired `deepseek-v4-flash` alias to V4.1 Flash; the library does not rewrite that constant. Prefer `Flash` explicitly. `UseReasonerModel()` selects Flash with thinking enabled at `High`.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

## `AIRequestProfile`

Apply one-shot runtime overrides per request without mutating long-lived service configuration.

```csharp
var response = await service.GetCompletionAsync(
    "Rewrite this query for retrieval.",
    RequestProfiles.QueryRewrite);
```

## `AIRequestContext`

Use request-scoped prompt injection when you need to pass derived prompt data only for the current call without polluting the real conversation history or the service's base system message.

Available fields:

| Field | Purpose |
|---|---|
| `SystemMessagePrefix` | Text prepended to the system message for this request only |
| `SystemMessageSuffix` | Text appended to the system message for this request only |
| `AdditionalMessages` | Extra messages injected into the conversation for this request only (reference docs, few-shot examples) |
| `RequestMessageOverride` | Replaces the initial input of the current logical request sent to the model, while preserving the original input in history and later tool calls and results |

Example — a query rewriter flow where the original user question should remain in chat history, but a retrieval-friendly rewrite is what actually gets sent to the model:

```csharp
var rewrittenQuery = await service.GetCompletionAsync(
    "Rewrite this question for retrieval.",
    RequestProfiles.QueryRewrite);

var response = await service.GetCompletionAsync(
    originalUserQuestion,
    context: new AIRequestContext
    {
        RequestMessageOverride = new Message(ActorRole.User, rewrittenQuery)
    });
```

Example — injecting retrieved RAG context as a suffix on the system message, without leaking it into conversation history:

```csharp
var answer = await service.GetCompletionAsync(userQuestion,
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nUse the following context to answer:\n{retrievedDocs}"
    });
```

For the full flow and before/after comparisons, see [`docs/request-contexts.md`](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-contexts.md).

## `SystemMessageProvider` — Automatic Baseline Injection

When the same dynamic data (today's date, active folder, session info) must be injected on **every** LLM call, passing an `AIRequestContext` at every entry point gets tedious and error-prone. `AIService.SystemMessageProvider` lets you register a callback once, and every outbound call (`GetCompletionAsync`, `StartRunAsync`, and the retained legacy entry points) automatically invokes it to build a baseline context.

```csharp
// Register once — typically at service construction / DI setup
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

// Every call below automatically receives the baseline context
var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
await using var run = await service.WithMaxRounds(10)
    .StartRunAsync(goal);
var agentResult = (await run.Result).Text;
```

When the baseline comes from a database, cache, or HTTP call, use the async overload so the provider does not have to block on `.Result`. Overload resolution picks the right one by lambda arity — no arg for sync, one `CancellationToken` for async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

`StartRunAsync` forwards its cancellation token to the async context provider whether or not output is observed. Legacy streaming paths also forward the caller token. Existing `GetCompletionAsync` and `RunAgentAsync` signatures do not accept a cancellation token; use a run when cancellation is needed.

When a call also passes an explicit `AIRequestContext`, the two merge field-by-field: explicit values win on scalar fields (`SystemMessagePrefix`, `SystemMessageSuffix`, `RequestMessageOverride`); `AdditionalMessages` concatenates (provider first, then explicit).

Available in Mythosia.AI v6.3.0+. Full details in [`docs/request-contexts.md`](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-contexts.md).

## Function Calling

### Quick Start with Functions

```csharp
// Define a simple function
var service = new OpenAIService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => $"The weather in {location} is sunny, 22°C"
    );

// AI will automatically call the function when needed
var response = await service.GetCompletionAsync("What's the weather in Seoul?");
// Output: "The weather in Seoul is currently sunny with a temperature of 22°C."
```

### Attribute-Based Function Registration

```csharp
public class WeatherService
{
    [AiFunction("get_current_weather", "Gets the current weather for a location")]
    public string GetWeather(
        [AiParameter("The city name", required: true)] string city,
        [AiParameter("Temperature unit", required: false)] string unit = "celsius")
    {
        // Your implementation
        return $"Weather in {city}: 22°{unit[0]}";
    }
}

// Register all functions from a class
var weatherService = new WeatherService();
var service = new OpenAIService(apiKey, httpClient)
    .WithFunctions(weatherService);
```

### Advanced Function Builder

```csharp
var service = new OpenAIService(apiKey, httpClient)
    .WithFunction(FunctionBuilder.Create("calculate")
        .WithDescription("Performs mathematical calculations")
        .AddParameter("expression", "string", "The math expression", required: true)
        .AddParameter("precision", "integer", "Decimal places", required: false, defaultValue: 2)
        .WithHandler(async (args) => 
        {
            var expr = args["expression"].ToString();
            var precision = Convert.ToInt32(args.GetValueOrDefault("precision", 2));
            // Calculate and return result
            return await CalculateAsync(expr, precision);
        })
        .Build());
```

### Multiple Functions with Different Types

```csharp
var service = new OpenAIService(apiKey, httpClient)
    // Parameterless function
    .WithFunction(
        "get_time",
        "Gets the current time",
        () => DateTime.Now.ToString("HH:mm:ss")
    )
    // Two-parameter function
    .WithFunction(
        "add_numbers",
        "Adds two numbers",
        ("a", "First number", true),
        ("b", "Second number", true),
        (double a, double b) => $"The sum is {a + b}"
    )
    // Async function
    .WithFunctionAsync(
        "fetch_data",
        "Fetches data from API",
        ("endpoint", "API endpoint", true),
        async (string endpoint) => await httpClient.GetStringAsync(endpoint)
    );

// The AI will automatically use the appropriate functions
var response = await service.GetCompletionAsync(
    "What time is it? Also, what's 15 plus 27?"
);
```

### Function Calling Policies

```csharp
// Pre-defined policies
service.DefaultPolicy = FunctionCallingPolicy.Fast;     // 30s timeout, 10 rounds
service.DefaultPolicy = FunctionCallingPolicy.Complex;   // 300s timeout, 50 rounds
service.DefaultPolicy = FunctionCallingPolicy.Vision;    // 200s timeout, for image analysis

// Custom policy
service.DefaultPolicy = new FunctionCallingPolicy
{
    MaxRounds = 25,
    TimeoutSeconds = 120,
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 5,
    EnableLogging = true  // Enable debug output
};

// Per-request policy override
var fastResponse = await service
    .WithPolicy(FunctionCallingPolicy.Fast)
    .GetCompletionAsync("Complex task requiring functions");

// Inline policy configuration
var configuredResponse = await service
    .BeginMessage()
    .AddText("Analyze this data")
    .WithMaxRounds(5)
    .WithTimeout(60)
    .SendAsync();
```

`Sequential` is the default for ordinary handler scheduling and preserves one-at-a-time
execution. The provider response and conversation history still retain the complete
multi-call batch introduced in v7.
`Parallel` runs calls from the same provider response concurrently up to
`MaxConcurrency`, then returns their results in the original provider order. Use
parallel execution only for independent, thread-safe handlers.

`TimeoutSeconds` controls provider requests and the surrounding round loop.
Cancellation-aware tools receive the execution token. Calls waiting to start are
skipped with cancellation results; started functions are awaited to keep call/result
history consistent, including legacy functions that cannot stop cooperatively.
Streaming uses one timeout for the complete round loop, including response headers
and the SSE body. Policy expiry raises `AIServiceException`; cancelling the token
passed to `StreamAsync` remains an `OperationCanceledException` with the caller token.

### Async Tool Calling

A slow lookup can leave time for useful work that does not depend on its result. For example, the model can explain general packing advice while a weather tool runs, then incorporate the forecast once it arrives. Allow this overlap for the selected handler:

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Returns a demo weather snapshot for Seoul")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "Demo weather in Seoul: clear, 24 C";
    })
    .Build();
service.WithFunction(weatherTool);

var answer = await service.GetCompletionAsync(
    "Check the demo Seoul weather. While waiting, list three packing essentials.");
```

`WithAsync()` sets `FunctionDefinition.AllowAsync = true`; its default is `false`, and `WithAsync(false)` disables the option. Attribute-based registration also accepts `[AiFunction("lookup", "Look up data", AllowAsync = true)]`. This permission is independent of `WithFunctionAsync` and `FunctionExecutionMode.Parallel`: those control .NET handlers, while `AllowAsync` allows the model to continue before a result arrives. Enable it only when overlapping model work is appropriate for that function.

Mythosia enables the API option for GPT-6 Astra / Sol / Luna through Responses. Other models and APIs omit the option and execute the same handler with the existing wait-for-result behavior, without modifying `AllowAsync`. The provider must also mark the actual call as async (`FunctionCall.IsAsync`); permission alone does not guarantee async execution. See the [official async tool calling guide](https://developers.openai.com/api/docs/guides/async-tool-calling).

`FunctionExecutionMode` still controls ordinary calls. Opted-in async jobs can overlap even in `Sequential` mode and share a separate pending-job limit set by `MaxConcurrency`.

Completion calls, existing input-taking streams, and `StartRunAsync` each manage their pending tool jobs within the originating execution and deliver outputs with their original call IDs. A successful completion or run `Result` waits for pending results to be processed. Cancellation-aware handlers receive the execution token; cleanup still waits for started handlers that ignore cancellation. Calls waiting to start receive cancellation results without executing. Exceptions are failed results, and cancellation is recorded with both `IsError` and `IsCancelled`. Disposing the existing `service.StreamAsync` iterator also cleans up execution. Ending a `run.StreamAsync()` reader only stops observation; use `run.Cancel()` or dispose the run to stop execution.

When async tools are used, `GetCompletionAsync` returns the intermediate independent text and the final text accumulated in order, after the request finishes. `StreamAsync` emits text as it arrives across those rounds.

Streaming starts handlers after complete function calls and a valid response boundary have been received, then continues the next model round while async jobs run. It does not dispatch handlers from incomplete call events. When the model returns no new calls while jobs remain pending, the library waits for results before resuming another round. Pending calls also prevent automatic context-overflow summarization retries, which could otherwise omit unfinished calls from history.

### Function Calling with Streaming

```csharp
// Stream with function calling support
await foreach (var content in service.StreamAsync(
    "What's the weather in Seoul and calculate 15% tip on $85",
    StreamOptions.WithFunctions))
{
    if (content.Type == StreamingContentType.FunctionCall && content.FunctionCall is { } call)
    {
        Console.WriteLine($"Calling function: {call.Name}");
    }
    else if (content.Type == StreamingContentType.FunctionResult && content.FunctionResult is { } result)
    {
        Console.WriteLine($"Function completed: {result.Call.Name}; error={result.IsError}");
    }
    else if (content.Type == StreamingContentType.Text)
    {
        Console.Write(content.Content);
    }
}
```

### Tool tasks through the common run

```csharp
// Collect the run result without observing events
await using var run = await service
    .CreateRequest("Find the weather in Seoul and explain what to wear today.")
    .WithMaxRounds(10)
    .StartRunAsync(
);
var answer = (await run.Result).Text;

// Observe a second task after the first task has finished
await using var streamedRun = await service
    .CreateRequest("Find the weather in Seoul and explain what to wear today.")
    .WithMaxRounds(10)
    .StartRunAsync();
await foreach (var content in streamedRun.StreamAsync())
{
    if (content.Type == StreamingContentType.FunctionCall && content.FunctionCall is { } call)
    {
        Console.WriteLine($"Calling: {call.Name}");
    }
    else if (content.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Tool result: {content.Content}");
    }
    else if (content.Type == StreamingContentType.Text)
    {
        Console.Write(content.Content);
    }
}
```

Normal completion and run execution already share the multi-round function loop. `RunAgentAsync` and `RunAgentStreamAsync` are obsolete compatibility helpers, retaining their legacy 10-round default and max-step exception behavior. `WithMaxRounds(10)` preserves the limit when migrating; new run failures use the common error contract.

### Disabling Functions Temporarily

```csharp
// Disable functions for a single request
var response = await service
    .WithoutFunctions()
    .GetCompletionAsync("Don't use any functions for this");

// Or use the async helper
var response = await service.AskWithoutFunctionsAsync(
    "Process this without calling functions"
);
```

## Structured Output

Deserialize LLM responses directly into C# POCOs with automatic JSON recovery.

### Basic Usage

```csharp
// Define your POCO
public class WeatherResponse
{
    public string City { get; set; }
    public double Temperature { get; set; }
    public string Condition { get; set; }
}

// Get typed result — schema is auto-generated and sent to the LLM
var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");
Console.WriteLine($"{result.City}: {result.Temperature}°C, {result.Condition}");
```

### Auto-Recovery Retry

When the LLM returns invalid JSON, a correction prompt is automatically sent asking the model to fix its output. This is **not** a network retry — it's an output quality/format correction loop.

```csharp
// Configure service-level retry count (default: 2)
service.StructuredOutputMaxRetries = 3;

// On final failure, StructuredOutputException is thrown with rich diagnostics:
// - FirstRawResponse, LastRawResponse
// - ParseError, AttemptCount, SchemaJson, TargetTypeName
```

The repair budget excludes the initial response. `StructuredOutputMaxRetries` and `MaxRepairAttempts` treat negative values as zero and accept up to `int.MaxValue - 1`; `int.MaxValue` raises `ArgumentOutOfRangeException` before provider work so the total attempt count cannot overflow. This applies to typed completion and `BeginStream(...).As<T>()`.

### Per-Call Structured Output Policy

Override retry behavior for a single request without changing service defaults:

```csharp
// Custom policy — applies only to this call, then auto-cleared
var result = await service
    .WithStructuredOutputPolicy(new StructuredOutputPolicy { MaxRepairAttempts = 5 })
    .GetCompletionAsync<MyDto>(prompt);

// Preset: no retry (1 attempt only)
var result = await service
    .WithNoRetryStructuredOutput()
    .GetCompletionAsync<MyDto>(prompt);

// Preset: strict mode (up to 3 retries = 4 total attempts)
var result = await service
    .WithStrictStructuredOutput()
    .GetCompletionAsync<MyDto>(prompt);
```

| Preset | MaxRepairAttempts | Description |
|--------|-------------------|-------------|
| `Default` | `null` (service default) | Uses `StructuredOutputMaxRetries` |
| `NoRetry` | `0` | Single attempt, no retry |
| `Strict` | `3` | Up to 3 correction retries |

### Streaming Structured Output

Stream text chunks in real-time to the UI while getting a final deserialized object with auto-repair:

```csharp
var run = service.BeginStream(prompt)
    .WithStructuredOutput(new StructuredOutputPolicy { MaxRepairAttempts = 2 })
    .As<MyDto>();

// Optional: observe chunks in real-time
await foreach (var chunk in run.Stream(cancellationToken))
{
    Console.Write(chunk); // UI display
}

// Final deserialized result (waits for stream + parse/repair)
MyDto dto = await run.Result;
```

- **`Result` works without `Stream()`** — just `await run.Result` internally consumes the stream and parses
- **`Stream()` is single-use** — second call throws `InvalidOperationException`
- **`Result` waits for stream completion** — even if awaited mid-stream, it won't resolve early
- **Repair retries are non-streaming** — correction prompts use `GetCompletionAsync()` for efficiency

### Collection Support (`List<T>`, `T[]`)

Both `GetCompletionAsync<T>()` and streaming support collection types — no wrapper DTO needed:

```csharp
// Non-streaming: get a list directly
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "Extract all entities from this document...");

// Streaming: observe chunks + get list result
var run = service.BeginStream(prompt).As<List<ItemDto>>();
await foreach (var chunk in run.Stream()) Console.Write(chunk);
List<ItemDto> items = await run.Result;
```

`List<T>`, `T[]`, `IReadOnlyList<T>` are all supported. JSON array schema is auto-generated from the element type.

## Conversation Summary Policy

Automatically summarize old conversation messages when the conversation exceeds a configured threshold. The summary is stored and injected into the system message on subsequent stateful LLM requests. Stateless requests omit the stored summary without changing it.

### Configuration

```csharp
// Token-based: summarize when total tokens exceed 3000, keep recent ~1000 tokens
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// Message-count-based: summarize when messages exceed 20, keep last 5
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// Combined (OR condition): triggers when either threshold is exceeded
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 3000,
    triggerCount: 20
);
```

### Usage

```csharp
// Just use as normal — summarization happens automatically
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(triggerCount: 20, keepRecentCount: 5);

var response = await service.GetCompletionAsync("Continue our conversation...");
// When message count exceeds 20, old messages are summarized automatically
```

### Session Persistence

```csharp
// Save summary for later
string saved = service.ConversationPolicy.CurrentSummary;

// Restore in a new session
policy.LoadSummary(saved);
```

### Key Design Decisions

- **StatelessMode protection** — Summary LLM calls use `StatelessMode = true` to prevent polluting the main conversation history
- **Explicit management** — `ConversationPolicy` defaults to `null`; in v7 that means the full active conversation history is sent without a hidden message-count window
- **Provider-agnostic** — Works with all providers (OpenAI, Claude, Gemini, Grok, DeepSeek, Perplexity)
- **Incremental summarization** — When re-summarizing, existing summary is included as context for the new summary

## Context-Overflow Recovery

*(Since v6.8.0)* When the server rejects a request for exceeding the model's context window, the conversation is compacted and the request is sent again — automatically. The limit belongs to the server, so being told "that did not fit" is the authoritative signal, more reliable than a client-side token estimate and always in step with the deployment's real limit.

```csharp
// On by default (ContextRecoveryMaxRetries = 1). Nothing to configure — a request that
// overflows the window is compacted and retried once instead of failing outright.
service.ConversationPolicy = SummaryConversationPolicy.ByToken(triggerTokens: 3000);
var response = await service.GetCompletionAsync("…a very long conversation…");

// Opt out (pre-6.8.0 behavior — the rejection propagates unchanged):
service.ContextRecoveryMaxRetries = 0;
```

- **Server-driven** — Detection reads the provider's actual 400/413 rejection (OpenAI, vLLM, Anthropic, Google), never a guessed token count. A rate limit or a server error is never mistaken for an overflow.
- **Costs nothing when it cannot help** — If there is nothing left to compact, recovery gives up *before* issuing a summary call or deleting any message; it never spends a summary or destroys history to arrive at the same rejection.
- **Streaming recovers per round** — An overflow mid-run recompacts and replays only the round that overflowed, keeping the tool results earlier rounds produced.
- **Diagnostics** — When recovery cannot save the request, the thrown `ContextLengthExceededException` carries `RecoverySkipReason` (`no-policy`, `nothing-to-cut`, `tool-side-effects`, `retries-exhausted`, …) and the server-reported `MaxContextTokens` / `RequestedTokens` when available.

See the [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md) for provider limits. DeepSeek uses the shared streaming recovery loop, but blocks automatic compaction when tools require preserved native reasoning history. Perplexity preserves native Agent API output for subsequent requests; see its [execution and history limits](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/perplexity.md).

## Enhanced Streaming

### Stream Options

```csharp
// Text only - fastest, no overhead
await foreach (var chunk in service.StreamAsync("Hello", StreamOptions.TextOnlyOptions))
{
    Console.Write(chunk.Content);
}

// With metadata - includes model info, timestamps, etc.
await foreach (var content in service.StreamAsync("Hello", StreamOptions.FullOptions))
{
    if (content.Metadata != null)
    {
        Console.WriteLine($"Model: {content.Metadata["model"]}");
    }
    Console.Write(content.Content);
}

// Custom options
var options = new StreamOptions()
    .WithMetadata(true)
    .WithFunctionCalls(true)
    .AsTextOnly(false);

await foreach (var content in service.StreamAsync("Query", options))
{
    // Process based on content.Type
    switch (content.Type)
    {
        case StreamingContentType.Text:
            Console.Write(content.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine($"Calling: {content.Metadata["function_name"]}");
            break;
        case StreamingContentType.Completion:
            Console.WriteLine($"Total length: {content.Metadata["total_length"]}");
            break;
    }
}
```

### Streaming Diagnostics

When an SSE stream dies mid-flight against a self-hosted backend (vLLM, ollama, internal proxy), you usually need to know exactly where it died. Register diagnostic hooks once on the service — every subsequent `StreamAsync` call picks them up automatically. Same fluent builder pattern as `WithRag`.

```csharp
using Mythosia.AI.Extensions;

service.WithStreamDiagnostics(d => d
    .OnRawLine(line => logger.LogDebug("SSE: {Line}", line))
    .OnComplete(diag => logger.LogInformation("Stream finished: {Diag}", diag)));

await foreach (var chunk in service.StreamAsync(message))
    Console.Write(chunk.Content);
```

Each `On*` method is independent — register only what you need:

```csharp
// Raw line trace only
service.WithStreamDiagnostics(d => d.OnRawLine(line => logger.LogDebug("SSE: {Line}", line)));

// Clear all hooks
service.WithStreamDiagnostics(_ => { });
```

When SSE reading throws, the library wraps the exception in `StreamReadException` with a `StreamDiagnostics` snapshot taken at the moment of failure. This works regardless of whether `WithStreamDiagnostics` was registered:

```csharp
try
{
    await foreach (var chunk in service.StreamAsync(message))
        Console.Write(chunk.Content);
}
catch (StreamReadException ex)
{
    logger.LogError(ex,
        "Stream died after {Lines} lines, {Chars} chars. Last raw line: {Line}",
        ex.Diagnostics.LinesRead,
        ex.Diagnostics.AccumulatedTextLength,
        ex.Diagnostics.LastRawLine);

    // ex.InnerException carries the original exception (IOException, etc.)
}
```

`StreamDiagnostics` exposes `LinesRead`, `DataLinesProcessed`, `ParseFailures`, `AccumulatedTextLength`, `LastRawLine`, and `Elapsed`. Hooks are propagated through `CopyFrom`, so cross-provider switches in a multi-provider chat UI keep the registered diagnostics without re-registration.

Available in Mythosia.AI v6.4.0+. Full guide: [`docs/streaming.md`](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/streaming.md).

### Token Usage

Streaming exposes token usage in two different places, with different meanings:

- `StreamingContentType.RoundUsage`: usage for one LLM round only.
- `StreamingContentType.Completion`: cumulative usage for the whole streaming run.

For a single LLM call, the final `RoundUsage.Usage` and `Completion.Usage` should describe
the same one-round request. For an agent or function-calling run, each LLM round emits its own
`RoundUsage`, while the final `Completion.Usage` remains the sum of all rounds.

This distinction is important for UI context meters. If you want to show "how many tokens the
current conversation state used when it entered the latest LLM call", use the latest
`RoundUsage.Usage.InputTokens`. If you want cost or diagnostics for the full agent run, use
`Completion.Usage.TotalTokens`.

`RoundUsage` events also include:

- `RoundIndex`: 1-based LLM round number.
- `IsFinalRound`: true when this is the last LLM round in the stream.

```csharp
await foreach (var content in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.RoundUsage && content.Usage != null)
    {
        Console.WriteLine($"Round: {content.RoundIndex}");
        Console.WriteLine($"Round total: {content.Usage.TotalTokens}");
        Console.WriteLine($"Final round: {content.IsFinalRound}");
    }

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"Input tokens: {content.Usage.InputTokens}");
        Console.WriteLine($"Output tokens: {content.Usage.OutputTokens}");
        Console.WriteLine($"Cached tokens: {content.Usage.CachedInputTokens}");
        Console.WriteLine($"Reasoning tokens: {content.Usage.ReasoningTokens}");
        Console.WriteLine($"Cache hit ratio: {content.Usage.CacheHitRatio:P1}");
    }
}
```

### Agent Token Meter Example

```csharp
int? contextTokenMeter = null;
TokenUsage? cumulativeRunUsage = null;

await using var streamedRun = await service
    .CreateRequest("Find the weather in Seoul and answer briefly.")
    .WithMaxRounds(10)
    .StartRunAsync();
await foreach (var content in streamedRun.StreamAsync())
{
    if (content.Type == StreamingContentType.RoundUsage && content.Usage != null)
    {
        // Best value for a UI context/token meter.
        contextTokenMeter = content.Usage.InputTokens;

        Console.WriteLine(
            $"Round {content.RoundIndex}: input={content.Usage.InputTokens}, total={content.Usage.TotalTokens} tokens");

        if (content.IsFinalRound)
        {
            Console.WriteLine($"Final context meter value: {contextTokenMeter}");
        }

        continue;
    }

    if (content.Type == StreamingContentType.Completion)
    {
        // Cumulative usage across the whole agent run.
        cumulativeRunUsage = content.Usage;
        continue;
    }

    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### Token Usage Contract

- `RoundUsage.Usage` is never an accumulated run total. It represents that one LLM round.
- `RoundUsage.Usage.TotalTokens` preserves an explicit provider total, even when the input/output breakdown is incomplete. The adapter calculates `InputTokens + OutputTokens` only when the provider omits its total.
- `Completion.Usage` keeps the existing cumulative meaning for the full stream or agent run.
- In function-calling streams, non-final rounds have `IsFinalRound = false`; the last round has `IsFinalRound = true`.
- Token usage collection does not depend on `IncludeMetadata`. Usage can still be emitted when metadata is disabled.
- Providers may attach official usage to different stream chunks internally. Consumers should read the normalized `RoundUsage` and `Completion` events rather than provider-specific chunk metadata.
- Gemini streams are drained after function calls so late `usageMetadata` chunks can still become `RoundUsage`.

The `Token` test category contains provider-level tests for this contract. If those tests pass
for a provider/model, Mythosia.AI considers round-level usage and final cumulative usage supported
for that provider/model. If a provider/model does not return official usage, these tests should fail
or be treated as unsupported for token usage.

`TokenUsage` fields:

| Field | Description | Providers |
|-------|-------------|-----------|
| `InputTokens` | Input/prompt tokens | All |
| `OutputTokens` | Output/completion tokens | All |
| `TotalTokens` | Total tokens used | All |
| `CachedInputTokens` | Tokens served from cache | OpenAI, Claude, DeepSeek, Gemini |
| `CacheCreationTokens` | Tokens written to cache | OpenAI, Claude |
| `ReasoningTokens` | Internal reasoning tokens | OpenAI, Gemini |

Computed properties: `NonCachedInputTokens`, `CacheHitRatio`, `HasCacheActivity`, `VisibleOutputTokens`.

## Reasoning Streaming

GPT-6 Astra / Sol / Luna, supported GPT-5.1–5.6 models, Claude, Gemini, Grok, and DeepSeek Flash expose provider-returned reasoning through streaming events. Enable DeepSeek thinking explicitly, then observe with `StreamOptions.WithReasoning()`; observing reasoning does not turn it on.

```csharp
await foreach (var content in service.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"[Thinking] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

## Service Support

| Service | Function Calling | Streaming | Reasoning | Notes |
|---------|-----------------|-----------|-----------|--------|
| **OpenAI GPT-6 Sol / Luna** | ✅ | ✅ | ✅ | Responses API, opt-in async function calls, optional reasoning (`None`–`Max`, excluding `Minimal`), Standard/Pro, streaming steering, paid Fast |
| **OpenAI GPT-6 Astra** | ✅ | ✅ | ✅ | Responses API, opt-in async function calls, mandatory reasoning through `Max`, verbosity, summaries, optional Pro reasoning mode |
| **OpenAI GPT-5.6 Sol / Terra / Luna** | ✅ | ✅ | ✅ | `Max` effort, verbosity, summaries, optional Pro reasoning mode |
| **OpenAI GPT-5.5 / 5.5 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.3 Codex** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.2 / 5.2 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.1** | ✅ | ✅ | ✅ | Reasoning + verbosity control |
| **OpenAI GPT-4.1 / 4.1 Mini / GPT-4o / 4o Mini** | ✅ | ✅ | — | Full function support |
| **Claude Opus 5.5** | ✅ | ✅ | ✅ | Since v8.1.0; always-on adaptive thinking, medium/omitted default, explicit updates, preserved-thinking controls; forced tools unsupported |
| **Claude Fable 5.1** | ✅ | ✅ | ✅ | Progress updates, per-message effort, turn instructions, binding diagnostics; forced tool choice unsupported |
| **Claude Mythos 5.1** | ✅ | ✅ | ✅ | Invitation only; same 5.1 controls without Fable's prefix check; forced tool choice unsupported |
| **Claude Fable 5** | ✅ | ✅ | ✅ | Adaptive thinking + tool use |
| **Claude Mythos 5** | ✅ | ✅ | ✅ | Limited availability; always-on adaptive thinking + tool use |
| **Claude Opus 5 / Sonnet 5** | ✅ | ✅ | ✅ | Adaptive thinking + signed tool continuation |
| **Claude Opus 4.8 / 4.7 / 4.6 / 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Sonnet 4.6 / 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Haiku 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Gemini 3.8 Flash / 3.7 Flash / 3.6 Flash / 3.5 Flash / 3.5 Flash-Lite / current Gemini 3 previews** | ✅ | ✅ | ✅ | ThinkingLevel + thought signatures |
| **Gemini 2.5 Pro / Flash / Flash-Lite** | ✅ | ✅ | ✅ | ThinkingBudget control |
| **xAI Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20 / Build** | ✅ | ✅ | ✅ | Model-specific `GrokReasoning`; common effort on 4.7 / 4.6; optional reasoning summaries |
| **DeepSeek Flash (V4.1 Flash)** | ✅ | ✅ | ✅ | User image input, Low/High/Max thinking, native reasoning history, local tools; Chat Completions rejects forced tool choice while thinking; opt-in Responses allows named functions |
| **Perplexity** | ✅ | ✅ | Model-dependent | Agent API, hosted tools, local functions, and citations |

## Complete Examples

### Building a Weather Assistant

```csharp
public class WeatherAssistant
{
    private readonly OpenAIService _service;
    private readonly HttpClient _httpClient;

    public WeatherAssistant(string apiKey)
    {
        _httpClient = new HttpClient();
        _service = new OpenAIService(apiKey, _httpClient)
            .WithSystemMessage("You are a helpful weather assistant.")
            .WithFunction(
                "get_weather",
                "Gets current weather for a city",
                ("city", "City name", true),
                GetWeatherData
            )
            .WithFunction(
                "get_forecast",
                "Gets weather forecast",
                ("city", "City name", true),
                ("days", "Number of days", false),
                GetForecast
            );
        
        // Configure function calling behavior
        _service.DefaultPolicy = new FunctionCallingPolicy
        {
            MaxRounds = 10,
            TimeoutSeconds = 30,
            EnableLogging = true
        };
    }

    private string GetWeatherData(string city)
    {
        // In real implementation, call weather API
        return $"{{\"city\":\"{city}\",\"temp\":22,\"condition\":\"sunny\"}}";
    }

    private string GetForecast(string city, int days = 3)
    {
        // In real implementation, call forecast API
        return $"{{\"city\":\"{city}\",\"forecast\":\"{days} days of sun\"}}";
    }

    public async Task<string> AskAsync(string question)
    {
        return await _service.GetCompletionAsync(question);
    }

    public async IAsyncEnumerable<string> StreamAsync(string question)
    {
        await foreach (var content in _service.StreamAsync(question))
        {
            if (content.Type == StreamingContentType.Text && content.Content != null)
            {
                yield return content.Content;
            }
        }
    }
}

// Usage
var assistant = new WeatherAssistant(apiKey);

// Functions are called automatically
var response = await assistant.AskAsync("What's the weather in Tokyo?");
// AI calls get_weather("Tokyo") and responds naturally

// Streaming also supports functions
await foreach (var chunk in assistant.StreamAsync(
    "Compare weather in Seoul and Tokyo for the next 5 days"))
{
    Console.Write(chunk);
}
```

### Math Tutor with Step-by-Step Solutions

```csharp
var mathTutor = new OpenAIService(apiKey, httpClient)
    .WithSystemMessage("You are a math tutor. Always explain your reasoning.")
    .WithFunction(
        "calculate",
        "Performs calculations",
        ("expression", "Math expression", true),
        (string expr) => {
            // Using a math expression evaluator
            var result = EvaluateExpression(expr);
            return $"Result: {result}";
        }
    )
    .WithFunction(
        "solve_equation",
        "Solves equations step by step",
        ("equation", "Equation to solve", true),
        (string equation) => {
            var steps = SolveWithSteps(equation);
            return JsonSerializer.Serialize(steps);
        }
    );

// The AI will use functions and explain the process
var response = await mathTutor.GetCompletionAsync(
    "Solve the equation 2x + 5 = 13 and verify the answer"
);
// Output includes step-by-step solution with verification
```

## Best Practices

1. **Function Design**: Keep functions focused and simple. Complex logic should be broken into multiple functions.

2. **Error Handling**: Throw for execution failures so the executor records failed tool results. A deliberately returned string, even error text, remains successful tool content.

3. **Performance**: Use appropriate policies for your use case (Fast for simple tasks, Complex for detailed analysis).

4. **Streaming**: Use `TextOnlyOptions` for best performance when metadata isn't needed.

5. **Testing**: Test function calling with various prompts to ensure robust behavior.

## Troubleshooting

**Q: Functions aren't being called when expected?**
- Ensure functions are registered with clear, descriptive names and descriptions
- Check that `EnableFunctions` is true on the service
- Verify the model supports function calling (see Service Support table above)

**Q: Function calling is too slow?**
- Adjust the policy timeout: `service.DefaultPolicy.TimeoutSeconds = 30`
- Use `FunctionCallingPolicy.Fast` for simple operations
- Consider using streaming for better perceived performance

**Q: How to debug function execution?**
- Enable logging: `service.DefaultPolicy.EnableLogging = true`
- Check the console output for round-by-round execution details
- Use `StreamOptions.FullOptions` to see function call metadata

**Q: Can I use functions with streaming?**
- Yes! Functions work seamlessly with streaming
- Use `StreamOptions.WithFunctions` to see function execution in real-time

## Validate processing speed against real providers

In a source checkout, from the repository root, run:

```powershell
./build/test-inference-speed-live.ps1
```

The paid suite uses the existing test Key Vault setup and synthetic prompts. It checks Anthropic Opus 5.5, OpenAI GPT-6 Astra, Gemini 3.8 Flash and Grok 4.6 across ProviderDefault/Standard/Fast and completion/Run paths: 24 cases. Account access errors, absent applied-mode reporting and server downgrades do not count as successful Fast validation; every case must pass without skips. Reports go to `artifacts/test-results/inference-speed-live`. Use `-NoBuild` only after building the current Release tests. This command documents how to run the suite, not a claim that the current account has passed it.
