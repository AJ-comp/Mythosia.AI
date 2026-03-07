# Release Notes — Mythosia.VectorDb.Pinecone

## v1.0.0

Initial release.

- **PineconeStore** — full `IVectorStore` implementation backed by Pinecone HTTP API.
- **3-tier isolation model** — `Collection` (physical Pinecone index) -> `Namespace` (1st-tier logical partition using Pinecone namespace) -> `Scope` (2nd-tier logical partition via `_scope` metadata).
- **Namespace is optional** — when null, operations use Pinecone default namespace behavior unless `PineconeOptions.DefaultNamespace` is configured.
- **Metadata filtering** — supports AND filtering for metadata key-value pairs.
- **MinScore filtering** — applies `VectorFilter.MinScore` on returned Pinecone matches.
- **Single and batch upsert** — batch upsert auto-chunks by `UpsertBatchSize`.
- **Delete by Id / filter / namespace-wide delete-all** — maps to Pinecone delete operations.
- **Client injection** — accepts externally managed `HttpClient` for advanced scenarios.
