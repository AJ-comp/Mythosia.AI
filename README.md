# Mythosia.AI

[![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI)

Unified .NET AI library with multi-provider support (OpenAI, Anthropic, Google, DeepSeek, Perplexity, xAI) and RAG extensions.

## Supported Providers

| Provider | Models |
| --- | --- |
| **OpenAI** | GPT-5.2 / 5.2 Codex / 5.1 / 5, GPT-4.1, GPT-4o, o3 |
| **Anthropic** | Claude Opus 4.6 / 4.5 / 4.1 / 4, Sonnet 4.6 / 4.5 / 4, Haiku 4.5 |
| **Google** | Gemini 3 Flash/Pro Preview, Gemini 2.5 Pro/Flash/Flash-Lite |
| **xAI** | Grok 4, Grok 4.1 Fast, Grok 3, Grok 3 Mini |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning |

## Packages

### Core

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.AI](Mythosia.AI/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI) | Core library — multi-provider AI service with streaming, function calling, and multimodal support |

### RAG

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.AI.Rag](Mythosia.AI.Rag/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag) | Fluent RAG extension for AIService with `.WithRag()` API |
| [Mythosia.AI.Rag.Abstractions](Mythosia.AI.Rag.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag.Abstractions) | Interfaces and models for RAG pipeline components |

