# Release Notes — Mythosia.VectorDb.Pinecone

## v1.1.0

### Added

- `VerifyConnectionAsync` — sends a `GET /describe_index_stats` request to verify HTTP connectivity to the Pinecone index.
  - Allows callers to verify connectivity before issuing queries or claiming "connected" in UI.
  - Implements the `IVectorStore.VerifyConnectionAsync` contract introduced in Abstractions v2.2.0.

### Compatibility

- Fully backward compatible with v1.0.0. No breaking changes.

---

## v1.0.0

Initial release.

- **PineconeStore** — full `IVectorStore` implementation backed by Pinecone HTTP API.
- **3-tier isolation model** — `Collection` (physical Pinecone index) -> `Namespace` (1st-tier logical partition using Pinecone namespace) -> `Scope` (2nd-tier logical partition via `_scope` metadata).
- **Namespace is optional** — when null, operations use Pinecone default namespace behavior unless `PineconeOptions.DefaultNamespace` is configured.
- **Metadata filtering** — supports AND filtering for metadata key-value pairs.
- **MinScore filtering** — applies `VectorFilter.MinScore` on returned Pinecone matches.
- **Single and batch upsert** — batch upsert auto-chunks by `UpsertBatchSize`.
- **Hybrid-capable storage model** — upserts store dense vectors together with BM25-derived sparse values so retrieval mode can be chosen at query time.
- **Native hybrid search** — `HybridSearchAsync` sends dense and sparse query components together and uses Pinecone server-side fusion.
- **Automatic index provisioning** — optional control-plane based index creation via `PineconeOptions.AutoCreateIndex` with `IndexName`, `Dimension`, `Cloud`, `Region`, and `ControlPlaneHost`.
- **Dotproduct metric guidance** — hybrid-search related failures now surface guidance that the Pinecone index metric must be `dotproduct`.
- **Sparse upserts** — supports upserting sparse vectors directly.
- **Delete by Id / filter / namespace-wide delete-all** — maps to Pinecone delete operations.
- **Client injection** — accepts externally managed `HttpClient` for advanced scenarios.
