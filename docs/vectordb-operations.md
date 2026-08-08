# Vector Store Operations

## Upsert

Insert or update a single record. If a record with the same `Id` already exists, it is replaced.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("Refunds are accepted within 30 days."),
    Content = "Refunds are accepted within 30 days.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "en",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## Batch Upsert

Upsert multiple records in a single call. More efficient than calling `UpsertAsync` in a loop — backends use batch APIs internally where available.

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## Search

Returns the top-K most similar records to a query vector. Optionally filter by metadata before scoring.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("What is the refund policy?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Source: {r.Record.Metadata["source"]}");
}
```

### Filtered Search

Combine vector similarity with metadata filtering:

```csharp
var filter = new VectorFilter()
    .Where("language", "en")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

See [VectorFilter](vector-filter.md) for the full filtering API.

## Hybrid Search

Merges dense vector similarity with keyword (BM25) search. Better recall for queries with specific terms, names, or codes.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("order #12345 status");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "order #12345 status",   // Raw text used for BM25
    topK: 5
);
```

How hybrid search works per backend:

| Backend | Mechanism |
|---------|-----------|
| **InMemory** | RRF merges cosine similarity + Lucene BM25 scores |
| **Qdrant** | Server-side: dense + sparse vectors fused with RRF or DBSF |
| **Pinecone** | Sparse + dense vectors merged server-side |
| **Postgres** | Vector similarity + `tsvector`/`trigram` scores merged in SQL |

## Get by ID

Retrieve a specific record by its ID:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("Not found");
```

Apply a filter to scope the lookup (e.g., when using multi-tenant namespaces):

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## Batch Get

Retrieve multiple records by ID in a single call:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## Delete by ID

Remove a single record:

```csharp
await store.DeleteAsync("record-id-123");
```

## Delete by Filter

Remove all records that match a filter. Use carefully — this is a bulk delete.

```csharp
// Delete all records from a specific document
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## Replace by Filter

Atomically delete all records matching a filter and insert a new set. Useful for re-indexing a document without leaving stale chunks.

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> On Postgres this runs inside a transaction, making it fully atomic.

## Count

Count stored records, optionally scoped by filter:

```csharp
long total   = await store.CountAsync();
long english = await store.CountAsync(new VectorFilter().Where("language", "en"));

Console.WriteLine($"Total: {total}, English: {english}");
```

## Verify Connection

Check that the backend is reachable. Useful in health checks or startup validation:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Vector store connection OK");
}
catch (Exception ex)
{
    Console.WriteLine($"Connection failed: {ex.Message}");
}
```

## Using with RAG

Pass an `IVectorStore` to `RagBuilder` to use any backend as the RAG retrieval store:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "knowledge-base",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey)
        .AddDocuments("docs/")
    );

var answer = await ragService.GetCompletionAsync("What is the return policy?");
```

Or build a `RagStore` independently and share it across multiple AI services:

```csharp
RagStore ragStore = await RagStore.BuildAsync(rag => rag
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey)
    .AddDocument("knowledge-base.pdf"));

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
