# Mythosia.AI.Rag.Abstractions

## Package Summary

Core interfaces and models for the Mythosia.AI RAG ecosystem.  
This package defines the contracts that all RAG components implement — you only need this directly if you're building a **custom implementation**.

## Interfaces

| Interface | Description |
| --- | --- |
| `IRagPipeline` | Main pipeline contract: `ProcessAsync(query)` → `RagProcessedQuery` |
| `IEmbeddingProvider` | Text → vector embedding (`GetEmbeddingAsync`, `GetEmbeddingsAsync`) |
| `IVectorStore` | Vector storage & search (`UpsertAsync`, `SearchAsync`, `DeleteAsync`) |
| `ITextSplitter` | Document → chunks (`Split(RagDocument)`) |
| `IContextBuilder` | Search results → LLM prompt (`BuildContext(query, results)`) |

## Models

| Model | Description |
| --- | --- |
| `RagChunk` | A chunk of text with ID, content, document ID, index, and metadata |
| `RagProcessedQuery` | Pipeline output: original query, augmented prompt, references |
| `RagPipelineOptions` | Configuration: TopK, MinScore, DefaultCollection |
| `VectorRecord` | Stored vector with ID, content, embedding, metadata, namespace |
| `VectorSearchResult` | Search result with record and similarity score |
| `VectorFilter` | Filter by namespace, metadata, or minimum score |

## Custom Implementation Example

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // Your embedding logic here
    }

    public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        // Batch embedding logic here
    }
}
```

Then register via the builder:

```csharp
.WithRag(rag => rag.UseEmbedding(new MyEmbeddingProvider()))
```
