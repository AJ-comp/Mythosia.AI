# Mythosia.AI

[![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI)

Unified .NET AI library with multi-provider support (OpenAI, Anthropic, Google, DeepSeek, Perplexity) and RAG extensions.

## Supported Providers

| Provider | Models |
| --- | --- |
| **OpenAI** | GPT-5.2 / 5.2 Codex / 5.1 / 5, GPT-4.1, GPT-4o, o3 |
| **Anthropic** | Claude Opus 4.6 / 4.5 / 4.1 / 4, Sonnet 4.5 / 4, Haiku 4.5 |
| **Google** | Gemini 3 Flash/Pro Preview, Gemini 2.5 Pro/Flash/Flash-Lite |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning |

## Packages

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.AI](Mythosia.AI/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI) | Core library — multi-provider AI service with streaming, function calling, and multimodal support |
| [Mythosia.AI.Rag](Mythosia.AI.Rag/) |  | Fluent RAG extension for AIService with `.WithRag()` API |
| [Mythosia.AI.Rag.Abstractions](Mythosia.AI.Rag.Abstractions/) |  | Interfaces and models for RAG pipeline components |
| [Mythosia.AI.VectorDB](Mythosia.AI.VectorDB/) |  | In-memory vector store with cosine similarity search |
| [Mythosia.AI.Loaders.Abstractions](Mythosia.AI.Loaders.Abstractions/) |  | Document loader interfaces and models |

## Quick Start

### Basic AI Completion

```csharp
using Mythosia.AI;

var service = new ChatGptService(apiKey, httpClient);
var response = await service.GetCompletionAsync("Hello!");
```

### Streaming

```csharp
await foreach (var token in service.StreamAsync("Tell me a story"))
{
    Console.Write(token);
}
```

### RAG (Retrieval-Augmented Generation)

```bash
dotnet add package Mythosia.AI.Rag
```

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

### Function Calling

```csharp
var service = new ChatGptService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => $"The weather in {location} is sunny, 22C"
    );

var response = await service.GetCompletionAsync("What's the weather in Seoul?");
```

## Repository Structure

```text
Mythosia.AI/                          # Core AI service library
Mythosia.AI.Rag/                      # RAG fluent API and pipeline
Mythosia.AI.Rag.Abstractions/         # RAG interfaces and models
Mythosia.AI.VectorDB/                 # In-memory vector store
Mythosia.AI.Loaders.Abstractions/     # Document loader contracts
tests/
  Mythosia.AI.Test/                   # Core AI service tests
  Mythosia.AI.Rag.Tests/             # RAG pipeline tests
```

## Installation

```bash
dotnet add package Mythosia.AI
```

For advanced LINQ operations with streams:

```bash
dotnet add package System.Linq.Async
```

## Documentation

- [Basic Usage Guide](https://github.com/AJ-comp/Mythosia.AI/wiki)
- [Mythosia.AI README](Mythosia.AI/README.md)  Full API reference with function calling, streaming, and model configuration
- [Mythosia.AI.Rag README](Mythosia.AI.Rag/README.md)  RAG pipeline usage and custom implementations
- [Release Notes](Mythosia.AI/RELEASE_NOTES.md)

## License

This project is licensed under the [MIT License](LICENSE).

## Originally

This project was originally part of [Mythosia](https://github.com/AJ-comp/Mythosia).

