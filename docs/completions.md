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
service.SystemPrompt = "You are a concise assistant. Answer in one sentence.";

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
service.ClearMessages();
```

## Building Messages Manually

Use `MessageBuilder` to construct messages explicitly:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("Summarize this text: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Image Input)

Providers that support vision accept image content alongside text:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("What does this diagram show?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Controlling Output Length and Temperature

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // lower = more deterministic
```
