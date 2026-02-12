# Mythosia.AI - Release Notes

## ?? What's New in v4.0.0

### **Architecture: Configuration moved from ChatBlock to AIService** ???

All configuration settings are now managed at the **service level** instead of per-ChatBlock:

- **`AIService`** holds: `Model`, `Temperature`, `TopP`, `MaxTokens`, `Functions`, `EnableFunctions`, `MaxMessageCount`, `Stream`, etc.
- **`ChatBlock`** now only holds: `Messages`, `SystemMessage`, `Id`

This simplifies the API ? configure once on the service, and all conversations share the same settings:

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
chat.Model;           // ? Removed
chat.Temperature;     // ? Removed
chat.Functions;       // ? Removed

// v4.0.0 - ChatBlock holds only conversation state
var chat = service.ActivateChat;
chat.Messages;        // ? Conversation history
chat.SystemMessage;   // ? System prompt
chat.Id;              // ? Unique identifier
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

---

## What's New in v3.2.0

### **GPT-5.1 / GPT-5.2 Model Support** ??

- **GPT-5.1**: Reasoning model with effort levels (none/low/medium/high) and text verbosity (low/medium/high)
- **GPT-5.2**: Best model for complex, coding, and agentic tasks with effort levels (none/low/medium/high/xhigh)
- **GPT-5.2 Pro**: High-compute model for tough problems (medium/high/xhigh)
- **`WithGpt5_1Parameters()`**: Configure reasoning effort, verbosity, and reasoning summary for GPT-5.1
- **`WithGpt5_2Parameters()`**: Configure reasoning effort, verbosity, and reasoning summary for GPT-5.2
- **Reasoning Summary**: All GPT-5 family models now support configurable reasoning summary (auto/concise/detailed/disabled)

#### **Model Updates**

- ? gpt-5.1 (full support)
- ? gpt-5.2, gpt-5.2-pro (full support)
- ? gpt-5, gpt-5-mini, gpt-5-nano (full support)
- ?? gpt-5-pro (temporarily suspended)
- ??? Removed deprecated: o3-mini, claude-3-5-sonnet-20241022

#### **Quick Example**

```csharp
// GPT-5.2 with reasoning effort and verbosity
var gptService = (ChatGptService)service;
gptService.WithGpt5_2Parameters(reasoningEffort: "high", verbosity: "high");
var response = await gptService.GetCompletionAsync("Solve: 15 * 17");

// GPT-5.1 with custom configuration
gptService.WithGpt5_1Parameters(reasoningEffort: "medium", verbosity: "low", reasoningSummary: "concise");
var response2 = await gptService.GetCompletionAsync("Explain quantum computing");

// GPT-5 reasoning streaming
var options = new StreamOptions().WithReasoning().WithMetadata();
await foreach (var content in service.StreamAsync("Solve: 15 * 17", options))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Thinking] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

---

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
