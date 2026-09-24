# Embedding

> 📍 **Question Answering Pipeline:** [Query Rewriting](rag-query-rewriting.md) → [Filtering](rag-filtering.md) → **`Embedding (when needed)`** → [Retrieval](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Context Build](rag-context-build.md)

The query’s `Embedding` stage now depends on the retriever; keyword retrieval does not report it. Custom retrievers can report relevant stages through `request.ProgressAsync`. Document embeddings are unchanged.

## What is Embedding?

Embedding is the process of converting text into numerical vectors (arrays of numbers) that capture meaning. These vectors live in a high-dimensional space where **texts with similar meanings end up close together**.

Think of it like plotting cities on a map. Cities that are geographically close appear near each other on the map. Similarly, sentences like "How do I cancel my subscription?" and "I want to end my membership" produce vectors that are close together — even though they use completely different words.

In the RAG pipeline, embedding happens at two points:

1. **Document indexing** — each chunk is embedded and stored in the vector store
2. **Query time** — the user's question is embedded so it can be compared against stored chunks

This page focuses on the query-time embedding (step 2), which converts the user's question into a vector for similarity search.

## Built-in Embedding Providers

Choose an embedding provider according to your document language, hosting requirements, and retrieval needs.

### Perplexity

Standard embeddings treat passages independently and implement `IEmbeddingProvider`, so they fit the existing builder. Contextualized embeddings preserve the order and grouping of neighbouring chunks; they use a separate API to prevent unrelated documents being flattened into one input.

[Perplexity Agent API, Search, and Embeddings](perplexity.md).

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

<a id="openai-dimensions"></a>

`text-embedding-ada-002` has a fixed size of **1536 dimensions**. The provider omits the unsupported `dimensions` field from single and batch requests; configuring another size throws `ArgumentOutOfRangeException` before any API call. For `text-embedding-3-small` and `text-embedding-3-large`, requests continue to include the configured `dimensions`.

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

<a id="ollama-dimensions"></a>

Document and query vectors must use the same model and dimensions. `OllamaEmbeddingProvider` sends its configured `dimensions` to `/api/embed` and validates that every returned vector has that length. The provider still defaults to `qwen3-embedding:4b` with **1024 requested dimensions**; the model's native output has 2560 dimensions. The Ollama server and selected model must support the requested size. An unsupported request or a response that ignores it fails instead of silently changing `Dimensions` or resizing vectors locally.

If you change the model or dimensions, rebuild document embeddings with the same settings used for queries and configure the vector store accordingly. Existing vectors are not converted automatically.

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
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // default: 100 chunks per API call
pipeline.Options = options;
```

A larger batch size means fewer API calls but higher memory usage per call. If you're hitting API rate limits or memory issues, try reducing this value.

`EmbeddingBatchSize` must be positive. The pipeline validates and captures it at the start of each document-indexing call, before embedding or replacing stored records. This prevents empty-batch loops and keeps a setting change during an awaited call from skipping chunks. Later indexing calls can use the new setting.

<a id="embedding-validation"></a>

## Keep each vector matched to its chunk

A successful HTTP response can still contain missing vectors or the wrong order. That would pair text with another chunk's meaning. A custom `IEmbeddingProvider` must return exactly one non-null `float[]` per input, in input order, with a positive `Dimensions` value. Every vector must have that many elements, all finite (no `NaN` or infinity).

During document indexing, the pipeline rejects invalid dimensions, response counts or vectors with `InvalidOperationException` before storage or `onDocumentEmbedded`. It copies each accepted vector before requesting the next batch, so reusing a provider buffer in a later batch cannot change earlier chunks. Keep returned data stable while the caller reads it; concurrent mutation during validation or copying is not supported. Existing records for that document are preserved when validation fails.

`OpenAIEmbeddingProvider` requires a valid, unique `index` for every response item and restores input order. `VllmEmbeddingProvider` does the same when indices are present; for compatibility it also accepts responses where every item omits `index`, using response order. Mixed indexed/index-free responses, duplicate or out-of-range indices are rejected. A custom or index-free provider remains responsible for correct order; shape checks cannot verify a vector's meaning.

<a id="query-embedding-validation"></a>

## Protect the query vector before search

A reused provider buffer must not change one question into another while progress reporting or search is awaiting. Built-in dense query retrieval, including the adapter for `IRetrievalStrategy`, requires positive `Dimensions`, a non-null vector of exactly that length and finite values. Invalid output throws `InvalidOperationException` before search. The accepted vector is copied immediately after the provider returns, before subsequent progress callbacks or search. Providers must keep returned data stable while it is being read; a custom `IRagRetriever` owns its own query preparation and validation.

`OllamaEmbeddingProvider` also validates response shape, exact vector count, dimensions and finite values on direct single/batch calls. Malformed JSON or vectors throw `InvalidOperationException` rather than silently producing incomplete output. The caller retains ownership of the supplied `HttpClient`; disposing individual HTTP requests/responses does not dispose that client.

## Dimensions

The `Dimensions` property controls the size of each embedding vector. This is critical because:

- **Vector store must match** — if your embeddings are 1536-dimensional, the vector store column must also be 1536
- **Higher dimensions = more detail** — but also more storage and slower searches
- **Lower dimensions = faster** — but may lose subtle meaning differences

Common dimension sizes:

| Provider | Model | Default Dimensions |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 requested (native: 2560) |
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
