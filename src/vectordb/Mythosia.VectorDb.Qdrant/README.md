# Mythosia.VectorDb.Qdrant

[Qdrant](https://qdrant.tech/) vector store implementation for the **Mythosia VectorDb** abstraction layer.

Uses a single Qdrant **collection** (physical container) with payload-based metadata filtering for logical isolation.
All isolation keys (e.g. `namespace`, `scope`, `category`) are standard metadata entries — there are no framework-reserved keys.

---

## Migration from v1.0.0

`v1.0.0` collections are dense-only. `v2.0.0` uses hybrid-capable collections and writes a schema marker so the tooling can detect whether migration is needed.

Install the migration tool first:

```powershell
Install-Package Mythosia.VectorDb.Tools
```

If `docs` is the collection you want to migrate, run:

```bash
mythosia-vectordb migrate qdrant --endpoint localhost:6334 --source docs --replace
```

This migrates through a staging collection, then recreates `docs` with the new schema and copies the migrated data back into `docs`.

If your Qdrant server is remote or authenticated, add `--api-key your-api-key` and use your remote endpoint URL.

Stop application writes before migration if consistency matters.

## Installation

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

Current package version:

```bash
dotnet add package Mythosia.VectorDb.Qdrant --version 4.1.0
```

## Quick Start

```csharp
using Mythosia.VectorDb;
using Mythosia.VectorDb.Qdrant;

// 1. Configure — CollectionName is the physical Qdrant collection
var options = new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "my_vectors",                  // physical collection
    Dimension      = 1536,                          // must match your embedding model
    DistanceStrategy = QdrantDistanceStrategy.Cosine
};

// 2. Create the store
using var store = new QdrantStore(options);

// 3. Upsert records
var record = new VectorRecord("doc-1", embedding, "Hello world");
record.Metadata["namespace"] = "documents";         // optional logical isolation via metadata
await store.UpsertAsync(record);

// 4. Search — use VectorFilter.Where() for logical isolation
var filter = new VectorFilter().Where("namespace", "documents");
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

## Options

| Property | Default | Description |
| --- | --- | --- |
| `Host` | `"localhost"` | Qdrant server host |
| `Port` | `6334` | Qdrant gRPC port |
| `UseTls` | `false` | Enable TLS for gRPC |
| `ApiKey` | `null` | Optional API key |
| `CollectionName` | *(required)* | Qdrant collection name (physical container) |
| `Dimension` | *(required)* | Embedding vector dimension |
| `DistanceStrategy` | `Cosine` | `Cosine`, `Euclidean`, or `DotProduct` |
| `AutoCreateCollection` | `true` | Auto-create the collection on first use |
| `HybridFusionStrategy` | `Rrf` | Server-side fusion for hybrid search — `Rrf` (Reciprocal Rank Fusion) or `Dbsf` (Distribution-Based Score Fusion) |
| `AdditionalPayloadIndexes` | `[]` | Extra payload fields to index on collection creation (e.g. `meta.author`). |

## Hybrid Search (v2.0.0)

`QdrantStore` always provisions/uses hybrid-capable storage (dense + sparse) and supports native `IVectorStore.HybridSearchAsync`.
Choose retrieval mode at query time (`SearchAsync` for vector-only, `HybridSearchAsync` for native hybrid):

```csharp
var options = new QdrantOptions
{
    Host              = "localhost",
    Port              = 6334,
    CollectionName    = "my_vectors",
    Dimension         = 1536
};

var store = new QdrantStore(options);
```

On upsert, BM25 sparse vectors are automatically computed from the record's `Content` and stored alongside the dense embedding. Hybrid search uses Qdrant's built-in **prefetch + fusion (RRF/DBSF)** for server-side scoring.

When used via the RAG pipeline:

```csharp
var store = await RagStore.BuildAsync(config => config
    .AddDocument("docs.txt")
    .UseOpenAIEmbedding(apiKey)
    .UseVectorStore(new QdrantStore(new QdrantOptions
    {
        Host = "localhost",
        Dimension = 1536
    }))
    .UseHybridSearch()
);
```

## Metadata Filtering

```csharp
// Logical isolation via metadata
var filter = new VectorFilter().Where("namespace", "docs");
var results = await store.SearchAsync(queryVector, topK: 10, filter: filter);

// Multiple metadata conditions
var filter = new VectorFilter()
    .Where("namespace", "docs")
    .Where("category", "science");
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);

