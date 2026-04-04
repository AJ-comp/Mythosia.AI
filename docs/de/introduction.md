# Einführung

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
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, o3-Serie |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / 3-Serie |
| **xAI** | Grok 3, Grok 4-Serie |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## Architekturübersicht

```
Mythosia.AI.Rag                 ← RAG-Pipeline, Orchestrierung
    └── Mythosia.AI             ← Kern-KI-Services (alle Anbieter)
        └── Mythosia.AI.Abstractions   ← IAIService-Interface

Mythosia.VectorDb.*             ← Vektorspeicher (einer oder mehrere)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← Dokument-Loader (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
