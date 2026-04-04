# RAG Pipeline Customization

## Why Customize the Pipeline?

The default RAG pipeline works well out of the box, but real-world projects often need more control:

- **Debugging** — which stage is slow? Is the rewriter changing the query in unexpected ways?
- **Prompt engineering** — the default prompt template may not fit your domain's tone or constraints
- **Architecture** — multiple services sharing one index saves memory and keeps embeddings consistent
- **Inspection** — sometimes you need to see what the retrieval returns *before* sending it to the LLM

This chapter covers the tools that give you that control.

## Progress Tracking

Track which RAG stage is executing via a per-query async callback:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Stages: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
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
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDocuments("docs/")
    .BuildAsync();

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
   Query rewrite → Embedding → Retrieval → Context assembly
    ↓
② TemplateContextBuilder replaces {context} and {question}
   → "Answer using the following info.\n[1] Returns within 30 days...\nQuestion: What is the return policy?"
    ↓
③ RagEnabledService creates an AIRequestContext
   RequestMessageOverride = assembled prompt
    ↓
④ _innerService.GetCompletionAsync(original message, context) is called
   → AIService stores context in AsyncLocal
   → Original question is added to conversation history
    ↓
⑤ AIService.GetLatestMessages() replaces the last message
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
// Inside RagEnabledService.GetCompletionAsync
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← original question (saved in history)
    context: BuildRequestContext(processed));    // ← assembled prompt (only the model sees this)

// BuildRequestContext — creates the AIRequestContext
private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)  // ← output of TemplateContextBuilder
    };
}
```

`AIService` stores this context in `AsyncLocal`, and `GetLatestMessages()` replaces the last message with the `RequestMessageOverride`. After the request completes, the context is automatically restored, ensuring no impact on subsequent requests.
