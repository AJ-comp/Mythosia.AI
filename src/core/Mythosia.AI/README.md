# Mythosia.AI

> **Upgrading to v7?** `MaxMessageCount`, `ChatBlock.RemoveFunctionMessages()`, `GenerateImageAsync`, and `GenerateImageUrlAsync` were removed, and custom provider function-extraction extension points now use typed batches. See the **[v7.0 release notes and migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v700)**.

> ⚠️ **Upgrading from v5.x?** Read the **[v6.0 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#migration-from-v5x)** first, then apply the v7 migration above.

## Package Summary

The `Mythosia.AI` library provides a unified interface for various AI models with **multimodal support**, **OpenAI and Gemini image generation and editing**, **function calling**, **reasoning streaming**, **round-level token usage**, **automatic context-overflow recovery**, and **advanced streaming capabilities**.

### Supported Providers

- **OpenAI** — GPT-5.6 alias / Sol / Terra / Luna, GPT-5.5 / 5.5 Pro, GPT-5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro, GPT-5.3 Codex, GPT-5.2 / 5.2 Pro, GPT-5.1, GPT-5 / 5 Mini / 5 Nano / 5 Pro, GPT-4.1 / 4.1 Mini, GPT-4o / 4o Mini, o3 / o3 Pro
- **Anthropic** — Claude Fable 5, Mythos 5 (limited), Opus 5 / 4.8 / 4.7 / 4.6 / 4.5, Sonnet 5 / 4.6 / 4.5, Haiku 4.5
- **Google** — Gemini 3.6 Flash, Gemini 3.5 Flash/Flash-Lite, Gemini 3.1 Pro Preview/Flash-Lite, Gemini 3 Flash Preview, Gemini 2.5 Pro/Flash/Flash-Lite, Gemini 3.1 Flash Image, Gemini 3.1 Flash-Lite Image, and Gemini 3 Pro Image
- **DeepSeek** — Chat and Reasoner models
- **xAI** — Grok 4.5 (default), Grok 4.3, Grok 4.20 (reasoning / non-reasoning), Grok Build
- **Perplexity** — Sonar / Sonar Pro / Sonar Reasoning Pro with web search and citations

## 📚 Documentation

- **[Getting Started](https://aj-comp.github.io/Mythosia.AI/docs/getting-started.html)** — Installation, provider setup, and first completion
- **[Function Calling](https://aj-comp.github.io/Mythosia.AI/docs/function-calling.html)** — Registration, execution policy, and streaming events
- **[Release Notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md)** — Full version history and migration guides
- **[Relationship to Microsoft.Extensions.AI](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Abstractions#relationship-to-microsoftextensionsai)** — How IAIService and IChatClient differ

> Claude Fable 5 and Claude Mythos 5 require 30-day data retention and cannot use zero-data-retention arrangements. Adaptive thinking is always on; a reasoning-off request is represented by low effort with readable reasoning omitted. Mythos 5 is limited to approved Project Glasswing customers.

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

## Image Generation and Editing

`OpenAIService` and `GoogleAIService` implement the optional `IImageGenerationService` contract. OpenAI defaults image requests to `AIModels.OpenAI.GptImage2` (`gpt-image-2`); Google defaults them to `AIModels.Google.Images.Gemini3_1FlashImage` (`gemini-3.1-flash-image`). The image model is independent from the service's chat `Model` and can be overridden per request.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);

var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A futuristic city at night",
    Count = 2,
    Size = "1024x1024",
    Quality = "high",
    OutputFormat = "png"
});

await File.WriteAllBytesAsync("city.png", generated.Images[0].Data);

var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "Add warm interior lighting",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("building.png"), "image/png", "building.png")
    }
});
```

For Gemini image generation or reference-image editing, construct the capability from the Google provider instead. Gemini accepts one requested output per call and has no separate mask input.

```csharp
IImageGenerationService geminiImages = new GoogleAIService(geminiApiKey, httpClient);

var generated = await geminiImages.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A precise architectural facade study",
    Model = AIModels.Google.Images.Gemini3_1FlashImage,
    Size = "2K",
    OutputFormat = "jpeg"
});
```

Gemini GenerateContent currently exposes an explicit JPEG output selector but no PNG selector. Use `OutputFormat = "jpeg"` when the format must be deterministic. For `png` or `auto`, Gemini selects the wire format; always use each returned `GeneratedImage.MediaType` as the authoritative format for `GeneratedImage.Data`.

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
| `GrokReasoning.Off` | Use `Auto` to omit `reasoning_effort`, or `None` to disable reasoning on Grok 4.3; Grok 4.5 cannot disable reasoning |
| `AIModels.xAI.Grok3Mini` / `XAIService.UseMiniModel()` | Select Grok 4.3 for configurable reasoning or Grok 4.5 for the current flagship |
| Retired OpenAI and Claude snapshot constants | Select a current constant from `AIModels`; see the release notes for the complete removal list |
| Implicit `gpt-image-1` default | Use the independent `IImageGenerationService.DefaultImageModel`, which defaults to GPT Image 2 on OpenAI |

## `AIModels` Catalog

Model selection is now documented around provider-grouped string constants via `AIModels`.

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.ChangeModel(AIModels.Anthropic.ClaudeSonnet4_6);
service.ChangeModel(AIModels.Google.Gemini3_6Flash);
service.ChangeModel(AIModels.xAI.Grok4_5);
```

