# Hybrid Search

## Why Hybrid Search?

Pure vector search excels at capturing semantic meaning — "cancel my subscription" matches "terminate membership" even though they share no words. However, it can miss **exact terms** like product names, error codes, or policy identifiers that a user types verbatim.

BM25 keyword search handles these cases perfectly but fails at semantic understanding. **Hybrid search combines both**, giving you the best of both worlds: semantic understanding plus precise keyword matching.

## Configuration

Blend dense vector search with BM25 keyword search using a single method call:

```csharp
.WithRag(rag => rag
    .UseHybridRetrieval(vectorWeight: 0.6f)  // 60% vector, 40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight` ranges from 0.0 (pure BM25) to 1.0 (pure vector). A value around **0.5–0.7** works well in most cases.

## When to Use What

| Scenario | Recommended Weight |
| --- | --- |
| General Q&A with natural language | 0.7–0.8 (more vector) |
| Technical docs with specific terms | 0.4–0.5 (balanced) |
| Code or error-code lookup | 0.2–0.3 (more BM25) |

## Example

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridRetrieval(vectorWeight: 0.5f)
        .AddDocument("product-catalog.txt")
        .AddDocument("error-codes.txt")
    );

// "ERR-4012" is matched by BM25; semantic context is matched by vector
var answer = await service.GetCompletionAsync("How do I fix ERR-4012?");
```
