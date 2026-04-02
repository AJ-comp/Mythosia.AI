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
    .AddDirectory("docs/", ".txt", ".md", ".pdf")
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
