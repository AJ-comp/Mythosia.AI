# Mythosia.VectorDb.Qdrant

[Qdrant](https://qdrant.tech/) vector store implementation for the **Mythosia VectorDb** abstraction layer.

Uses a single Qdrant **collection** (physical container) with payload-based logical isolation:

- **`_namespace`** — first-tier logical partition
- **`_scope`** — second-tier logical partition

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
dotnet add package Mythosia.VectorDb.Qdrant --version 2.0.0
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

// 3. Upsert records — "documents" is a logical namespace within the collection
var record = new VectorRecord("doc-1", embedding, "Hello world");
await store.InNamespace("documents").UpsertAsync(record);

// 4. Search
var results = await store.InNamespace("documents")
    .SearchAsync(queryVector, topK: 5);
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

## Scope & Metadata Filtering

```csharp
// Scope isolation (2nd-tier within namespace)
await store.InNamespace("docs").InScope("tenant-1").UpsertAsync(record);
var results = await store.InNamespace("docs").InScope("tenant-1")
    .SearchAsync(queryVector, topK: 10);

// Metadata filtering
var filter = VectorFilter.ByMetadata("category", "science");
var results = await store.InNamespace("docs")
    .SearchAsync(queryVector, topK: 5, filter: filter);

// Minimum score threshold
var filter = new VectorFilter { MinScore = 0.7 };
var results = await store.InNamespace("docs")
    .SearchAsync(queryVector, topK: 5, filter: filter);
```

## Advanced: Inject a Pre-configured Client

```csharp
using Qdrant.Client;

var client = new QdrantClient("my-qdrant-cloud.example.com", 6334, https: true, apiKey: "my-key");
var store = new QdrantStore(options, client);
// The caller is responsible for disposing the QdrantClient.
```

## Payload Layout

Records are stored as Qdrant points with the following payload keys:

| Key | Description |
| --- | --- |
| `_id` | Original string record ID |
| `_namespace` | Logical namespace for first-tier isolation (omitted if null) |
| `_content` | Text content |
| `_scope` | Scope value for second-tier isolation (omitted if null) |
| `meta.<key>` | User metadata entries |

## ID Mapping

Point IDs are deterministic UUIDs derived from `namespace + record Id` (when namespace is set) or just `record Id` (when null) via MD5 hash. This ensures the same record Id in different namespaces produces distinct points within the shared collection. The original string ID is preserved in the `_id` payload field.

## License

See repository root for license information.
