# Context Build

> 📍 **Question Answering Pipeline:** [Query Rewriting](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → [Filtering](rag-filtering.md) → [Retrieval](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → **`Context Build`**

## What is Context Build?

Context Build is the final stage of the RAG pipeline. After retrieving and ranking the most relevant chunks, this stage **assembles them into a prompt** that the LLM can understand and use to generate an answer.

Think of it as writing a briefing document for someone before a meeting. You've gathered all the relevant information (retrieval) and sorted it by importance (re-ranking). Now you need to **organize it clearly** and frame the question so the reader knows exactly what to do with the information.

The quality of this stage directly impacts the LLM's response quality. A well-structured prompt reduces hallucination and helps the model stay grounded in the provided context.

## Default Context Builder

When no custom configuration is set, the pipeline uses `DefaultContextBuilder`, which produces this format:

```
Answer the question based on the following context:

[1] (Source: manual.txt)
Refunds are available within 30 days of purchase...

[2] (Source: policy.txt)
Digital products are non-refundable...

Question: What is the refund policy?
```

The default builder has configurable properties:

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "Answer the question based on the following context:",
    QueryPrefix = "Question:",
    IncludeScores = false,    // show similarity scores?
    IncludeSource = true      // show source metadata?
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

### Including Scores

When `IncludeScores = true`, each chunk shows its similarity score:

```
[1] (Source: manual.txt) [Score: 0.892]
Refunds are available within 30 days of purchase...
```

This is useful for debugging and understanding why certain chunks were selected.

## Prompt Templates

For more control over the final prompt, use a **prompt template** with `{context}` and `{question}` placeholders:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        You are a customer support assistant. Use ONLY the following documents
        to answer the question. If the answer is not in the documents, say
        "I don't have that information."

        Documents:
        {context}

        Customer Question: {question}
        """)
    .AddDocument("support-kb.txt")
)
```

The pipeline replaces `{context}` with the numbered chunk list and `{question}` with the user's query. Internally, this creates a `TemplateContextBuilder` that formats chunks as:

```
[1] First chunk content...

[2] Second chunk content...
```

### When to Use Templates

Templates are especially powerful when you need to:

- **Restrict behavior** — "If the answer is not in the context, say 'I don't know'"
- **Set the tone** — "Respond in a professional, concise manner"
- **Add role context** — "You are a medical assistant" or "You are a legal advisor"
- **Control language** — "Always respond in Korean" or "Use formal Japanese"

### Template Design Tips

| Tip | Example |
| --- | --- |
| Tell the model to stay in context | "Base your answer ONLY on the provided documents" |
| Handle missing information | "If the answer is not found, say 'I don't have that information'" |
| Specify output format | "Respond with bullet points" |
| Set language constraints | "Always respond in the same language as the question" |

## Custom Context Builder

For complete control, implement `IContextBuilder`:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### Relevant Information ###");
        sb.AppendLine();

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "unknown";
            sb.AppendLine($"📄 From: {source} (relevance: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine();
        sb.AppendLine($"Based on the above information, answer: {query}");

        return sb.ToString();
    }
}
```

Register it with the builder:

```csharp
.WithRag(rag => rag
    .WithContextBuilder(new MyContextBuilder())
    .AddDocument("docs.txt")
)
```

## What Happens Internally

The context build stage receives:

1. The original query string
2. The final list of `VectorSearchResult` objects (after filtering, retrieval, and optional re-ranking)

It produces a single string that becomes the prompt content sent to the LLM:

```
Search results + Query → ContextBuilder.BuildContext() → Prompt string → LLM
```

The resolution order for which context builder is used:

1. **Custom `IContextBuilder`** — if set via `.WithContextBuilder()`
2. **`TemplateContextBuilder`** — if a prompt template is set via `.WithPromptTemplate()`
3. **`DefaultContextBuilder`** — the fallback default

## Next Steps

- [Pipeline Customization](rag-pipeline.md) — fine-tune the overall RAG behavior
- [Re-ranking](rag-reranking.md) — improve the quality of chunks before context building
- [RAG Basics](rag.md) — review the full RAG flow
