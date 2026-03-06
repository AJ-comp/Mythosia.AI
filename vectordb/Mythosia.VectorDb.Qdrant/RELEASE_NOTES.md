# Release Notes — Mythosia.VectorDb.Qdrant

## v1.0.0

Initial release.

- **QdrantStore** — full `IVectorStore` implementation backed by Qdrant.
- **Single-collection architecture** — uses a single Qdrant collection (`QdrantOptions.CollectionName`) with payload-based logical isolation.
- **3-tier isolation model** — `Collection` (physical) → `Namespace` (1st-tier optional payload filter via `VectorRecord.Namespace`) → `Scope` (2nd-tier optional payload filter via `VectorRecord.Scope`).
- **Namespace is optional** — when `Namespace` is null, no `_namespace` payload filter is applied. Records without namespace coexist in the same collection.
- **Distance strategies** — Cosine, Euclidean (L2), and Dot Product.
- **Auto-create collection** — the collection is provisioned automatically on first use (configurable via `AutoCreateCollection`).
- **Scope isolation** — scope values stored in payload and filtered via Qdrant payload conditions.
- **Metadata filtering** — metadata key-value pairs stored as payload fields with `meta.` prefix.
- **Deterministic UUID mapping** — string record IDs mapped to stable UUIDs via MD5 hash (derived from `namespace + id` when namespace is set, or just `id` when null).
- **Fluent API** — supports `InNamespace()` / `InScope()` via the shared abstractions layer.
- **Client injection** — accepts a pre-configured `QdrantClient` for advanced connection scenarios (TLS, API key, custom gRPC options).
- **Thread-safe collection caching** — avoids redundant `CollectionExists` calls using `SemaphoreSlim`.
