# Embedding

> 📍 **Fragen & Antworten Pipeline:** [Query-Umschreibung](rag-query-rewriting.md) → **`Embedding`** → [Filtering](rag-filtering.md) → [Retrieval](rag-hybrid-search.md) → [Re-Ranking](rag-reranking.md) → [Kontextaufbau](rag-context-build.md)

## Was ist Embedding?

Embedding wandelt Text in **numerische Vektoren** (Zahlenarrays) um, die die Bedeutung erfassen. In diesem Vektorraum landen **Texte mit ähnlicher Bedeutung nah beieinander**.

Stellen Sie sich vor, Sie platzieren Städte auf einer Karte: geographisch nahe Städte liegen auch auf der Karte nah beieinander. Genauso erzeugen „Wie kündige ich mein Abo?" und „Ich möchte meine Mitgliedschaft beenden" ähnliche Vektoren — obwohl sie völlig andere Wörter verwenden.

Im RAG-Pipeline geschieht Embedding an zwei Stellen:

1. **Dokumentenindexierung** — jeder Chunk wird vektorisiert und gespeichert
2. **Query-Zeit** — die Benutzerfrage wird vektorisiert für den Ähnlichkeitsvergleich

Diese Seite konzentriert sich auf das Query-Zeit-Embedding (Schritt 2).

## Integrierte Anbieter

### OpenAI

```csharp
var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Builder-Kurzform:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (lokal)

Embeddings lokal ausführen mit [Ollama](https://ollama.com/):

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

### vLLM (selbst gehostet)

Für Teams mit eigenem [vLLM](https://docs.vllm.ai/)-Server:

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (ohne API)

Leichtgewichtiger Anbieter basierend auf Feature-Hashing. Kein API-Schlüssel oder externer Dienst erforderlich — allerdings ist die Embedding-Qualität deutlich schlechter als bei neuronalen Modellen und wird daher **nicht für den produktiven Einsatz empfohlen**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **Tipp:** Verwenden Sie stattdessen `OpenAIEmbeddingProvider` mit dem Modell `text-embedding-3-small`. Es ist extrem günstig — nahezu kostenlos — und liefert deutlich bessere Ergebnisse.

## Batch-Verarbeitung

Bei der Indexierung werden Chunks in Batches verarbeitet:

```csharp
var options = new RagPipelineOptions
{
    EmbeddingBatchSize = 100   // Standard: 100 Chunks pro API-Aufruf
};
```

## Dimensionen

| Anbieter | Modell | Standard-Dimensionen |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (Feature-Hashing) | 1024 |

## Eigener Embedding-Anbieter

Implementieren Sie `IEmbeddingProvider` für andere Dienste:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Hier Ihre API aufrufen
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Batch-Aufruf
    }
}
```

## Interner Ablauf

```
Benutzerfrage (string) → EmbeddingProvider.GetEmbeddingAsync() → Query-Vektor (float[])
```

Dieser Vektor wird an die nächste Stufe ([Filtering](rag-filtering.md)) und dann an das [Retrieval](rag-hybrid-search.md) weitergegeben.

## Nächste Schritte

- [Filtering](rag-filtering.md) — Chunks eingrenzen
- [Hybridsuche](rag-hybrid-search.md) — Vektor- und Stichwortsuche kombinieren
- [Pipeline-Anpassung](rag-pipeline.md) — Embedding-Anbieter serviceübergreifend teilen
