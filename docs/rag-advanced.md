# Advanced RAG

## Hybrid Search

Blends dense vector search with BM25 keyword search. Better recall for queries with specific terms or names:

```csharp
.WithRag(rag => rag
    .UseHybridRetrieval(vectorWeight: 0.6f)  // 60% vector, 40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight` ranges from 0.0 (pure BM25) to 1.0 (pure vector). A value around 0.5–0.7 works well in most cases.

## Query Rewriting

Resolves multi-turn pronoun references and expands queries for better retrieval. A `LlmQueryRewriter` uses the AI service itself to rewrite the query before embedding:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter()             // Uses the same AI service
    .WithQueryRewriteMaxTokens(250)  // Token budget for rewriting
    .AddDocument("docs.txt")
)
```

Given a conversation like:
> User: "Tell me about the refund policy."
> User: "What about exceptions to **it**?"

The rewriter expands "it" → "the refund policy exceptions" before retrieval.

It also implements a **search gate**: if the query doesn't need retrieval (e.g. "Thanks!"), it skips the vector search entirely.

## Re-ranking

Re-rankers score the initial retrieval candidates and reorder them by relevance before building the context:

### LLM Reranker

Uses your AI service to score results. Effective but adds latency:

```csharp
.WithRag(rag => rag
    .UseLlmReranker(aiService)
    .AddDocument("corpus.txt")
)
```

### Cohere Reranker

Calls the Cohere Rerank API — fast and accurate:

```csharp
.WithRag(rag => rag
    .UseCohereReranker(cohereApiKey)
    .AddDocument("corpus.txt")
)
```

### vLLM Reranker

Uses a locally hosted vLLM reranking endpoint:

```csharp
.WithRag(rag => rag
    .UseVllmReranker("http://localhost:8000")
    .AddDocument("corpus.txt")
)
```

## Retrieval Parameters

Control how many candidates are retrieved and how they are filtered before final selection:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // Final number of chunks returned
    .WithRetrievalMultiplier(3)    // Retrieve topK × 3 candidates (for reranking)
    .WithMinScore(0.6)             // Minimum similarity score
    .AddDocument("corpus.txt")
)
```

`WithRetrievalMultiplier` is useful when using a reranker — retrieving more candidates gives the reranker more to work with.

## Final Selection Mode

When a reranker is used, choose how the final ranking score is calculated:

```csharp
using Mythosia.AI.Rag;

// Default: trust reranker scores only
.WithFinalSelectionMode(RagFinalSelectionMode.RerankerOnly)

// Blend retrieval score and reranker score
.WithFinalSelectionMode(RagFinalSelectionMode.WeightedBlend)
.WithRetrievalWeightBlend(0.65)  // 65% retrieval, 35% reranker
```

`WeightedBlend` preserves the original retrieval signal while incorporating reranker judgment.

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

## Sharing a RagStore

Build the index once and reuse it across multiple service instances:

```csharp
// Build once
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDirectory("docs/", ".txt", ".md", ".pdf")
    .BuildAsync();

// Reuse across services
var claudeRag = new ClaudeService(apiKey, http).WithRag(store);
var gptRag    = new ChatGptService(apiKey, http).WithRag(store);
```

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

`result.RequestMessageContent` contains the fully assembled prompt that would be sent to the LLM.

## Multi-Turn RAG

Pass conversation history to the store query so the rewriter can resolve references:

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("What is the refund policy?", "You can return items within 30 days."),
    new ConversationTurn("What about digital products?", "Digital products are non-refundable.")
};

var result = await store.QueryAsync(
    query: "Are there any exceptions to that?",
    conversationHistory: history
);
```
