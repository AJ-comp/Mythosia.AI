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
| `IRagDiagnosticsStore` | Optional diagnostics contract (`ListAllRecordsAsync`, `ScoredListAsync`) |
| `ITextSplitter` | Document → chunks (`Split(RagDocument)`) |
| `IContextBuilder` | Search results → LLM prompt (`BuildContext(query, results)`) |
| `IQueryRewriter` | Rewrites follow-up queries into standalone queries using conversation history |

## Models

| Model | Description |
| --- | --- |
| `RagChunk` | A chunk of text with ID, content, document ID, index, and metadata |
| `RagProcessedQuery` | Pipeline output: original query, rewritten query, augmented prompt, references, `HasReferences` flag, and `Diagnostics` |
| `ConversationTurn` | Lightweight DTO representing a single conversation turn (role + content) for `IQueryRewriter` |
| `RagQueryDiagnostics` | Applied retrieval metadata (`AppliedNamespace`, `AppliedTopK`, `AppliedMinScore`, `ElapsedMs`) |
| `RagPipelineOptions` | Configuration: TopK, MinScore, DefaultCollection |
| `RagQueryOptions` | Per-request overrides: TopK, MinScore, Namespace |
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
