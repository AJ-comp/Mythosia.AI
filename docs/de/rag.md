# RAG (Retrieval-Augmented Generation)

RAG ermöglicht es dem Modell, Fragen auf Basis deiner eigenen Dokumente zu beantworten, indem zur Abfragezeit relevante Abschnitte abgerufen werden.

## Installation

```bash
dotnet add package Mythosia.AI.Rag
```

## Schnellstart

Verwende `.WithRag()` auf einem beliebigen `IAIService`, um RAG mit einer Fluent-API zu aktivieren:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("handbuch.txt")
        .AddDocument("richtlinie.txt")
    );

var response = await service.GetCompletionAsync("Was ist die Rückgaberichtlinie?");
```

Die Dokumente werden automatisch aufgeteilt, eingebettet und gespeichert. Bei der Abfrage werden die relevantesten Abschnitte abgerufen und in den Prompt injiziert.

## Dokumente hinzufügen

Mehrere Quellentypen werden unterstützt:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // lokale Datei
    .AddUrl("https://example.com/dok.txt")        // URL
    .AddText("Inline-Inhalt kann hier rein.")      // direkte Zeichenkette
)
```

## Benutzerdefinierter Embedding-Anbieter

Standardmäßig nutzt RAG den integrierten lokalen Embedding-Anbieter. Für ein dediziertes Embedding-Modell:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("wissensdatenbank.txt")
    );
```

## Benutzerdefinierter Vektorspeicher

Standardmäßig wird ein In-Memory-Speicher verwendet. Für den Produktivbetrieb binde einen persistenten Vektorspeicher ein:

```csharp
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("grosser-korpus.txt")
    );
```

## Abfrageoptionen

Das Retrieval-Verhalten pro Abfrage feinjustieren:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,              // Anzahl der abzurufenden Abschnitte
        MinScore = 0.7         // Minimale Ähnlichkeitsbewertung
    }
};

var response = await service.GetCompletionAsync("Deine Frage", options: options);
```

## Nächste Schritte

- [Hybridsuche](rag-hybrid-search.md) — semantische und Stichwortsuche kombinieren
- [Query-Rewriting](rag-query-rewriting.md) — Abfragen mit Gesprächskontext optimieren
- [Re-Ranking](rag-reranking.md) — Suchergebnis-Genauigkeit weiter verbessern
- [Pipeline-Anpassung](rag-pipeline.md) — feingranulare Steuerung des RAG-Prozesses
- [Agentisches RAG](rag-agentic.md) — AI entscheidet selbst, wann und was gesucht wird
- [Vektorspeicher](vectordb-overview.md) — persistente Speicher einrichten
- [Text-Splitter](text-splitters.md) — Anpassen der Dokument-Segmentierung
