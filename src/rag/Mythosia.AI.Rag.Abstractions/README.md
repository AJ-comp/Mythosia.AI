# Mythosia.AI.Rag.Abstractions

> **v6.3.0:** Adds `IRagRetriever` and `RagRetrievalRequest` for custom request-based retrieval. Use `Mythosia.AI.Rag` 8.1.0 or later for the corresponding builder and pipeline APIs; `IRetrievalStrategy` remains supported. See the [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag.Abstractions/RELEASE_NOTES.md#v630).

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
| `IRagRetriever` | Request-based retrieval without a mandatory query embedding; owns preparation, filtering, limits and cancellation |
| `IRetrievalStrategy` | Existing dense-input strategy, retained through a compatibility adapter |
| `IReranker` | Re-ranks search results post-retrieval for improved relevance |

## Models

| Model | Description |
| --- | --- |
| `RagRetrievalRequest` | Read-only `Query`, nullable `TextQuery`, `TopK`, `Filter`, `ProgressAsync`; null lexical override uses the full query in built-in retrievers |
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

Custom implementations must preserve the association between each document, chunk and vector. A splitter returns non-null chunks with nonblank IDs unique across the target store; include the document ID and chunk index, and copy inherited metadata when access filters depend on it. Indexing rejects blank or duplicate chunk IDs within a document before embedding or persistence and does not invent replacement IDs.

An embedding batch returns exactly one vector per input, in input order, with the provider's positive `Dimensions` and finite values in every coordinate. RAG indexing validates each batch and copies its vectors before asking for another batch. Provider-owned buffers may therefore be reused by a later sequential call; they must remain stable while the caller is reading the current response. A validation failure preserves that document's previous index when it occurs before persistence. See the [indexing validation guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-pipeline.md#indexing-validation) for exception types and storage boundaries.

Single query embeddings follow the same dimension and finite-value contract. Built-in vector, hybrid and legacy retrieval adapters validate and copy a completed query vector before invoking retrieval progress callbacks or the store. This keeps a later sequential call from changing an earlier query through a reused provider buffer. Providers must still keep returned data stable while it is being read or copied; custom `IRagRetriever` implementations own their preparation and validation. Keyword-only retrieval does not inspect or call the embedding provider. See [query embedding validation](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-embedding.md#query-embedding-validation).

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

## Custom retrieval without mandatory embeddings

An external search index should not need a dense query embedding merely to connect to RAG. Implement `IRagRetriever` and register it with `RagBuilder.UseRetriever(...)`. Preparation belongs to the retriever; the rest of the pipeline can still apply reranking and assemble context. Respect the request filter, top-K and cancellation, and return the content and metadata needed downstream. See the [custom retriever guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-pipeline.md#custom-retriever). Existing `IRetrievalStrategy` implementations remain supported through an embedding adapter. This changes query retrieval, not the document-ingestion contract.
