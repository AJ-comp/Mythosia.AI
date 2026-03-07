# Mythosia.AI.Rag

## Package Summary

`Mythosia.AI.Rag` provides **RAG (Retrieval-Augmented Generation)** as an optional extension for `Mythosia.AI`.  
Install this package to add `.WithRag()` to any `AIService` — no changes to the AI core required.

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

## Embedding Providers

```csharp
// Local feature-hashing (default, no API key required)
.UseLocalEmbedding(dimensions: 1024)

// OpenAI embedding API
.UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)

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

By default, follow-up questions like *"Tell me more about that"* fail in RAG because the search query lacks context from previous turns. `WithQueryRewriter()` solves this by automatically rewriting follow-up queries into standalone queries before vector search.

```csharp
var service = new ChatGptService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .WithQueryRewriter()   // Enables automatic query rewriting
    );

// Turn 1: "Do you know about OPM?" → RAG finds OPM documents ✓
var r1 = await service.GetCompletionAsync("Do you know about OPM?");

// Turn 2: "Tell me more about that" → rewritten to "Tell me more about OPM" → RAG finds OPM documents ✓
var r2 = await service.GetCompletionAsync("Tell me more about that");
```

Use a cheaper/smaller LLM for rewriting to reduce cost:

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

## Disable RAG Per-Request

```csharp
var ragService = service.WithRag(rag => rag.AddDocument("doc.txt"));

// Use RAG
var withRag = await ragService.GetCompletionAsync("question with context");

// Temporarily bypass RAG
var withoutRag = await ragService.WithoutRag().GetCompletionAsync("general question");
```

## Retrieve Without LLM Call

Inspect the augmented prompt and references before sending to the LLM:

```csharp
var result = await ragService.RetrieveAsync("What is the refund policy?");

if (result.HasReferences)
{
    Console.WriteLine(result.AugmentedPrompt);  // Context + query
    Console.WriteLine(result.References.Count); // Number of matched chunks
    Console.WriteLine($"TopK={result.Diagnostics.AppliedTopK}, MinScore={result.Diagnostics.AppliedMinScore}, Namespace={result.Diagnostics.AppliedNamespace}, Elapsed={result.Diagnostics.ElapsedMs}ms");
    foreach (var r in result.References)
    {
        Console.WriteLine($"Score: {r.Score:F4} | {r.Record.Content}");
    }
}
else
{
    // No references found — AugmentedPrompt contains the original query unchanged
    Console.WriteLine(result.AugmentedPrompt);
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
    new RagQueryOptions { TopK = 15, MinScore = 0.2 }
);
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
