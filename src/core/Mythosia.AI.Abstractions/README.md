# Mythosia.AI.Abstractions

Use this package when building middleware, retrieval integrations or custom providers that need a shared AI contract without pulling in provider SDKs. It defines `IAIService`, messages, streaming events and shared models, with optional `IAIRunService`, `IAIRequestFeatureService` and `IImageGenerationService` capabilities. Applications normally receive it through `Mythosia.AI`; the only package dependency is the lightweight `Mythosia` base library.

Version 4.1.0 speed contracts add `InferenceSpeed`, immutable `AIProcessingInfo`, request-feature `WithSpeed`, tri-state speed capabilities and `AIRunResult.Processing`. They describe processing mode and provider reports, not measured tokens per second. The matching core implementation provides provider validation and transport wiring; `IAIService` gains no required members; optional `IAIProcessingInfoService` and `GetLastProcessing()` expose observations through interface references. See [processing speed](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md#inference-speed).

## Current release: 4.1.0

This additive contracts release pairs with **Mythosia.AI 8.1.0**. It adds model identifiers and optional speed contracts without changing existing required interface members, constructors or enum values. See the [v4.1.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v410).

The v4 contracts below remain available. When upgrading from 3.x or earlier, the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) explains the required source changes and rebuilds.

| Contract | What it enables and what changes |
| --- | --- |
| Typed image options | Named quality/background/format choices and immutable `ImageSize.Auto`, `.Pixels(...)` and `.Preset(...)`; replace former strings and the separate `AspectRatio` property. |
| Completion cancellation | Both `IAIService.GetCompletionAsync` signatures append optional `CancellationToken`; custom implementations must update and propagate it. |
| `AIRunResult` | `AIRun.Result` returns a completed snapshot with text, reported usage, citations, requested/actual model, rounds and finish details; former string callers read `.Text`. |
| Tool result contracts | `HandlerWithCancellation` and `FunctionCallResult.IsCancelled` support cooperative execution; core normalizes asynchronous object results. |
| `AIModelCapabilities` / `ImageModelCapabilities` | Immutable typed choices distinguish `Supported`, `Unsupported` and `Unknown` without asserting live account access. |

`GetCompletionAsync` and typed `StructuredStreamRun<T>.Result` keep their return types. Custom `AIService` providers retain the single-`Message` completion override and forward protected `RequestCancellationToken` to transport work. Cancellation cannot guarantee remote inference or billing cancellation. See the [completion contract](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation-migration) and [Run result migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result-migration).

