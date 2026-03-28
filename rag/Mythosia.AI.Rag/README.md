# Mythosia.AI.Rag

## Package Summary

`Mythosia.AI.Rag` provides **RAG (Retrieval-Augmented Generation)** as an optional extension for `Mythosia.AI`.  
Install this package to add `.WithRag()` to any `AIService` — no changes to the AI core required.

> **Abstractions Compatibility:** Implements **`Mythosia.AI.Rag.Abstractions v4.x`** — all interfaces (`IRagPipeline`, `IQueryRewriter`, `IRetrievalStrategy`, `IReranker`, etc.) and models (`RagQueryOptions`, `RagFilter`, `QueryRewriteResult`, etc.) are from Abstractions v4.0.0+.

## Installation

```bash
dotnet add package Mythosia.AI.Rag
```

## Quick Start

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

That's it. Documents are automatically loaded, chunked, embedded, and indexed on the first query (lazy initialization).

## Document Sources

```csharp
.WithRag(rag => rag
    // Single file
    .AddDocument("docs/manual.txt")

    // All files in a directory (recursive)
    .AddDocuments("./knowledge-base/")

    // Per-extension routing in a directory
    .AddDocuments("./knowledge-base/", src => src
        .WithExtension(".pdf")
        .WithLoader(new PdfDocumentLoader())
        .WithTextSplitter(new CharacterTextSplitter(800, 80))
    )
    .AddDocuments("./knowledge-base/", src => src
        .WithExtension(".docx")
        .WithLoader(new WordDocumentLoader())
        .WithTextSplitter(new TokenTextSplitter(600, 60))
    )

    // Inline text
    .AddText("Product price is $99.", id: "price-info")

    // URL (fetched via HTTP GET)
    .AddUrl("https://example.com/faq.txt")

    // Custom loader
    .AddDocuments(new MyPdfLoader(), "docs/manual.pdf")
)
```

## Search Settings

```csharp
.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithTopK(5)              // Number of results to retrieve (default: 3)
    .WithChunkSize(500)       // Characters per chunk (default: 300)
    .WithChunkOverlap(50)     // Overlap between chunks (default: 30)
    .WithScoreThreshold(0.5)  // Minimum similarity score (default: none)
)
```

## Hybrid Search

Combine dense vector similarity with BM25 keyword matching using **Reciprocal Rank Fusion (RRF)**. Documents that rank highly in both keyword and semantic search are boosted to the top.

For stores that support native hybrid storage/search, the recommended model is:

- store both dense and sparse/keyword-searchable data at write time
- choose retrieval mode at query time
  - `SearchAsync` for vector-only retrieval
  - `HybridSearchAsync` for hybrid retrieval

If a store does not support native hybrid retrieval, the RAG layer falls back to application-level fusion automatically.

```csharp
.WithRag(rag => rag
    .AddDocument("docs.txt")
    .UseHybridSearch()            // Enable hybrid search (default weight: 0.5)
)
```

Adjust the balance between vector and keyword search:

```csharp
.UseHybridSearch(vectorWeight: 0.7f)  // 70% vector, 30% keyword
```

### How It Works

| Store Type | Behavior |
| --- | --- |
| **InMemoryVectorStore** | Application-level BM25 index + vector search, merged via RRF |
| **PostgresStore** | Native parallel `tsvector` full-text + `pgvector` similarity, merged via RRF |
| **QdrantStore** | Native sparse-dense prefetch + Qdrant's built-in RRF fusion |
| **PineconeStore** | Native dense + sparse server-side fusion on `dotproduct` indexes |

The strategy is selected automatically based on the store — no configuration needed.

To revert to pure vector search:

```csharp
.UseVectorSearch()  // Explicit pure vector mode (same as default)
```

## Re-ranking

Re-rank search results after retrieval for improved relevance. Works with both pure vector and hybrid search.

When a reranker is configured, the pipeline automatically fetches a wider candidate pool (`TopK × TopKMultiplier`) and then the reranker selects the best `TopK` results. This ensures the reranker has enough diversity to work with.

```csharp
// Default: retrieves TopK × 3 candidates, reranks down to TopK
.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithReranker(new CohereReranker(cohereApiKey))
)

// Custom multiplier via RagStore.UpdateOptions
store.UpdateOptions(opt => opt.DefaultQuery.RetrievalDerivation.TopKMultiplier = 5);
```

### Cohere Reranker

```csharp
using Mythosia.AI.Rag.Reranking;

.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithReranker(new CohereReranker(cohereApiKey))
)
```

### LLM-based Reranker

Use any existing `AIService` to score and reorder results:

```csharp
using Mythosia.AI.Rag.Reranking;

var scorer = new ChatGptService(apiKey, httpClient, AIModel.OpenAI_Gpt4oMini);

.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithReranker(new LlmReranker(scorer))
)
```

