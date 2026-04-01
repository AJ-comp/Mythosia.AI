# Vector Database Overview

Mythosia.AI provides a unified `IVectorStore` abstraction that works across multiple vector database backends. You write your application against the interface once and swap backends without changing any retrieval logic.

## Core Interface: `IVectorStore`

```csharp
// Upsert
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Search
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Get by ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Delete
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Utility
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Data Models

### VectorRecord

Every stored entry is a `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Unique identifier
    public float[] Vector { get; set; }                      // Embedding vector
    public string Content { get; set; }                      // Original text content
    public Dictionary<string, string> Metadata { get; set; } // Custom key-value metadata
}
```

Use the `Metadata` dictionary for any custom fields — source file, language, date, category, etc.:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("Some text"),
    Content = "Some text",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "en",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

Search results pair a record with its similarity score:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (higher = more similar)
}
```

## Available Backends

| Backend | Package | Use Case |
|---------|---------|----------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Development, testing, demos |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Production, native hybrid search |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Serverless managed service |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Existing Postgres deployments, ACID |

All backends implement the same `IVectorStore` interface. See [Backend Setup](vectordb-backends.md) for per-backend configuration.

## Dependency Injection

Register any backend as `IVectorStore`:

```csharp
// In-Memory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "my-collection",
    Dimension = 1536
}));

// PostgreSQL
services.AddSingleton<IVectorStore>(new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Database=vectors;",
    Dimension = 1536,
    EnsureSchema = true
}));
```

## Filter Execution by Backend

`VectorFilter` conditions are pushed down to the backend where possible:

| Operator | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Client | **Server** | **Server** | **SQL** |
| In / NotIn | Client | **Server** | **Server** | **SQL** |
| Gt / Gte / Lt / Lte | Client | Client | Client | **SQL** |
| Like | Client | Client | Client | **SQL** |
| Exists / NotExists | Client | Client | Client | **SQL** |

Postgres has full SQL pushdown for all operators. Qdrant and Pinecone push down equality and set membership natively.
