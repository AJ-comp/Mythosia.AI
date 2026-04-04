# VectorFilter

`VectorFilter` is a fluent API for filtering vector store queries by metadata. It applies to `IVectorStore.SearchAsync`, `HybridSearchAsync`, and RAG queries.

## Basic Equality

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "en");
```

## Comparison Operators

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| Method | SQL Equivalent |
|--------|---------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## Set Membership

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## Key Existence

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // Key must be present
    .WhereNotExists("deprecated");   // Key must be absent
```

## Logical Grouping (AND / OR)

Conditions at the same level are combined with AND by default. Use `.Or()` to create OR groups:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

Nested AND:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "en").Where("region", "us"))
        .And(a => a.Where("lang", "ko").Where("region", "kr"))
    );
// (lang = "en" AND region = "us") OR (lang = "ko" AND region = "kr")
```

## Score Threshold

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Using with Vector Store

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contract")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## Using with RAG

Pass as `StoreFilter` in `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "product-manual.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("How do I reset the device?", options);
```

## Merging Filters

Use `AppendConditionsFrom` to combine two filters (e.g., merging a pipeline-level filter with a per-query filter):

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "en");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter now has both conditions
```
