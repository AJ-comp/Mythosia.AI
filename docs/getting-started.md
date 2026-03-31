# Getting Started

## Installation

Install the core package:

```bash
dotnet add package Mythosia.AI
```

If you plan to use streaming with LINQ operators (e.g. `ToListAsync`), also add:

```bash
dotnet add package System.Linq.Async
```

## Your First Completion

Pick a provider and create a service instance with your API key and an `HttpClient`:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new ChatGptService("your-openai-api-key", http);

// Anthropic
// var service = new ClaudeService("your-anthropic-api-key", http);

// Google
// var service = new GeminiService("your-google-api-key", http);
```

Then call `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("Hello!");
Console.WriteLine(response);
```

## Choosing a Model

Each service defaults to a sensible model, but you can specify one explicitly:

```csharp
var service = new ChatGptService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

See the [API Reference](../api/Mythosia.AI.Models.AIModels.yml) for all available model constants.

## Next Steps

- [Basic Completions](completions.md) — system prompts, conversation history, multimodal
- [Streaming](streaming.md) — token-by-token output and reasoning streaming
- [Function Calling](function-calling.md) — let the model call your code
- [Structured Output](structured-output.md) — deserialize responses into C# types