### vLLM Reranker

Use a vLLM-served reranker model (e.g., Qwen3-Reranker):

```csharp
using Mythosia.AI.Rag.Reranking;

.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithReranker(new VllmReranker(
        model: "Qwen/Qwen3-Reranker-0.6B",
        baseUrl: "http://localhost:8003"))
)
```

### Final Selection Policy

By default, the pipeline trusts the reranker's scores for final result selection (`RerankerOnly`). Use `WithFinalSelectionPolicy` to blend retrieval and reranker scores instead:

```csharp
.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithReranker(new CohereReranker(cohereApiKey))
    .WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)
)
```

### Combined: Hybrid Search + Re-ranking

```csharp
.WithRag(rag => rag
    .AddDocument("docs.txt")
    .UseHybridSearch(vectorWeight: 0.6f)
    .WithReranker(new CohereReranker(cohereApiKey))
)
```

## Embedding Providers

```csharp
// Local feature-hashing (default, no API key required)
.UseLocalEmbedding(dimensions: 1024)

// OpenAI embedding API
.UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)

// vLLM-served embedding model
.UseEmbedding(new VllmEmbeddingProvider(
    httpClient,
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"))

// Custom provider
.UseEmbedding(new MyCustomEmbeddingProvider())
```

## Vector Stores

```csharp
// In-memory (default, data lost on process exit)
.UseInMemoryStore()

// Custom store (e.g., Qdrant, Chroma, Pinecone)
.UseStore(new MyQdrantVectorStore())
```

## Prompt Templates

```csharp
.WithPromptTemplate(@"
[Reference Documents]
{context}

[Question]
{question}

Answer based only on the provided documents.
")
```

Use `{context}` and `{question}` placeholders. If no template is specified, a default numbered-reference format is used.

## Multi-Turn Conversations (Query Rewriting)

By default, follow-up questions like *"Tell me more about that"* fail in RAG because the search query lacks context from previous turns. `WithQueryRewriter()` solves this by automatically rewriting follow-up queries into retrieval-ready form before vector search, and can also derive keyword terms for hybrid/text retrieval.

```csharp
var service = new ChatGptService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .WithQueryRewriter()   // Enables automatic query rewriting and retrieval keyword derivation
    );

// Turn 1: "Do you know about OPM?" → RAG finds OPM documents ✓
var r1 = await service.GetCompletionAsync("Do you know about OPM?");

// Turn 2: "Tell me more about that" → rewritten to "Tell me more about OPM" → RAG finds OPM documents ✓
var r2 = await service.GetCompletionAsync("Tell me more about that");
```

Use a cheaper/smaller LLM for rewriting and retrieval keyword derivation to reduce cost:

```csharp
var rewriterService = new ChatGptService(apiKey, httpClient, AIModel.OpenAI_Gpt4oMini);

var service = new ChatGptService(apiKey, httpClient, AIModel.OpenAI_Gpt4o)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .WithQueryRewriter(new LlmQueryRewriter(rewriterService))
    );
```

You can also provide a fully custom `IQueryRewriter` implementation:

```csharp
.WithRag(rag => rag
    .AddDocument("manual.txt")
    .WithQueryRewriter(new MyCustomRewriter())
)
```

Inspect the rewritten query via `RagProcessedQuery.RewrittenQuery`:

```csharp
var result = await service.RetrieveAsync("Tell me more about that");
Console.WriteLine(result.RewrittenQuery);  // "Tell me more about OPM"
```

## Streaming

```csharp
var ragService = new ChatGptService(apiKey, httpClient)
    .WithRag(rag => rag.AddDocument("manual.txt"));

await foreach (var chunk in ragService.StreamAsync("How do I use this product?"))
{
    Console.Write(chunk);
}
```

## Document Indexing Callback

`BuildAsync` accepts an optional `onDocumentEmbedded` callback invoked after each document's embedding is complete. When omitted, records are saved to the configured store automatically (default behavior). When provided, the callback replaces the default `UpsertBatchAsync` — you decide how to persist the records.

### Atomic File Replacement

When a file is modified and needs re-embedding, use the callback with `ReplaceByFilterAsync` to atomically swap old vectors with new ones — no query gap where the document temporarily disappears:

```csharp
var store = await RagStore.BuildAsync(config => config
    .AddDocuments(loader, file.LocalPath)
    .UseEmbedding(embeddingProvider)
    .UseStore(vectorStore),
    onDocumentEmbedded: records =>
        vectorStore.ReplaceByFilterAsync(
            VectorFilter.ByMetadata("full_path", file.FullPath), records)
);
```

On `PostgresStore`, `ReplaceByFilterAsync` wraps DELETE + INSERT in a single transaction — queries always see either the old data or the new data, never an empty gap. Other stores (InMemory, Qdrant, Pinecone) perform sequential delete + insert via the default interface method.

