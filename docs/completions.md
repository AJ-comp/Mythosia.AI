# Basic Completions

## Single Turn

The simplest usage — send a message, get a response:

```csharp
var response = await service.GetCompletionAsync("What is the capital of France?");
Console.WriteLine(response); // Paris
```

## System Prompt

Set a system prompt to give the model a persona or instructions:

```csharp
service.SystemMessage = "You are a concise assistant. Answer in one sentence.";

var response = await service.GetCompletionAsync("Explain recursion.");
```

## Multi-Turn Conversation

Messages are accumulated automatically. Each call to `GetCompletionAsync` appends to the conversation history:

```csharp
await service.GetCompletionAsync("My name is Alice.");
var response = await service.GetCompletionAsync("What is my name?");
// → "Your name is Alice."
```

To clear the conversation history:

```csharp
service.ActivateChat.ClearMessages();
```

## Building Messages Manually

Use `MessageBuilder` to construct messages explicitly:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Summarize this text: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Image Input)

Providers that support vision accept image content alongside text:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("What does this diagram show?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Quick Ask (Static API)

For one-off queries without constructing a service instance, use the static `QuickAskAsync`. The provider is auto-detected from the model name:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "What is the capital of France?",
    model: AIModels.OpenAI.Gpt4oMini  // default
);
```

Image variant:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Describe this image",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4_1
);
```

## Image Convenience Methods

Analyse images without `MessageBuilder` — the service reads the file and resolves the MIME type automatically:

```csharp
// From file path
var response = await service.GetCompletionWithImageAsync(
    "What does this diagram show?", "diagram.png");

// From URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Describe this photo", "https://example.com/photo.jpg");
```

## Retry Last Message

Remove the last assistant response and resend the last user message:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Useful when the previous response was unsatisfactory and you want the model to try again.

## Token Counting

Estimate token usage before sending a request. Available on **all providers**:

```csharp
// Count tokens for the current conversation history
uint conversationTokens = await service.GetInputTokenCountAsync();

// Count tokens for a specific prompt
uint promptTokens = await service.GetInputTokenCountAsync("Your prompt here");
```

OpenAI and most providers use local TikToken-based estimation. Anthropic and Google call their native token counting APIs for exact results.

## Fluent Message Chain

`BeginMessage()` provides a fluent API for building and sending messages in a single chain — including text, images, streaming, and policy configuration:

```csharp
// Simple text + image → send
string response = await service.BeginMessage()
    .AddText("What does this diagram show?")
    .AddImage("diagram.png")
    .SendAsync();

// One-off query (no conversation history)
string answer = await service.BeginMessage()
    .AddText("Translate this to Korean")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("Write a poem about spring")
    .StreamAsync(chunk => Console.Write(chunk));

// With custom timeout and policy
string result = await service.BeginMessage()
    .AddText("Analyze this image")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` also supports `IAsyncEnumerable`:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("Tell me a story").StreamAsync())
    Console.Write(chunk);
```

## Controlling Output Length and Temperature

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // lower = more deterministic
```
