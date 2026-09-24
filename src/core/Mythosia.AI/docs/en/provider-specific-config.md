# Provider-Specific Configuration Architecture

> GPT-6 Sol/Luna are unreleased additions; see [model selection and requirements](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#gpt-6-sol-luna).

Need the completed answer together with usage and sources? `await run.Result` now returns an `AIRunResult` snapshot; use `result.Text` for the string. No stream reader is required. This is an API change in Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0; `GetCompletionAsync` and typed `StructuredStreamRun<T>.Result` keep their existing return types. [Run result and migration](../../../../../docs/execution-api-transition.md#run-result).


For independent settings and reusable variations, use [the request builder](../../../../../docs/request-building.md). Call `CreateRequest(...)` before `With...`; service-level setters and fluent methods retain their existing behavior.

> [Claude Fable 5.1](../../../../../docs/fable-5-1.md) adds progress updates, turn-scoped instructions, and thinking-binding diagnostics from `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 requires invitation access. Both reject forced tool choice.

> GPT-6 Astra, `AllowAsync`, `StartRunAsync`, and the common reasoning/search API are available from `Mythosia.AI` 7.1.0, with shared types in `Mythosia.AI.Abstractions` 3.1.0.

## Principle

Applications can now express task-level effort and hosted retrieval through the [common reasoning and search API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/reasoning-and-search.md). `AIRequestFeatures` is copied for one logical request; provider adapters validate and translate it, while provider-specific defaults remain on the service. Cache-preserving changes retain protocol state in the tracked conversation. `AICitation` keeps hosted source references independently of stream observation. Custom services opt in through `IAIRequestFeatureService`, without adding mandatory `IAIService` members.

| Config Type | Location | Examples |
|-------------|----------|----------|
| **Common** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty, etc. |
| **Provider-specific** | Each service class | ThinkingLevel/ThinkingBudget (Gemini), ReasoningEffort (GPT), etc. |
| **Per-function permission** | `FunctionDefinition` | `AllowAsync` (default `false`) |

`AllowAsync` is a caller-controlled permission; the service determines model/API support internally. `FunctionBuilder.WithAsync()` and `[AiFunction("lookup", "Look up data", AllowAsync = true)]` enable the same permission. GPT-6 Astra / Sol / Luna use it through Responses, while unsupported models omit the API option and wait for the same handler's result without changing the permission.

## Current Implementation: Service Level

Provider-specific settings are managed as properties of each service class.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;

geminiService.ChangeModel(AIModels.Google.Gemini3_8Flash);

// Common settings → ChatBlock
geminiService.ActivateChat.MaxTokens = 4096;

// Provider-specific settings → Service
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;
```

Use `Low` for a lighter first pass and `High` for a more demanding review; more reasoning can increase latency and token use. Both models accept `Low`, `Medium`, and `High`, but not `Minimal` or `None`. `GeminiThinkingLevel.Auto` omits the override; 3.8's provider default is `Medium`. `ThinkingLevel` sets the service baseline, while `WithReasoning(...)` overrides one logical request. The adapter omits `temperature`, `topP`, and `topK` for both models. Their provider limits are 1,048,576 input and 65,536 output tokens.

### Grok 4.6

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

Each `GeneratedImage.Data` contains decoded image bytes; use `MediaType` when choosing the file extension. The adapter requests inline base64 output and does not download provider-hosted image URLs. `Count` accepts 1–10 outputs; editing accepts 1–5 JPEG, PNG, or WebP reference images.

xAI supports only `ImageOutputFormat.Auto`, the new shared default. It has no output-codec selector and rejects explicit `Jpeg`, `Png`, and `WebP` before sending. Read `GeneratedImage.MediaType` and use the matching extension; the library does not transcode. `Quality` accepts `ImageQuality.Auto`, `Low`, or `Medium`; `Background` must be `ImageBackground.Auto`. Explicit compression and a separate `Mask` are unsupported.

Google accepts `ImageSize.Auto` or `Preset` with model-specific resolutions and ratios; see [Google model-specific image options](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#google-image-options). It accepts `ImageOutputFormat.Auto` or explicit `Jpeg`, and rejects `Png`/`WebP`. Google and xAI reject `Pixels`; OpenAI accepts `Auto`/`Pixels` and rejects `Preset`. See [migration examples](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#image-options-migration).

For Google, `Resolutions` and `AspectRatios` depend on the selected image model and also govern generation/editing validation. See the [model-specific table](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#google-image-options), including the conservative Flash-Lite 1K policy. Unsupported explicit choices fail before HTTP; custom unknown models retain `Unknown` capabilities and provider-wide option validation.

### DeepSeek Flash

Use DeepSeek Flash when a task needs a quick answer, a more careful review, or an explanation of a chart or screenshot. `AIModels.DeepSeek.Flash` (`deepseek-flash`) selects V4.1 Flash, released on September 10, 2026, with native visual understanding. The existing completion, streaming, Run, function-calling, and RAG APIs remain the entry points; support starts with `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0.

> Published `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 already include baseline Flash support. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, the Files API and `DeepSeekImageFileContent` are unreleased source additions requiring matching core and abstractions source builds; they are not included in those published packages. [Pending release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased).

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

Both models advertise 1M context and up to 384K (`393216`) output tokens; the library keeps its 8,000-token request default. Thinking omits temperature/penalties and uses `top_p` of at least 0.95; non-thinking omits `top_p`. Responses uses existing typed-output APIs for native JSON schema. Background execution, server-side `store`/`previous_response_id`, hosted web/file search, `CachePreservation.Required`, native asynchronous tools, `SteerAsync` and image generation are unsupported. Local RAG and ordinary tool rounds remain available.

### Perplexity Agent API

Use Perplexity when an answer must reflect recent information and readers need sources they can check. `PerplexityService` calls the Agent API, while independent search and embeddings let you build retrieval around your own answer model.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low,
        MaxSteps = 8
    });
string answer = await service.GetCompletionAsync("Compare the latest approaches to battery recycling and cite the sources.");
```

`WithPerplexityOptions(...)` sets persistent service options; each logical request captures a copy. Common `WithReasoning(...)` and `WithWebSearch(...)` apply to the next logical request, including client-tool rounds and typed-output repair. Internal RAG query rewriting does not inherit the final answer's search settings.

`UsePreset(...)` is a shortcut for selecting a preset. A preset/profile chooses its own model; `ModelOverride` explicitly replaces it. `DisableWebSearch` only removes the adapter's default tool and cannot promise to disable a preset's built-in search. Agent effort accepts `Minimal`, `Low`, `Medium`, `High`, `XHigh`, or `Max` when supported; `None` is rejected and direct Sonar rejects explicit effort. Internal `DisableReasoning` uses an available low effort or omits the setting, without promising reasoning is off.

Factory methods `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, and `Connector` create tool options. MCP calls execute without an approval pause; restrict `allowedTools` as needed. Connectors are a provider preview and reference an existing connected integration.

`StartBackgroundAsync` captures input without appending conversation history and rejects active local functions or `Store = false`. `GetResponseAsync` polls once; `WaitForCompletionAsync` polls until a terminal status. Save `Id` and `LastSequenceNumber`; `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` reconnects. Cancel the remote job with `CancelAsync`; cancelling a polling/reading token stops that client operation. `LastResponse` contains text, status, usage, citations, and `OutputJson`. Check the terminal status before using an answer.

The selected model determines tool, reasoning, image, and schema compatibility. Common `WithFileSearch` is not a Perplexity vector-store adapter. Sandbox-produced files, uploaded attachments, and remote MCP data are separate resources; they do not become a shared file-search store.

[Perplexity Agent API, Search, and Embeddings](../../../../../docs/perplexity.md).


### Pros
- ChatBlock remains completely provider-agnostic (clean separation)
- Follows OOP principles (each service manages its own config)
- One setting per service instance → simple structure

### Cons
- All ChatBlocks within one service share the same provider-specific settings

## When to Migrate to ChatBlock Level

If a requirement arises where **each ChatBlock needs independent provider-specific settings**, migrate by adding a lazy-initialized config class inside ChatBlock.

```csharp
// Example (not currently implemented)
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// Usage
chatBlock.Gemini.ThinkingBudget = 1024;
```

### Scenarios Requiring This
- ChatBlock A and B within a single service instance need different ThinkingBudgets
- In practice, this case is extremely rare, so service-level is maintained for now

## Decision Log

- **2026-02-12**: Initially implemented as ChatBlock-level (Option B), then rolled back to service-level. Provider-specific settings belong in their respective service classes.

## Choosing execution controls

An application may need to show progress or let a user revise a long task while it runs. `StartRunAsync` returns an `AIRun` for that task; the provider determines whether mid-turn steering is supported. Keep model settings on the service, configure them before startup, and check `run.CanSteer` before sending an instruction. See [why and when to use a run](../../../../../docs/execution-api-transition.md) for examples, cancellation, and compatibility.

## Custom provider implementation

Public service properties remain configuration defaults, even during execution. A custom `AIService` subclass must use protected request getters such as `RequestTemperature`, `RequestTopP`, `RequestMaxTokens`, `RequestSystemMessage`, `RequestModel`, and `RequestFunctions` when creating an outbound payload. Reading `Temperature` directly would read the service default and bypass a builder override. Capture additional native defaults in `CaptureRequestSettings`, call the base implementation, and copy mutable collections before storing them. Read custom values with `RequestSetting<T>`. If a provider captures a separate options object, implement `CloneProviderRequestOptions` to copy it. Legacy override entry points that bypass base execution should enter `BeginRequestSettingsScope()` and retain the existing feature scope. This is an implementation extension contract; `IAIService` gains no required members.

Custom tool-execution overrides keep the protected virtual `ProcessFunctionCallAsync(FunctionCall)` signature. Use the protected `FunctionCancellationToken` to forward execution cancellation to I/O or `HandlerWithCancellation`. Calling the legacy `Handler` delegate directly uses `CancellationToken.None`; the stock executor already selects the cancellation-aware path. See the [tool contract](../../../../../docs/function-calling.md#tool-execution-contract).

Need only the completed answer and a Stop button? Pass `cancellationToken` to `GetCompletionAsync`. Use Run for progress events or supported steering. See [completion cancellation](../../../../../docs/completions.md#completion-cancellation).

This cancellation contract is included in Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0. Application source calls that omit the token remain valid, including positional profile/context arguments, but consumers must rebuild. Custom `IAIService` implementations must append `CancellationToken cancellationToken = default` to both completion signatures and propagate it. Custom `AIService` providers retain their existing `GetCompletionAsync(Message)` override and must forward the protected `RequestCancellationToken` into their transport. Builder and Run capabilities alone did not require that interface change. Subclasses that override changed public virtual overloads for string/profile/context completion, image helpers, or `RunAgentAsync` must also append and forward the new `CancellationToken`; only the single-`Message` provider override retains its old signature. Method-group delegates targeting a changed signature may need an explicit lambda that passes or omits the token.

[Choose model controls using shared capability definitions](../../../../../docs/model-capabilities.md).

If a custom provider profile changes native mode flags, override `ApplyCapabilityRequestProfile(AIRequestProfile)` and use `SetExecutionSetting(...)` only for the flags needed by the resolver. The default hook does nothing. Common profile overrides are already captured by the builder; inspection never calls `ApplyRequestProfile` or `ApplyProviderSpecificRequestProfile`. Keep this hook free of validation, callbacks, serialization, budget reservation and changes to service or caller-owned state. Its temporary settings are restored after inspection, including when an override throws.
