# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides a unified interface for various AI models with **multimodal support**, including **OpenAI GPT-4**, **Anthropic Claude 3**, **Google Gemini**, **DeepSeek**, and **Perplexity Sonar**.

### 🚀 What's New in v2.0

- **Multimodal Support**: Send images along with text to compatible AI models
- **Stateless Mode**: Process requests independently without maintaining conversation history
- **Fluent Message Builder**: Easily construct complex multimodal messages
- **Enhanced Extensions**: Convenient helper methods for common scenarios

## Installation

```bash
dotnet add package Mythosia.AI
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
```

**Common Issue**: If `BeginMessage()` or other extension methods don't appear in IntelliSense, make sure you have added `using Mythosia.AI.Extensions;` at the top of your file.

## Quick Start

### Basic Setup

```csharp
using Mythosia.AI;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;  // Required for extension methods like BeginMessage()
using System.Net.Http;

var httpClient = new HttpClient();
var aiService = new ChatGptService("your-api-key", httpClient);
```

### Text-Only Queries (Backward Compatible)

```csharp
// Simple completion
string response = await aiService.GetCompletionAsync("What is AI?");

// Streaming
await aiService.StreamCompletionAsync("Explain quantum computing", 
    content => Console.Write(content));
```

### Image Analysis (New!)

**Note**: The `BeginMessage()` method requires `using Mythosia.AI.Extensions;`

```csharp
// Analyze a single image
var description = await aiService.GetCompletionWithImageAsync(
    "What's in this image?", 
    "photo.jpg"
);

// Compare multiple images using fluent API
// Requires: using Mythosia.AI.Extensions;
var comparison = await aiService
    .BeginMessage()
    .AddText("What are the differences between these images?")
    .AddImage("before.jpg")
    .AddImage("after.jpg")
    .SendAsync();

// One-off image query (doesn't affect conversation history)
var quickAnalysis = await aiService
    .BeginMessage()
    .AddText("What color is this?")
    .AddImage("sample.jpg")
    .SendOnceAsync();

// Stream response for detailed analysis
await aiService
    .BeginMessage()
    .AddText("Describe this artwork in detail")
    .AddImage("painting.jpg")
    .WithHighDetail()
    .StreamAsync(chunk => Console.Write(chunk));
```

### Multimodal Messages

```csharp
// Using MessageBuilder
var message = MessageBuilder.Create()
    .AddText("Analyze this chart and explain the trend")
    .AddImage("sales-chart.png")
    .WithHighDetail()  // For detailed analysis
    .Build();

var analysis = await aiService.GetCompletionAsync(message);

// Using image URLs
var urlMessage = MessageBuilder.Create()
    .AddText("What's in this image?")
    .AddImageUrl("https://example.com/image.jpg")
    .Build();
```

### Stateless Mode (New!)

```csharp
// Enable stateless mode for independent requests
aiService.StatelessMode = true;

// Each request is now independent
await aiService.GetCompletionAsync("Translate: Hello");  // No history
await aiService.GetCompletionAsync("Translate: World");  // No history

// Or use one-off queries while maintaining conversation
aiService.StatelessMode = false;  // Back to normal
var oneOffResult = await aiService.AskOnceAsync("What time is it?");
```

### Usage Scenarios

```csharp
// Scenario 1: Conversational Chatbot
var service = new ChatGptService(apiKey, httpClient);
await service.GetCompletionAsync("안녕하세요");
await service.GetCompletionAsync("날씨가 어때요?"); // Conversation continues

// Scenario 2: Stateless API-like calls
var service = new ChatGptService(apiKey, httpClient);
service.StatelessMode = true; // All calls are independent
await service.GetCompletionAsync("번역: Hello");
await service.GetCompletionAsync("번역: World");

// Scenario 3: Mixed mode - mostly conversational with occasional one-off
var service = new ChatGptService(apiKey, httpClient);
await service.GetCompletionAsync("대화 시작");
await service.GetCompletionAsync("계속 대화");
// Quick one-off question
var quickAnswer = await service.AskOnceAsync("오늘 날짜는?");
// Continue conversation
await service.GetCompletionAsync("아까 얘기 계속해요");

// Scenario 4: One-time script usage
var answer = await AIService.QuickAskAsync(apiKey, "Quick question");
```

## Service-Specific Features

### OpenAI GPT-4

```csharp
var gptService = new ChatGptService(apiKey, httpClient);

// Use GPT-4o model (supports vision natively)
gptService.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);

// Analyze images
var result = await gptService.GetCompletionWithImageAsync(
    "Describe this image in detail", 
    "complex-diagram.png"
);

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
- `gpt-4-vision-preview` model is deprecated. Use `gpt-4o` models instead.
- GPT-4o models (`gpt-4o-latest`, `gpt-4o`, `gpt-4o-2024-08-06`) support vision natively
- `gpt-4o-mini` does NOT support vision

### Anthropic Claude 3

```csharp
var claudeService = new ClaudeService(apiKey, httpClient);

// Claude 3 models support vision natively
var analysis = await claudeService
    .BeginMessage()
    .AddText("Analyze this medical image")
    .AddImage("xray.jpg")
    .SendAsync();

// Token counting
uint tokens = await claudeService.GetInputTokenCountAsync();
```

### Google Gemini

```csharp
var geminiService = new GeminiService(apiKey, httpClient);

// Gemini Pro Vision for multimodal tasks
geminiService.ActivateChat.ChangeModel(AIModel.GeminiProVision);

var result = await geminiService.GetCompletionWithImageAsync(
    "What objects are in this image?",
    "objects.jpg"
);
```

### DeepSeek

```csharp
var deepSeekService = new DeepSeekService(apiKey, httpClient);

// Use Reasoner model for complex reasoning
deepSeekService.UseReasonerModel();

