# Filtering

> 📍 **Question Answering Pipeline:** [Query Rewriting](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → **`Filtering`** → [Retrieval](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Context Build](rag-context-build.md)

## What is Filtering?

Filtering narrows down **which chunks are even considered** before the similarity search runs. Instead of searching the entire vector store, you can limit the search to specific subsets based on metadata or score thresholds.

Think of it like searching a library. Without filtering, you're searching every book in the entire building. With filtering, you first walk to the right section (e.g., "Medical" or "Legal") and then search only those shelves. The search is faster and the results are more relevant.

The RAG pipeline applies two types of filtering:

1. **Metadata filtering** — include or exclude chunks based on their metadata (e.g., category, tenant, date)
2. **Score filtering** — set a minimum similarity score threshold to discard low-quality matches

## Metadata Filtering

Every chunk stored in the vector store can carry metadata — key-value pairs attached during indexing. Filtering lets you query only the chunks that match specific conditions.

### Per-query Filter

Pass a `VectorFilter` when querying to scope the search:

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("How do I get a refund?", filter: filter);
```

### Fluent Filter API

`VectorFilter` supports a rich set of operators:

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")         // exact match
    .WhereNot("status", "archived")             // not equal
    .WhereIn("region", "us-east", "eu-west")    // value in set
    .WhereGreaterThan("year", "2023")           // range comparison
    .WhereLike("title", "%kubernetes%");        // pattern matching
```

Available operators:

| Method | SQL Equivalent | Description |
| --- | --- | --- |
| `Where` | `=` | Exact match |
| `WhereNot` | `!=` | Not equal |
| `WhereIn` | `IN (...)` | Value in a set |
| `WhereNotIn` | `NOT IN (...)` | Value not in a set |
| `WhereGreaterThan` | `>` | Greater than |
| `WhereGreaterThanOrEqual` | `>=` | Greater than or equal |
| `WhereLessThan` | `<` | Less than |
| `WhereLessThanOrEqual` | `<=` | Less than or equal |
| `WhereLike` | `LIKE` | Pattern matching (`%` = any, `_` = single char) |
| `WhereExists` | `IS NOT NULL` | Metadata key exists |
| `WhereNotExists` | `IS NULL` | Metadata key does not exist |

### Logical Grouping

Combine conditions with AND/OR logic:

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
// Matches: tenant = "acme" AND (category = "billing" OR category = "refund")
```

## Pipeline-level Store Filter

For conditions that should **always apply** (like tenant isolation), set a `StoreFilter` on `RagQueryOptions`. This filter is automatically merged with any per-query filter:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};

var response = await ragService.GetCompletionAsync("question", ragOptions: options);
```

This follows the same pattern as EF Core's Global Query Filter — the store filter always applies and per-query filters add further constraints on top.

### How Filters Merge

When both a pipeline-level `StoreFilter` and a per-query filter are present, they are AND-combined:

```
Final Filter = StoreFilter conditions AND per-query filter conditions
```

Neither side is silently dropped. The store filter conditions come first (permission/tenant constraints), then per-query conditions are appended.

## Score Filtering

The `MinScore` threshold discards chunks whose similarity score falls below a certain level. This prevents low-relevance chunks from polluting the context:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7   // discard anything below 0.7 similarity
    }
};
```

When a [re-ranker](rag-reranking.md) is configured, the pipeline automatically relaxes the retrieval-stage score threshold (using `RetrievalDerivation.MinScoreDivider`) to give the re-ranker a wider candidate pool, then applies the strict `MinScore` after re-ranking.

## Common Use Cases

### Multi-tenant Isolation

Ensure each tenant only sees their own documents:

```csharp
// During indexing — attach tenant metadata
var doc = new RagDocument
{
    Id = "doc-1",
    Content = "...",
    Metadata = { ["tenant_id"] = "tenant-abc" }
};

// During query — filter by tenant
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### Category-scoped Search

Search only within a specific document category:

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("error 404", filter: filter);
```

### Time-based Filtering

Restrict results to recent documents:

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## What Happens Internally

The filtering stage sits between [Embedding](rag-embedding.md) and [Retrieval](rag-hybrid-search.md):

```
Query vector (from embedding) + VectorFilter conditions
    → merged with StoreFilter (if any)
    → MinScore threshold applied
    → passed to retrieval strategy for search
```

The filter doesn't run a separate database query — it's passed along to the vector store's search method, which applies the conditions during the similarity search itself. This keeps filtering efficient and atomic.

## Next Steps

- [Retrieval (Hybrid Search)](rag-hybrid-search.md) — combine vector and keyword search
- [VectorFilter Reference](vector-filter.md) — full filter API documentation
- [Re-ranking](rag-reranking.md) — refine results after retrieval
