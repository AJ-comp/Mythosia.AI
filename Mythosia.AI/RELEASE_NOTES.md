# Mythosia.AI - Release Notes

## 🚀 v4.5.0 - Structured Output with Auto-Recovery, Streaming & Collection Support

### **Structured Output: `GetCompletionAsync<T>()`** 🎯

Deserialize LLM responses directly into C# POCOs with automatic JSON recovery:

```csharp
var result = await service.GetCompletionAsync<WeatherResponse>("What's the weather in Seoul?");
Console.WriteLine($"{result.City}: {result.Temperature}°C, {result.Condition}");
```

#### Auto-Recovery Retry
When the LLM returns invalid JSON, Mythosia.AI automatically sends a correction prompt asking the model to fix its output. This is **not** a network retry — it's an output quality/format correction loop.

- Configurable via `StructuredOutputMaxRetries` (default: 2, meaning up to 3 total attempts)
- Correction prompt includes the previous invalid response and the parse error
- On final failure, throws `StructuredOutputException` with rich diagnostics

#### `StructuredOutputException`
Contains all context needed for debugging:

| Property | Description |
|----------|-------------|
| `TargetTypeName` | The C# type that deserialization was attempted for |
| `FirstRawResponse` | Raw LLM response from the first attempt |
| `LastRawResponse` | Raw LLM response from the last attempt |
| `ParseError` | Last JSON parse/deserialization error message |
| `AttemptCount` | Total number of attempts made |
| `SchemaJson` | The JSON schema that was sent to the LLM |

#### OpenAI-Strict JSON Schema Generation
`JsonSchemaGenerator` now produces schemas compliant with OpenAI Structured Outputs:
- All properties listed in `required` array
- `additionalProperties: false` at every level
- `definitions` → `$defs`, `$ref` paths updated
- `$schema` field removed

### **Per-Call Structured Output Policy** 🔧

Override retry behavior for a single request without changing service defaults:

```csharp
// Custom policy — applies only to this call
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

Policy is consumed after one `GetCompletionAsync<T>()` call and automatically cleared.

#### `StructuredOutputPolicy` Presets

| Preset | MaxRepairAttempts | Description |
|--------|-------------------|-------------|
| `Default` | `null` (service default) | Uses `StructuredOutputMaxRetries` |
| `NoRetry` | `0` | Single attempt, no retry |
| `Strict` | `3` | Up to 3 correction retries |

### **Streaming Structured Output: `BeginStream().As<T>()`** 🌊

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

#### Key Behaviors

- **`Result` works without `Stream()`** — just `await run.Result` internally consumes the stream and parses
- **`Stream()` is single-use** — second call throws `InvalidOperationException`
- **`Result` waits for stream completion** — even if awaited mid-stream, it won't resolve early
- **Repair retries are non-streaming** — correction prompts use `GetCompletionAsync()` for efficiency
- **Throws `StructuredOutputException`** on final failure with full diagnostics

### **Collection Support: `List<T>`, `T[]`** 📋

Both `GetCompletionAsync<T>()` and streaming support collection types as `T`:

```csharp
// Non-streaming
var items = await service.GetCompletionAsync<List<ItemDto>>(prompt);

// Streaming
var run = service.BeginStream(prompt).As<List<ItemDto>>();
await foreach (var chunk in run.Stream()) Console.Write(chunk);
List<ItemDto> items = await run.Result;
```

- `List<T>`, `T[]`, `IReadOnlyList<T>` all supported — no wrapper DTO needed
- JSON array schema auto-generated from element type
- Array extraction from markdown code blocks supported
- Empty arrays (`[]`) handled correctly

### 🧪 Test Coverage

- 32 unit tests for structured output (11 retry/policy + 4 List<T> non-streaming + 14 streaming + 3 List<T> streaming)
- Tests cover: success on first attempt, retry then success, all attempts fail, zero retries, markdown-wrapped JSON extraction, schema cleanup, policy override/consumption, stream single-use guard, result-only mode, chunk ordering, List<T> array parsing/repair

### ✅ Compatibility

- Fully backward compatible with v4.4.0
- No breaking changes
- New classes: `StructuredOutputPolicy`, `StructuredOutputException`, `StreamBuilder`, `StructuredStreamRun<T>`
- New extension methods: `WithStructuredOutputPolicy()`, `WithNoRetryStructuredOutput()`, `WithStrictStructuredOutput()`
- New entry point: `AIService.BeginStream(prompt)`

---

## 🚀 v4.4.0 - xAI Grok Provider & AIModel Enum Reordering

### **New Provider: xAI (Grok)** 🤖

Added full support for xAI's Grok models via `GrokService`:

| Model | Enum | API Identifier |
|-------|------|----------------|
| **Grok 4** | `Grok4` | `grok-4-0709` |
| **Grok 4.1 Fast** | `Grok4_1Fast` | `grok-4-1-fast` |
| **Grok 3** | `Grok3` | `grok-3` |
| **Grok 3 Mini** | `Grok3Mini` | `grok-3-mini` |

#### Features
- **Function calling** via OpenAI-compatible `tools`/`tool_calls` format
- **Streaming** with multi-round function call support
- **Vision/multimodal** support for Grok 4 models (Grok 3/3 Mini do not support image inputs)
- **Reasoning model handling** — `grok-3-mini` and `grok-4*` are reasoning models that reject `frequency_penalty`, `presence_penalty`, `stop`, and `temperature` parameters; these are automatically excluded

#### Usage Example

```csharp
var grokService = new GrokService(apiKey, new HttpClient());

