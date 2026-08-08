# Getting Started

## Installation

Install the core package:

```bash
dotnet add package Mythosia.AI
```

Upgrading an existing application? Read [Migrating to Mythosia.AI 7](v7-migration.md) before changing package versions.

If you plan to use streaming with LINQ operators (e.g. `ToListAsync`), also add:

```bash
dotnet add package System.Linq.Async
```

## Your First Completion

Pick a provider and create a service instance with your API key and an `HttpClient`:

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

Then call `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("Hello!");
Console.WriteLine(response);
```

## Choosing a Model

Each service defaults to a sensible model, but you can specify one explicitly:

```csharp
var service = new OpenAIService("your-api-key", http);
service.ChangeModel(AIModels.OpenAI.Gpt4_1);
```

See the [API Reference](../api/Mythosia.AI.Models.AIModels.yml) for all available model constants.

## Next Steps

- [Basic Completions](completions.md) — system prompts, conversation history, multimodal
- [Streaming](streaming.md) — token-by-token output and reasoning streaming
- [Function Calling](function-calling.md) — let the model call your code
- [Structured Output](structured-output.md) — deserialize responses into C# types