### Custom Processing

Use the callback for logging, validation, or routing to different stores:

```csharp
var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        Console.WriteLine($"Indexed {records.Count} chunks");
        await vectorStore.UpsertBatchAsync(records);
    }
);
```

## Shared RagStore (Multiple Services)

Build the index once, share across multiple AI services:

```csharp
var ragStore = await RagStore.BuildAsync(config => config
    .AddDocuments("./knowledge-base/")
    .UseOpenAIEmbedding(embeddingApiKey)
    .WithTopK(5)
);

var claude = new ClaudeService(claudeKey, http).WithRag(ragStore);
var gpt = new ChatGptService(gptKey, http).WithRag(ragStore);

// Both use the same pre-built index
var resp1 = await claude.GetCompletionAsync("What is the refund policy?");
var resp2 = await gpt.GetCompletionAsync("How long does shipping take?");
```

### Runtime Options Update

Update pipeline options at runtime without rebuilding the index:

```csharp
ragStore.UpdateOptions(opt =>
{
    opt.DefaultQuery.FinalFilter.TopK = 8;
    opt.DefaultQuery.FinalFilter.MinScore = 0.4;
    opt.DefaultQuery.RetrievalDerivation.TopKMultiplier = 3;
    opt.PromptTemplate = @"
[Reference Documents]
{context}

[Question]
{question}

Answer based only on the provided documents.
";
});
```

## Disable RAG Per-Request

```csharp
var ragService = service.WithRag(rag => rag.AddDocument("doc.txt"));

// Use RAG
var withRag = await ragService.GetCompletionAsync("question with context");

// Temporarily bypass RAG
var withoutRag = await ragService.WithoutRag().GetCompletionAsync("general question");
```

## Retrieve Without LLM Call

Inspect the request message content and references before sending to the LLM:

```csharp
var result = await ragService.RetrieveAsync("What is the refund policy?");

if (result.HasReferences)
{
    Console.WriteLine(result.RequestMessageContent);  // Context + query
    Console.WriteLine(result.References.Count);        // Number of matched chunks
    Console.WriteLine($"FinalTopK={result.Diagnostics.FinalTopK}, RetrievalTopK={result.Diagnostics.RetrievalTopK}, FinalMinScore={result.Diagnostics.AppliedFinalMinScore}, Namespace={result.Diagnostics.AppliedNamespace}, Elapsed={result.Diagnostics.ElapsedMs}ms");
    foreach (var r in result.References)
    {
        Console.WriteLine($"Score: {r.Score:F4} | {r.Record.Content}");
    }
}
else
{
    // No references found — RequestMessageContent contains the original query unchanged
    Console.WriteLine(result.RequestMessageContent);
}
```

## Per-Request Query Overrides

Keep global defaults in `RagBuilder`, then override per request when needed:

```csharp
var ragStore = await RagStore.BuildAsync(config => config
    .AddDocuments("./knowledge-base/")
    .WithTopK(3)
    .WithScoreThreshold(0.5)
);

var normal = await ragStore.QueryAsync("refund policy?");

var highRecall = await ragStore.QueryAsync(
    "refund policy?",
    new RagQueryOptions
    {
        FinalFilter = new RagFilter { TopK = 15, MinScore = 0.2 }
    }
);
```

## Progress Reporting

Track pipeline stage progress with an async callback:

```csharp
var result = await ragStore.QueryAsync("refund policy?",
    new RagQueryOptions
    {
        ProgressAsync = stage =>
        {
            Console.WriteLine($"Stage: {stage}");
            return Task.CompletedTask;
        }
    });
```

## Architecture

```text
Mythosia.AI (core)                    <- unchanged
    |
Mythosia.AI.Rag.Abstractions         <- interfaces (IRagPipeline, IVectorStore, etc.)
    |
Mythosia.AI.Rag                      <- fluent API, pipeline, builders, extensions
Mythosia.VectorDb.InMemory (optional) <- InMemoryVectorStore
Mythosia.AI.Loaders.Abstractions     <- IDocumentLoader, RagDocument
```

The AI core has zero knowledge of RAG. Everything is wired through the `IRagPipeline` interface and C# extension methods.

## Custom Implementations

### Custom Embedding Provider

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // Your embedding logic
    }

    public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        // Batch embedding logic
    }
}
```

### Custom Vector Store

```csharp
public class MyVectorStore : IVectorStore
{
    // Implement: CreateCollectionAsync, UpsertAsync, SearchAsync, DeleteAsync, etc.
}
```

### Custom Document Loader

```csharp
public class MyPdfLoader : IDocumentLoader
{
    public Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken ct = default)
    {
        // Parse PDF and return documents
    }
}
```