// Default model: Grok 3
var response = await grokService.GetCompletionAsync("Hello from Grok!");

// Switch to Grok 4
grokService.ChangeModel(AIModel.Grok4);

// Function calling
grokService.WithFunction("get_weather", "Get current weather",
    ("city", "City name", true),
    (string city) => $"Weather in {city}: 22°C, sunny");

var result = await grokService.GetCompletionAsync("What's the weather in Seoul?");

// Streaming with function calls
await foreach (var content in grokService.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (content.Type == StreamingContentType.FunctionCall)
        Console.WriteLine($"[Calling] {content.Metadata["function_name"]}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### **AIModel Enum Reordering** 📋

- OpenAI models moved to the top of the `AIModel` enum for consistency
- Added `AIProvider.xAI` to the `AIProvider` enum
- New enum values: `Grok4`, `Grok4_1Fast`, `Grok3`, `Grok3Mini`

### 🧪 Test Updates

- **New test classes**: `xAI_Grok4_Tests`, `xAI_Grok4_1Fast_Tests`, `xAI_Grok3_Tests`, `xAI_Grok3Mini_Tests`
- Per-model `SupportsMultimodal()` — only Grok 4 models return `true`
- Per-model `GetAlternativeModel()` for conversation management tests

### ✅ Compatibility

- Fully backward compatible with v4.3.0
- **Breaking change**: `AIModel` enum values have been reordered (OpenAI first). If you persist enum integer values, update your mappings.
- New enum values: `AIModel.Grok4`, `AIModel.Grok4_1Fast`, `AIModel.Grok3`, `AIModel.Grok3Mini`
- New enum value: `AIProvider.xAI`

---

## 🚀 v4.3.0 - GPT-5.2 Codex & Claude Haiku 4.5 Extended Thinking

### **New Model: GPT-5.2 Codex** 🤖

- Added `Gpt5_2Codex` (`gpt-5.2-codex`) — Coding-optimized model for agentic coding tasks
- Reasoning effort: `low` / `medium` (default) / `high` / `xhigh` — **`none` is not supported**
- If `none` is set, automatically adjusted to `low` with a console warning
- `IsGpt5_2CodexModel()` added for Codex-specific parameter routing

### **Claude Haiku 4.5 Extended Thinking** 🧠

- Extended thinking is now supported on **Claude Haiku 4.5** (`haiku-4` model detection added to `IsExtendedThinkingModel()`)
- New **`SupportsExtendedThinking`** public property on `ClaudeService` for external callers to check thinking capability
- `ApplyThinkingConfig()` reordered: temperature set before thinking block

### 🧪 Test Improvements

- **`RunIfSupported()` error logging** — Now catches `AIServiceException` and logs `ErrorDetails` before rethrowing, improving test diagnostics
- **Streaming assertion relaxed** — `StreamingTest` now accepts Korean number words (하나, 셋, 삼) alongside digits for multilingual model responses
- **`SupportsReasoning()` refactored** — `ClaudeServiceTests` now uses `SupportsExtendedThinking` property instead of manual model string checking
- **New test class**: `OpenAI_Gpt5_2Codex_Tests`

### ✅ Compatibility

- Fully backward compatible with v4.2.0
- No breaking changes
- New enum value: `AIModel.Gpt5_2Codex`

---

## � v4.2.0 - Claude Sonnet 4.6 & Deprecated Model Cleanup

### **New Model: Claude Sonnet 4.6** ✨

- Added `ClaudeSonnet4_6` (`claude-sonnet-4-6`) with **64K max output tokens** (65,536)
- Extended thinking supported (Sonnet 4+)

### **Deprecated Claude 3.x Models Removed** 🗑️

All Claude 3.x models have been retired by Anthropic and are removed from the library:

| Model | API ID | Status |
|-------|--------|--------|
| Claude 3.7 Sonnet | `claude-3-7-sonnet-latest` | EOL (Feb 2026) |
| Claude 3.5 Haiku | `claude-3-5-haiku-20241022` | Retired (404) |
| Claude 3 Opus | `claude-3-opus-20240229` | Retired |
| Claude 3 Haiku | `claude-3-haiku-20240307` | Retired |

**Breaking changes:**
- `AIModel.Claude3_7SonnetLatest` — removed, use `AIModel.ClaudeSonnet4_250514` or newer
- `AIModel.Claude3_5Haiku241022` — removed, use `AIModel.ClaudeHaiku4_5_251001`
- `AIModel.Claude3Opus240229` — removed, no direct replacement
- `AIModel.Claude3Haiku240307` — removed, use `AIModel.ClaudeHaiku4_5_251001`
- `GetModelMaxOutputTokens()` entries for 3.x models removed
- `IsExtendedThinkingModel()` no longer references 3.7 Sonnet

---

## �🔧 v4.1.0 - Error Reporting, New Claude Models & Code Quality

### **Enhanced Error Reporting** 🚨

All AI services now include **HTTP status code and error body** in `AIServiceException`, replacing the unreliable `ReasonPhrase` which is null on HTTP/2 connections.

#### Before (v4.0.x)
```
API request failed: <none>
```

#### After (v4.1.0)
```
API request failed (400): {"type":"error","error":{"type":"invalid_request_error","message":"..."}}
```

- Applied to all services: **Claude**, **ChatGPT**, **Gemini**, **DeepSeek**, **Sonar**
- Covers both non-streaming and streaming endpoints, plus Audio, Images, TokenCount, and Search
- `AIServiceException.ErrorDetails` provides the raw API error body for programmatic inspection

### **Function Argument Conversion Fix** 🐛

Fixed `ConvertValue` to handle non-string `JsonElement` when target type is `string`. Some models (e.g., Claude Opus 4.6) send function arguments as raw JSON arrays instead of stringified JSON, which previously caused `InvalidOperationException`.

```csharp
// Before: jsonElement.GetString() throws on arrays/objects
// After: falls back to GetRawText() for non-string JsonElement
return jsonElement.ValueKind == JsonValueKind.String
    ? jsonElement.GetString()
    : jsonElement.GetRawText();
```

### **New Claude Models** 🤖

| Model | Enum | API Identifier |
|-------|------|----------------|
| **Claude Opus 4.6** | `ClaudeOpus4_6` | `claude-opus-4-6` |
| **Claude Opus 4.5** | `ClaudeOpus4_5_251101` | `claude-opus-4-5-20251101` |
| **Claude Sonnet 4.5** | `ClaudeSonnet4_5_250929` | `claude-sonnet-4-5-20250929` |
| **Claude Haiku 4.5** | `ClaudeHaiku4_5_251001` | `claude-haiku-4-5-20251001` |

### **Code Refactoring** 🏗️

#### ClaudeService
- Extracted constants: `AnthropicApiVersion`, `DefaultImageMimeType`, `SseDataPrefix`, `SseEventPrefix`
- Helper methods: `AddClaudeHeaders()`, `CreateFunctionCallMessage()`, `CreateFunctionResultMessage()`, `ApplySystemMessage()`
- Unified `ProcessMultipleToolUses` loop (removed first/rest duplication)

#### GeminiService
- `SendAndReadAsync()` — Unified HTTP send + error handling
- `ProcessFunctionCallLoopAsync()` — Extracted function call loop from `GetCompletionAsync`
- SSE helpers: `ReadSseLines()`, `TryExtractSseData()`, `SendStreamingRequestAsync()`
- `AddAssistantMessage()`, `AddFunctionCallMessage()`, `AddFunctionResultMessage()` helpers
- `CreateCompletionContent()`, `CreateErrorContent()` factory methods

#### ChatGptService
- General code cleanup and simplification

### 🧪 Test Improvements

- **CrossProvider test diagnostics** — Debug message history dump before Phase 2 API call, `ErrorDetails` output in catch blocks
- **New test classes**: `Claude_Opus4_6_Tests`, `Claude_Opus4_5_Tests`, `Claude_Sonnet4_5_Tests`, `Claude_Haiku4_5_Tests`

### ✅ Compatibility

- Fully backward compatible with v4.0.x
- No breaking changes
- Error message format changed (more detailed), but `AIServiceException` API unchanged

---

## 🛠️ v4.0.1 - MaxTokens Auto-Capping & Cross-Provider Function Fallback

### **Automatic MaxTokens Capping** 🔒

`MaxTokens` is now automatically capped at each model's maximum allowed output tokens before sending API requests. This prevents errors when `MaxTokens` is set higher than a model supports (e.g., after `CopyFrom()` transfers settings between providers).

#### How It Works

- **`GetModelMaxOutputTokens()`** — New virtual method in `AIService`, overridden per service with model-specific limits
- **`GetEffectiveMaxTokens()`** — Returns `Math.Min(MaxTokens, GetModelMaxOutputTokens())`, used in all `BuildRequestBody()` methods

#### Model-Specific Output Token Limits

| Provider | Model | Max Output Tokens |
|----------|-------|-------------------|
| **Claude** | opus-4 / opus-4-1 | 32,768 |
| **Claude** | sonnet-4 | 16,384 |
| **OpenAI** | gpt-5 family | 128,000 |
| **OpenAI** | o3 / o3-pro | 100,000 |
| **OpenAI** | gpt-4.1 family | 32,768 |
| **OpenAI** | gpt-4o / 4o-mini | 16,384 |
| **OpenAI** | gpt-4-vision | 4,096 |
| **Gemini** | all current models | 65,536 |
| **DeepSeek** | all models | 8,192 |
| **Perplexity** | all models | 8,192 |

#### Usage Example

```csharp
// Before v4.0.1: CopyFrom could cause API errors
var gptService = new ChatGptService(apiKey, httpClient);
gptService.MaxTokens = 16000;  // fine for GPT-4o

var claudeService = new ClaudeService(claudeKey, httpClient);
claudeService.CopyFrom(gptService);
claudeService.ChangeModel(AIModel.ClaudeHaiku4_5_251001);
// MaxTokens=16000 > Haiku limit 65536 → no issue (Haiku 4.5 supports 64K)

// After v4.0.1: automatically capped
// MaxTokens stays 16000 but GetEffectiveMaxTokens() returns 8192
// No API error, no manual adjustment needed
```

### **Cross-Provider Function History Fallback** 🔄

When function calling is disabled (`FunctionsDisabled = true`), function-related messages in conversation history are now automatically converted to plain text, preventing API errors from unsupported message formats.

- **`GetLatestMessagesWithFunctionFallback()`** — New helper in `AIService`
  - `function_call` assistant messages → `"[Called funcName(args)]"`
  - `Function` role messages → `User` role with `"[Function funcName returned: result]"`
- Applied in non-function `BuildRequestBody()` for Claude, OpenAI, and Gemini

### 🧪 Test

- **`CrossProvider_FunctionOff_WithFunctionHistory`** — New test verifying that function call history transfers correctly between providers with function calling disabled

### ✅ Compatibility

- Fully backward compatible with v4.0.0
- No breaking changes

---

## �🚀 What's New in v4.0.0

### **Architecture: Configuration moved from ChatBlock to AIService** 🏗️

All configuration settings are now managed at the **service level** instead of per-ChatBlock:

- **`AIService`** holds: `Model`, `Temperature`, `TopP`, `MaxTokens`, `Functions`, `EnableFunctions`, `MaxMessageCount`, `Stream`, etc.
- **`ChatBlock`** now only holds: `Messages`, `SystemMessage`, `Id`

This simplifies the API — configure once on the service, and all conversations share the same settings:

```csharp
var service = new ChatGptService(apiKey, httpClient);
service.Temperature = 0.9f;
service.MaxTokens = 2048;
service.SystemMessage = "You are a helpful assistant."; // delegates to ActivateChat.SystemMessage
```

**`CopyFrom`** now copies both conversation data and service-level settings (except `Model`, which stays provider-specific):

```csharp
var claudeService = new ClaudeService(claudeKey, httpClient).CopyFrom(gptService);
claudeService.ChangeModel(AIModel.ClaudeSonnet4_250514);
// Messages, Functions, Temperature, etc. are all preserved
```

### Migration Guide from v3.2.x to v4.0.0

#### Configuration properties moved to AIService
```csharp
// v3.2.x - Settings on ChatBlock via ActivateChat
service.ActivateChat.Temperature = 0.9f;
service.ActivateChat.MaxTokens = 2048;
service.ActivateChat.ChangeModel(AIModel.Gpt4oMini);
service.ActivateChat.AddFunction(functionDef);
service.ActivateChat.EnableFunctions = true;
service.ActivateChat.MaxMessageCount = 30;

// v4.0.0 - Settings directly on AIService
service.Temperature = 0.9f;
service.MaxTokens = 2048;
service.ChangeModel(AIModel.Gpt4oMini);
service.Functions.Add(functionDef);
service.EnableFunctions = true;
service.MaxMessageCount = 30;
```

#### ChatBlock is now conversation-only
```csharp
// v3.2.x - ChatBlock held everything
var chat = service.ActivateChat;
chat.Model;           // ❌ Removed
chat.Temperature;     // ❌ Removed
chat.Functions;       // ❌ Removed

// v4.0.0 - ChatBlock holds only conversation state
var chat = service.ActivateChat;
chat.Messages;        // ✅ Conversation history
chat.SystemMessage;   // ✅ System prompt
chat.Id;              // ✅ Unique identifier
```

#### CopyFrom copies service settings
```csharp
// v3.2.x - CopyFrom only cloned ChatBlock (which had everything)
var newService = new ClaudeService(key, http).CopyFrom(oldService);

// v4.0.0 - CopyFrom clones ChatBlock + copies service-level settings
// (Functions, Temperature, MaxTokens, etc. are all copied)
// Model is NOT copied (stays as the new provider's default)
var newService = new ClaudeService(key, http).CopyFrom(oldService);
newService.ChangeModel(AIModel.ClaudeSonnet4_250514); // set model explicitly
```

### **Gemini 2.5 GA + Gemini 3 Flash/Pro Preview** 🌐

#### Gemini 2.5 (GA)
- **Gemini 2.5 Pro**, **Gemini 2.5 Flash**, **Gemini 2.5 Flash-Lite** now fully supported (GA)
- 🗑️ Removed deprecated Gemini 1.0, 1.5, 2.0 models

#### Gemini 3 Preview
- **Gemini 3 Flash Preview** (`gemini-3-flash-preview`) and **Gemini 3 Pro Preview** (`gemini-3-pro-preview`) models added
- **Thought Signature Circulation**: Gemini 3 function calling requires thought signatures to be sent back in follow-up requests
- **ThinkingLevel** (`GeminiThinkingLevel` enum: `Auto`/`Minimal`/`Low`/`Medium`/`High`, default: `Auto`) for Gemini 3 vs **ThinkingBudget** (int) for Gemini 2.5
- **IsGemini3Model()** helper for model-specific branching
- Function response role changed to `"user"` for Gemini 3

#### Reasoning Streaming Support
- **`StreamingContentType.Reasoning`**: Gemini thinking parts (`"thought": true`) are now classified as reasoning content
- When `StreamOptions.WithReasoning()` is enabled, thought parts are emitted as `Reasoning` type
- When reasoning is not requested, thought parts are silently skipped

#### Gemini Function Calling Improvements
- Multi-round function call loop with `policy.MaxRounds`
- Proper conversation history management during streaming function calls
- `ConvertParameterProperty` for normalized parameter serialization
- `ParameterProperty.Items` added for array parameter schemas

#### Usage Example

```csharp
var geminiService = new GeminiService(apiKey, httpClient);

// Gemini 3 with thinking level
geminiService.ChangeModel(AIModel.Gemini3FlashPreview);
geminiService.ThinkingLevel = GeminiThinkingLevel.High;  // Auto = model default (High)
var response = await geminiService.GetCompletionAsync("Explain quantum entanglement");

// Streaming with reasoning
await foreach (var content in geminiService.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"[Thinking] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

---

## v3.2.0

### 🧠 GPT-5.1 / GPT-5.2 Model Support

#### New Models
- **GPT-5.1** (`gpt-5.1`) — Reasoning model with effort levels (none/low/medium/high) and text verbosity control (low/medium/high)
- **GPT-5.2** (`gpt-5.2`) — Best model for complex, coding, and agentic tasks with effort levels (none/low/medium/high/xhigh)
- **GPT-5.2 Pro** (`gpt-5.2-pro`) — High-compute model for tough problems, supports medium/high/xhigh reasoning effort
- **GPT-5.2 Codex** (`gpt-5.2-codex`) — Coding-optimized model for agentic coding tasks, supports low/medium/high/xhigh reasoning effort (default: medium)

#### New Builder Methods
- **`WithGpt5_1Parameters()`** — Configure reasoning effort (`Gpt5_1Reasoning` enum), verbosity (`Verbosity` enum), and reasoning summary (`ReasoningSummary` enum) for GPT-5.1
- **`WithGpt5_2Parameters()`** — Configure reasoning effort (`Gpt5_2Reasoning` enum), verbosity (`Verbosity` enum), and reasoning summary (`ReasoningSummary` enum) for GPT-5.2
- **`WithGpt5Parameters()` updated** — Uses `Gpt5Reasoning` enum for effort and `ReasoningSummary` enum for summary

#### Usage Example

```csharp
var gptService = (ChatGptService)service;

// GPT-5.2 with high reasoning and verbose output
gptService.WithGpt5_2Parameters(reasoningEffort: Gpt5_2Reasoning.High, verbosity: Verbosity.High);
var response = await gptService.GetCompletionAsync("Solve: 15 * 17");

// GPT-5.1 with concise reasoning summary
gptService.WithGpt5_1Parameters(reasoningEffort: Gpt5_1Reasoning.Medium, verbosity: Verbosity.Low, reasoningSummary: ReasoningSummary.Concise);
var response2 = await gptService.GetCompletionAsync("Explain quantum computing");

// GPT-5 base with reasoning summary disabled
gptService.WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.High, reasoningSummary: null);
```

### 🔧 Model Detection Improvements

#### GPT-5 Family Hierarchy
- **`IsGpt5Family()`** — Unified detection for all GPT-5 variants (gpt-5, gpt-5.1, gpt-5.2), used for shared behaviors like Responses API endpoint routing and unsupported parameter removal
- **`IsGpt5Model()`** — Matches only GPT-5 base models (gpt-5, gpt-5-mini, gpt-5-nano), excludes gpt-5.1/5.2
- **`IsGpt5_1Model()`** — Matches GPT-5.1 models
- **`IsGpt5_2Model()`** — Matches GPT-5.2 models (including gpt-5.2-pro, gpt-5.2-codex)
- **`IsGpt5_2CodexModel()`** — Matches GPT-5.2 Codex models specifically (Codex does not support 'none' reasoning effort)
- **Per-model parameter routing** — `ApplyModelSpecificParameters` now routes from most specific to least specific (5.2 → 5.1 → 5)

#### GPT-5.2 Pro / Codex Defaults
- GPT-5.2 Pro automatically applies `Gpt5_2Reasoning.Medium` as default (regular GPT-5.2 defaults to `Gpt5_2Reasoning.None`)
- GPT-5.2 Codex automatically applies `Gpt5_2Reasoning.Medium` as default; 'none' is not supported and will be adjusted to 'low'

### 🗑️ Deprecated Model Removal

| Model | Status | Reason |
|-------|--------|--------|
| `o3-mini` | ❌ Removed | Deprecated by OpenAI |
| `claude-3-5-sonnet-20241022` | ❌ Removed | Deprecated by Anthropic |
| `gpt-5-pro` | ⏸️ Suspended | Temporarily unavailable (not deprecated) |

### 🧪 New Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Gpt5ReasoningEffort` | `Gpt5Reasoning` | `Auto` | GPT-5 reasoning effort (Auto/Minimal/Low/Medium/High) |
| `Gpt5ReasoningSummary` | `ReasoningSummary?` | `Auto` | GPT-5 reasoning summary mode |
| `Gpt5_1ReasoningEffort` | `Gpt5_1Reasoning` | `Auto` | GPT-5.1 reasoning effort (Auto/None/Low/Medium/High) |
| `Gpt5_1ReasoningSummary` | `ReasoningSummary?` | `Auto` | GPT-5.1 reasoning summary mode |
| `Gpt5_1Verbosity` | `Verbosity?` | `null` | GPT-5.1 text verbosity (Low/Medium/High) |
| `Gpt5_2ReasoningEffort` | `Gpt5_2Reasoning` | `Auto` | GPT-5.2 reasoning effort (Auto/None/Low/Medium/High/XHigh) |
| `Gpt5_2ReasoningSummary` | `ReasoningSummary?` | `Auto` | GPT-5.2 reasoning summary mode |
| `Gpt5_2Verbosity` | `Verbosity?` | `null` | GPT-5.2 text verbosity (Low/Medium/High) |

### 🧪 Test Updates
- **Added test classes**: `OpenAI_o3_Tests`, `OpenAI_Gpt5_1_Tests`, `OpenAI_Gpt5_2_Tests`, `OpenAI_Gpt5_2Pro_Tests`, `OpenAI_Gpt5_2Codex_Tests`
- **Removed test classes**: `OpenAI_o3MiniTests`, `OpenAI_Gpt5Pro_Tests`, `Claude_3_5Sonnet_Tests`
- **Relaxed streaming assertions** — Chunk count assertions changed from exact match to range-based (`Assert.IsTrue(count >= 1 && count <= N)`) to accommodate reasoning models that may return fewer chunks
- **Updated Claude vision fallback** — Changed from `Claude3_5Sonnet241022` to `ClaudeSonnet4_250514`

### 📋 GPT-5 Family Model Support Status

| Model | Status | Reasoning Effort | Verbosity |
|-------|--------|-----------------|-----------|
| **gpt-5** | ✅ Full Support | minimal/low/medium/high | — |
| **gpt-5-mini** | ✅ Full Support | minimal/low/medium/high | — |
| **gpt-5-nano** | ✅ Full Support | minimal/low/medium/high | — |
| **gpt-5-pro** | ⏸️ Suspended | — | — |
| **gpt-5.1** | ✅ Full Support | none/low/medium/high | low/medium/high |
| **gpt-5.2** | ✅ Full Support | none/low/medium/high/xhigh | low/medium/high |
| **gpt-5.2-pro** | ✅ Full Support | medium/high/xhigh | low/medium/high |
| **gpt-5.2-codex** | ✅ Full Support | low/medium/high/xhigh | low/medium/high |

### ✅ Compatibility
- Fully backward compatible with v3.1.x
- No breaking changes
- `WithGpt5Parameters()` model guard removed — can now be called regardless of active model
- New enum values in `AIModel`: `Gpt5_1`, `Gpt5_2`, `Gpt5_2Pro`, `Gpt5_2Codex`
- Removed enum values: `o3_mini`, `Gpt5Pro`, `Gpt5Pro_251006`, `Claude3_5Sonnet241022`

---

## v3.1.0

### 🧠 GPT-5 Reasoning Support

#### Reasoning Streaming
- **`StreamingContentType.Reasoning`** - New streaming content type for reasoning data from GPT-5 models
- **`StreamOptions.IncludeReasoning`** - Enable reasoning summary streaming via `new StreamOptions().WithReasoning()`
- **Real-time reasoning output** - Receive reasoning chunks as they arrive, separately from text content

#### Usage Example (Streaming)

```csharp
var options = new StreamOptions().WithReasoning().WithMetadata();

await foreach (var content in service.StreamAsync("Solve this step by step: 15 * 17", options))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Reasoning] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

#### Non-Streaming Reasoning
- **`LastReasoningSummary`** - Access the reasoning summary from the most recent non-streaming GPT-5 response
- Automatically extracted from the `reasoning` output item when `reasoning.summary = "auto"` is configured

#### Usage Example (Non-Streaming)

```csharp
var gptService = (ChatGptService)service;
var response = await gptService.GetCompletionAsync("What is 15 * 17?");

Console.WriteLine($"Answer: {response}");
Console.WriteLine($"Reasoning: {gptService.LastReasoningSummary}");
```

### 🔧 GPT-5 Responses API Enhancements

#### Streaming Metadata Fix
- **Fixed metadata not populating for New API format** - `response.created` and `response.done` events now correctly extract `model`, `response_id`, `usage`, and `finish_reason` into streaming metadata
- Previously, `IncludeMetadata` only worked with legacy `chat/completions` format; now fully supports the Responses API SSE format used by GPT-5 and o3 models

#### Incomplete Response Handling
- **Detects `status=incomplete` responses** - When reasoning exhausts the entire `max_output_tokens` budget before generating text, a clear warning is returned instead of an empty string
- **Reasoning-only output detection** - If the API returns only reasoning content with no text output, a descriptive message is provided

#### GPT-5 Parameter Safeguards
- **`max_output_tokens` minimum floor (4096)** - Prevents reasoning from consuming the entire output budget by enforcing a minimum, with a logged warning when the user's value is overridden
- **`reasoning.summary = "auto"`** - Automatically configured for GPT-5 models to enable reasoning summary extraction

### 🏗 Code Quality Improvements

#### Streaming Parser Refactoring
- **Decomposed `ParseNewApiStreamChunk`** into focused helper methods for better readability and maintainability:
  - `ParseStreamTextDelta` - Text delta parsing
  - `ParseStreamFunctionCallEvent` - Function call event parsing
  - `ParseStreamOutputItemEvent` - Output item event parsing
  - `ParseStreamReasoningEvent` - Reasoning summary event parsing
  - `ParseStreamCreatedEvent` - Response lifecycle event parsing
  - `ParseStreamCompletionEvent` - Stream completion event parsing

#### Test Framework Extension
- **`SupportsReasoning()`** - New virtual method in `AIServiceTestBase` for conditional reasoning test execution
- **`ReasoningSummaryTest`** - Common test verifying both streaming and non-streaming reasoning extraction, automatically skipped for non-reasoning models via `RunIfSupported` pattern

### ✅ Compatibility
- Fully backward compatible with v3.0.x
- No breaking changes
- New `StreamingContentType.Reasoning` enum value added (non-breaking)
- New `StreamOptions.IncludeReasoning` property added (default: false)

---

## v3.0.3

### 🚨 Critical Bug Fixes

#### Claude Function Calling Fix
- **Fixed "non-empty content" error** - Resolved critical issue where Claude API would reject messages with empty content during function calling sequences
- **Claude API compatibility** - Added proper handling for tool_use responses that don't include text content, ensuring all assistant messages have valid content
- **Message cloning fix** - Fixed `Message.Clone()` not properly copying metadata, which could cause function call information to be lost during conversation transfers

### ✨ Improvements

#### Enhanced CopyFrom Method
- **Automatic model preservation** - `CopyFrom` now automatically preserves the target service's model, eliminating the need to call `SwitchModel` afterwards
- **Simplified usage** - Model switching is now handled internally, making cross-provider transfers more intuitive

#### Before (v3.0.2):

```csharp
gptService.CopyFrom(claudeService);
gptService.SwitchModel("gpt-4o");  // Required extra step
```

#### After (v3.0.3):

```csharp
gptService.CopyFrom(claudeService);  // Model automatically preserved
```

### 🔧 Technical Details
- Added content validation in `ExtractFunctionCallWithMetadata` to ensure Claude assistant messages always have non-empty content
- Enhanced `Message.Clone()` to properly copy all message metadata including function call information
- Improved `CopyFrom` to maintain target service model configuration automatically

### 📋 Known Limitations
- Array parameters in function definitions have limited support - full array parameter support with proper `items` schema planned for next release

### ✅ Compatibility
- Fully backward compatible with v3.0.x
- Recommended immediate upgrade from v3.0.2 to resolve Claude function calling issues
- No breaking changes

---

## v3.0.2

### 🐛 Bug Fixes
#### Function Calling Improvements
- **Fixed Claude API function calling errors** - "unexpected tool_use_id" errors when switching to Claude models after function calls from other providers (OpenAI, etc.)
- **Unified ID system** - Implemented internal unified ID management for seamless function calling across different providers
- **Cross-provider compatibility** - Function call history now persists correctly when switching between OpenAI and Claude models

### ✨ New Features
#### Cross-Model Conversation Transfer
- **Added `CopyFrom` method** - Transfer entire conversation history between different AI service instances
- **Cross-provider migration** - Seamlessly migrate conversations from one AI provider to another (e.g., Claude to GPT, Gemini to DeepSeek)
- **Context preservation** - Maintains full chat history, system messages, and settings when switching between different AI models

#### Usage Example

```csharp
// Transfer conversation from Claude to GPT
var claudeService = new ClaudeService(apiKey1, httpClient);
// ... have conversation with Claude ...

var gptService = new ChatGptService(apiKey2, httpClient);
gptService.CopyFrom(claudeService);  // Transfer entire conversation
gptService.SwitchModel("gpt-4o");  // Required in v3.0.2
```

### 🔧 Technical Changes
- Added `MessageMetadataKeys` for standardized metadata handling
- Function messages no longer removed when switching models
- Improved provider-specific ID mapping (`call_id` for OpenAI, `tool_use_id` for Claude)
- Enhanced `ChatBlock.Clone()` method for deep copying conversation state

### ✅ Compatibility
- Fully backward compatible with v3.0.0 and v3.0.1
- No breaking changes

---

*Latest version (v3.2.0) includes GPT-5.1/5.2 model support with verbosity control and reasoning summary configuration. We strongly recommend upgrading from v3.1.x for the latest model support.*

## What's New in v3.0.0

### Function Calling
- Full function calling support for OpenAI GPT-4o and Claude 3+
- Fluent API with `WithFunction()` / `WithFunctionAsync()` / `WithFunctions()`
- Attribute-based registration with `[AiFunction]` / `[AiParameter]`
- Advanced `FunctionBuilder` for complex scenarios
- Function calling policies (`Fast`, `Complex`, `Vision`, custom)

### Enhanced Streaming
- `StreamingContent` with metadata, function call events, and completion info
- `StreamOptions` for fine-grained control (`TextOnlyOptions`, `FullOptions`, custom)

### Migration Guide from v2.x to v3.0.0

#### Function Calling (New Feature)
```csharp
// v3.0.0 - Functions are now supported!
var service = new ChatGptService(apiKey, httpClient)
    .WithFunction("my_function", "Description", 
        ("param", "Param description", true),
        (string param) => $"Result: {param}");

// AI will automatically use functions when appropriate
var response = await service.GetCompletionAsync("Use my function");
```

#### Streaming Changes
```csharp
// v2.x - Returns string chunks
await foreach (var chunk in service.StreamAsync("Hello"))
{
    Console.Write(chunk); // chunk is string
}

// v3.0.0 - Can return StreamingContent with metadata
await foreach (var content in service.StreamAsync("Hello", StreamOptions.FullOptions))
{
    Console.Write(content.Content); // Access text via .Content
    var metadata = content.Metadata; // Access metadata
}

// For backward compatibility, default behavior unchanged
await foreach (var chunk in service.StreamAsync("Hello"))
{
    Console.Write(chunk); // Still works, chunk is string
}
```

#### Policy System (New)
```csharp
// v3.0.0 - Control function execution behavior
service.DefaultPolicy = FunctionCallingPolicy.Fast;

// Per-request override
await service
    .WithTimeout(60)
    .WithMaxRounds(5)
    .GetCompletionAsync("Complex task");
```
