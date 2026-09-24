# RAG Pipeline Customization

<a id="indexing-validation"></a>

## Protect existing documents when indexing fails

An invalid custom splitter or embedding response must not silently replace a searchable document with incomplete or mismatched content. The pipeline checks each document before it starts persistence, including when you use `onDocumentEmbedded`.

Before embedding, storage or the persistence callback, a null, empty or whitespace-only `RagDocument.Id` throws `ArgumentException`. Invalid splitter output throws `InvalidOperationException`: a null chunk list or chunk, null `Content` or `Metadata`, a blank chunk ID, or repeated chunk IDs within that document. Duplicate IDs use `StringComparer.Ordinal` (case-sensitive). Chunk values and metadata are copied before the first embedding call.

Valid custom IDs are retained exactly as supplied. There is no automatic ID generation, trimming or repair, and collisions between custom chunk IDs belonging to different documents are not detected globally. Use IDs that are unique in the target collection, such as the [custom splitter example](text-splitters.md). The reserved `document_id` is normalized only on the copy used for storage; source metadata remains unchanged.

Invalid IDs, splitter failures and invalid embedding batches leave that document's previous records intact and do not invoke the persistence callback. All of its batches must pass [embedding validation](rag-embedding.md#embedding-validation) before storage starts. This does not roll back documents already completed earlier in the operation; rollback after storage starts depends on the store or callback.

These checks and response-order corrections do not automatically recover content already overwritten or previously stored vectors paired with the wrong chunks; reindex affected documents from their original sources.

<a id="custom-persistence"></a>

## Replace a whole document in a persistence callback

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
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

A successful zero-chunk split does not invoke this callback or access the default store. Explicitly delete that known document ID from your own store; use `DeleteDocumentAsync` only when targeting the pipeline's store. Replacement atomicity and rollback depend on the chosen store or callback.

<a id="url-documents"></a>

## Read URL documents safely

A server may compress a text document for transport. `AddUrl` decodes `gzip`, `deflate` and Brotli (`br`) before reading text and checks that the compressed stream is complete. A successful HTTP transfer is not enough: truncated compressed data, decompression errors or failed checksum checks in formats that provide a checksum abort loading before embedding or persistence, preserving that document's previous records. Unsupported or stacked `Content-Encoding` values are also rejected before embedding or persistence.

Pass `cancellationToken` to `RagStore.BuildAsync` to stop waiting for a slow URL document. The token reaches the HTTP request, response-body reading and decompression. Cancellation is cooperative and does not undo previously completed document writes.

<a id="custom-retriever"></a>

## Connect a retriever without mandatory embeddings

Product codes often benefit from keyword search, while questions worded differently from the document benefit from semantic search. The selected retriever now prepares only what its search needs; keyword retrieval no longer requires a query embedding first.

- Before: every retrieval strategy received a query embedding.
- After: the selected retriever prepares only the representation it needs.

Implement `IRagRetriever` for an external index or another query representation. `RagRetrievalRequest` carries `Query` (the full semantic query), nullable `TextQuery` (a lexical override), `TopK`, `Filter` and `ProgressAsync`. Built-in retrievers use `Query` when `TextQuery` is null; an empty text override skips the text leg. Custom retrievers own query preparation and must apply the filter, result limit and cancellation.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

Register with `UseRetriever(...)` or `RagPipeline.SetRetriever(...)`. Existing `IRetrievalStrategy` and `SetRetrievalStrategy(...)` remain available through an adapter that still creates query embeddings. Returned records need the content and metadata used by reranking and context assembly.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` skips query embeddings. Document ingestion still splits and embeds chunks for the existing vector store; this is not a text-only indexing API. Lazy initialization can therefore still call document embeddings on the first question.

The query’s `Embedding` stage now depends on the retriever; keyword retrieval does not report it. Custom retrievers can report relevant stages through `request.ProgressAsync`. Document embeddings are unchanged.

## Why Customize the Pipeline?

The default RAG pipeline works well out of the box, but real-world projects often need more control:

- **Debugging** — which stage is slow? Is the rewriter changing the query in unexpected ways?
- **Prompt engineering** — the default prompt template may not fit your domain's tone or constraints
- **Architecture** — multiple services sharing one index saves memory and keeps embeddings consistent
- **Inspection** — sometimes you need to see what the retrieval returns *before* sending it to the LLM

This chapter covers the tools that give you that control.

You can observe retrieval progress separately from answer generation. `ProgressAsync` below reports pipeline stages; the run returned by `StartRunAsync` provides subsequent output and execution control. See the [Run guide](execution-api-transition.md) for the boundary between these stages.

## Progress Tracking

Track which RAG stage is executing via a per-query async callback:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Stages: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Your question", options);
```

