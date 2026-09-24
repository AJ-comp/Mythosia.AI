# Mythosia.AI.Rag

Ground answers in documents your application manages. `Mythosia.AI.Rag` adds `.WithRag()` to an `IAIService` and handles document loading, splitting, embeddings, retrieval and context assembly. Use Agentic RAG tools when the model should decide when to search again.

Version **8.1.0** depends on lightweight contracts instead of the full provider implementation: **Mythosia.AI.Abstractions 4.1.0** and **Mythosia.AI.Rag.Abstractions 6.3.0**.

> **v8.1.0:** Adds request-based retrieval, configurable hybrid search, indexing and embedding safeguards, and processing-speed integration. See the [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v810) for changes and index migration guidance.

In v8.1.0, `RagEnabledService.WithSpeed(InferenceSpeed.Fast)` can request paid low-latency processing for the next answer when the inner provider/model supports it. It preserves retrieval settings and keeps internal query rewriting separate. Read `LastProcessing` or `(await run.Result).Processing` for reported applied modes; missing information remains unknown. See [speed selection](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md#inference-speed).

## Current release: 8.1.0

The 8.1.0 release adds compatible APIs and fixes; existing retrieval interfaces remain supported. If upgrading from before 8.0.0, follow the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) for the earlier completion and Run contract changes.

