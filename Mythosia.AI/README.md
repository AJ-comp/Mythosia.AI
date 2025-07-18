# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides a unified interface for various AI models with **multimodal support**, including **OpenAI GPT-4o**, **Anthropic Claude 3**, **Google Gemini**, **DeepSeek**, and **Perplexity Sonar**.

### 🚀 What's New in v2.1.0

- **IAsyncEnumerable Streaming**: Modern C# async streaming with `await foreach`
- **Simplified API**: `StreamAsync()` and `StreamOnceAsync()` methods directly on AIService
- **Better Performance**: Channel-based implementation for efficient streaming
- **Backward Compatible**: All existing callback-based methods still work

## Installation

```bash
dotnet add package Mythosia.AI
```

For advanced LINQ operations with streams:
```bash
dotnet add package System.Linq.Async
```

## Important Usage Notes

### Required Using Statements

Many convenient features are implemented as extension methods and require specific using statements:

```csharp
// Core functionality
using Mythosia.AI;
using Mythosia.AI.Services;

// For MessageBuilder
using Mythosia.AI.Builders;

// For extension methods (IMPORTANT!)
using Mythosia.AI.Extensions;  // Required for:
                               // - BeginMessage()
                               // - WithSystemMessage()
                               // - WithTemperature()
                               // - WithMaxTokens()
                               // - AskOnceAsync()
                               // - StartNewConversation()
                               // - GetLastAssistantResponse()
                               // - And more...

// For models and enums
using Mythosia.AI.Models.Enums;

// For advanced LINQ operations (optional)
using System.Linq;
```

**Common Issue**: If `BeginMessage()` or other extension methods don't appear in IntelliSense, make sure you have added `using Mythosia.AI.Extensions;` at the top of your file.

## Quick Start

### Basic Setup

```csharp
using Mythosia.AI;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using System.Net.Http;

var httpClient = new HttpClient();
var aiService = new ChatGptService("your-api-key", httpClient);
```

### Text-Only Queries

```csharp
// Simple completion
string response = await aiService.GetCompletionAsync("What is AI?");

// With conversation history
await aiService.GetCompletionAsync("Tell me about machine learning");
await aiService.GetCompletionAsync("How does it differ from AI?"); // Remembers context

// One-off query (no history)
string quickAnswer = await aiService.AskOnceAsync("What time is it in Seoul?");
```

### Streaming Responses (New in v2.1.0!)

```csharp
// Modern IAsyncEnumerable streaming
await foreach (var chunk in aiService.StreamAsync("Explain quantum computing"))
{
    Console.Write(chunk);
}

// One-off streaming without affecting conversation history
await foreach (var chunk in aiService.StreamOnceAsync("Quick question"))
{
    Console.Write(chunk);
}

// Traditional callback streaming (still supported)
await aiService.StreamCompletionAsync("Explain AI", 
    chunk => Console.Write(chunk));

// With cancellation support
var cts = new CancellationTokenSource();
await foreach (var chunk in aiService.StreamAsync("Long explanation", cts.Token))
{
    Console.Write(chunk);
    if (chunk.Contains("enough")) cts.Cancel();
}
```

### Image Analysis (Multimodal)

```csharp
// Analyze a single image
var description = await aiService.GetCompletionWithImageAsync(
    "What's in this image?", 
    "photo.jpg"
);

// Compare multiple images using fluent API
var comparison = await aiService
    .BeginMessage()
    .AddText("What are the differences between these images?")
    .AddImage("before.jpg")
    .AddImage("after.jpg")
    .SendAsync();

// Stream image analysis
await foreach (var chunk in aiService
    .BeginMessage()
    .AddText("Describe this artwork in detail")
    .AddImage("painting.jpg")
    .WithHighDetail()
    .StreamAsync())
{
    Console.Write(chunk);
}

// One-off image query (doesn't affect conversation history)
var quickAnalysis = await aiService
    .BeginMessage()
    .AddText("What color is this?")
    .AddImage("sample.jpg")
    .SendOnceAsync();
```

### Advanced Streaming with LINQ

