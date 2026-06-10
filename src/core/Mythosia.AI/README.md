# Mythosia.AI

> ⚠️ **Upgrading from v5.x?** See the **[v6.0 Migration Guide](RELEASE_NOTES.md#migration-from-v5x)**.

## Package Summary

The `Mythosia.AI` library provides a unified interface for various AI models with **multimodal support**, **function calling**, **reasoning streaming**, **round-level token usage**, and **advanced streaming capabilities**.

### Supported Providers

- **OpenAI** — GPT-5.5 / 5.5 Pro / 5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro / 5.3 Codex / 5.2 / 5.2 Pro / 5.2 Codex / 5.1 / 5 / 5 Pro (with reasoning), GPT-4.1, GPT-4o, o3
- **Anthropic** — Claude Fable 5, Opus 4.8 / 4.7 / 4.6 / 4.5 / 4.1 / 4, Sonnet 4.6 / 4.5, Haiku 4.5
- **Google** — Gemini 3.1 Pro Preview, Gemini 3.5 Flash, Gemini 3 Flash Preview, Gemini 3.1 Flash-Lite, Gemini 2.5 Pro/Flash/Flash-Lite
- **DeepSeek** — Chat and Reasoner models
- **xAI** — Grok 4.3, Grok 4.20 (reasoning / non-reasoning), Grok Build 0.1, Grok 3 Mini
- **Perplexity** — Sonar / Sonar Pro / Sonar Reasoning Pro with web search and citations

## 📚 Documentation

- **[Basic Usage Guide](https://github.com/AJ-comp/Mythosia.AI/wiki)** — Getting started with text queries, streaming, image analysis, and more
- **[Advanced Features](https://github.com/AJ-comp/Mythosia.AI/wiki/Advanced-Features)** — Function calling, policies, and enhanced streaming
- **[Release Notes](RELEASE_NOTES.md)** — Full version history and migration guides
- **[Relationship to Microsoft.Extensions.AI](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Abstractions#relationship-to-microsoftextensionsai)** — How IAIService and IChatClient differ

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
// OpenAI GPT
var gptService = new OpenAIService(apiKey, httpClient);
var response = await gptService.GetCompletionAsync("Hello!");

// Anthropic Claude
var claudeService = new AnthropicService(apiKey, httpClient);
var response = await claudeService.GetCompletionAsync("Hello!");

// Google Gemini
var geminiService = new GoogleAIService(apiKey, httpClient);
geminiService.ChangeModel(AIModels.Google.Gemini3FlashPreview);
var response = await geminiService.GetCompletionAsync("Hello!");
```

## `AIModels` Catalog

Model selection is now documented around provider-grouped string constants via `AIModels`.

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.ChangeModel(AIModels.Anthropic.ClaudeSonnet4_6);
service.ChangeModel(AIModels.Google.Gemini3FlashPreview);
```

## Static Quick Helpers

For simple stateless usage, use `AIService` static helpers.

```csharp
var answer = await AIService.QuickAskAsync(apiKey, "Summarize this text.");
var vision = await AIService.QuickAskWithImageAsync(apiKey, "Describe this image.", imagePath);
```

## GPT-5 Family Configuration

GPT-5 family models (GPT-5 / 5.1 / 5.2 / 5.3 / 5.4 / 5.5) support **type-safe reasoning configuration** with per-model enums.

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
```

`Auto` uses the model-appropriate default (e.g., Medium for GPT-5, None for GPT-5.1/5.2, Medium for GPT-5.2 Pro/Codex, Medium for GPT-5.3 Codex, None for GPT-5.4, Medium for GPT-5.4 Pro, None for GPT-5.5, Medium for GPT-5.5 Pro). The `-pro` variants reject `None`/`Low` and are clamped up to `Medium`.

### Reasoning Summary

All GPT-5 family models support `ReasoningSummary` enum (`Auto` / `Concise` / `Detailed`). Set to `null` to disable.

## Gemini Configuration

### Gemini 3 — ThinkingLevel

```csharp
var geminiService = new GoogleAIService(apiKey, httpClient);
geminiService.ChangeModel(AIModels.Google.Gemini3FlashPreview);

// GeminiThinkingLevel enum: Auto / Minimal / Low / Medium / High
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;  // Auto = model default (High)
```

### Gemini 2.5 — ThinkingBudget

```csharp
geminiService.ChangeModel(AIModels.Google.Gemini2_5Pro);
geminiService.ThinkingBudget = 8192;  // -1 = dynamic (default), 0 = disable
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
grokService.ChangeModel(AIModels.xAI.Grok3Mini);

// GrokReasoning enum: Off / Low / High
grokService.WithGrokParameters(reasoningEffort: GrokReasoning.High);
```

> **Note:** Only `grok-3-mini` supports the `reasoning_effort` API parameter. Other Grok models ignore it.

### Reasoning Content Streaming

Grok reasoning models (`grok-3-mini`, `grok-4.3`) stream `reasoning_content` when reasoning is enabled:

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
    new AIRequestContext
    {
        RequestMessageOverride = new Message(ActorRole.User, rewrittenQuery)
    });
```

Example — injecting retrieved RAG context as a suffix on the system message, without leaking it into conversation history:

```csharp
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
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
    MaxConcurrency = 5,
    EnableLogging = true  // Enable debug output
};

// Per-request policy override
var response = await service
    .WithPolicy(FunctionCallingPolicy.Fast)
    .GetCompletionAsync("Complex task requiring functions");

// Inline policy configuration
var response = await service
    .BeginMessage()
    .AddText("Analyze this data")
    .WithMaxRounds(5)
    .WithTimeout(60)
    .SendAsync();
```

### Function Calling with Streaming

```csharp
// Stream with function calling support
await foreach (var content in service.StreamAsync(
    "What's the weather in Seoul and calculate 15% tip on $85",
    StreamOptions.WithFunctions))
{
    if (content.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {content.Metadata["function_name"]}");
    }
    else if (content.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function completed: {content.Metadata["status"]}");
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
- **Backward compatible** — `ConversationPolicy` defaults to `null`; existing behavior is unchanged
- **Provider-agnostic** — Works with all providers (OpenAI, Claude, Gemini, Grok, DeepSeek, Perplexity)
- **Incremental summarization** — When re-summarizing, existing summary is included as context for the new summary

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
| `CacheCreationTokens` | Tokens written to cache | Claude |
| `ReasoningTokens` | Internal reasoning tokens | OpenAI, Gemini |

Computed properties: `NonCachedInputTokens`, `CacheHitRatio`, `HasCacheActivity`, `VisibleOutputTokens`.

## Reasoning Streaming

GPT-5, Gemini 3, and Grok reasoning models support streaming reasoning (thinking) content.

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
| **OpenAI GPT-5.5 / 5.5 Pro / 5 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.4 / 5.4 Pro** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.3 Codex** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.2 / 5.2 Pro / 5.2 Codex** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.1** | ✅ | ✅ | ✅ | Reasoning + verbosity control |
| **OpenAI GPT-5 / Mini / Nano** | ✅ | ✅ | ✅ | Reasoning streaming + summary |
| **OpenAI GPT-4.1 / GPT-4o** | ✅ | ✅ | — | Full function support |
| **OpenAI o3 / o3-pro** | ✅ | ✅ | ✅ | Advanced reasoning |
| **Claude Fable 5** | ✅ | ✅ | ✅ | Adaptive thinking + tool use |
| **Claude Opus 4.8 / 4.7 / 4.6 / 4.5 / 4.1 / 4** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Sonnet 4.6 / 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Haiku 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Gemini 3.1 Pro / 3.5 Flash / 3 Flash / 3.1 Flash-Lite** | ✅ | ✅ | ✅ | ThinkingLevel + thought signatures |
| **Gemini 2.5 Pro/Flash** | ✅ | ✅ | ✅ | ThinkingBudget control |
| **xAI Grok 4.3 / 4.20 / Build 0.1 / 3 Mini** | ✅ | ✅ | ✅ | `GrokReasoning` effort + reasoning streaming |
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

---

## 📋 TODO — Unsupported Models (Planned)

The following OpenAI models are **not yet supported** due to significant API differences:

| Model | API Name | Status | Notes |
|-------|----------|--------|-------|
| GPT-5.2 Instant | `gpt-5.2-chat-latest` | ⏳ Planned | ChatGPT-optimized model; uses a different routing/parameter set than standard Responses API models |
| GPT-5.3 Instant | `gpt-5.3-chat-latest` | ⏳ Planned | ChatGPT-optimized model; same API constraints as GPT-5.2 Instant |
| GPT-5.3 Codex Spark | `gpt-5.3-codex-spark` | ⏳ Planned | Research preview; completely different infrastructure (Cerebras-powered, WebSocket-based, text-only) |

### Why are these models different?

**`chat-latest` models (Instant)**
- These are ChatGPT-internal models exposed to the API. OpenAI recommends using the standard models (e.g., `gpt-5.2`, `gpt-5.3-codex`) for API usage instead.
- They do not support the full set of Responses API parameters such as `reasoning.effort`, `text.verbosity`, and other model-specific configurations.
- Response format and content structure may differ from standard models.

**`gpt-5.3-codex-spark`**
- Research preview available only to ChatGPT Pro subscribers.
- Powered by Cerebras inference hardware for near-instant responses.
- Uses persistent **WebSocket connections** and an optimized Responses API — a fundamentally different transport layer than the standard HTTP-based request/response pattern.
- Text-only (no multimodal support).
- Designed specifically for real-time coding iteration within Codex, not general-purpose API usage.
