# Hybrid Search

Product codes often benefit from keyword search, while questions worded differently from the document benefit from semantic search. The selected retriever now prepares only what its search needs; keyword retrieval no longer requires a query embedding first.

## Built-in retrieval modes

```csharp
// Semantic search (default)
.UseVectorSearch()

// Keyword search without query embeddings
.UseKeywordSearch()

// Weighted hybrid search
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` skips query embeddings. Document ingestion still splits and embeds chunks for the existing vector store; this is not a text-only indexing API. Lazy initialization can therefore still call document embeddings on the first question.

## Combine keyword and semantic results

`VectorWeight` sets the vector contribution (0–1); the keyword contribution is `1 - VectorWeight`. `CandidateMultiplier` controls the candidate pool per search leg; `RrfK` controls rank smoothing for weighted Reciprocal Rank Fusion. These are separate from the RAG reranker candidate multiplier. Evaluate settings with representative documents and questions.

Pure vector and keyword modes retain native scores. Configurable hybrid retrieval uses normalized weighted RRF even with one active leg; zero vector weight skips query embeddings. These scores are not probabilities. `WeightedBlend` directly combines retrieval and reranker scores without calibration; prefer `RerankerOnly` for keyword search unless you calibrate the inputs.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## Store support and compatibility

InMemory, PostgreSQL and Qdrant support the new keyword and configurable weighted-RRF paths. Text scoring differs: InMemory uses BM25, PostgreSQL its configured full-text or trigram scoring, and Qdrant its sparse index. Scores are not interchangeable across engines.

Pinecone retains legacy native hybrid search through `UseHybridSearch()` with default settings on a compatible `dotproduct` index. This adapter does not support keyword mode or mixed configurable weighted RRF. Other stores must implement the corresponding optional interfaces. Unsupported modes or options fail explicitly instead of silently switching to vector search or ignoring weights.

The built-in InMemory, PostgreSQL and Qdrant adapters do not install a neural model or migrate indexes. Distinctions such as `C#` versus `C++` depend on their analyzers. The separate PIXIE option below also requires evaluation for exact identifier matching.

See [retrieval modes and store support](rag.md#retrieval-modes) and [custom retrievers](rag-pipeline.md#custom-retriever).

<a id="pixie-search"></a>

## Try local neural search with PIXIE

When document wording differs from the question, learned sparse search can add related vocabulary instead of relying only on literal token overlap. The optional `Mythosia.AI.Rag.Search.Pixie` package runs PIXIE locally for both document and query encoding and can combine its sparse results with your existing dense embeddings. PIXIE inference needs neither a Python server nor an API key; your chosen dense embedding or answer provider can still use a remote API.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

With this store, `UseKeywordSearch()` selects neural sparse retrieval: it skips the dense query embedding provider, but still runs PIXIE on the query. RAG ingestion still creates dense document embeddings. `UseHybridSearch(...)` combines sparse dot-product and dense cosine rankings with the configured weighted RRF.

This preview supplies `PixieInMemoryStore`, an in-memory index. It does not add PIXIE encoding to PostgreSQL, Qdrant or Pinecone. Rebuild the index after restart or model/settings changes. Keep the encoder alive for all store operations, then dispose it. Existing search remains the default; compare both paths on the same corpus and judged questions before switching. PIXIE does not guarantee exact `C#`/`C++` matching or exclusion constraints.

[PIXIE setup and comparison guide](rag-pixie-search.md).
