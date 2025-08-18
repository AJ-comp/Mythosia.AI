# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides a unified interface for various AI models with **multimodal support**, including **OpenAI GPT-4o**, **Anthropic Claude 3**, **Google Gemini**, **DeepSeek**, and **Perplexity Sonar**.

### 🚀 What's New in v2.2.0

- **Latest AI Models**: 
  - **OpenAI GPT-5**: Full support for GPT-5 series models (Gpt5, Gpt5Mini, Gpt5Nano, Gpt5ChatLatest)
  - **OpenAI GPT-4.1**: Support for GPT-4.1 series models (Gpt4_1, Gpt4_1Mini, Gpt4_1Nano)
  - **Anthropic Claude 4**: Support for latest Claude 4 Opus and Sonnet models
  - **Google Gemini 2.5**: Added Gemini 2.5 Pro, Flash, and Flash Lite models
  - **Enhanced DeepSeek**: DeepSeek Reasoner model support
- **Model Migration**: Deprecated `Gemini15Pro` and `Gemini15Flash` in favor of `Gemini1_5Pro` and `Gemini1_5Flash`
- **Improved Stability**: Enhanced error handling and model compatibility

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

### Streaming Responses

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

**Streaming Limitations:**
- **GPT-5 models**: Streaming is not yet supported in this library. Use regular `GetCompletionAsync()` for GPT-5 models.
- All other models support both streaming and regular completion methods.

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

### OpenAI GPT Models

```csharp
var gptService = new ChatGptService(apiKey, httpClient);

// Use latest GPT-4o model (supports vision natively)
gptService.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);

// GPT-5 models (available)
gptService.ActivateChat.ChangeModel(AIModel.Gpt5);

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
- GPT-4o models (`gpt-4o-latest`, `gpt-4o`, `gpt-4o-2024-08-06`, `gpt-4o-2024-11-20`) support vision natively
- `gpt-4o-mini` does NOT support vision
- `gpt-4-vision-preview` is deprecated, use `gpt-4o` instead
- **GPT-5 models are available and support all core features**
- **⚠️ GPT-5 streaming is not yet supported in this library (text completion only)**

### Anthropic Claude Models

```csharp
var claudeService = new ClaudeService(apiKey, httpClient);

// Use latest Claude models
claudeService.ActivateChat.ChangeModel(AIModel.Claude3_5Sonnet241022);

// Claude 4 models (available)
claudeService.ActivateChat.ChangeModel(AIModel.ClaudeOpus4_250514);

// Claude models support vision natively
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

### Google Gemini Models

```csharp
var geminiService = new GeminiService(apiKey, httpClient);

// Use latest Gemini 2.5 models (recommended)
geminiService.ActivateChat.ChangeModel(AIModel.Gemini2_5Pro);

// Gemini 1.5 models (use new enum values)
geminiService.ActivateChat.ChangeModel(AIModel.Gemini1_5Pro);  // ✅ Recommended
// geminiService.ActivateChat.ChangeModel(AIModel.Gemini15Pro);  // ⚠️ Deprecated

// Stream multimodal analysis with Gemini 2.5
await foreach (var chunk in geminiService
    .BeginMessage()
    .AddText("What objects are in this image?")
    .AddImage("objects.jpg")
    .StreamAsync())
{
    ProcessChunk(chunk);
}

// Gemini Pro Vision for legacy support
geminiService.ActivateChat.ChangeModel(AIModel.GeminiProVision);
```

**Model Migration Note:**
- `Gemini15Pro` → `Gemini1_5Pro` (deprecated, please update)
- `Gemini15Flash` → `Gemini1_5Flash` (deprecated, please update)
- New Gemini 2.5 models offer improved performance and capabilities

### DeepSeek Models

```csharp
var deepSeekService = new DeepSeekService(apiKey, httpClient);

// Use Reasoner model for complex reasoning
deepSeekService.ActivateChat.ChangeModel(AIModel.DeepSeekReasoner);

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

### Perplexity Sonar Models

```csharp
var sonarService = new SonarService(apiKey, httpClient);

