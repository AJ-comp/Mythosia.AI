# Release Notes — Mythosia.VectorDb.Pinecone

## v2.0.0

### Breaking Changes

`VectorFilter` construction API changed (see `Mythosia.VectorDb.Abstractions` v3.0.0). Any code that builds a `VectorFilter` to pass to `SearchAsync`, `HybridSearchAsync`, `GetAsync`, `GetBatchAsync`, `CountAsync`, `DeleteAsync`, `DeleteByFilterAsync`, or `ReplaceByFilterAsync` must be updated:

```csharp
// Before — compile error in v2.0.0
store.SearchAsync(vector, filter: VectorFilter.ByMetadata("k", "v"));
store.CountAsync(new VectorFilter { MetadataMatch = new Dictionary<string, string> { ["k"] = "v" } });

// After
store.SearchAsync(vector, filter: new VectorFilter().Where("k", "v"));
store.CountAsync(new VectorFilter().Where("k", "v"));
```

### Changed

- **Pinecone metadata filter builder** — rewrote `BuildMetadataFilter` / `BuildPineconeMetadataCondition` to support the `VectorFilter` fluent condition tree introduced in `Mythosia.VectorDb.Abstractions` v3.0.0.
  - **`Eq`** → `{ "$eq": value }`
  - **`Ne`** → `{ "$ne": value }`
  - **`Gt / Gte / Lt / Lte`** → `{ "$gt": value }` / `{ "$gte": value }` / `{ "$lt": value }` / `{ "$lte": value }`
  - **`In`** → `{ "$in": [values] }`
  - **`NotIn`** → `{ "$nin": [values] }`
  - **`And / Or` groups** → `{ "$and": [...] }` / `{ "$or": [...] }`
  - **`Like / Exists / NotExists`** — silently skipped (Pinecone does not support these operators). Evaluated client-side in `MatchesFilter` for `GetAsync` / `GetBatchAsync`.
- **`MatchesFilter`** — updated to recursive condition tree evaluation for `GetAsync` / `GetBatchAsync` post-retrieval filtering.
- **`DeleteAsync`** — condition check updated from `filter.MetadataMatch != null` to `filter.Conditions.Count > 0`.

---

## v1.3.0

### Added

- **`PineconeStore.GetBatchAsync`** — fetches multiple records in a single HTTP call using the Pinecone `/vectors/fetch?ids=...` endpoint. IDs are URL-encoded and batched in one request per namespace. Records that are missing or filtered out are omitted.
- **`PineconeStore.CountAsync`** — returns vector count via `describe_index_stats`. When a metadata filter is present, POSTs the filter to get a filtered count; otherwise uses GET. Namespace-specific counts are read from the `namespaces` stats map.

### Changed

- **Dependency updates**: `System.IO.Hashing` → 10.0.5, `System.Text.Json` → 10.0.5.

### Compatibility

- Fully backward compatible with v1.2.0. No breaking changes.

---

## v1.2.0

### Compatibility

- Compatible with `Mythosia.VectorDb.Abstractions` v2.3.0.
- `ReplaceByFilterAsync` is available via the default interface method (sequential `DeleteByFilterAsync` → `UpsertBatchAsync`). Pinecone does not support server-side transactions, so sequential execution is the best available behavior.

---

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
