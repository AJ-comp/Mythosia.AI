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
| `IQueryRewriter` | Rewrites queries into retrieval-ready form using conversation history, and decides whether document search is needed (search gate) |
| `IRetrievalStrategy` | Abstracts retrieval logic — pure vector or hybrid (BM25 + vector + RRF) |
| `IReranker` | Re-ranks search results post-retrieval for improved relevance |

## Models

| Model | Description |
| --- | --- |
| `RagChunk` | A chunk of text with ID, content, document ID, index, and metadata |
| `RagDocument` | A loaded document with `Id`, `Content`, `Source`, and `Metadata` for the RAG pipeline |
| `RagProcessedQuery` | Pipeline output: original query, rewritten semantic query, retrieval keywords, `RequestMessageContent`, references, `RetrievalCandidates`, `SearchSkipped`, `RewriteResult`, `HasReferences` flag, and `Diagnostics` |
| `QueryRewriteResult` | Result of rewriting a query into retrieval-ready form, including search gate decision (`NeedsSearch`) and optional retrieval keywords. Factory methods `Pass()` and `Search()` |
| `ConversationTurn` | Lightweight DTO representing a single conversation turn (role + content) for `IQueryRewriter` context |
| `RagQueryDiagnostics` | Applied retrieval metadata (`FinalTopK`, `RetrievalTopK`, `AppliedFinalMinScore`, `AppliedRetrievalMinScore`, `ElapsedMs`, `RewriteElapsedMs`) |
| `RagPipelineOptions` | Configuration: `DefaultQuery`, `PromptTemplate`, `EmbeddingBatchSize`. Includes `Clone()` for snapshot-style runtime updates |
| `RagQueryOptions` | Per-request overrides: `FinalFilter`, `RetrievalDerivation`, `StoreFilter`, `FinalSelection`, `ProgressAsync`. Includes `Clone()` for safe per-query overrides on top of `RagPipelineOptions.DefaultQuery` |
| `RagFinalSelectionOptions` | Final selection policy after re-ranking (`Mode`, `RetrievalWeight`). Includes `Clone()` |
| `RagFinalSelectionMode` | Enum: `RerankerOnly` (default) or `WeightedBlend` |
| `RagFilter` | Final selection policy (`TopK`, `MinScore`). Includes `Clone()` |
| `RagRetrievalDerivation` | Controls how retrieval candidates are derived (`TopKMultiplier`, `MinScoreDivider`). Includes `Clone()` |
| `RagRetrievalFilter` | Immutable computed retrieval filter (`TopK`, `MinScore`) |
| `RagProgressStage` | Enum for pipeline stage progress reporting (`QueryRewrite`, `Embedding`, `Filtering`, `Retrieval`, `Reranking`, `ContextBuild`) |
| `VectorRecord` | Stored vector with ID, content, embedding, and metadata |
| `VectorSearchResult` | Search result with record and similarity score |
| `VectorFilter` | Filter by metadata conditions or minimum score |

## Per-Query Overrides via `Clone()`

`RagPipelineOptions`, `RagQueryOptions`, `RagFilter`, `RagRetrievalDerivation`, and `RagFinalSelectionOptions` all expose a `Clone()` method.
Use `Clone()` to override a single field on top of `RagPipelineOptions.DefaultQuery` without losing the other configured fields (notably `StoreFilter` and `ProgressAsync`):

```csharp
var options = pipeline.Options.DefaultQuery.Clone();
options.FinalFilter.TopK = 10;
await pipeline.QueryAsync(query, options);
```

Constructing `new RagQueryOptions { FinalFilter = ... }` from scratch silently drops every other field, including any tenant/permission scope on `StoreFilter` and any progress callback on `ProgressAsync`. `Clone()` makes the "inherit defaults, override one field" pattern safe.

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