// Minimum score threshold
var filter = new VectorFilter { MinScore = 0.7 };
var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

### Supported filter operators

| Operator | Qdrant translation |
| --- | --- |
| `Eq` | `Must` → `FieldCondition` keyword match |
| `Ne` | `MustNot` → `FieldCondition` |
| `In` | `Must` → nested `Should` (keyword per value) |
| `NotIn` | `MustNot` → nested `Should` |
| `And / Or groups` | Nested `Condition{Filter}` in `Must` / `Should` |
| `Gt / Gte / Lt / Lte / Like / Exists / NotExists` | **Silently ignored** for `SearchAsync` / `HybridSearchAsync` (no client-side fallback). Evaluated client-side via `MatchesFilter` for `GetAsync` / `GetBatchAsync` only. |

> **Important**: Unsupported operators produce no error but filter nothing in search queries. Use `GetAsync` / `GetBatchAsync` when these operators are required.

## VectorFilter

For the full operator reference and fluent API examples (`Where`, `WhereNot`, `WhereIn`, `WhereLike`, `WhereExists`, `Or`, `And`, `WithMinScore`, etc.), see the [Mythosia.VectorDb.Abstractions README](../Mythosia.VectorDb.Abstractions/README.md#vectorfilter).

> **Qdrant-specific note**: `Gt`, `Gte`, `Lt`, `Lte`, `Like`, `Exists`, `NotExists` are silently ignored in `SearchAsync` / `HybridSearchAsync`. They are only evaluated client-side in `GetAsync` / `GetBatchAsync`.

## Resource Disposal

`QdrantStore` implements `IDisposable`. When created via the standard constructor, it owns the internal `QdrantClient` and disposes it on `Dispose()`:

```csharp
using var store = new QdrantStore(options);
// QdrantClient disposed automatically
```

When injecting a pre-configured client, the caller retains ownership and is responsible for disposal:

```csharp
using Qdrant.Client;

var client = new QdrantClient("my-qdrant-cloud.example.com", 6334, https: true, apiKey: "my-key");
var store = new QdrantStore(options, client);
// store.Dispose() will NOT dispose client — caller must dispose it separately
```

## Payload Layout

Records are stored as Qdrant points with the following payload keys:

| Key | Description |
| --- | --- |
| `_id` | Original string record ID |
| `_content` | Text content |
| `meta.<key>` | User metadata entries (e.g. `meta.namespace`, `meta.category`) |

## ID Mapping

Point IDs are deterministic UUIDs derived from `record Id` via MD5 hash. The original string ID is preserved in the `_id` payload field.

## Batch Get & Count

```csharp
// Fetch multiple records by ID — single Qdrant gRPC RetrieveAsync call
var records = await store.GetBatchAsync(new[] { "id-1", "id-2", "id-3" });

// Count total vectors (excludes internal schema marker)
long count = await store.CountAsync();

// Count with metadata filter
long filtered = await store.CountAsync(
    new VectorFilter().Where("category", "finance"));
```

`GetBatchAsync` maps string IDs to deterministic Qdrant point UUIDs, calls the Qdrant batch `RetrieveAsync` API, and applies metadata conditions client-side. `CountAsync` uses the Qdrant server-side `CountAsync` API and always excludes the internal schema marker point from the result.

## Vector Replacement

`ReplaceByFilterAsync` is available via the `IVectorStore` default interface method. It performs sequential `DeleteByFilterAsync` → `UpsertBatchAsync`. Qdrant does not support server-side transactions, so sequential execution is the best available behavior:

```csharp
var filter = new VectorFilter().Where("full_path", "/docs/file.md");
await store.ReplaceByFilterAsync(filter, newRecords);
```

## Connection Verification

Call `VerifyConnectionAsync` to test gRPC connectivity before running queries:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    Host = "localhost",
    Port = 6334,
    CollectionName = "my_vectors",
    Dimension = 1536
});

try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Connected!");
}
catch (Exception ex)
{
    Console.WriteLine($"Connection failed: {ex.Message}");
}
```

## License

See repository root for license information.
