# Backend Setup

## In-Memory

The simplest backend — no external dependencies. Data is held in RAM and lost when the process exits. Good for development, tests, and demos.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Built-in hybrid search**: RRF (Reciprocal Rank Fusion) merges cosine similarity and BM25 keyword scores.

### Diagnostics

```csharp
// List all stored records
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Total: {store.GetTotalRecordCount()}");

// Inspect raw similarity scores
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Production-grade vector database with native hybrid search. Runs as a standalone service via Docker or Qdrant Cloud.

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# Start Qdrant locally
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // gRPC port
    CollectionName   = "my-docs",
    Dimension        = 1536,           // Must match your embedding model
    AutoCreateCollection = true        // Creates collection on first upsert
});
```

### All Options

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Required for Qdrant Cloud

    CollectionName         = "my-collection",  // Required
    Dimension              = 1536,             // Required

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Add extra payload indexes for faster server-side filtering
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Distance Strategies

| Value | Description |
|-------|-------------|
| `Cosine` | Cosine similarity — best for normalized embeddings (default) |
| `Euclidean` | L2 distance — lower distance = more similar |
| `DotProduct` | Dot product — use with unit-normalized vectors |

### Hybrid Fusion Strategies

| Value | Description |
|-------|-------------|
| `Rrf` | Reciprocal Rank Fusion — robust rank-based merging (default) |
| `Dbsf` | Distribution-Based Score Fusion — merges by score distribution |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "your-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "your-qdrant-cloud-key",
    CollectionName = "production",
    Dimension      = 1536
}
```

### Using an External QdrantClient

If you already have a configured `QdrantClient` (e.g., from a DI container), pass it directly:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

The store will **not** dispose the externally provided client.

> All vector stores implement `IDisposable`. When you create a store with the standard constructor, call `Dispose()` (or use `using`) to release internal resources.

---

## Pinecone

Fully managed serverless vector database. No infrastructure to manage.

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "your-api-key"
});
```

### Auto-Create Index

If you don't have an index yet, let the SDK create it:

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp", or "azure"
    Region          = "us-east-1"
}
```

> When `AutoCreateIndex` is enabled, the index is created with `dotproduct` metric — required for hybrid (sparse + dense) search.

### All Options

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // Required (or use AutoCreateIndex)
    ApiKey                 = "...",           // Required
    Namespace              = "production",    // Optional: applied to all operations

    UpsertBatchSize        = 100,             // Records per batch upsert request
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### Using an External HttpClient

If you already have a configured `HttpClient` (e.g., from `IHttpClientFactory`):

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

The store will **not** dispose the externally provided client.

---

## PostgreSQL (pgvector)

Uses the [`pgvector`](https://github.com/pgvector/pgvector) extension to add vector similarity search to a standard PostgreSQL database.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Prerequisites

```sql
-- Run once on your PostgreSQL server
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Only if using Trigram text search
```

Or let the SDK handle it automatically with `EnsureSchema = true`.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // Auto-creates extension, table, and indexes
});
```

### Index Types

| Type | Class | When to Use |
|------|-------|-------------|
| HNSW | `HnswIndexOptions` | Default. Fast approximate search. Best for most use cases. |
| IVFFlat | `IvfFlatIndexOptions` | Lower memory. Good for large static datasets. |
| None | `NoIndexOptions` | Sequential scan. Use only for tiny datasets. |

```csharp
// HNSW (default)
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // Max neighbor connections per node
        EfConstruction = 64,   // Search scope during index build (higher = better quality)
        EfSearch       = 40    // Runtime search scope (higher = better recall, slower)
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // Number of inverted lists
        Probes = 10    // How many lists to probe at query time
    }
}

// No index (sequential scan)
new PostgresOptions { Index = new NoIndexOptions() }
```

### Text Search Modes

Used for the keyword side of hybrid search:

| Mode | Best For |
|------|----------|
| `TsVector` | Standard full-text search — English, most Western languages |
| `Trigram` | CJK languages (Korean, Chinese, Japanese), fuzzy matching |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // PostgreSQL text search configuration
}
```

### Distance Strategies

| Value | Postgres Operator | Notes |
|-------|------------------|-------|
| `Cosine` | `<=>` | 1 − cosine similarity (default) |
| `Euclidean` | `<->` | L2 distance |
| `InnerProduct` | `<#>` | Negative dot product — use with unit-normalized vectors |

### Runtime Search Profile

Fine-tune recall vs. latency at query time:

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // Override HNSW ef_search directly
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### All Options

```csharp
new PostgresOptions
{
    ConnectionString  = "...",
    Dimension         = 1536,

    SchemaName        = "public",
    TableName         = "vectors",

    EnsureSchema      = false,
    DistanceStrategy  = DistanceStrategy.Cosine,
    Index             = new HnswIndexOptions(),

    TextSearchConfig  = "simple",
    TextSearchMode    = TextSearchMode.TsVector,

    FailFastOnIndexCreationFailure = true
}
```