This is invaluable for profiling latency — you can measure the time between stages to find bottlenecks.

## Custom Prompt Template

Control how retrieved context is injected into the prompt using `{context}` and `{question}` placeholders:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Use only the following information to answer the question.
        If the answer is not in the context, say "I don't know."

        Context:
        {context}

        Question: {question}
        """)
    .AddDocument("faq.txt")
)
```

A well-crafted template can dramatically reduce hallucination by instructing the model to stay within the provided context.

## Sharing a RagStore

Build the index once and reuse it across multiple service instances — useful when you want to compare providers or run A/B tests:

```csharp
// Build once
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// Reuse across services
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

Both services share the same embeddings and vector index — no duplication of storage or compute.

## RagStore Direct Query

Query the store independently of any AI service to inspect what would be retrieved:

```csharp
RagProcessedQuery result = await store.QueryAsync("What is the return policy?");

Console.WriteLine($"Rewritten query: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` contains the fully assembled prompt that would be sent to the LLM. This is extremely useful for debugging retrieval quality without spending LLM tokens.

## How It Works Internally

When you call `.WithRag()`, a `RagEnabledService` wrapper is created around your AIService. This wrapper automatically connects the RAG pipeline to the LLM call. The key mechanism behind this is [AIRequestContext](request-contexts.md).

### The Full Flow

```
ragService.GetCompletionAsync("What is the return policy?")
    ↓
① RagEnabledService executes the RAG pipeline
   Query rewrite → Filtering → Embedding (when needed) → Retrieval → Context assembly
    ↓
② TemplateContextBuilder replaces {context} and {question}
   → "Answer using the following info.\n[1] Returns within 30 days...\nQuestion: What is the return policy?"
    ↓
③ RagEnabledService creates an AIRequestContext
   RequestMessageOverride = assembled prompt
    ↓
④ _innerService.GetCompletionAsync(original message, context: context) is called
   → AIService stores context in AsyncLocal
   → Original question is added to conversation history
    ↓
⑤ AIService.GetLatestMessages() replaces the initial input of the current request
   Conversation history: "What is the return policy?" (original preserved)
   What the model sees: assembled prompt (RequestMessageOverride)
```

### Why This Design?

The key insight is **separating conversation history from model input**:

- **Conversation history keeps the original question** — so follow-up questions like "what about that?" have correct context
- **The model receives the assembled prompt** — the full prompt with retrieved documents + question
- **AIService state is never mutated** — `AsyncLocal<T>` provides per-request isolation

This is the real-world use case of `RequestMessageOverride` described in the [AIRequestContext](request-contexts.md) documentation. The RAG pipeline leverages this mechanism automatically, so all you need to do is call `.WithRag()`.

### In Code

Here's the core code inside `RagEnabledService` where this connection happens:

```csharp
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
var original = new Message(ActorRole.User, query);
return await _innerService.GetCompletionAsync(
    original,
    context: BuildRequestContext(processed, original),
    cancellationToken: cancellationToken);

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
{
    var requestMessage = original.Clone();
    requestMessage.Content = processed.RequestMessageContent;
    if (original.HasMultimodalContent)
    {
        requestMessage.Contents = new List<MessageContent>
        {
            new TextContent(processed.RequestMessageContent)
        };
        requestMessage.Contents.AddRange(
            original.Contents.Where(content => !(content is TextContent)));
    }
    return new AIRequestContext { RequestMessageOverride = requestMessage };
}
```

`AIService` stores the context in `AsyncLocal`. `GetLatestMessages()` applies `RequestMessageOverride` to the initial input of the current logical request, preserving later assistant tool calls and tool results. This keeps retrieved documents and tool outputs together in subsequent model requests. After completion, the previous context is restored.