```csharp
// Requires: dotnet add package System.Linq.Async

// Take only first 1000 characters
var limitedResponse = await aiService
    .StreamAsync("Tell me a long story")
    .Take(100)  // Take first 100 chunks
    .ToListAsync();

// Filter empty chunks
await foreach (var chunk in aiService
    .StreamAsync("Explain something")
    .Where(c => !string.IsNullOrWhiteSpace(c)))
{
    ProcessChunk(chunk);
}

// Collect full response
var fullText = await aiService
    .StreamAsync("Explain AI")
    .ToListAsync()
    .ContinueWith(t => string.Concat(t.Result));

// Transform chunks
await foreach (var upper in aiService
    .StreamAsync("Hello")
    .Select(chunk => chunk.ToUpper()))
{
    Console.Write(upper);
}
```

### Stateless Mode

```csharp
// Enable stateless mode for all requests
aiService.StatelessMode = true;

// Each request is independent
await aiService.GetCompletionAsync("Translate: Hello");  // No history
await aiService.GetCompletionAsync("Translate: World");  // No history

// Or use one-off methods while maintaining conversation
aiService.StatelessMode = false;  // Back to normal

// These don't affect the conversation history
var oneOffResult = await aiService.AskOnceAsync("What time is it?");

await foreach (var chunk in aiService.StreamOnceAsync("Quick question"))
{
    Console.Write(chunk);
}
```

### Fluent Message Building

```csharp
// Build complex multimodal messages
var result = await aiService
    .BeginMessage()
    .WithRole(ActorRole.User)
    .AddText("Analyze this chart and explain the trend")
    .AddImage("sales-chart.png")
    .WithHighDetail()
    .SendAsync();

// Stream with fluent API
await foreach (var chunk in aiService
    .BeginMessage()
    .AddText("Compare these approaches:")
    .AddText("1. Traditional ML")
    .AddText("2. Deep Learning") 
    .AddImage("comparison.jpg")
    .StreamAsync())
{
    ProcessChunk(chunk);
}

// Using image URLs
var urlAnalysis = await aiService
    .BeginMessage()
    .AddText("What's in this image?")
    .AddImageUrl("https://example.com/image.jpg")
    .SendAsync();
```

## Service-Specific Features

### OpenAI GPT-4

```csharp
var gptService = new ChatGptService(apiKey, httpClient);

// Use GPT-4o model (supports vision natively)
gptService.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);

// Stream with GPT-4o
await foreach (var chunk in gptService
    .BeginMessage()
    .AddText("Analyze this complex diagram")
    .AddImage("diagram.png")
    .StreamAsync())
{
    Console.Write(chunk);
}

// Generate images
byte[] imageData = await gptService.GenerateImageAsync(
    "A futuristic city at sunset",
    "1024x1024"
);

// Audio features
// Text-to-Speech
byte[] audioData = await gptService.GetSpeechAsync(
    "Hello, world!", 
    voice: "alloy", 
    model: "tts-1"
);

// Speech-to-Text
string transcription = await gptService.TranscribeAudioAsync(
    audioData, 
    "audio.mp3", 
    language: "en"
);
```

**Important Notes:**
- GPT-4o models (`gpt-4o-latest`, `gpt-4o`, `gpt-4o-2024-08-06`) support vision natively
- `gpt-4o-mini` does NOT support vision
- `gpt-4-vision-preview` is deprecated, use `gpt-4o` instead

### Anthropic Claude 3

```csharp
var claudeService = new ClaudeService(apiKey, httpClient);

// Claude 3 models support vision natively
await foreach (var chunk in claudeService
    .BeginMessage()
    .AddText("Analyze this medical image")
    .AddImage("xray.jpg")
    .StreamAsync())
{
    Console.Write(chunk);
}

// Token counting
uint tokens = await claudeService.GetInputTokenCountAsync();
```

### Google Gemini

