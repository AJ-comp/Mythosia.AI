# Introduction

Mythosia.AI is a modular .NET AI library that provides a unified interface for working with multiple AI providers, RAG pipelines, document loaders, and vector databases.

## Why Mythosia.AI?

Most AI provider SDKs expose different APIs, making it hard to swap providers or combine features. Mythosia.AI wraps them behind a single `IAIService` interface, so your application code stays the same regardless of which model or provider you use.

## Package Structure

You only install what you need:

| Step | Package | Purpose |
|:----:|---------|---------|
| **1** | `Mythosia.AI` | Start here — completions, streaming, function calling, structured output |
| **2** | `Mythosia.AI.Rag` | Add when you need RAG — splitters, embeddings, hybrid search, reranking |
| **3** | `Mythosia.VectorDb.*` | Add when you need a production vector store — Postgres, Qdrant, or Pinecone |

## Supported Providers

All providers are included in the core `Mythosia.AI` package (except Alibaba):

| Provider | Models |
|----------|--------|
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, o3 series |
| **Anthropic** | Claude Fable 5, Mythos 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 2.5 / 3 series |
| **xAI** | Grok 4 series, Grok Build |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning Pro |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Architecture Overview

```
Mythosia.AI                     ← Core AI services (all providers)
    └── Mythosia.AI.Abstractions   ← IAIService interface

Mythosia.AI.Rag                 ← RAG pipeline, orchestration
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← Vector stores (pick one or more)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Document loaders (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
