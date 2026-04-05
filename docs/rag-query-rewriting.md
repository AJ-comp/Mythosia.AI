# Query Rewriting

> 📍 **Question Answering Pipeline:** **`Query Rewriting`** → Embedding → Filtering → [Retrieval](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → Context Build

## Why Query Rewriting?

In a multi-turn conversation, users naturally use pronouns and short references:

> User: "Tell me about the refund policy."
> User: "What about exceptions to **it**?"

If "What about exceptions to it?" is sent to the vector store as-is, the embedding has no idea what "it" refers to. The search returns irrelevant results, and the answer suffers.

**Query rewriting** resolves these references before retrieval, expanding "it" → "the refund policy exceptions" so the embedding captures the full intent. It also implements a **search gate** — if the query doesn't need retrieval (e.g. "Thanks!"), it skips the vector search entirely, saving latency and cost.

## Configuration

A `LlmQueryRewriter` uses the AI service itself to rewrite the query before embedding:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter()             // Uses the same AI service
    .WithQueryRewriteMaxTokens(250)  // Token budget for rewriting
    .AddDocument("docs.txt")
)
```

The rewriter examines the conversation context and produces a self-contained search query that the vector store can understand without history.

## Multi-Turn RAG

When querying the `RagStore` directly, pass conversation history so the rewriter can resolve references:

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

The rewriter sees the full history and rewrites "Are there any exceptions to that?" into something like "exceptions to the digital product non-refundable policy", producing far better retrieval results.

## How the Search Gate Works

Not every user message needs a document search. The rewriter classifies the query and returns an empty rewrite for messages like:

- "Thanks!"
- "Got it, that's helpful."
- "Can you summarise what you just said?"

When the gate triggers, the entire retrieval pipeline is skipped — no embedding, no vector search, no reranking — and the LLM responds directly from the conversation context.