```csharp
var geminiService = new GeminiService(apiKey, httpClient);

// Gemini Pro Vision for multimodal tasks
geminiService.ActivateChat.ChangeModel(AIModel.GeminiProVision);

// Stream multimodal analysis
await foreach (var chunk in geminiService
    .BeginMessage()
    .AddText("What objects are in this image?")
    .AddImage("objects.jpg")
    .StreamAsync())
{
    ProcessChunk(chunk);
}
```

### DeepSeek

```csharp
var deepSeekService = new DeepSeekService(apiKey, httpClient);

// Use Reasoner model for complex reasoning
deepSeekService.UseReasonerModel();

// Stream code generation
deepSeekService.WithCodeGenerationMode("python");
await foreach (var chunk in deepSeekService.StreamAsync(
    "Write a fibonacci function"))
{
    Console.Write(chunk);
}

// Math mode with Chain of Thought
deepSeekService.WithMathMode();
var solution = await deepSeekService.GetCompletionWithCoTAsync(
    "Solve: 2x^2 + 5x - 3 = 0"
);
```

### Perplexity Sonar

```csharp
var sonarService = new SonarService(apiKey, httpClient);

// Web search with streaming
await foreach (var chunk in sonarService.StreamAsync(
    "Latest AI breakthroughs in 2024"))
{
    Console.Write(chunk);
}

// Get search with citations
var searchResult = await sonarService.GetCompletionWithSearchAsync(
    "Recent developments in quantum computing",
    domainFilter: new[] { "arxiv.org", "nature.com" },
    recencyFilter: "month"
);

// Access citations
foreach (var citation in searchResult.Citations)
{
    Console.WriteLine($"{citation.Title}: {citation.Url}");
}
```

## Advanced Usage

### Conversation Management

```csharp
// Start fresh conversation
aiService.StartNewConversation();

// Start with different model
aiService.StartNewConversation(AIModel.Claude3_5Sonnet241022);

// Switch models mid-conversation
aiService.SwitchModel(AIModel.Gpt4o241120);

// Get conversation info
var summary = aiService.GetConversationSummary();
var lastResponse = aiService.GetLastAssistantResponse();

// Retry last message
var betterResponse = await aiService.RetryLastMessageAsync();

// Clear specific messages
aiService.ActivateChat.RemoveLastMessage();
aiService.ActivateChat.ClearMessages();
```

### Token Management

```csharp
// Check tokens before sending
uint currentTokens = await aiService.GetInputTokenCountAsync();
if (currentTokens > 3000)
{
    aiService.ActivateChat.MaxMessageCount = 10; // Reduce history
}

// Check tokens for specific prompt
uint promptTokens = await aiService.GetInputTokenCountAsync("Long prompt...");

// Configure max tokens
aiService.WithMaxTokens(2000);
```

### Configuration

```csharp
// Method chaining for configuration
aiService
    .WithSystemMessage("You are a helpful coding assistant")
    .WithTemperature(0.7f)
    .WithMaxTokens(2000)
    .WithStatelessMode(false);

// Configure chat parameters
aiService.ActivateChat.Temperature = 0.5f;
aiService.ActivateChat.TopP = 0.9f;
aiService.ActivateChat.MaxTokens = 4096;
aiService.ActivateChat.MaxMessageCount = 20;

// Custom models
aiService.ActivateChat.ChangeModel("gpt-4-turbo-preview");
```

### Error Handling

```csharp
try
{
    await foreach (var chunk in aiService.StreamAsync(message))
    {
        Console.Write(chunk);
    }
}
catch (MultimodalNotSupportedException ex)
{
    Console.WriteLine($"Service {ex.ServiceName} doesn't support {ex.RequestedFeature}");
}
catch (TokenLimitExceededException ex)
{
    Console.WriteLine($"Too many tokens: {ex.RequestedTokens} > {ex.MaxTokens}");
}
catch (RateLimitExceededException ex)
{
    Console.WriteLine($"Rate limit hit. Retry after: {ex.RetryAfter}");
}
catch (AIServiceException ex)
{
    Console.WriteLine($"API Error: {ex.Message}");
    Console.WriteLine($"Details: {ex.ErrorDetails}");
}
```

### Static Quick Methods

For one-off queries without managing service instances:

```csharp
// Quick text query
var answer = await AIService.QuickAskAsync(
    apiKey, 
    "What's the capital of France?",
    AIModel.Gpt4oMini
);

// Quick image analysis
var description = await AIService.QuickAskWithImageAsync(
    apiKey,
    "Describe this image",
    "image.jpg",
    AIModel.Gpt4o240806
);
```

## Model Support Matrix

| Service | Text | Vision | Audio | Image Gen | Web Search | Streaming |
|---------|------|--------|-------|-----------|------------|-----------|
| **OpenAI GPT-4o** | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| **OpenAI GPT-4o-mini** | ✅ | ❌ | ✅ | ✅ | ❌ | ✅ |
| **Claude 3** | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ |
| **Gemini** | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ |
| **DeepSeek** | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| **Sonar** | ✅ | ❌ | ❌ | ❌ | ✅ | ✅ |

## Best Practices

1. **Streaming Best Practices**:
   - Use `StreamAsync()` for better performance with long responses
   - Always handle cancellation tokens for user-initiated stops
   - Consider using `StreamOnceAsync()` for queries that don't need history
   - Use `System.Linq.Async` for advanced stream manipulation

2. **Model Selection**: 
   - Use `gpt-4o` models for vision tasks
   - Use `gpt-4o-mini` for cost-effective text-only tasks
   - Use Claude 3.5 Sonnet for complex reasoning
   - Use Gemini 1.5 Flash for fast multimodal tasks

3. **Image Handling**:
   - Keep images under 4MB
   - Supported formats: JPEG, PNG, GIF, WebP
   - Use `WithHighDetail()` for detailed analysis (costs more tokens)
   - For URLs, ensure they are publicly accessible

4. **Performance**:
   - Reuse HttpClient instances
   - Monitor token usage to manage costs
   - Use streaming for long responses
   - Enable stateless mode for independent queries

5. **Error Handling**:
   - Always wrap API calls in try-catch blocks
   - Check model capabilities before sending multimodal content
   - Handle rate limits gracefully with exponential backoff
   - Log errors for debugging

## Migration Guide

### From v2.0.x to v2.1.0

Version 2.1.0 adds IAsyncEnumerable support while maintaining full backward compatibility:

```csharp
// Old way (still works)
await service.StreamCompletionAsync("Hello", chunk => Console.Write(chunk));

// New way (recommended)
await foreach (var chunk in service.StreamAsync("Hello"))
{
    Console.Write(chunk);
}

// Fluent API now supports both
await service.BeginMessage()
    .AddText("Hello")
    .StreamAsync(chunk => Console.Write(chunk));  // Callback version

await foreach (var chunk in service.BeginMessage()
    .AddText("Hello")
    .StreamAsync())  // IAsyncEnumerable version
{
    Console.Write(chunk);
}
```

### From v1.x to v2.x

Version 2.x adds multimodal support and many new features. All v1.x code continues to work:

```csharp
// This still works exactly as before
var response = await aiService.GetCompletionAsync("Hello");
```

New features are additive and optional.

## Troubleshooting

**Q: Extension methods like `BeginMessage()` not showing up?**
- Add `using Mythosia.AI.Extensions;` to your file

**Q: Want to use LINQ with streams?**
- Install `System.Linq.Async` package: `dotnet add package System.Linq.Async`

**Q: Getting "Channel not found" error?**
- The project targets .NET Standard 2.1. Make sure your project targets .NET Core 3.0+ or .NET 5.0+

**Q: Images not working with GPT-4?**
- Use `gpt-4o` models, not the deprecated `gpt-4-vision-preview`
- Make sure images are in supported formats (JPEG, PNG, GIF, WebP)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.

## Links

- [GitHub Repository](https://github.com/AJ-comp/Mythosia)
- [NuGet Package](https://www.nuget.org/packages/Mythosia.AI)
- [Documentation](https://github.com/AJ-comp/Mythosia/tree/master/Mythosia.AI)
- [Samples](https://github.com/AJ-comp/Mythosia/tree/master/Mythosia.AI.Samples)