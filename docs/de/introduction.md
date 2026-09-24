# Einführung

> Grok 4.7 ist eine noch unveröffentlichte Ergänzung; siehe [Modellwahl, Reasoning und Verarbeitungsgeschwindigkeit](providers.md#grok-47).

> GPT-6 Sol/Luna sind noch nicht veröffentlicht. Siehe [Modellwahl und Voraussetzungen](providers.md#gpt-6-sol-luna).

Mythosia.AI ist eine modulare .NET-KI-Bibliothek, die eine einheitliche Schnittstelle für die Arbeit mit verschiedenen KI-Anbietern, RAG-Pipelines, Dokument-Loadern und Vektordatenbanken bietet.

## Warum Mythosia.AI?

Die SDKs der meisten KI-Anbieter haben unterschiedliche APIs, was den Wechsel zwischen Anbietern oder das Kombinieren von Features umständlich macht. Mythosia.AI kapselt all das hinter einem einzigen `IAIService`-Interface — dein Anwendungscode bleibt gleich, egal welches Modell oder welchen Anbieter du verwendest.

## Paketstruktur

Du installierst nur, was du brauchst:

| Schritt | Paket | Zweck |
|:----:|---------|---------|
| **1** | `Mythosia.AI` | Einstiegspunkt — Textvervollständigung, Streaming, Funktionsaufruf, strukturierte Ausgabe |
| **2** | `Mythosia.AI.Rag` | Für RAG — Splitter, Embeddings, Hybridsuche, Re-Ranking |
| **3** | `Mythosia.VectorDb.*` | Für Produktiv-Vektorspeicher — Postgres, Qdrant oder Pinecone |

## Unterstützte Anbieter

Alle Anbieter sind im Kernpaket `Mythosia.AI` enthalten (außer Alibaba):

| Anbieter | Modelle |
|----------|--------|
| **OpenAI** | GPT-6 Astra / Sol / Luna, GPT-5.1–5.6, GPT-4.1, GPT-4o |
| **Anthropic** | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (limited), Opus / Sonnet 5 and 4.x, Haiku 4.5 |
| **Google** | Gemini 3.8 / 3.7 / 3.6 Flash, Gemini 3.5 / 3.1 / 3, Gemini 2.5 |
| **xAI** | Grok 4.7 / 4.6 / 4.5 / 4.3 / 4.20, Grok Build |
| **DeepSeek** | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | Agent-API-Presets und `perplexity/sonar` |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Architekturübersicht

```
Mythosia.AI                     ← Kern-KI-Services (alle Anbieter)
    └── Mythosia.AI.Abstractions   ← IAIService-Interface

Mythosia.AI.Rag                 ← RAG-Pipeline, Orchestrierung
    ├── Mythosia.AI.Abstractions
    ├── Mythosia.AI.Rag.Abstractions
    │   └── Mythosia.VectorDb.Abstractions
    ├── Mythosia.Documents.Office / Mythosia.Documents.Pdf
    │   └── Mythosia.Documents.Abstractions
    └── Mythosia.VectorDb.InMemory
        ├── Mythosia.VectorDb.Abstractions
        └── Mythosia.AI.Rag.Abstractions

Mythosia.VectorDb.*             ← Vektorspeicher (einer oder mehrere)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Dokument-Loader (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
