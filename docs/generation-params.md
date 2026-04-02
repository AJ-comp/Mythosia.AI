# Generation Parameters

## Common Properties

All AI service instances expose these properties:

```csharp
service.Temperature = 0.7f;        // Randomness [0, 2]. Lower = more deterministic
service.TopP = 1.0f;               // Nucleus sampling threshold
service.MaxTokens = 1024;          // Max output tokens
service.FrequencyPenalty = 0.0f;   // Penalize repeated tokens
service.PresencePenalty = 0.0f;    // Penalize tokens already present
service.MaxMessageCount = 20;      // Conversation window size
```

## Fluent Extension Methods

These return `this` for chaining:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("You are a helpful assistant.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Method | Description |
|--------|-------------|
| `.WithSystemMessage(string)` | Set system prompt |
| `.WithTemperature(float)` | Clamped to [0, 2] |
| `.WithMaxTokens(uint)` | Max output tokens |
| `.WithStatelessMode(bool)` | Disable conversation history accumulation |

## Stateless Mode

When enabled, each request is independent — no conversation history is sent or stored:

```csharp
service.StatelessMode = true;

// Equivalent:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Useful for one-off queries where you don't want history overhead.

## One-Shot Queries

These extension methods run a single query without affecting or using conversation history:

```csharp
// Text prompt
string response = await service.AskOnceAsync("What is 2+2?");

// Message (multimodal)
string response = await service.AskOnceAsync(message);

// Image from file path
string response = await service.AskOnceWithImageAsync("Describe this", "photo.jpg");
```

## Switching Models

Change model mid-session while preserving conversation history:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// Or via extension method — clears history and starts fresh:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Managing Multiple Conversations

A single service instance can hold multiple independent conversation threads:

```csharp
// Start a new conversation block
var chat1 = service.AddNewChat();

// Switch to a different block
service.SetActivateChat(chat2Id);

// Access all blocks
var allChats = service.ChatRequests;
```

## Inspecting Conversation State

Retrieve the last assistant response or a quick summary of the current session:

```csharp
// Get the last assistant message (or null if none)
string? lastReply = service.GetLastAssistantResponse();

// Get a text summary of the current service state
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: You are a helpful assistant.
```

## Copying Service Configuration

Clone all settings from another service instance (without conversation history):

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