Pass `cancellationToken` to stop cooperative retrieval, query rewriting and the inner completion call when a user stops waiting. `WithAgenticRag` forwards tool cancellation into `RagStore.QueryAsync`; search exceptions become failed tool results. Cancellation avoids later model rounds, but cleanup can wait for components that ignore the token and does not guarantee that a provider stops inference or billing. See the [completion contract](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation) and [tool contract](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/function-calling.md#tool-execution-contract).

Keep an `AIRunResult` when the answer needs usage, sources and execution details: `await run.Result` provides that snapshot without a stream reader, and `(await run.Result).Text` provides the string. Usage describes the inner model execution; retrieval and embedding usage are separate. `GetCompletionAsync` remains a string API. See the [Run result migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result).

Perplexity standard 0.6B/4B embeddings plug into the existing RAG builder. Separate contextualized APIs preserve each document's ordered chunks, and packed binary results use an explicit vector type. See the [Perplexity guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/perplexity.md) and [v8.0.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800).

RAG Run controls, request-scoped reasoning/search forwarding and duplicate-registration filtering were introduced in [v7.6.0](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v760). Use a supporting inner service, such as Mythosia.AI 8.1.0, for the current provider integrations.

## Installation

```bash
dotnet add package Mythosia.AI.Rag
```

## Quick Start

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

That's it. Documents are automatically loaded, chunked, embedded, and indexed on the first query (lazy initialization).

To choose model controls before starting RAG, inspect the concrete inner `AIService` or its request builder. Model capabilities describe the model connection; they do not describe retrieval-store features or add capability methods to the RAG wrapper. [Capability guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/model-capabilities.md).

## Answer about an attachment using your documents

To explain a product photo using your manual, pass a `Message` containing the question and image to `RagEnabledService.GetCompletionAsync(Message)` or `StartRunAsync(Message)`. Both preserve non-text attachments in the request sent to the inner AI service. Retrieval uses the message text; attachments are not automatically indexed or embedded. The selected provider and model must support the attachment type. Retrieved context is added only to the outgoing request: it does not overwrite the original `Message` or replace the user's text in conversation history.

When an answer needs both a manual and live inventory, combine RAG with your registered tools. During `GetCompletionAsync` tool rounds, retrieved context stays on the initial input and each subsequent tool result is sent to the model unchanged. The original user input remains in conversation history.

## Start a RAG run

After finding relevant documents, a long answer may still take time to write. Use a run to display that answer as it arrives and let the user stop execution. See the [Run guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md) for additional controls and supported steering.

```csharp
// ragService is returned by service.WithRag(...).
await using var run = await ragService.StartRunAsync(
    "What is the refund policy?",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

The wrapper retrieves and augments context once before the model run. `options:` accepts per-query `RagQueryOptions`; `streamOptions:` controls displayed event detail. The returned run supports the inner provider's controls, but steering does not automatically repeat retrieval. Use `WithAgenticRag` when the model should request further searches as a tool. Custom inner services must implement optional `IAIRunService`; unsupported services fail before indexing.

## Document Sources

If the provider already manages your document index, [hosted file search](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/reasoning-and-search.md) can search it directly. RAG remains useful when your application needs control of loading, splitting, embeddings and retrieval. `RagEnabledService.WithReasoning`, `.WithWebSearch` and `.WithFileSearch` configure its final answer without affecting the internal query rewrite; `LastCitations` contains hosted sources, while RAG retrieval references remain on `RagProcessedQuery`.

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

### Files with the same name

Separate document roots often contain the same names, such as `company-a/docs/faq.txt` and `company-b/docs/faq.txt`. Both must stay indexed. The built-in `PlainTextDocumentLoader` and `DirectoryDocumentLoader` now use the normalized absolute file path (`Path.GetFullPath`) as `Source` and the automatic document ID. Registering the same normalized path again reuses the ID; files in different directories remain separate. Explicit `AddText` IDs, `RagDocument.Id` and custom loader `Source` rules are unchanged.

The default RAG storage flow assigns document IDs before writing to a vector store and replaces records matching `document_id`. The PostgreSQL (pgvector) implementation follows this ID filter rather than inspecting original file paths. Previously, directory registration reduced both files above to `faq.txt`, causing replacement of the first document by the second. This fix preserves the full path in the automatic ID; the PostgreSQL schema is unchanged. A custom `full_path` filter uses caller-supplied metadata and does not automatically make document or record IDs unique.

Default citations may now show an absolute path. Use `filename`, or `relative_path` from the default directory loader, for display. Existing relative-path IDs are not automatically migrated or removed: prefer a new collection, reindex all documents, verify it, then switch the application. See [document identity and existing indexes](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#document-identity) for path limits and scoped cleanup when reusing a collection.

To keep updates and deletions limited to the intended document, `document_id` is reserved by the pipeline. Before persistence, each record receives the actual `RagDocument.Id`, even if input metadata supplies another value. The input document's and splitter's metadata dictionaries are not modified; custom persistence callbacks also receive the normalized records. Use a different key for an application-specific ID.

This does not repair previously stored records with an incorrect `document_id`. Rebuild from trusted source documents into a new collection, or identify and clean up only the affected records before reindexing. Reindexing the correct ID alone cannot reliably find records stored under another ID.

Registering the same file through a relative path and an absolute path must update one document, while same-named files in different folders must stay separate. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` and `PdfDocumentLoader` now set `DoclingDocument.Source` to the normalized absolute file path, as the built-in TXT loaders do. RAG derives automatic document IDs from this value; explicit IDs remain caller-controlled. Default citations may therefore show absolute paths.

Previously stored relative-path IDs are not migrated or deleted automatically. Identify the old document ID, explicitly remove only that document from the relevant store and reindex it. Alternatively, index the complete source set into a new empty collection, validate it and switch the application to it. Reindexing only the new absolute-path ID in the existing collection leaves the old records behind. Do not delete unrelated documents. See [document identity and migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#document-identity).

### Protect existing documents when indexing fails

An invalid custom splitter or embedding response must not silently replace a searchable document with incomplete or mismatched content. The pipeline checks each document before it starts persistence, including when you use `onDocumentEmbedded`.

Before embedding, storage or the persistence callback, a null, empty or whitespace-only `RagDocument.Id` throws `ArgumentException`. Invalid splitter output throws `InvalidOperationException`: a null chunk list or chunk, null `Content` or `Metadata`, a blank chunk ID, or repeated chunk IDs within that document. Duplicate IDs use `StringComparer.Ordinal` (case-sensitive). Chunk values and metadata are copied before the first embedding call.

Valid custom IDs are retained exactly as supplied. There is no automatic ID generation, trimming or repair, and collisions between custom chunk IDs belonging to different documents are not detected globally. Use IDs that are unique in the target collection, such as the [custom splitter example](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/text-splitters.md). The reserved `document_id` is normalized only on the copy used for storage; source metadata remains unchanged.

Invalid IDs, splitter failures and invalid embedding batches leave that document's previous records intact and do not invoke the persistence callback. All of its batches must pass [embedding validation](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-embedding.md#embedding-validation) before storage starts. This does not roll back documents already completed earlier in the operation; rollback after storage starts depends on the store or callback.

### Empty document updates

Clearing a retired policy must also remove its old searchable text. With default RAG storage, successful splitting with zero chunks replaces records matching the same `document_id` with an empty set, without calling embeddings or changing other document IDs. Reuse the stored document ID; an omitted document or an empty loader result does not identify anything to delete. Loading/parsing/splitting exceptions and cancellation observed before the storage call preserve that document's current records; rollback after storage starts depends on the store. Batches do not roll back earlier completed documents.

With custom persistence through `onDocumentEmbedded`, zero chunks still skip the callback and the default store. The application must explicitly delete the known ID in its own storage, or use `DeleteDocumentAsync` for the pipeline's store. See [empty document updates](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#empty-document-updates) for an example and failure handling.

`EmbeddingBatchSize` must be positive. The pipeline validates and captures it at the start of each document-indexing call, before embedding or replacing stored records. This prevents empty-batch loops and keeps a setting change during an awaited call from skipping chunks. Later indexing calls can use the new setting. See [batch sizing](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-embedding.md) for configuration.

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

## Choose document chunks

A search result needs enough surrounding text to answer the question. Use `RecursiveTextSplitter(500, 50)` for prose, or `MarkdownTextSplitter(500)` when headings, table rows and fenced code must stay together. Select the splitter explicitly with `.WithTextSplitter(...)` or per document; the default remains `CharacterTextSplitter(300, 30)`, including for `.md` files.

Character/Recursive sizes count UTF-16 code units, with overlap adjusted to text boundaries. Invalid sizes and negative overlaps fail before processing; overlap at least as large as the size disables overlap. Surrogate pairs are not cut, so a pair needs 2 units even when the limit is 1. There is no extra trailing chunk containing only repeated overlap.

Markdown takes a single size argument and has no overlap option. Its content budget excludes repeated heading breadcrumbs; complete fenced code blocks or a table header plus one row may exceed it. Original headings remain when `IncludeHeadingBreadcrumb = false`, heading-only content is retained, and GFM tables support optional outer pipes. This is a rule-based splitter, not a complete Markdown parser. Table conditions and code indentation retain their meaning; excessive repeated Markdown context fails explicitly before it can expand without a bound.

Repeated headings and table headers must not turn a small document into an unbounded amount of text to embed. Markdown therefore has a separate per-document output budget of `max(65536, 32 × document.Content.Length)` UTF-16 code units, summed across all final chunks, including repeated breadcrumbs, table headers and labels. It checks the budget before constructing excessive repeated output and throws `InvalidOperationException` if it would be exceeded; it neither truncates content nor returns a partial result. `ChunkSize` and its atomic-block exceptions still apply within this overall limit. In the default RAG indexing flow, this splitting failure occurs before embedding or storage replacement, so the document's existing index is left unchanged. This is a text-output limit, not a model-token or process-memory limit. The budget applies to each `Split` call and grows with input length; it is not a fixed maximum document size.

`TokenTextSplitter` counts whitespace-separated units rather than model tokens. For a strict embedding limit, count the final text—including repeated headers—with the target model's tokenizer. See the [splitter guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/text-splitters.md) for examples and limits.

These fixes change chunk boundaries and IDs for affected documents. Rebuild the index for the same document IDs so obsolete chunks are replaced, then refresh embedding caches and retrieval evaluation baselines as applicable. Existing persisted chunks are not rewritten automatically.

## Choose a retrieval mode

Use keyword search for lexical matches, semantic search for different wording with similar meaning, and hybrid search to combine them. The selected retriever prepares the query representation it needs instead of always creating a query embedding first.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` skips query embeddings, not document embeddings. Existing ingestion still splits and embeds documents for the vector store, including lazy initialization on the first query. Use `UseVectorSearch()` for the default semantic mode.

## Hybrid Search

```csharp
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`VectorWeight` contributes to weighted Reciprocal Rank Fusion; the text weight is `1 - VectorWeight`. `CandidateMultiplier` controls candidates per active leg and `RrfK` controls rank smoothing. The existing `.UseHybridSearch(vectorWeight: 0.7f)` overload remains available. Unsupported modes or options fail explicitly instead of silently falling back or discarding settings.

| Store | Keyword mode | Configurable weighted RRF |
| --- | --- | --- |
| InMemory | BM25 | Supported |
| PostgreSQL | Configured full-text or trigram search | Supported |
| Qdrant | Sparse index | Supported |
| Pinecone | Unsupported by this adapter | Mixed configuration unsupported; the vector-only endpoint remains available, and default `UseHybridSearch()` retains legacy native fusion on compatible `dotproduct` indexes |

New configurable hybrid results use normalized weighted RRF, including one active leg or no text matches. Pure vector and pure keyword modes retain native scores; these scores are not interchangeable. Direct legacy Qdrant `HybridSearchAsync` retains its configured server fusion. Existing backend adapters keep their analyzers and indexes; exact `C#`/`C++` distinctions remain dependent on the selected search implementation.

### Compare local neural search with PIXIE

When questions and documents use different wording, learned sparse retrieval can add related vocabulary. The optional **`Mythosia.AI.Rag.Search.Pixie` 0.1.0-preview** package provides local ONNX document/query encoding and `PixieInMemoryStore`. Connect it through `.UseStore(searchStore)`, retain the existing `.UseEmbedding(embeddings)`, and select `.UseHybridSearch(options)` or `.UseKeywordSearch()`.

The package bundles a pinned tokenizer and derived 8-bit quantized model. PIXIE does not require a Python server or API key; other RAG providers keep their own execution requirements. The index is memory-only, must be rebuilt after restart, and does not migrate PostgreSQL, Qdrant or Pinecone. The caller disposes the encoder after all store operations finish. Existing search remains the default until you choose to switch after comparison. See the [full PIXIE guide and example](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-pixie-search.md).

## Custom retrieval

Implement `IRagRetriever.RetrieveAsync(RagRetrievalRequest, CancellationToken)` and register it with `.UseRetriever(retriever)`. The request carries the full `Query`, optional lexical `TextQuery`, `TopK`, `Filter` and `ProgressAsync`. Built-in retrievers use the full query when `TextQuery` is null; an empty override skips the text leg. Custom retrievers own preparation and must enforce the supplied filter, result limit and cancellation, and return records suitable for reranking and context assembly. See the [complete custom retriever example](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-pipeline.md#custom-retriever).

Runtime configuration is available through `RagPipeline.SetRetriever(...)`, `RagStore.UpdateRetriever(...)`, `RagStore.UseKeywordSearch()` and `RagStore.UpdateRetrievalStrategy(HybridSearchOptions)`. `UpdateRetriever(null)` resets to vector retrieval. Existing `IRetrievalStrategy` implementations and `SetRetrievalStrategy(...)` keep their dense-input behavior through a compatibility adapter. Agentic RAG uses the same selected retrieval path.

## Re-ranking

Re-rank search results after retrieval for improved relevance. Works with vector, keyword, hybrid and custom retrieval. Native keyword scores such as BM25 and `ts_rank` are not calibrated against reranker scores; prefer the default `RerankerOnly` policy unless you have calibrated the inputs to `WeightedBlend`.

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

var scorer = new OpenAIService(apiKey, httpClient, AIModel.OpenAI_Gpt4oMini);

.WithRag(rag => rag
    .AddDocument("docs.txt")
    .WithReranker(new LlmReranker(scorer))
)
```

To keep each assessment's question and documents separate from earlier assessments and the service conversation, `LlmReranker` uses a stateless request for every evaluation. It neither reads nor appends conversation history or stored conversation summaries, and it does not trigger automatic summarization of the existing conversation. Service defaults and your calling code remain unchanged. Evaluations by rerankers sharing the same AI service are processed sequentially.

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

Use Perplexity standard embeddings to index independent passages with the same RAG pipeline. Select `PerplexityEmbeddingModels.Standard0_6B` (1024 dimensions) or `Standard4B` (2560 dimensions); supply a Perplexity key and your application's `HttpClient`.

```csharp
var rag = service.WithRag(builder => builder
    .AddText("Returns are accepted within 30 days.", id: "returns")
    .UsePerplexityEmbedding(perplexityApiKey, httpClient));
```

For context-sensitive chunks, `PerplexityContextualizedEmbeddingProvider` preserves document groups and their order through `GetDocumentEmbeddingsAsync`; embed queries with its `GetQueryEmbeddingAsync` using the same contextual model. This separate API does not implement the flat `IEmbeddingProvider`. Float methods decode signed-int8 and normalize vectors, while explicit binary methods return packed bits for Hamming distance. See the [Perplexity embedding guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/perplexity.md) for all four models, dimensions, limits, and binary methods.

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

`text-embedding-ada-002` is fixed at 1536 dimensions. `OpenAIEmbeddingProvider` omits the unsupported `dimensions` field from its single/batch requests and rejects other configured sizes with `ArgumentOutOfRangeException` before calling the API. `text-embedding-3-small` and `text-embedding-3-large` continue to send the configured size. See [OpenAI embedding dimensions](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-embedding.md#openai-dimensions).

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
var service = new OpenAIService(apiKey, httpClient)
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
var rewriterService = new OpenAIService(apiKey, httpClient, AIModel.OpenAI_Gpt4oMini);

var service = new OpenAIService(apiKey, httpClient, AIModel.OpenAI_Gpt4o)
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

To temporarily disable rewriting or replace its implementation without rebuilding the index, use `store.SetQueryRewriter(null)` or `store.SetQueryRewriter(rewriter)`. When you call `RagStore.QueryAsync` directly through the overload accepting `conversationHistory`, it captures the selected rewriter as the query begins. That query keeps the same instance even if rewriting is disabled or replaced while progress reporting or rewriting is awaiting; later queries use the new setting. This behavior applies to direct store queries and does not update a rewriter already captured by a `RagEnabledService` wrapper.

## Streaming

```csharp
var ragService = new OpenAIService(apiKey, httpClient)
    .WithRag(rag => rag.AddDocument("manual.txt"));

await foreach (var chunk in ragService.StreamAsync("How do I use this product?"))
{
    Console.Write(chunk);
}
```

## Query vector protection

A reused provider buffer must not change one question into another while progress reporting or search is awaiting. Built-in dense query retrieval, including the adapter for `IRetrievalStrategy`, requires positive `Dimensions`, a non-null vector of exactly that length and finite values. Invalid output throws `InvalidOperationException` before search. The accepted vector is copied immediately after the provider returns, before subsequent progress callbacks or search. Providers must keep returned data stable while it is being read; a custom `IRagRetriever` owns its own query preparation and validation.

`OllamaEmbeddingProvider` also validates response shape, exact vector count, dimensions and finite values on direct single/batch calls. Malformed JSON or vectors throw `InvalidOperationException` rather than silently producing incomplete output. The caller retains ownership of the supplied `HttpClient`; disposing individual HTTP requests/responses does not dispose that client.

Document and query vectors must use the same model and dimensions. `OllamaEmbeddingProvider` sends its configured `dimensions` to `/api/embed` and validates that every returned vector has that length. The provider still defaults to `qwen3-embedding:4b` with **1024 requested dimensions**; the model's native output has 2560 dimensions. The Ollama server and selected model must support the requested size. An unsupported request or a response that ignores it fails instead of silently changing `Dimensions` or resizing vectors locally.

If you change the model or dimensions, rebuild document embeddings with the same settings used for queries and configure the vector store accordingly. Existing vectors are not converted automatically.

## Document Indexing Callback

`BuildAsync` accepts an optional `onDocumentEmbedded` callback invoked after a document's nonempty chunks have been embedded. When omitted, the pipeline calls `ReplaceByFilterAsync(Where("document_id", docId), records)` to replace the document's existing chunks, including removal when splitting succeeds with zero chunks. When provided, the callback replaces this default behavior entirely — you decide how to persist the records. A zero-chunk result invokes neither the callback nor the default store; handle deletion explicitly using the known document ID. See [empty document updates](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag.md#empty-document-updates).

On `PostgresStore`, `ReplaceByFilterAsync` wraps DELETE + INSERT in a single transaction — queries always see either the old data or the new data, never an empty gap. Other stores (InMemory, Qdrant, Pinecone) perform sequential delete + insert via the default interface method.

### Custom Processing

When a document becomes shorter, upserting its new chunks alone leaves old tail chunks searchable. `onDocumentEmbedded` completely replaces default persistence, so use the normalized `document_id` supplied in the records to replace the entire document. The callback receives one validated nonempty document at a time:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        Console.WriteLine($"Indexed {records.Count} chunks");
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

<a id="url-documents"></a>

## Read URL documents safely

A server may compress a text document for transport. `AddUrl` decodes `gzip`, `deflate` and Brotli (`br`) before reading text and checks that the compressed stream is complete. A successful HTTP transfer is not enough: truncated compressed data, decompression errors or failed checksum checks in formats that provide a checksum abort loading before embedding or persistence, preserving that document's previous records. Unsupported or stacked `Content-Encoding` values are also rejected before embedding or persistence.

Gzip/deflate stream completeness and checksum validation use SharpZipLib 1.4.2; Brotli uses the standard decoder.

Pass `cancellationToken` to `RagStore.BuildAsync` to stop waiting for a slow URL document. The token reaches the HTTP request, response-body reading and decompression. Cancellation is cooperative and does not undo previously completed document writes.

## Agentic RAG

In standard RAG the pipeline runs once per user message. In Agentic RAG the agent decides **when** to search, **what** to search for, and whether to search **again** if the first result is insufficient — all autonomously inside a ReAct loop.

Register the `RagStore` as a search tool with `WithAgenticRag`, then use `StartRunAsync` for both the collected result and optional streaming:

```csharp
// Build the index once
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// Register RAG as a tool and run the agent
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

await using var run = await service.WithMaxRounds(10).StartRunAsync("Summarise the refund policy.");
var answer = (await run.Result).Text;
```

### Streaming Agentic RAG

```csharp
// Build the index once
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// Register RAG as a tool and stream the agent run
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

await using var streamedRun = await service.WithMaxRounds(10).StartRunAsync(
    "Summarise the refund policy and mention the key eligibility rules.");
await foreach (var content in streamedRun.StreamAsync())
{
    if (content.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Searching docs via: {content.Metadata["function_name"]}");
    }
    else if (content.Type == StreamingContentType.Text)
    {
        Console.Write(content.Content);
    }
}
```

`streamedRun.StreamAsync()` emits text, tool-call, and tool-result events from the same run. The library executes registered tools automatically.

### Combining with Other Tools

```csharp
service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Look up an order status by order ID.",
           ("order_id", "The order ID to look up.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// The agent searches documents for policy AND calls the API for live order data
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Order #12345 — am I eligible for a refund based on the current policy?");
var answer = (await run.Result).Text;
```

### Custom Tool Description

The tool description controls when the agent decides to call RAG. Tailor it to your domain:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "Search internal HR policies, product manuals, and compliance documents. " +
        "Call this tool whenever company-specific policy or product information is needed.");
```

### Per-Call Filters and Structured Traces

Use `queryOptions` when each agent search step needs a fresh `RagQueryOptions`.
If the host app wants structured access to `References`, `Diagnostics`, and other
RAG metadata, register tracing separately with `WithAgenticRagTracing(...)`.

These calls have separate responsibilities:

- `WithAgenticRag(...)` registers the RAG search tool and resolves per-call query options.
- `WithAgenticRagTracing(...)` registers trace observers for Agentic RAG search executions.

```csharp
var traces = new List<AgenticRagSearchTrace>();

service.WithAgenticRag(
    ragStore,
    queryOptions: _ => new RagQueryOptions
    {
        StoreFilter = new VectorFilter()
            .Where("tenant", currentTenantId)
            .Where("storage_id", currentStorageId)
    },
    toolDescription: "Search only the documents the current user is allowed to access.")
    .WithAgenticRagTracing(trace =>
    {
        traces.Add(trace);
    });
```

`queryOptions` receives an `AgenticRagQueryContext` with the current tool name and self-contained search query.
Use `_ => ...` when the filter is fixed for the whole request, or inspect `ctx.Query` / `ctx.ToolName`
when the filter or retrieval policy should vary by search step.

Tracing is registered by service instance and tool name. If you customize the Agentic RAG tool name,
pass the same name to `WithAgenticRagTracing(...)`:

```csharp
service
    .WithAgenticRag(ragStore, toolName: "search_private_docs")
    .WithAgenticRagTracing(
        trace => traces.Add(trace),
        toolName: "search_private_docs");
```

Each `AgenticRagSearchTrace` contains:

- `Query` — the self-contained query generated by the agent for that search step
- `QueryOptions` — the resolved per-call `RagQueryOptions`
- `Result.References` — final selected references
- `Result.RetrievalCandidates` / `Result.RerankedCandidates` — pre-final-selection candidates
- `Result.Diagnostics` — applied TopK/min-score and elapsed timings
- `Succeeded` / `Exception` ??whether the search completed successfully and why it failed when it did not

This makes it easier to implement permission-aware Agentic RAG, reference panels, search-quality analysis,
and audit logging without coupling `StartRunAsync(...)` itself to RAG-specific request types.

### How It Differs from Standard RAG

| | Standard RAG | Agentic RAG |
|---|---|---|
| Search timing | Every message | Agent decides |
| Query formulation | QueryRewriter | Agent itself |
| Number of searches | Once per turn | One or more as needed |
| Tool combination | Not applicable | Any registered tool |
| Setup | `.WithRag()` | `.WithAgenticRag()` + `StartRunAsync` |

> `QueryRewriter` is intentionally bypassed in Agentic RAG. The agent formulates its own self-contained search query, so a separate rewriting step is redundant and could distort the agent's intent.

## Shared RagStore (Multiple Services)

Build the index once, share across multiple AI services:

```csharp
var ragStore = await RagStore.BuildAsync(config => config
    .AddDocuments("./knowledge-base/")
    .UseOpenAIEmbedding(embeddingApiKey)
    .WithTopK(5)
);

var claude = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gpt = new OpenAIService(gptKey, http).WithRag(ragStore);

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

`UpdateOptions` applies the delegate to a cloned options snapshot and swaps it into the pipeline when complete, so in-flight queries continue using the previous snapshot instead of observing partially updated settings.

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
    Console.WriteLine($"FinalTopK={result.Diagnostics.FinalTopK}, RetrievalTopK={result.Diagnostics.RetrievalTopK}, FinalMinScore={result.Diagnostics.AppliedFinalMinScore}, Elapsed={result.Diagnostics.ElapsedMs}ms");
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

### Cloning a Baseline with `Clone()`

When you maintain a baseline `RagQueryOptions` (e.g. tenant `StoreFilter` + a `ProgressAsync` callback) and want per-query variations on top, use `RagQueryOptions.Clone()` so every other field is preserved:

```csharp
// Baseline carried across many queries
var baseline = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant", currentTenantId),
    ProgressAsync = stage => { Console.WriteLine($"Stage: {stage}"); return Task.CompletedTask; }
};

// Per-query override — Clone() keeps StoreFilter and ProgressAsync
var highRecall = baseline.Clone();
highRecall.FinalFilter.TopK = 15;
highRecall.FinalFilter.MinScore = 0.2;

var result = await ragStore.QueryAsync("refund policy?", highRecall);
```

`Clone()` is a deep copy for the option records (`FinalFilter`, `RetrievalDerivation`, `FinalSelection`) and a reference copy for the handle-typed fields (`ProgressAsync`, `StoreFilter`). Reassign those properties explicitly when you want a different callback or filter for that one call.

## Store-Level Metadata Filtering (StoreFilter)

Use `RagQueryOptions.StoreFilter` to pass a `VectorFilter` directly to `IVectorStore.SearchAsync` / `HybridSearchAsync` on every retrieval call. This allows scoping retrieval by tenant, user, category, time range, or any metadata key without wrapping the store in a custom decorator.

```csharp
// Single condition
var options = new RagQueryOptions();
options.StoreFilter = new VectorFilter().Where("storage_id", storageId);
var result = await ragStore.QueryAsync("질문", options, cancellationToken);

// Multiple conditions — storage_id AND folder_path (AND logic, fluent chaining)
options.StoreFilter = new VectorFilter()
    .Where("storage_id", storageId)
    .Where("folder_path", "/docs/private");

// Multi-value filter — only documents from specific tenants
options.StoreFilter = new VectorFilter()
    .WhereIn("storage_id", tenantId1, tenantId2, tenantId3);

// Tenant isolation + user scoping via metadata
options.StoreFilter = new VectorFilter()
    .Where("tenant", "tenant-A")
    .Where("user_id", currentUserId);
```

For this to work, the metadata keys must be stored on `VectorRecord.Metadata` at index time:

```csharp
var doc = new RagDocument
{
    Content = "문서 내용...",
    Metadata = new Dictionary<string, string>
    {
        ["storage_id"] = storageId,
        ["user_id"] = userId
    }
};
await ragPipeline.IndexDocumentAsync(doc, cancellationToken: ct);
```

`StoreFilter = null` (the default) preserves the existing behavior with no filtering.

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
Mythosia.AI.Abstractions              <- IAIService interface
    |
Mythosia.AI.Rag.Abstractions         <- interfaces (IRagPipeline, ITextSplitter, etc.), RagDocument
    |
Mythosia.AI.Rag                      <- fluent API, pipeline, builders, extensions
Mythosia.VectorDb.InMemory (optional) <- InMemoryVectorStore
Mythosia.Documents.Abstractions      <- IDocumentLoader, DoclingDocument
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
    public Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken ct = default)
    {
        // Parse PDF and return documents
    }
}
```
