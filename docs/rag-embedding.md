# Embedding

> 📍 **Question Answering Pipeline:** [Query Rewriting](rag-query-rewriting.md) → **`Embedding`** → [Filtering](rag-filtering.md) → [Retrieval](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Context Build](rag-context-build.md)

## What is Embedding?

Embedding is the process of converting text into numerical vectors (arrays of numbers) that capture meaning. These vectors live in a high-dimensional space where **texts with similar meanings end up close together**.

Think of it like plotting cities on a map. Cities that are geographically close appear near each other on the map. Similarly, sentences like "How do I cancel my subscription?" and "I want to end my membership" produce vectors that are close together — even though they use completely different words.

In the RAG pipeline, embedding happens at two points:

1. **Document indexing** — each chunk is embedded and stored in the vector store
2. **Query time** — the user's question is embedded so it can be compared against stored chunks

This page focuses on the query-time embedding (step 2), which converts the user's question into a vector for similarity search.

## Built-in Embedding Providers

Mythosia.AI.Rag ships with four embedding providers. Choose one based on your needs:

### OpenAI Embedding

The most popular cloud-based option. High quality, requires an API key:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",   // default
    dimensions: 1536                    // default
);
```

You can also use the fluent builder shorthand:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (Local)

Run embeddings locally without sending data to the cloud. Requires [Ollama](https://ollama.com/) running on your machine:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",       // default
    dimensions: 1024,                    // default
    baseUrl: "http://localhost:11434"    // default
);
```

### vLLM (Self-hosted)

For teams running their own embedding server with [vLLM](https://docs.vllm.ai/):

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B", // default
    dimensions: 1024,                     // default
    baseUrl: "http://localhost:8002"      // default
);
```

### Local (No API Required)

A lightweight, zero-configuration provider based on feature hashing. No API key, no external service — but the embedding quality is significantly lower than neural models, so **it is not recommended for production use**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **Tip:** Use `OpenAIEmbeddingProvider` with the `text-embedding-3-small` model instead. It's extremely affordable — nearly free — and delivers far better results.

## Batch Processing

When indexing documents, the pipeline embeds chunks in batches to avoid sending thousands of texts in a single API call. The batch size is configurable:

```csharp
var options = new RagPipelineOptions
{
    EmbeddingBatchSize = 100   // default: 100 chunks per API call
};
```

A larger batch size means fewer API calls but higher memory usage per call. If you're hitting API rate limits or memory issues, try reducing this value.

## Dimensions

The `Dimensions` property controls the size of each embedding vector. This is critical because:

- **Vector store must match** — if your embeddings are 1536-dimensional, the vector store column must also be 1536
- **Higher dimensions = more detail** — but also more storage and slower searches
- **Lower dimensions = faster** — but may lose subtle meaning differences

Common dimension sizes:

| Provider | Model | Default Dimensions |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (feature hashing) | 1024 |

## Custom Embedding Provider

If you use a different embedding service, implement `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Call your embedding API here
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Batch embedding call
    }
}
```

Register it with the builder:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```

## What Happens Internally

When `QueryAsync` runs, the embedding stage does exactly one thing:

```
User question (string) → EmbeddingProvider.GetEmbeddingAsync() → Query vector (float[])
```

This query vector is then passed to the next stage ([Filtering](rag-filtering.md)) along with any metadata filters, and then on to [Retrieval](rag-hybrid-search.md) for similarity search.

## Next Steps

- [Filtering](rag-filtering.md) — narrow down which chunks are searched
- [Retrieval (Hybrid Search)](rag-hybrid-search.md) — combine vector and keyword search
- [Pipeline Customization](rag-pipeline.md) — share embedding providers across services