### Document Loaders

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.AI.Loaders.Abstractions](Mythosia.AI.Loaders.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Loaders.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Loaders.Abstractions) | Document loader interfaces and models |
| [Mythosia.AI.Loaders.Office](Mythosia.AI.Loaders.Office/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Loaders.Office.svg)](https://www.nuget.org/packages/Mythosia.AI.Loaders.Office) | OpenXml parsers for Word / Excel / PowerPoint |
| [Mythosia.AI.Loaders.Pdf](Mythosia.AI.Loaders.Pdf/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Loaders.Pdf.svg)](https://www.nuget.org/packages/Mythosia.AI.Loaders.Pdf) | PDF parser via PdfPig |

### Vector Stores

> **Pick one or more** — all implement `IVectorStore` from the Abstractions package.

| Package | NuGet | Description |
| --- | --- | --- |
| [Mythosia.VectorDb.Abstractions](vectordb/Mythosia.VectorDb.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Abstractions) | `IVectorStore` · `VectorRecord` · `VectorFilter` contracts |
| [Mythosia.VectorDb.InMemory](vectordb/Mythosia.VectorDb.InMemory/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.InMemory.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.InMemory) | In-memory store — zero infrastructure, great for prototyping |
| [Mythosia.VectorDb.Postgres](vectordb/Mythosia.VectorDb.Postgres/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Postgres.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Postgres) | PostgreSQL + pgvector — HNSW / IVFFlat indexes, production-ready |
| [Mythosia.VectorDb.Qdrant](vectordb/Mythosia.VectorDb.Qdrant/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Qdrant.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Qdrant) | Qdrant gRPC client — Cosine / Euclidean / Dot, auto-provisioning |

## Architecture

```mermaid
graph TD
    subgraph "🔗 Orchestration Layer"
        Rag["<b>Mythosia.AI.Rag</b><br/>RagPipeline · TextSplitters<br/>EmbeddingProviders · Diagnostics<br/><i>netstandard2.1 · v2.0.0</i>"]
    end

    subgraph "📐 Composite Abstractions"
        RagAbs["<b>Mythosia.AI.Rag.Abstractions</b><br/>ITextSplitter · IEmbeddingProvider<br/>IContextBuilder<br/><i>netstandard2.1 · v2.0.0</i>"]
    end

    subgraph "⚡ Core AI"
        AI["<b>Mythosia.AI</b><br/>ChatGPT · Claude · Gemini<br/>Grok · DeepSeek · Sonar<br/><i>netstandard2.1 · v4.6.2</i>"]
    end

    subgraph "📄 Document Loaders"
        Office["<b>Mythosia.AI.Loaders.Office</b><br/>Word · Excel · PowerPoint<br/><i>netstandard2.1 · v1.1.0</i>"]
        Pdf["<b>Mythosia.AI.Loaders.Pdf</b><br/>PdfPig Parser<br/><i>netstandard2.1 · v1.1.0</i>"]
    end

    subgraph "🗄️ Vector Stores — pick one or more"
        InMem["<b>Mythosia.VectorDb.InMemory</b><br/>Cosine Similarity · TopK<br/><i>netstandard2.1 · v1.1.0</i>"]
        Pg["<b>Mythosia.VectorDb.Postgres</b><br/>pgvector · HNSW · IVFFlat<br/><i>net10.0 · v10.1.0</i>"]
        Qd["<b>Mythosia.VectorDb.Qdrant</b><br/>gRPC · Cosine · Euclidean · Dot<br/><i>netstandard2.1 · v1.0.0</i>"]
    end

    subgraph "🧱 Foundation Abstractions"
        LoaderAbs["<b>Mythosia.AI.Loaders.Abstractions</b><br/>IDocumentLoader · IDocumentParser<br/>ParsedDocument · DoclingDocument<br/><i>netstandard2.1 · v1.2.0</i>"]
        VdbAbs["<b>Mythosia.VectorDb.Abstractions</b><br/>IVectorStore · VectorRecord<br/>VectorFilter · VectorSearchResult<br/><i>netstandard2.1 · v1.1.0</i>"]
    end

    %% Orchestration → dependencies
    Rag --> RagAbs
    Rag --> AI
    Rag --> Office
    Rag --> Pdf
    Rag --> InMem

    %% Composite → Foundation
    RagAbs --> LoaderAbs
    RagAbs --> VdbAbs

    %% Loaders → Foundation
    Office --> LoaderAbs
    Pdf --> LoaderAbs

    %% VectorStores → Foundation
    InMem --> VdbAbs
    Pg --> VdbAbs
    Qd --> VdbAbs
```

## Demo / Test Bed (Chat UI)

This repository includes a sample Chat UI built on Mythosia.AI — launch Mythosia.AI.Samples.ChatUi to test the library in action.

### Run the sample

Run **`Mythosia.AI.Samples.ChatUi`** to try it locally:

```bash
# from repo root
dotnet run --project Mythosia.AI.Samples.ChatUi
```

https://github.com/user-attachments/assets/62094afe-9add-4c14-b818-6b31f200dc01


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

### Reasoning Streaming

All reasoning-capable providers (OpenAI, Claude, Gemini, Grok, DeepSeek) share the same streaming pattern:

```csharp
await foreach (var content in service.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Think] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
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

### Structured Output (Basic)

```csharp
// Deserialize LLM responses directly into C# POCOs with auto-recovery
var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");
```

### Structured Output (List)

```csharp
// Collection types work directly — no wrapper DTO needed
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "Extract all entities from this document...");
```

### Structured Output (Streaming)

```csharp
// Stream text chunks in real-time + get final deserialized object
var run = service.BeginStream(prompt).As<MyDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);          // real-time UI

MyDto dto = await run.Result;      // parsed & auto-repaired
```

### Conversation Summary Policy

```csharp
// Automatically summarize old messages when conversation gets long
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// Token-based trigger
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// Just use as normal — summarization happens automatically
await service.GetCompletionAsync("Continue our conversation...");

// For streaming, call summarization explicitly before StreamAsync()
await service.ApplySummaryPolicyIfNeededAsync();
await foreach (var chunk in service.StreamAsync("Continue..."))
    Console.Write(chunk.Content);

// Save/restore summary across sessions
string saved = service.ConversationPolicy.CurrentSummary;
policy.LoadSummary(saved);
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
- Loaders Guide: [EN](Mythosia.AI.Loaders.Abstractions/docs/en/loaders.md) · [KO](Mythosia.AI.Loaders.Abstractions/docs/ko/loaders.md) · [JA](Mythosia.AI.Loaders.Abstractions/docs/ja/loaders.md) · [ZH](Mythosia.AI.Loaders.Abstractions/docs/zh/loaders.md)
- [Release Notes](Mythosia.AI/RELEASE_NOTES.md)

## License

This project is licensed under the [MIT License](LICENSE).

## Originally

This project was originally part of [Mythosia](https://github.com/AJ-comp/Mythosia).

