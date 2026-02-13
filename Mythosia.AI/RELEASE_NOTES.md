# Mythosia.AI - Release Notes

## 🚀 What's New in v4.0.0

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
claudeService.ChangeModel(AIModel.Claude3_7SonnetLatest);
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
newService.ChangeModel(AIModel.Claude3_7SonnetLatest); // set model explicitly
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
- **`IsGpt5_2Model()`** — Matches GPT-5.2 models (including gpt-5.2-pro)
- **Per-model parameter routing** — `ApplyModelSpecificParameters` now routes from most specific to least specific (5.2 → 5.1 → 5)

#### GPT-5.2 Pro Defaults
- GPT-5.2 Pro automatically applies `Gpt5_2Reasoning.Medium` as default (regular GPT-5.2 defaults to `Gpt5_2Reasoning.None`)

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
- **Added test classes**: `OpenAI_o3_Tests`, `OpenAI_Gpt5_1_Tests`, `OpenAI_Gpt5_2_Tests`, `OpenAI_Gpt5_2Pro_Tests`
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

### ✅ Compatibility
- Fully backward compatible with v3.1.x
- No breaking changes
- `WithGpt5Parameters()` model guard removed — can now be called regardless of active model
- New enum values in `AIModel`: `Gpt5_1`, `Gpt5_2`, `Gpt5_2Pro`
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