// Code generation mode
deepSeekService.WithCodeGenerationMode("python");
var code = await deepSeekService.GetCompletionAsync(
    "Write a fibonacci function"
);

// Math mode with Chain of Thought
deepSeekService.WithMathMode();
var solution = await deepSeekService.GetCompletionWithCoTAsync(
    "Solve: 2x^2 + 5x - 3 = 0"
);
```

### Perplexity Sonar

```csharp
var sonarService = new SonarService(apiKey, httpClient);

// Web search with citations
var searchResult = await sonarService.GetCompletionWithSearchAsync(
    "Latest AI breakthroughs in 2024",
    domainFilter: new[] { "arxiv.org", "nature.com" },
    recencyFilter: "month"
);

// Access citations
foreach (var citation in searchResult.Citations)
{
    Console.WriteLine($"{citation.Title}: {citation.Url}");
}

// Use different Sonar models
sonarService.UseSonarPro();       // Enhanced capabilities
sonarService.UseSonarReasoning(); // Complex reasoning
```

## Advanced Usage

### Fluent Message Building

The fluent API requires `using Mythosia.AI.Extensions;` for methods like `BeginMessage()`, `WithSystemMessage()`, `WithTemperature()`, etc.

```csharp
using Mythosia.AI.Extensions;  // Required!

// SendAsync() - Adds to conversation history
await aiService
    .BeginMessage()
    .AddText("Analyze this data")
    .AddImage("chart.png")
    .SendAsync();

// SendOnceAsync() - One-off query without affecting history
var result = await aiService
    .BeginMessage()
    .AddText("Quick question about this image")
    .AddImage("photo.jpg")
    .SendOnceAsync();

// StreamAsync() - Stream response with history
await aiService
    .BeginMessage()
    .AddText("Explain in detail")
    .AddImage("diagram.png")
    .StreamAsync(chunk => Console.Write(chunk));

// StreamOnceAsync() - Stream without history
await aiService
    .BeginMessage()
    .AddText("Translate this")
    .AddImage("text.jpg")
    .StreamOnceAsync(chunk => Console.Write(chunk));
```

### Conversation Management

**Note**: All these methods require `using Mythosia.AI.Extensions;`

```csharp
// Start fresh conversation
aiService.StartNewConversation();

// Switch models mid-conversation
aiService.SwitchModel(AIModel.Claude3_5Sonnet241022);

// Get conversation summary
var summary = aiService.GetConversationSummary();

// Get last assistant response
var lastResponse = aiService.GetLastAssistantResponse();

// Retry last message
var betterResponse = await aiService.RetryLastMessageAsync();

// One-off query without affecting history
var quickAnswer = await aiService.AskOnceAsync("Quick question");
```

### Token Management

```csharp
// Check tokens before sending
uint tokens = await aiService.GetInputTokenCountAsync();
if (tokens > 3000)
{
    aiService.ActivateChat.MaxMessageCount = 10; // Reduce history
}

// Check tokens for specific prompt
uint promptTokens = await aiService.GetInputTokenCountAsync("Long prompt...");
```

### Error Handling

```csharp
try
{
    var response = await aiService.GetCompletionAsync(message);
}
catch (MultimodalNotSupportedException ex)
{
    // Handle services that don't support images
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

## Static Quick Methods

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
    AIModel.Gpt4o240806  // Use vision-capable model
);
```

## Configuration

### System Messages

```csharp
aiService
    .WithSystemMessage("You are a helpful assistant specialized in image analysis")
    .WithTemperature(0.7f)
    .WithMaxTokens(2000);
```

### Custom Models

```csharp
// Use string for custom model names
aiService.ActivateChat.ChangeModel("gpt-4o-2024-11-20");
```

### Service-Specific Parameters

```csharp
// OpenAI specific
var gptService = (ChatGptService)aiService;
gptService.WithOpenAIParameters(
    presencePenalty: 0.5f,
    frequencyPenalty: 0.3f
);

// Claude specific
var claudeService = (ClaudeService)aiService;
claudeService.WithClaudeParameters(topK: 10);

// Gemini specific
var geminiService = (GeminiService)aiService;
geminiService.WithSafetyThreshold("BLOCK_MEDIUM_AND_ABOVE");
```

## Model Support Matrix

| Service | Text | Vision | Audio | Image Gen | Web Search |
|---------|------|--------|-------|-----------|------------|
| **OpenAI GPT-4o** | ✅ | ✅ | ✅ | ✅ | ❌ |
| **OpenAI GPT-4o-mini** | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Claude 3** | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Gemini** | ✅ | ✅ | ❌ | ❌ | ❌ |
| **DeepSeek** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Sonar** | ✅ | ❌ | ❌ | ❌ | ✅ |

## Best Practices

1. **Model Selection**: 
   - Use `gpt-4o` models for vision tasks (not `gpt-4-vision-preview` which is deprecated)
   - Use `gpt-4o-mini` for cost-effective text-only tasks
   - Use Claude 3.5 Haiku for fast, affordable multimodal tasks

2. **Image Handling**:
   - Keep images under 4MB
   - Supported formats: JPEG, PNG, GIF, WebP
   - Use `WithHighDetail()` for detailed analysis (costs more tokens)

3. **Performance**:
   - Reuse HttpClient instances
   - Monitor token usage to manage costs
   - Use streaming for long responses

4. **Error Handling**:
   - Always wrap API calls in try-catch blocks
   - Check model capabilities before sending multimodal content
   - Handle rate limits gracefully

## Migration from v1.x

Version 2.0 is fully backward compatible. Existing code will continue to work:

```csharp
// This still works exactly as before
var response = await aiService.GetCompletionAsync("Hello");
```

New features are additive and optional.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.