`AIModels.OpenAI.Gpt5_6` is the rolling GPT-5.6 alias and currently routes to Sol. Use `Gpt5_6Sol` for an explicit flagship-capability selection, `Gpt5_6Terra` for strong performance at a lower price, or `Gpt5_6Luna` for efficient high-volume workloads.

## Static Quick Helpers

For simple stateless usage, use `AIService` static helpers.

```csharp
var answer = await AIService.QuickAskAsync(apiKey, "Summarize this text.");
var vision = await AIService.QuickAskWithImageAsync(apiKey, "Describe this image.", imagePath);
```

## GPT-5 Family Configuration

GPT-5 family models (GPT-5 / 5.1 / 5.2 / 5.3 Codex / 5.4 / 5.5 / 5.6) support **type-safe reasoning configuration** with per-model enums.

### Reasoning Effort (Per-Model Enums)

Each GPT-5 variant has its own enum to ensure only valid options are available at compile time.

```csharp
var gptService = (OpenAIService)service;

// GPT-5: Gpt5Reasoning (Auto/Minimal/Low/Medium/High)
gptService.WithGpt5Parameters(
    reasoningEffort: Gpt5Reasoning.High,
    reasoningSummary: ReasoningSummary.Concise);

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

GPT-5.6 requests use `reasoning.context: "current_turn"` because Mythosia rebuilds conversation history locally instead of relying on `previous_response_id`. During tool calls, the original reasoning and function output items are replayed within the active turn.

For OpenAI Responses API calls, Mythosia consumes output and executes tools only after a top-level `status: "completed"`. Failed, incomplete, refused, malformed, or prematurely ended responses surface as errors; collected function calls are discarded before a handler can run. Function-call requests preserve multimodal message parts and image detail, structured-output `text.format`, forced function selection, and each parameter's declared required/optional contract. Empty, malformed, or non-object function arguments also fail before handler execution.

### Reasoning Summary

All GPT-5 family models support `ReasoningSummary` enum (`Auto` / `Concise` / `Detailed`). Set to `null` to disable.

## Gemini Configuration

### Gemini 3 — ThinkingLevel

```csharp
var geminiService = new GoogleAIService(apiKey, httpClient);
geminiService.ChangeModel(AIModels.Google.Gemini3_6Flash);

// GeminiThinkingLevel enum: Auto / Minimal / Low / Medium / High
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;
```

Gemini 3 thinking cannot be fully disabled. `Auto` omits the override and keeps the selected model's provider default: Medium for Gemini 3.6 Flash and Gemini 3.5 Flash, Minimal for Gemini 3.5 Flash-Lite, and High for the current preview models. Gemini 3 Pro models reject `Minimal`; their floor is `Low`.

Gemini 3.6 Flash and Gemini 3.5 Flash-Lite use the latest request contract, so Mythosia omits legacy `temperature`, `topP`, `topK`, and `candidateCount` fields for those models. Other Gemini 3 models omit `candidateCount` while retaining their supported sampling controls.

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

When streaming with `StreamOptions.WithReasoning()`, Mythosia.AI now requests Gemini thought chunks (`includeThoughts: true`) and emits them as `StreamingContentType.Reasoning`.

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

```csharp
var grokService = new XAIService(apiKey, httpClient);
grokService.ChangeModel(AIModels.xAI.Grok4_5);

// Auto omits the parameter; Grok 4.5 accepts Low / Medium / High.
grokService.WithGrokParameters(reasoningEffort: GrokReasoning.Medium);
```

`GrokReasoning` is `Auto`, `None`, `Low`, `Medium`, or `High`. Grok 4.3 accepts `None` through `High`; Grok 4.5 accepts `Low` through `High`, defaults to `High` when `Auto` omits the parameter, and cannot disable reasoning. Unsupported model/effort combinations fail before a request is sent.

### Reasoning Content Streaming

Grok 4.5 can stream summarized `reasoning_content` when reasoning output is enabled:

```csharp
await foreach (var content in grokService.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Think] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

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
| `RequestMessageOverride` | Completely replaces the user message sent to the model while the original prompt stays in chat history |

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