// Use enhanced reasoning model
sonarService.ActivateChat.ChangeModel(AIModel.PerplexitySonarReasoning);

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
| **OpenAI GPT-5** | ✅ | ✅ | ✅ | ✅ | ❌ | ⚠️ Limited* |
| **Claude 3/4** | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ |
| **Gemini 2.5** | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ |
| **Gemini 1.5** | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ |
| **DeepSeek** | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| **Sonar** | ✅ | ❌ | ❌ | ❌ | ✅ | ✅ |

*GPT-5 streaming support is not yet implemented in this library (text completion only)

## Available Models

### OpenAI Models
- **GPT-5 Series**: `Gpt5`, `Gpt5Mini`, `Gpt5Nano`, `Gpt5ChatLatest`
  - ⚠️ **Note**: GPT-5 models support text completion but streaming is not yet implemented in this library
  - Use `GetCompletionAsync()` instead of `StreamAsync()` for GPT-5 models
- **GPT-4.1 Series**: `Gpt4_1`, `Gpt4_1Mini`, `Gpt4_1Nano`
- **GPT-4o Series**: `Gpt4oLatest`, `Gpt4o`, `Gpt4o241120`, `Gpt4o240806`, `Gpt4oMini`
- **Legacy**: `Gpt4Vision` (deprecated)

### Anthropic Claude Models
- **Claude 4 Series**: `ClaudeOpus4_1_250805`, `ClaudeOpus4_250514`, `ClaudeSonnet4_250514`
- **Claude 3.7**: `Claude3_7SonnetLatest`
- **Claude 3.5**: `Claude3_5Sonnet241022`, `Claude3_5Haiku241022`
- **Claude 3**: `Claude3Opus240229`, `Claude3Haiku240307`

### Google Gemini Models
- **Gemini 2.5 Series** (Latest): `Gemini2_5Pro`, `Gemini2_5Flash`, `Gemini2_5FlashLite`
- **Gemini 2.0**: `Gemini2_0Flash`
- **Gemini 1.5**: `Gemini1_5Pro`, `Gemini1_5Flash` (use these instead of deprecated `Gemini15Pro`, `Gemini15Flash`)
- **Legacy**: `GeminiPro`, `GeminiProVision`

### DeepSeek Models
- `DeepSeekChat`, `DeepSeekReasoner`

### Perplexity Models
- `PerplexitySonar`, `PerplexitySonarPro`, `PerplexitySonarReasoning`

## Best Practices

1. **Model Selection**: 
   - Use latest model versions for best performance and features
   - Migrate from deprecated model enum values (e.g., `Gemini15Pro` → `Gemini1_5Pro`)
   - Use `Gemini2_5Pro` for newest Google capabilities
   - Use `gpt-4o` models for vision tasks
   - Use `gpt-4o-mini` for cost-effective text-only tasks

2. **Streaming Best Practices**:
   - Use `StreamAsync()` for better performance with long responses
   - Always handle cancellation tokens for user-initiated stops
   - Consider using `StreamOnceAsync()` for queries that don't need history
   - Use `System.Linq.Async` for advanced stream manipulation

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

### Model Enum Updates

Update deprecated model references:

```csharp
// ❌ Deprecated (will be removed in future versions)
service.ActivateChat.ChangeModel(AIModel.Gemini15Pro);
service.ActivateChat.ChangeModel(AIModel.Gemini15Flash);

// ✅ Use these instead
service.ActivateChat.ChangeModel(AIModel.Gemini1_5Pro);
service.ActivateChat.ChangeModel(AIModel.Gemini1_5Flash);

// ✅ Or upgrade to latest Gemini 2.5 models
service.ActivateChat.ChangeModel(AIModel.Gemini2_5Pro);
service.ActivateChat.ChangeModel(AIModel.Gemini2_5Flash);
```

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

**Q: Getting obsolete warnings for Gemini models?**
- Update `Gemini15Pro` to `Gemini1_5Pro`
- Update `Gemini15Flash` to `Gemini1_5Flash`
- Consider upgrading to newer `Gemini2_5Pro` or `Gemini2_5Flash` models

**Q: GPT-5 streaming not working?**
- GPT-5 streaming is not yet supported in this library
- Use `GetCompletionAsync()` instead of `StreamAsync()` for GPT-5 models
- All other models support both streaming and regular completion