# Re-ranking & Retrieval Tuning

## Why Re-ranking?

Vector search returns candidates sorted by embedding similarity, but embedding similarity is an **approximation**. A chunk that scores 0.82 might actually be more relevant than one scoring 0.85 — the embedding just couldn't tell them apart.

A **re-ranker** takes the initial candidate list and scores each chunk against the original query with a more powerful model, producing a much more accurate relevance ordering. This is especially valuable when:

- Your corpus contains many similar-looking chunks (e.g. FAQ entries)
- The top results from vector search feel "close but not quite right"
- You need high-precision answers for critical use cases

## Re-ranker Options

### LLM Reranker

Uses your AI service to score results. Effective but adds latency:

```csharp
.WithRag(rag => rag
    .UseLlmReranker(aiService)
    .AddDocument("corpus.txt")
)
```

### Cohere Reranker

Calls the Cohere Rerank API — fast and accurate:

```csharp
.WithRag(rag => rag
    .UseCohereReranker(cohereApiKey)
    .AddDocument("corpus.txt")
)
```

### vLLM Reranker

Uses a locally hosted vLLM reranking endpoint:

```csharp
.WithRag(rag => rag
    .UseVllmReranker("http://localhost:8000")
    .AddDocument("corpus.txt")
)
```

## Retrieval Parameters

Control how many candidates are retrieved and how they are filtered before final selection:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // Final number of chunks returned
    .WithRetrievalMultiplier(3)    // Retrieve topK × 3 candidates (for reranking)
    .WithMinScore(0.6)             // Minimum similarity score
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — how many chunks end up in the LLM context
- **`RetrievalMultiplier`** — cast a wider net so the reranker has more to work with. A multiplier of 3 means 15 candidates are fetched, then the best 5 survive reranking.
- **`MinScore`** — discard anything below this similarity threshold, even if fewer than `TopK` chunks remain

## Final Selection Mode

When a reranker is used, choose how the final ranking score is calculated:

```csharp
using Mythosia.AI.Rag;

// Default: trust reranker scores only
.WithFinalSelectionMode(RagFinalSelectionMode.RerankerOnly)

// Blend retrieval score and reranker score
.WithFinalSelectionMode(RagFinalSelectionMode.WeightedBlend)
.WithRetrievalWeightBlend(0.65)  // 65% retrieval, 35% reranker
```

**`RerankerOnly`** is the safe default — the reranker's judgment completely replaces the initial retrieval score.

**`WeightedBlend`** preserves the original retrieval signal while incorporating reranker judgment. This can help when your vector embeddings are already high-quality and you want the reranker to act as a tiebreaker rather than a full override.
