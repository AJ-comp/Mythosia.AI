# RAG (Retrieval-Augmented Generation)

Need only the completed answer and a Stop button? Pass `cancellationToken` to `GetCompletionAsync`. Use Run for progress events or supported steering. See [completion cancellation](completions.md#completion-cancellation).

For a retrieval-augmented answer, pass `cancellationToken` to `RagEnabledService.GetCompletionAsync` too. The same token reaches retrieval, `LlmQueryRewriter`, `LlmReranker`, and the inner completion; cancellation during retrieval prevents the later model call. `RagPipeline.QueryAndGenerateAsync` also forwards its token. Each component must cooperate with cancellation, and already completed retrieval or tool actions are not rolled back.

RAG lets the model answer questions based on your own documents by retrieving relevant chunks at query time.

To display an answer while it is being written from retrieved material, or let the user stop it, use `RagEnabledService.StartRunAsync`. Retrieval runs once before execution; steering does not automatically retrieve again. See the [Run guide](execution-api-transition.md) for usage.


When you hold an `IAIService`, use `GetLastProcessing()` from `Mythosia.AI.Extensions`; it reads the optional `IAIProcessingInfoService` and returns an empty list when diagnostics are unavailable. `IAIService` gains no required member. For RAG, `RagEnabledService.WithSpeed(...)` configures the next answer after retrieval, and `LastProcessing` describes that answer; internal query rewriting remains separate. Run results expose the same `Processing` records. [WithSpeed](request-building.md#inference-speed)

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

To compare local neural sparse retrieval with the existing search, use the optional `Mythosia.AI.Rag.Search.Pixie` preview. It keeps your dense embedding provider and uses an in-memory PIXIE index; it does not migrate persistent stores or replace the default search. [PIXIE setup and comparison guide](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## Answer about an attachment using your documents

To explain a product photo using your manual, pass a `Message` containing the question and image to `RagEnabledService.GetCompletionAsync(Message)` or `StartRunAsync(Message)`. Both preserve non-text attachments in the request sent to the inner AI service. Retrieval uses the message text; attachments are not automatically indexed or embedded. The selected provider and model must support the attachment type. Retrieved context is added only to the outgoing request: it does not overwrite the original `Message` or replace the user's text in conversation history.

When an answer needs both a manual and live inventory, combine RAG with your registered tools. During `GetCompletionAsync` tool rounds, retrieved context stays on the initial input and each subsequent tool result is sent to the model unchanged. The original user input remains in conversation history.

<a id="retrieval-modes"></a>

## Choose how documents are retrieved

Product codes often benefit from keyword search, while questions worded differently from the document benefit from semantic search. The selected retriever now prepares only what its search needs; keyword retrieval no longer requires a query embedding first.

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` skips query embeddings. Document ingestion still splits and embeds chunks for the existing vector store; this is not a text-only indexing API. Lazy initialization can therefore still call document embeddings on the first question.

See [retrieval modes and store support](rag-hybrid-search.md) and [custom retrievers](rag-pipeline.md#custom-retriever).

## Adding Documents

Several source types are supported:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // local file
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Inline content can go here too.")   // raw string
)
```

`AddUrl` validates and decodes supported HTTP compression before reading text, rejecting incomplete, unsupported or stacked encodings. See [URL decoding and cancellation](rag-pipeline.md#url-documents).

<a id="document-identity"></a>

### Keeping files with the same name separate

Two companies may each provide a `docs/faq.txt`. Both documents must remain in the index, while registering the same file again should reuse its identity:

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

In the default RAG storage flow, document IDs are assigned before records reach the vector store; reindexing replaces records matching `document_id`. This library's PostgreSQL (pgvector) store follows that filter and does not inspect the original file path itself. Previously, directory registration produced `faq.txt` for both `company-a/docs/faq.txt` and `company-b/docs/faq.txt`, so the second document replaced the first. The fix retains the full path when forming the ID; the PostgreSQL schema is unchanged. A `full_path` filter in a storage example uses caller-supplied metadata; it does not automatically generate unique document or record IDs.

The built-in `PlainTextDocumentLoader` and `DirectoryDocumentLoader` use the file's normalized absolute path (`Path.GetFullPath`) as `Source` and the automatic document ID. Different directories therefore produce different IDs. Relative, absolute and `./` paths reuse an ID when they resolve to the same absolute path with the same casing. Keep the working directory consistent when using relative paths. Moving files or accessing them through symbolic links, hard links or different casing is not guaranteed to preserve the ID.

`AddText(..., id: ...)`, an explicitly assigned `RagDocument.Id`, and custom loader `Source` rules remain unchanged. No caller API changes are required. Since `Source` is now absolute for these built-in loaders, default citations may also display an absolute path. For display, use `filename`, or `relative_path` from the default directory loader. The configured directory overload does not automatically add `relative_path`.

**Existing indexes:** old relative-path IDs are not automatically deleted or migrated. Prefer rebuilding all documents in a new collection, verifying it, then switching the application to it. If reusing a collection, delete only old document IDs whose ownership you have confirmed, then reindex their source files. Do not broadly delete matching filenames: other directories may own documents with the same name.

To keep updates and deletions limited to the intended document, `document_id` is reserved by the pipeline. Before persistence, each record receives the actual `RagDocument.Id`, even if input metadata supplies another value. The input document's and splitter's metadata dictionaries are not modified; custom persistence callbacks also receive the normalized records. Use a different key for an application-specific ID.

This does not repair previously stored records with an incorrect `document_id`. Rebuild from trusted source documents into a new collection, or identify and clean up only the affected records before reindexing. Reindexing the correct ID alone cannot reliably find records stored under another ID.

Registering the same file through a relative path and an absolute path must update one document, while same-named files in different folders must stay separate. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` and `PdfDocumentLoader` now set `DoclingDocument.Source` to the normalized absolute file path, as the built-in TXT loaders do. RAG derives automatic document IDs from this value; explicit IDs remain caller-controlled. Default citations may therefore show absolute paths.

[Keep file identity stable across registrations](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### Clearing a document without keeping stale search results

If you clear a retired refund policy and reindex the same document, its old text must stop appearing in answers. With default RAG storage, a successful split that produces zero chunks replaces records matching that document's `document_id` with an empty set. No embeddings are requested, and other document IDs are untouched. This includes empty or whitespace-only documents when their splitter returns zero chunks, as well as custom splitters that successfully return zero chunks.

For an already configured `RagPipeline` named `pipeline`, reuse the stored document ID:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

You can index new nonempty content under the same ID later. A loader returning no documents, or a document missing from a later file list, is not a deletion instruction: no document ID was supplied for replacement.

Loading, parsing or splitting exceptions, and cancellation observed before the storage call, leave that document's stored records unchanged. Loaders and parsers must report failures as exceptions; a successful zero-chunk result cannot be distinguished from intentional clearing. Once storage starts, failure/cancellation rollback depends on the store implementation (the PostgreSQL replacement uses a transaction). A batch processes documents individually; it does not roll back earlier completed documents.

**Custom persistence:** when `onDocumentEmbedded` is supplied, it continues to own persistence. Zero chunks do not invoke the callback or access the default store. The application must explicitly delete the known document ID in its own store, or use `DeleteDocumentAsync` for the pipeline's store.

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

If your provider already manages the document index, compare [hosted file search and RAG](reasoning-and-search.md) before choosing who will handle retrieval. The same guide explains reasoning options and hosted citations on a RAG answer.

- [Hybrid Search](rag-hybrid-search.md) — combine semantic and keyword search
- [Query Rewriting](rag-query-rewriting.md) — optimize queries with conversation context
- [Re-ranking](rag-reranking.md) — further refine search result accuracy
- [Pipeline Customization](rag-pipeline.md) — fine-grained control over the RAG process
- [Agentic RAG](rag-agentic.md) — AI decides when and what to search
- [Vector Stores](vectordb-overview.md) — persistent storage setup
- [Text Splitters](text-splitters.md) — customize how documents are chunked

Perplexity: [Use Perplexity vectors in your document index / Search without generating an answer](perplexity.md).