`AIService.CreateRequest(...)`, immutable `AIRequestBuilder`, capability queries and asynchronous tool execution live in the implementation package. Those features add no mandatory `IAIService` members themselves; the completion cancellation signature changes above still apply. See [request settings](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md), [tool returns/errors/cancellation](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/function-calling.md#tool-execution-contract), and [capability inspection](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/model-capabilities.md).

The `AIModels.Anthropic.ClaudeOpus5_5` identifier selects `claude-opus-5-5` using the existing reasoning/display contracts. Its provider validation, preserved-thinking behavior and capabilities require Mythosia.AI 8.1.0 / Abstractions 4.1.0. See [Opus 5.5](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#claude-opus-55).

The `AIModels.OpenAI.Gpt6Sol`, `Gpt6Luna` and additive `Gpt6Reasoning.None` let applications select complex agent work or economical volume without changing execution APIs. They require Mythosia.AI 8.1.0 / Abstractions 4.1.0. [Model selection and controls](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#gpt-6-sol-luna).

The `AIModels.xAI.Grok4_7` identifier selects `grok-4.7` with the existing `GrokReasoning`, request, Run and processing-speed contracts. Core supplies model-specific validation for Low/Medium/High/XHigh, mandatory reasoning and priority processing. This integration requires Mythosia.AI 8.1.0 / Abstractions 4.1.0. No required interface members or service defaults change. See [Grok 4.7](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#grok-47).

The `AIModels.DeepSeek.V4Pro` identifier selects text-only DeepSeek V4 Pro and requires Mythosia.AI 8.1.0 / Abstractions 4.1.0. The core package also adds optional Responses execution and Files support for reusing uploaded images with Flash; V4 Pro rejects images. See [DeepSeek models, image reuse and limits](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#deepseek-deepseekservice).

Model contracts include Fable/Mythos 5.1, Gemini 3.7/3.8 Flash, Grok 4.6 with `XHigh`, DeepSeek Flash, Grok Imagine Image 2.0 and GPT Image 2.5. Perplexity Agent API contracts replace legacy Sonar-specific selections; `AIModels.Perplexity.Sonar` now identifies `perplexity/sonar`. Provider execution and validation require Mythosia.AI 8.0.0. GPT-6 Astra and optional Run/request-feature contracts were introduced in v3.1.

Snapshot fixes preserve supported JSON ownership, array bounds, standard read-only wrappers, dictionary comparers and shared references without adding a JSON dependency. See the [v4.0.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) for contract details and compatibility.

## Installation

```bash
dotnet add package Mythosia.AI.Abstractions
```

Install this package directly only when writing a **library that depends on the AI service contract** (e.g., RAG orchestration, custom middleware).
Applications normally take a transitive dependency through `Mythosia.AI`.

---

## Core Interface

### `IAIService`

The central abstraction for AI completion and streaming.

```csharp
public interface IAIService
{
    string Model { get; }
    string Provider { get; }
    string SystemMessage { get; set; }
    bool StatelessMode { get; set; }
    ChatBlock ActivateChat { get; }

    Task<string> GetCompletionAsync(
        string prompt,
        AIRequestProfile? profile = null,
        AIRequestContext? context = null,
        CancellationToken cancellationToken = default);

    Task<string> GetCompletionAsync(
        Message message,
        AIRequestProfile? profile = null,
        AIRequestContext? context = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(
        Message message,
        AIRequestContext? context = null,
        CancellationToken ct = default);

    IAsyncEnumerable<StreamingContent> StreamAsync(
        string prompt,
        StreamOptions options,
        CancellationToken ct = default);

    IAsyncEnumerable<StreamingContent> StreamAsync(
        Message message,
        StreamOptions options,
        AIRequestContext? context = null,
        CancellationToken ct = default);
}
```

All concrete providers (`OpenAIService`, `AnthropicService`, `GoogleAIService`, etc.) in `Mythosia.AI` implement this interface.

---

### `IAIRunService` and `AIRun`

Libraries that need to display or cancel ongoing work can depend on `IAIRunService` without referencing a concrete provider. See the [Run guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md) for application examples.

To adjust effort for a task or ground an answer in hosted sources without coupling middleware to a provider, use optional `IAIRequestFeatureService`. Its `WithReasoning`, `WithWebSearch` and `WithFileSearch` extensions retain the concrete service type, copy settings for the next logical request, and reject unsupported capabilities explicitly. `AICitation`, `StreamingContent.Citation` and `AIRun.Citations` carry provider source references independently of text observation. This optional request-feature capability adds no required `IAIService` members; the [completion signature migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation-migration) still applies in v4.0.0. See [reasoning and search](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/reasoning-and-search.md) for scope, provider support and citation indexing.

Run startup remains an optional capability and adds no mandatory `IAIService` members of its own. The separate [completion signature migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation-migration) applies to ordinary completion. It exposes `StartRunAsync` overloads for string and `Message` input with `onText`, streaming options, request context, and cancellation.

```csharp
using Mythosia.AI.Services;

if (service is IAIRunService runService)
{
    await using var run = await runService.StartRunAsync(
        "Summarize the documents.",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
    string answer = (await run.Result).Text;
}
```

`RequestedModel` is the single explicit model sent in the request, captured at startup, including a provider model override. It is `null` when a preset, profile, or server-side model routing selects the model without a single explicit model field (for example, a Perplexity `Models` list). This is independent of the actual response model in `Model`.

`AIRun` is in `Mythosia.AI.Models.Runs`. It exposes `Result`, output-only `StreamAsync`, `CanSteer`, `SteerAsync`, `Cancel`, and `DisposeAsync`. A single event reader may accompany the text callback. Execution and result collection continue without an event reader; see the [run guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md) for buffering and lifetime contracts.

### `IImageGenerationService`

Use this capability to create visual drafts or revise reference images while retaining the same application-facing request and result types across OpenAI, Google, and xAI. It is optional rather than part of the LLM-focused `IAIService` contract, so consumers do not have to assume that every chat provider generates images.

For fast visual drafts or precise revisions, select `AIModels.OpenAI.GptImage2_5Flare` or `GptImage2_5Sunburst` explicitly through `ImageGenerationRequest.Model` or `ImageEditRequest.Model`. `GptImage2_5Flare_260908` and `GptImage2_5Sunburst_260908` pin the September 8, 2026 snapshots. The existing request/result types and method signatures are reused; OpenAI's default remains `GptImage2`. Model-specific quality, transparency, size, and input validation belongs to the provider implementation. See the [GPT Image 2.5 guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#gpt-image-25).

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;

if (service is IImageGenerationService imageService)
{
    ImageGenerationResult result = await imageService.GenerateImagesAsync(
        new ImageGenerationRequest
        {
            Prompt = "A glass pavilion at sunrise",
            Count = 1,
            Size = ImageSize.Auto,
            OutputFormat = ImageOutputFormat.Auto
        });

    IReadOnlyList<GeneratedImage> images = result.Images;
}
```

`DefaultImageModel` is independent from `IAIService.Model`. xAI defaults to `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`) from `Mythosia.AI` 8.0.0. `ImageEditRequest` adds ordered `InputImages` and an optional `Mask`; provider support varies. OpenAI supports mask editing. Gemini accepts reference images but rejects a separate mask and requires `Count = 1`. xAI accepts 1–10 outputs and 1–5 JPEG/PNG/WebP reference images, without a separate mask.

For autocomplete and clear sizing intent, use `ImageQuality`, `ImageBackground`, `ImageOutputFormat`, and immutable `ImageSize`. `ImageSize.Pixels(width, height)` requests exact dimensions; `ImageSize.Preset(resolution, aspectRatio)` requests a resolution grade and optional ratio. The separate request `AspectRatio` property is removed. This is a breaking change; see [before/after migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#image-options-migration). OpenAI accepts `Auto`/`Pixels`; Google and xAI accept `Auto`/`Preset`. Unsupported modes fail before HTTP instead of silently approximating pixels.

The shared output-format default is now `ImageOutputFormat.Auto`: OpenAI resolves it to PNG, while Google and xAI use provider-selected output. OpenAI also accepts `Png`, `Jpeg`, and `WebP`; Google accepts explicit `Jpeg` only; xAI rejects every explicit format because its API cannot select a codec. Read `GeneratedImage.MediaType` and save with the matching extension. xAI rejects explicit compression and non-`Auto` backgrounds and accepts `ImageQuality.Auto`, `Low`, and `Medium`. Validation belongs to the provider implementation; the contracts package adds no image codec or provider SDK.

---

## Models

| Type | Description |
| --- | --- |
| `Message` | A conversation message with role, content, and optional multimodal content |
| `MessageContent` | Base class for multimodal content (`TextContent`, `ImageContent`, `AudioContent`) |
| `ChatBlock` | Conversation container holding system message and message history |
| `ActorRole` | Message role enum (`System`, `User`, `Assistant`, `Function`) |
| `AIRequestContext` | Per-request context overrides (system message prefix/suffix, message override) |
| `AIRequestProfile` | Per-request parameter overrides (temperature, max tokens, stateless mode) |
| `AIModels` | Provider model identifiers, including `AIModels.Anthropic.ClaudeOpus5_5`, `ClaudeFable5_1`, `ClaudeMythos5_1`, GPT-6 Astra / Sol / Luna, GPT-5.6, and current xAI aliases |
| `ClaudeThinkingDisplay` | `Omitted`, `Summarized`, or `Updates`; 5.1 progress updates keep reasoning hidden |
| `ClaudeThinkingPrefixMismatchBehavior` | `Error` or `DropBlock` for the provider's handling of thinking bound to a changed conversation |
| `ClaudeInputTransformation` | Provider-reported thinking changes: `Type`, `Path`, `Reason`, `ResponseId`, and `Model` |
| `Gpt6Reasoning` | GPT-6 effort (`Auto`, `Low`, `Medium`, `High`, `XHigh`, `Max`, plus `None`); `None` is supported by Sol/Luna, not Astra; existing numeric values remain unchanged |
| `Gpt6ReasoningMode` | Standard or Pro reasoning execution on the same selected GPT-6 model ID |
| `Gpt5_6Reasoning` | GPT-5.6 reasoning effort (`Auto`, `None`, `Low`, `Medium`, `High`, `XHigh`, `Max`) |
| `Gpt5_6ReasoningMode` | Standard or Pro reasoning execution; Pro is a request mode, not a separate GPT-5.6 model ID |
| `GrokReasoning` | xAI reasoning effort (`Auto`, `None`, `Low`, `Medium`, `High`, `XHigh`); Grok 4.6 adds `XHigh` and cannot disable reasoning; valid levels depend on the selected model |
| `DeepSeekReasoning` | Native effort (`Auto`, `Low`, `High`, `Max`); thinking activation is separate, while `WithDeepSeekReasoning` enables it. Common `ReasoningLevel` also supports `None` and the provider's documented level mappings |
| `AIProvider` | Provider enum (`OpenAI`, `Anthropic`, `Google`, `xAI`, `DeepSeek`, `Perplexity`) |
| `ImageGenerationRequest` | Provider-neutral prompt, typed size, quality, background, and output controls for generating one or more images; supported values depend on the provider |
| `ImageQuality` / `ImageBackground` / `ImageOutputFormat` | Named image options; provider/model support is validated before HTTP |
| `ImageSize` | Immutable `Auto`, exact `Pixels`, or resolution-and-ratio `Preset` intent |
| `ImageResolution` / `ImageAspectRatio` | Resolution grades and ratios for `ImageSize.Preset`; supported values vary by model |
| `ImageEditRequest` | Image-generation request with ordered reference images and an optional mask |
| `ImageInput` | Binary image input with MIME type and file name |
| `GeneratedImage` | Generated bytes, MIME type, optional URL, and revised prompt |
| `ImageGenerationResult` | Images plus provider, model, request ID, and optional token usage |

## Streaming

| Type | Description |
| --- | --- |
| `StreamingContent` | Streaming chunk with content, type, metadata, token usage, and round information |
| `StreamingContentType` | Chunk type enum (`Text`, `Reasoning`, `FunctionCall`, `FunctionResult`, `Status`, `Error`, `Completion`, `RoundUsage`) |
| `StreamOptions` | Streaming behavior options (metadata, function calls, reasoning) |
| `TokenUsage` | Token count data (input, output, cached input, cache creation, reasoning) |
| `StreamDiagnostics` | SSE round observability snapshot — lines read, accumulated chars, last raw line, elapsed time |
| `StreamDiagnosticsBuilder` | Fluent configurator for service-level streaming diagnostics; consumed by `Mythosia.AI`'s `WithStreamDiagnostics(d => d.OnRawLine(...).OnComplete(...))` |

## Functions

| Type | Description |
| --- | --- |
| `FunctionDefinition` | Function schema with optional `AllowAsync` permission (default `false`) |
| `FunctionCall` | One typed provider function call with ID, order, arguments, provider metadata, and actual provider `IsAsync` status |
| `FunctionCallBatch` | Ordered calls returned by one assistant response |
| `FunctionCallResult` | Output or isolated error for one call |
| `FunctionCallResultBatch` | Results correlated to one function-call batch; native async delivery can be partial |
| `FunctionCallingPolicy` | Controls function calling behavior and iteration limits |
| `FunctionExecutionMode` | Selects sequential or bounded-parallel execution for ordinary calls; opted-in async jobs use a separate `MaxConcurrency` limit |
| `AiFunctionAttribute` | Marks a method as an AI-callable function, with optional `AllowAsync` permission (default `false`) |
| `AiParameterAttribute` | Describes a function parameter for the AI |

When a slow lookup leaves room for independent model work, such as giving general advice while waiting for a forecast, `AllowAsync` permits the two to overlap on a supporting provider, model, and API. The implementation enables it for GPT-6 Astra / Sol / Luna through Responses; other connections omit the API option and wait for the same handler's result. The permission is preserved when switching models. `FunctionCall.IsAsync` records the provider's actual call status, so enabling the permission does not guarantee async execution. In `Mythosia.AI`, `FunctionBuilder.WithAsync()` is the fluent equivalent of `AllowAsync = true`.

This is separate from `Task`-returning handlers and `FunctionExecutionMode.Parallel`. Pending function jobs belong to the existing completion or streaming request; they are completed and cleaned up before that request ends. Cancellation-aware handlers receive the execution token through `HandlerWithCancellation`; started handlers that ignore cancellation are still awaited during cleanup. Calls not yet started are skipped with matching cancelled results. Calling the legacy `Handler` delegate directly uses `CancellationToken.None`.

## Exceptions

| Type | Description |
| --- | --- |
| `AIServiceException` | Base exception for AI service errors |
| `AgentMaxStepsExceededException` | Thrown when agent exceeds maximum iteration steps |
| `ContextLengthExceededException` | Provider context-window rejection with recovery metadata when available |
| `StreamReadException` | Thrown when an SSE read fails (transport error, premature stream end, etc.). Wraps the underlying exception in `InnerException` and attaches a `StreamDiagnostics` snapshot via the `Diagnostics` property |

---

## Relationship to Microsoft.Extensions.AI

`IAIService` is Mythosia.AI's provider-neutral contract and is independent from `Microsoft.Extensions.AI.IChatClient`. It exposes Mythosia-specific stateful sessions (`ChatBlock`), request profiles and contexts, typed streaming events, and the built-in multi-round function loop. This package does not implement or reference `IChatClient`, and the two interfaces are not implicitly interchangeable.

Applications that use both ecosystems should put an explicit adapter at their integration boundary and decide how message history, tool execution, streaming metadata, and usage are mapped. Keeping that conversion explicit avoids silently losing semantics when either abstraction evolves.

---

## Why This Package?

```
Mythosia.AI.Rag  →  Mythosia.AI.Abstractions  (no provider SDK dependencies)
                     instead of
                     Mythosia.AI  (Azure.AI.OpenAI, NJsonSchema, TiktokenSharp, ...)
```

By depending on abstractions rather than the full implementation package, libraries like `Mythosia.AI.Rag` avoid pulling in provider-specific dependencies. The concrete provider is chosen by the final application.

---

## Links

- [Mythosia.AI (implementation)](https://www.nuget.org/packages/Mythosia.AI)
- [GitHub](https://github.com/AJ-comp/Mythosia.AI)
- [Documentation](https://aj-comp.github.io/Mythosia.AI/)
- [v4.1.0 Release Notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v410)
