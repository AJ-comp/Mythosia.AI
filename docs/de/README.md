<div align="center">

🌐 [English](../../README.md) · [한국어](../ko/README.md) · [日本語](../ja/README.md) · [Français](../fr/README.md) · [Deutsch](README.md) · [Русский](../ru/README.md) · [Українська](../uk/README.md) · [简体中文](../zh-Hans/README.md) · [繁體中文](../zh-Hant/README.md)

<br>

[![OPEN SOURCE](https://img.shields.io/badge/OPEN%20SOURCE%20·%20.NET%20·%20NUGET-111827?style=flat-square&labelColor=111827&color=111827)](https://github.com/AJ-comp/Mythosia.AI)

<img width="694" height="181" alt="title_60" src="https://github.com/user-attachments/assets/57fd8c63-5b9b-46f6-be30-274354808c0d" />

### Eine modulare .NET-AI-Bibliothek für intelligente Anwendungen

**Anbieter wechseln, RAG hinzufügen, Dokumente laden — alles mit einer einheitlichen API.**

<br>

[![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg?style=for-the-badge&logo=nuget&label=NuGet&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Downloads](https://img.shields.io/nuget/dt/Mythosia.AI.svg?style=for-the-badge&logo=nuget&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Docs](https://img.shields.io/badge/Docs-GitHub%20Pages-0ea5e9?style=for-the-badge&logo=readthedocs&logoColor=white)](https://aj-comp.github.io/Mythosia.AI/)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-6d28d9?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)

<br>

**[📖 Erste Schritte](https://aj-comp.github.io/Mythosia.AI/)** &nbsp;·&nbsp; **[API-Referenz](https://aj-comp.github.io/Mythosia.AI/api/)** &nbsp;·&nbsp; **[GitHub ↗](https://github.com/AJ-comp/Mythosia.AI)**

<br>

</div>

---

### Welche Pakete werden benötigt?

```
dotnet add package Mythosia.AI                    # hier starten (mehr brauchen Sie nicht)
dotnet add package Mythosia.AI.Rag                # optional: wenn Sie RAG benötigen
dotnet add package Mythosia.VectorDb.Postgres     # optional: wenn Sie einen produktiven Vector Store benötigen
```

| Schritt | Paket | Wann |
| :--: | --- | --- |
| **1** | **`Mythosia.AI`** | **Hier starten** — Completion, Streaming, Funktionsaufrufe, strukturierte Ausgabe (OpenAI / Claude / Gemini / Grok / DeepSeek / Perplexity) |
| **2** | **`Mythosia.AI.Rag`** | Wenn Sie RAG benötigen — Textsplitting, Embeddings, hybride Suche, Reranking, InMemory Vector Store und Dokumentenlader (Word / Excel / PowerPoint / PDF) |
| **3** | **`Mythosia.VectorDb.Postgres`** / **`Qdrant`** / **`Pinecone`** | Wenn Sie statt InMemory einen produktiven Vector Store benötigen — wählen Sie einen |

## Architektur

```mermaid
graph TD
    subgraph "🔗 Orchestration Layer"
        Rag["<b>Mythosia.AI.Rag</b><br/>RagPipeline · TextSplitters<br/>EmbeddingProviders · HybridSearch · Reranking<br/><i>netstandard2.1 · v6.2.0</i>"]
    end

    subgraph "⚡ Core AI"
        AI["<b>Mythosia.AI</b><br/>OpenAI · Anthropic · Google<br/>xAI · DeepSeek · Perplexity<br/><i>netstandard2.1 · v5.2.0</i>"]
        AIAbs["<b>Mythosia.AI.Abstractions</b><br/>IAIService · shared models<br/><i>netstandard2.1 · v1.0.0</i>"]
    end

    subgraph "🔌 Provider Packages"
        Alibaba["<b>Mythosia.AI.Providers.Alibaba</b><br/>Qwen / Alibaba provider package<br/><i>netstandard2.1 · v1.1.0</i>"]
    end

    subgraph "📄 Document Loaders"
        Office["<b>Mythosia.Documents.Office</b><br/>Word · Excel · PowerPoint<br/><i>netstandard2.1 · v1.0.0</i>"]
        Pdf["<b>Mythosia.Documents.Pdf</b><br/>PdfPig Parser<br/><i>netstandard2.1 · v1.0.0</i>"]
    end

    subgraph "📐 Composite Abstractions"
        RagAbs["<b>Mythosia.AI.Rag.Abstractions</b><br/>ITextSplitter · IEmbeddingProvider<br/>IContextBuilder · IRetrievalStrategy · IReranker<br/>RagDocument<br/><i>netstandard2.1 · v5.1.0</i>"]
    end

    subgraph "🗄️ Vector Stores — einen oder mehrere wählen"
        InMem["<b>Mythosia.VectorDb.InMemory</b><br/>Cosine Similarity · TopK · BM25<br/><i>netstandard2.1 · v2.3.0</i>"]
        Pine["<b>Mythosia.VectorDb.Pinecone</b><br/>Managed Index · Namespace · Scope<br/><i>netstandard2.1 · v1.3.0</i>"]
        Pg["<b>Mythosia.VectorDb.Postgres</b><br/>pgvector · HNSW · IVFFlat · HybridSearch<br/><i>net10.0 · v10.5.0</i>"]
        Qd["<b>Mythosia.VectorDb.Qdrant</b><br/>gRPC · Cosine · Euclidean · Dot · HybridSearch<br/><i>netstandard2.1 · v2.3.0</i>"]
    end

    subgraph "🧱 Foundation Abstractions"
        LoaderAbs["<b>Mythosia.Documents.Abstractions</b><br/>IDocumentLoader · IDocumentParser<br/>ParsedDocument · DoclingDocument<br/><i>netstandard2.1 · v1.0.0</i>"]
        VdbAbs["<b>Mythosia.VectorDb.Abstractions</b><br/>IVectorStore · HybridSearchAsync · VectorRecord<br/>VectorFilter · VectorSearchResult · Bm25Tokenizer<br/><i>netstandard2.1 · v2.4.0</i>"]
    end

    %% Core AI internal
    AI --> AIAbs

    %% Orchestration → dependencies
    Rag --> AIAbs
    Rag --> Office
    Rag --> Pdf
    Rag --> RagAbs
    Rag --> InMem

    %% Provider packages → core
    Alibaba --> AI

    %% Composite → Foundation
    RagAbs --> VdbAbs

    %% Loaders → Foundation
    Office --> LoaderAbs
    Pdf --> LoaderAbs

    %% VectorStores → Foundation
    InMem --> VdbAbs
    Pine --> VdbAbs
    Pg --> VdbAbs
    Qd --> VdbAbs
```

## Demo / Testumgebung (Chat UI)

Dieses Repository enthält eine auf Mythosia.AI basierende Beispiel-Chat-UI — starten Sie Mythosia.AI.Samples.ChatUi, um die Bibliothek in Aktion zu testen.

### Beispiel ausführen

Führen Sie **`Mythosia.AI.Samples.ChatUi`** lokal aus:

```bash
# vom Repository-Root
dotnet run --project samples/Mythosia.AI.Samples.ChatUi
```

https://github.com/user-attachments/assets/62094afe-9add-4c14-b818-6b31f200dc01


## Schnellstart

### Einfache AI-Completion

```csharp
using Mythosia.AI;

var service = new OpenAIService(apiKey, httpClient);
var response = await service.GetCompletionAsync("Hello!");
```

### Streaming

```csharp
await foreach (var token in service.StreamAsync("Tell me a story"))
{
    Console.Write(token);
}
```

### Reasoning-Streaming

Alle Anbieter mit Reasoning-Unterstützung (OpenAI, Claude, Gemini, Grok, DeepSeek) verwenden dasselbe Streaming-Muster:

```csharp
await foreach (var content in service.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Think] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### Funktionsaufrufe

```csharp
var service = new OpenAIService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => $"The weather in {location} is sunny, 22C"
    );

var response = await service.GetCompletionAsync("What's the weather in Seoul?");
```

### Strukturierte Ausgabe (einfach)

```csharp
// LLM-Antworten direkt in C#-POCOs deserialisieren mit automatischer Wiederherstellung
var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");
```

### Strukturierte Ausgabe (Liste)

```csharp
// Sammlungstypen funktionieren direkt — kein Wrapper-DTO nötig
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "Extract all entities from this document...");
```

### Strukturierte Ausgabe (Streaming)

```csharp
// Text-Chunks in Echtzeit streamen + finales deserialisiertes Objekt erhalten
var run = service.BeginStream(prompt).As<MyDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);          // Echtzeit-UI

MyDto dto = await run.Result;      // geparst und automatisch repariert
```

### Gesprächszusammenfassungs-Richtlinie

```csharp
// Alte Nachrichten automatisch zusammenfassen, wenn das Gespräch lang wird
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// Tokenbasierter Trigger
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// Einfach wie gewohnt verwenden — die Zusammenfassung erfolgt automatisch
await service.GetCompletionAsync("Continue our conversation...");

// Beim Streaming die Zusammenfassungsrichtlinie vor StreamAsync() explizit anwenden
await service.ApplySummaryPolicyIfNeededAsync();
await foreach (var chunk in service.StreamAsync("Continue..."))
    Console.Write(chunk.Content);

// Zusammenfassung sitzungsübergreifend speichern/wiederherstellen
string saved = service.ConversationPolicy.CurrentSummary;
policy.LoadSummary(saved);
```

### RAG (Retrieval-Augmented Generation)

```bash
dotnet add package Mythosia.AI.Rag
```

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

## Unterstützte Anbieter

| Anbieter | Paket | Modelle |
| --- | --- | --- |
| **OpenAI** | `Mythosia.AI` | GPT-5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro / 5.3 Codex / 5.2 / 5.2 Pro / 5.2 Codex / 5.1 / 5 / 5 Mini / 5 Nano, GPT-4.1 / 4.1 Mini / 4.1 Nano, GPT-4o / 4o Mini, o3 / o3 Pro |
| **Anthropic** | `Mythosia.AI` | Claude Opus 4.6 / 4.5 / 4.1 / 4, Sonnet 4.6 / 4.5 / 4, Haiku 4.5 |
| **Google** | `Mythosia.AI` | Gemini 3 Flash/Pro Preview, Gemini 2.5 Pro/Flash/Flash-Lite |
| **xAI** | `Mythosia.AI` | Grok 4, Grok 4.1 Fast, Grok 3, Grok 3 Mini |
| **DeepSeek** | `Mythosia.AI` | Chat, Reasoner |
| **Perplexity** | `Mythosia.AI` | Sonar, Sonar Pro, Sonar Reasoning |
| **Alibaba / Qwen** | `Mythosia.AI.Providers.Alibaba` | Qwen Max / Plus / Turbo / Qwen3 / Qwen3.5 Varianten |

## Pakete

### Kern

| Paket | NuGet | Beschreibung |
| --- | --- | --- |
| [Mythosia.AI](../../src/core/Mythosia.AI/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI) | Kernbibliothek — integrierte Anbieter, Streaming, Funktionsaufrufe und multimodale Unterstützung |
| [Mythosia.AI.Abstractions](../../src/core/Mythosia.AI.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Abstractions) | `IAIService`-Schnittstelle und gemeinsame Modelle — leichtes Vertragspaket für Bibliotheken |
| [Mythosia.AI.Providers.Alibaba](../../src/core/Mythosia.AI.Providers.Alibaba/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Providers.Alibaba.svg)](https://www.nuget.org/packages/Mythosia.AI.Providers.Alibaba) | Alibaba / Qwen-Anbieterpaket, basierend auf `Mythosia.AI` |

### RAG

| Paket | NuGet | Beschreibung |
| --- | --- | --- |
| [Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag) | Fluent-RAG-Erweiterung für IAIService mit `.WithRag()`-API |
| [Mythosia.AI.Rag.Abstractions](../../src/rag/Mythosia.AI.Rag.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag.Abstractions) | Schnittstellen und Modelle für RAG-Pipeline-Komponenten |

### Dokumentenlader

| Paket | NuGet | Beschreibung |
| --- | --- | --- |
| [Mythosia.Documents.Abstractions](../../src/loaders/Mythosia.Documents.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | Schnittstellen und Modelle der Dokumentenlader (`IDocumentLoader`, `DoclingDocument`) |
| [Mythosia.Documents.Office](../../src/loaders/Mythosia.Documents.Office/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Office.svg)](https://www.nuget.org/packages/Mythosia.Documents.Office) | OpenXml-Parser für Word / Excel / PowerPoint |
| [Mythosia.Documents.Pdf](../../src/loaders/Mythosia.Documents.Pdf/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Pdf.svg)](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | PDF-Parser via PdfPig |

### Vector Stores

> **Einen oder mehrere wählen** — alle implementieren `IVectorStore` aus dem Abstractions-Paket.

| Paket | NuGet | Beschreibung |
| --- | --- | --- |
| [Mythosia.VectorDb.Abstractions](../../src/vectordb/Mythosia.VectorDb.Abstractions/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Abstractions) | `IVectorStore` · `VectorRecord` · `VectorFilter`-Verträge |
| [Mythosia.VectorDb.InMemory](../../src/vectordb/Mythosia.VectorDb.InMemory/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.InMemory.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.InMemory) | In-Memory-Store — keine Infrastruktur nötig, ideal für Prototyping |
| [Mythosia.VectorDb.Pinecone](../../src/vectordb/Mythosia.VectorDb.Pinecone/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Pinecone.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Pinecone) | Pinecone HTTP API — Index-/Namespace-/Scope-Isolierung für verwaltete Vektordatenbank |
| [Mythosia.VectorDb.Postgres](../../src/vectordb/Mythosia.VectorDb.Postgres/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Postgres.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Postgres) | PostgreSQL + pgvector — HNSW / IVFFlat-Indizes, produktionsbereit |
| [Mythosia.VectorDb.Qdrant](../../src/vectordb/Mythosia.VectorDb.Qdrant/) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Qdrant.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Qdrant) | Qdrant gRPC-Client — Cosine / Euclidean / Dot, automatische Bereitstellung |

## Repository-Struktur

```text
src/
  core/
    Mythosia.AI/                        # Kern-AI-Dienstbibliothek
    Mythosia.AI.Abstractions/           # IAIService-Schnittstelle und gemeinsame Modelle
    Mythosia.AI.Providers.Alibaba/      # Alibaba / Qwen-Anbieterpaket
  loaders/
    Mythosia.Documents.Abstractions/    # Dokumentenlader-Verträge (IDocumentLoader, DoclingDocument)
    Mythosia.Documents.Office/          # Office-Dokumentenlader (Word/Excel/PowerPoint)
    Mythosia.Documents.Pdf/             # PDF-Dokumentenlader
  rag/
    Mythosia.AI.Rag/                    # RAG Fluent API und Pipeline
    Mythosia.AI.Rag.Abstractions/       # RAG-Schnittstellen und -Modelle (RagDocument)
  vectordb/
    Mythosia.VectorDb.Abstractions/     # Vector-Store-Verträge
    Mythosia.VectorDb.InMemory/         # In-Memory Vector Store
    Mythosia.VectorDb.Pinecone/         # Pinecone Vector Store
    Mythosia.VectorDb.Postgres/         # PostgreSQL + pgvector Store
    Mythosia.VectorDb.Qdrant/           # Qdrant Vector Store
samples/                                # Beispielanwendungen
tests/                                  # Unit-/Integrationstestprojekte
```

## Installation

```bash
dotnet add package Mythosia.AI
```

Für erweiterte LINQ-Operationen mit Streams:

```bash
dotnet add package System.Linq.Async
```

## Dokumentation

- [Grundlegende Nutzungsanleitung](https://github.com/AJ-comp/Mythosia.AI/wiki)
- [Mythosia.AI README](../../src/core/Mythosia.AI/README.md)  Vollständige API-Referenz mit Funktionsaufrufen, Streaming und Modellkonfiguration
- [Mythosia.AI.Rag README](../../src/rag/Mythosia.AI.Rag/README.md)  RAG-Pipeline-Nutzung und eigene Implementierungen
- Lader-Leitfaden: [EN](../../src/loaders/Mythosia.Documents.Abstractions/docs/en/loaders.md) · [KO](../../src/loaders/Mythosia.Documents.Abstractions/docs/ko/loaders.md) · [JA](../../src/loaders/Mythosia.Documents.Abstractions/docs/ja/loaders.md) · [ZH](../../src/loaders/Mythosia.Documents.Abstractions/docs/zh/loaders.md)
- [Versionshinweise](../../src/core/Mythosia.AI/RELEASE_NOTES.md)

## Lizenz

Dieses Projekt steht unter der [MIT-Lizenz](../../LICENSE).

## Ursprung

Dieses Projekt war ursprünglich Teil von [Mythosia](https://github.com/AJ-comp/Mythosia).
