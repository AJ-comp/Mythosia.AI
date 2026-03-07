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

## Options

| Property | Default | Description |
| --- | --- | --- |
| `IndexHost` | *(required)* | Pinecone data-plane host for your index |
| `ApiKey` | *(required)* | Pinecone API key |
| `DefaultNamespace` | `null` | Namespace used when record/filter namespace is null |
| `UpsertBatchSize` | `100` | Max vectors per upsert request |
| `RequestTimeoutSeconds` | `100` | Timeout when store owns `HttpClient` |

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

## API Key Note

Set your Pinecone API key securely (for example, environment variables or secret manager). Do not hardcode secrets in source code.
