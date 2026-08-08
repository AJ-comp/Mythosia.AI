# RAG (Retrieval-Augmented Generation)

RAG lets the model answer questions based on your own documents by retrieving relevant chunks at query time.

## Installation

```bash
dotnet add package Mythosia.AI.Rag
```

## Quick Start

Use `.WithRag()` on any `IAIService` to enable RAG with a fluent API:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

The documents are split, embedded, and stored automatically. At query time, the most relevant chunks are retrieved and injected into the prompt.

## Adding Documents

Several source types are supported:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // local file
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Inline content can go here too.")   // raw string
)
```

## Custom Embedding Provider

By default, RAG uses the built-in local embedding provider. To use a dedicated embedding model:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## Custom Vector Store

By default, an in-memory store is used. For production, plug in a persistent vector store:

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
        .AddDocument("large-corpus.txt")
    );
```

## Query Options

Fine-tune retrieval behavior per query:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,       // number of chunks to retrieve
        MinScore = 0.7  // minimum similarity score
    }
};

var response = await service.GetCompletionAsync("Your question", options: options);
```

## Next Steps

- [Hybrid Search](rag-hybrid-search.md) — combine semantic and keyword search
- [Query Rewriting](rag-query-rewriting.md) — optimize queries with conversation context
- [Re-ranking](rag-reranking.md) — further refine search result accuracy
- [Pipeline Customization](rag-pipeline.md) — fine-grained control over the RAG process
- [Agentic RAG](rag-agentic.md) — AI decides when and what to search
- [Vector Stores](vectordb-overview.md) — persistent storage setup
- [Text Splitters](text-splitters.md) — customize how documents are chunked
