# Mythosia.VectorDb.Pinecone

[Pinecone](https://www.pinecone.io/) vector store implementation for the **Mythosia VectorDb** abstraction layer.

Isolation model used by this package:

- **Collection** = Pinecone **index** (physical storage)
- **Namespace** = Pinecone **namespace** (1st-tier logical partition)
- **Scope** = metadata key **`_scope`** (2nd-tier logical partition)

---

## Installation

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

## Quick Start

```csharp
using Mythosia.VectorDb;
using Mythosia.VectorDb.Pinecone;

var options = new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey = "YOUR_PINECONE_API_KEY"
};

using var store = new PineconeStore(options);

var record = new VectorRecord("doc-1", embedding, "Hello world");
await store.InNamespace("documents").UpsertAsync(record);

var results = await store.InNamespace("documents")
    .SearchAsync(queryVector, topK: 5);
```

`PineconeStore` follows the same hybrid-capable storage model as `QdrantStore`:

- upserts always store dense vectors and sparse BM25-derived values together
- retrieval mode is chosen at query time
  - `SearchAsync` for vector-only retrieval
  - `HybridSearchAsync` for native dense + sparse retrieval

For native hybrid search, the Pinecone index metric must be `dotproduct`.

## Options

| Property | Default | Description |
| --- | --- | --- |
| `IndexHost` | *(required)* | Pinecone data-plane host for your index |
| `ApiKey` | *(required)* | Pinecone API key |
| `DefaultNamespace` | `null` | Namespace used when record/filter namespace is null |
| `UpsertBatchSize` | `100` | Max vectors per upsert request |
| `RequestTimeoutSeconds` | `100` | Timeout when store owns `HttpClient` |
| `AutoCreateIndex` | `false` | Auto-create the index through the Pinecone Control Plane API |
| `IndexName` | `null` | Required when `AutoCreateIndex = true` |
| `Dimension` | `0` | Required when `AutoCreateIndex = true` |
| `Cloud` | `null` | Required when `AutoCreateIndex = true` |
| `Region` | `null` | Required when `AutoCreateIndex = true` |
| `ControlPlaneHost` | `https://api.pinecone.io` | Pinecone Control Plane API base URL |

When `AutoCreateIndex = true`, the index is created with `dotproduct` metric automatically.

## Hybrid Search

```csharp
var hybridResults = await store.InNamespace("documents")
    .HybridSearchAsync(queryVector, "hello world", topK: 5);
```

`SearchAsync` remains pure dense retrieval. `HybridSearchAsync` sends both dense and sparse query components and lets Pinecone perform server-side fusion.

## Scope & Metadata Filtering

```csharp
await store.InNamespace("docs").InScope("tenant-1").UpsertAsync(record);

var results = await store.InNamespace("docs").InScope("tenant-1")
    .SearchAsync(queryVector, topK: 10);

var filter = VectorFilter.ByMetadata("category", "science");
var filtered = await store.InNamespace("docs")
    .SearchAsync(queryVector, topK: 5, filter: filter);
```

## Metadata Layout

Each stored vector uses reserved metadata keys:

| Key | Description |
| --- | --- |
| `_content` | Original text content |
| `_scope` | Scope for 2nd-tier isolation (omitted if null) |
| `<custom>` | User metadata entries from `VectorRecord.Metadata` |

## Vector Replacement

`ReplaceByFilterAsync` is available via the `IVectorStore` default interface method. It performs sequential `DeleteByFilterAsync` → `UpsertBatchAsync`. Pinecone does not support server-side transactions, so sequential execution is the best available behavior:

```csharp
var filter = VectorFilter.ByMetadata("full_path", "/docs/file.md");
await store.InNamespace("documents").ReplaceByFilterAsync(filter, newRecords);
```

## Batch Get & Count

```csharp
// Fetch multiple records by ID — single HTTP call to /vectors/fetch?ids=...
var records = await store.InNamespace("documents").GetBatchAsync(new[] { "id-1", "id-2", "id-3" });

// Count total vectors in a namespace
long count = await store.InNamespace("documents").CountAsync();

// Count with metadata filter (POSTs filter to describe_index_stats)
long filtered = await store.InNamespace("documents").CountAsync(
    VectorFilter.ByMetadata("category", "finance"));
```

`GetBatchAsync` issues a single `GET /vectors/fetch?ids=id1&ids=id2&...` request per namespace. `CountAsync` calls `describe_index_stats`; when a metadata filter is present, it is sent in a POST body for a server-side filtered count.

## Connection Verification

Call `VerifyConnectionAsync` to test HTTP connectivity to the Pinecone index before running queries:

```csharp
var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey = "YOUR_PINECONE_API_KEY"
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

## API Key Note

Set your Pinecone API key securely (for example, environment variables or secret manager). Do not hardcode secrets in source code.