When the same dynamic data (today's date, active folder, session info) must be injected on **every** LLM call, passing an `AIRequestContext` at every entry point gets tedious and error-prone. `AIService.SystemMessageProvider` lets you register a callback once, and every outbound call (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) automatically invokes it to build a baseline context.

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
var agentResult = await service.RunAgentAsync(goal);
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

Streaming paths (`StreamAsync`, `RunAgentStreamAsync`) forward the caller's `CancellationToken` through to the async provider. Non-streaming paths (`GetCompletionAsync`, `RunAgentAsync`) do not support cancellation — use the streaming counterparts if your provider needs to be cancellable.

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

`Sequential` is the default for local handler scheduling and preserves one-at-a-time
execution. The provider response and conversation history still retain the complete
multi-call batch introduced in v7.
`Parallel` runs calls from the same provider response concurrently up to
`MaxConcurrency`, then returns their results in the original provider order. Use
parallel execution only for independent, thread-safe handlers.

`TimeoutSeconds` controls provider requests and the surrounding round loop. Once a
validated handler batch starts, the batch is completed to keep call/result history
consistent. Registered handlers do not currently receive a `CancellationToken`, so
an already-running handler is not interrupted by request cancellation or timeout.
Streaming uses one timeout for the complete round loop, including response headers
and the SSE body. Policy expiry raises `AIServiceException`; cancelling the token
passed to `StreamAsync` remains an `OperationCanceledException` with the caller token.

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

### ReAct Agent Helpers

```csharp
// Non-streaming agent helper
var answer = await service.RunAgentAsync(
    "Find the weather in Seoul and explain what to wear today."
);

// Streaming agent helper
await foreach (var content in service.RunAgentStreamAsync(
    "Find the weather in Seoul and explain what to wear today.",
    maxSteps: 10))
{
    if (content.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling: {content.Metadata["function_name"]}");
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

`RunAgentStreamAsync(...)` is the streaming counterpart to `RunAgentAsync(...)`. It keeps function calling enabled for the request and disables `TextOnly` so agent runs can emit function call, function result, and completion events.

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

Automatically summarize old conversation messages when the conversation exceeds a configured threshold. The summary is stored and injected into the system message on each subsequent LLM request.

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

See the [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md) for the full behavior, including per-provider limitations (DeepSeek/Perplexity recover only on the non-streaming path).

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

await foreach (var content in service.RunAgentStreamAsync(
    "Find the weather in Seoul and answer briefly.",
    maxSteps: 10))
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
- `RoundUsage.Usage.TotalTokens` is normalized to `InputTokens + OutputTokens`.
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

GPT-5/o3, Claude, Gemini 3, Grok, and DeepSeek reasoning models support streaming reasoning (thinking) content.

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
| **OpenAI GPT-5.6 Sol / Terra / Luna** | ✅ | ✅ | ✅ | `Max` effort, verbosity, summaries, optional Pro reasoning mode |
| **OpenAI GPT-5.5 / 5.5 Pro / 5 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.3 Codex** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.2 / 5.2 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.1** | ✅ | ✅ | ✅ | Reasoning + verbosity control |
| **OpenAI GPT-5 / Mini / Nano** | ✅ | ✅ | ✅ | Reasoning streaming + summary |
| **OpenAI GPT-4.1 / 4.1 Mini / GPT-4o / 4o Mini** | ✅ | ✅ | — | Full function support |
| **OpenAI o3 / o3-pro** | ✅ | ✅ | ✅ | Advanced reasoning |
| **Claude Fable 5** | ✅ | ✅ | ✅ | Adaptive thinking + tool use |
| **Claude Mythos 5** | ✅ | ✅ | ✅ | Limited availability; always-on adaptive thinking + tool use |
| **Claude Opus 5 / Sonnet 5** | ✅ | ✅ | ✅ | Adaptive thinking + signed tool continuation |
| **Claude Opus 4.8 / 4.7 / 4.6 / 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Sonnet 4.6 / 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Haiku 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Gemini 3.6 Flash / 3.5 Flash / 3.5 Flash-Lite / current Gemini 3 previews** | ✅ | ✅ | ✅ | ThinkingLevel + thought signatures |
| **Gemini 2.5 Pro / Flash / Flash-Lite** | ✅ | ✅ | ✅ | ThinkingBudget control |
| **xAI Grok 4.5 / 4.3 / 4.20 / Build** | ✅ | ✅ | ✅ | Model-specific `GrokReasoning` effort + reasoning streaming |
| **DeepSeek** | ❌ | ✅ | ✅ | Reasoner model streaming |
| **Perplexity** | ❌ | ✅ | — | Web search + citations |

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

2. **Error Handling**: Functions should return meaningful error messages that the AI can understand.

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
