# Release Notes — Mythosia.VectorDb.Qdrant

## v3.0.1

### Deprecated

- **`VectorRecord.Namespace`**, **`VectorRecord.Scope`**, **`VectorFilter.Namespace`**, **`VectorFilter.Scope`**, **`VectorFilter.WithNamespace()`**, **`INamespaceContext`**, **`IScopeContext`**, and **`InNamespace()` / `InScope()`** are now marked `[Obsolete]`.
  - These will be removed in a future major version.
  - Use `Metadata` entries (e.g. `Metadata["namespace"]`) and `VectorFilter.Where("namespace", value)` for logical isolation instead.

### Compatibility

- Backward compatible with v3.0.0. Deprecated APIs still function but produce `CS0618` compiler warnings.

---

## v3.0.0

### Breaking Changes

`VectorFilter` construction API changed (see `Mythosia.VectorDb.Abstractions` v3.0.0). Any code that builds a `VectorFilter` to pass to `SearchAsync`, `HybridSearchAsync`, `GetAsync`, `GetBatchAsync`, `CountAsync`, `DeleteAsync`, `DeleteByFilterAsync`, or `ReplaceByFilterAsync` must be updated:

```csharp
// Before — compile error in v3.0.0
store.SearchAsync(vector, filter: VectorFilter.ByMetadata("k", "v"));
store.CountAsync(new VectorFilter { MetadataMatch = new Dictionary<string, string> { ["k"] = "v" } });

// After
store.SearchAsync(vector, filter: new VectorFilter().Where("k", "v"));
store.CountAsync(new VectorFilter().Where("k", "v"));
```

### Changed

- **Qdrant filter builder** — rewrote `BuildFilter` / `AppendConditionsToFilter` to support the `VectorFilter` fluent condition tree introduced in `Mythosia.VectorDb.Abstractions` v3.0.0.
  - **`Eq`** — `Must` → `FieldCondition` keyword match.
  - **`Ne`** — `MustNot` → `FieldCondition` (in `And` context) or nested NOT `Filter` (in `Or` context).
  - **`In`** — `Must` / `Should` → nested `Filter` with per-value `Should` keyword conditions.
  - **`NotIn`** — `MustNot` → nested `Filter` with `Should` keyword conditions.
  - **`And / Or` groups** — nested `Condition { Filter }` in `Must` / `Should` respectively.
  - **`Gt / Gte / Lt / Lte / Like / Exists / NotExists`** — silently skipped in server-side Qdrant filter. Evaluated client-side in `MatchesFilter` for `GetAsync` / `GetBatchAsync`.
- **`MatchesFilter`** — updated to recursive condition tree evaluation for `GetAsync` / `GetBatchAsync` post-retrieval filtering.
- **`DeleteAsync`** — condition check updated from `filter.MetadataMatch != null` to `filter.Conditions.Count > 0`.

---

## v2.3.0

### Added

- **`QdrantStore.GetBatchAsync`** — batch record fetch using the Qdrant gRPC `RetrieveAsync` multi-point API. Applies namespace and filter conditions client-side; records that do not match are omitted.
- **`QdrantStore.CountAsync`** — uses the Qdrant gRPC `CountAsync` API with a constructed filter. Always excludes the internal schema marker point (`SchemaMarkerId`) from the count so the result reflects only user-inserted records.

### Changed

- **`QdrantStore.GetAsync`** — now validates scope and metadata filter conditions via the new `MatchesFilter` helper after the point is fetched. Previously only the namespace payload key was checked.
- **`QdrantStore.DeleteAsync`** — when the filter contains scope or metadata conditions, uses a Qdrant filter-based delete (adds an `id = @id` must-condition) instead of a bare point-ID delete. This ensures scope/metadata conditions are respected atomically at the server. Plain deletes (no scope/metadata filter) continue to use the direct point-ID path.
- **Dependency update**: `System.IO.Hashing` → 10.0.5.

### Compatibility

- Fully backward compatible with v2.2.0. The `GetAsync`/`DeleteAsync` behavior changes only affect callers that pass a scope or metadata filter; plain calls without filter behave identically to before.

---

## v2.2.0

### Compatibility

- Compatible with `Mythosia.VectorDb.Abstractions` v2.3.0.
- `ReplaceByFilterAsync` is available via the default interface method (sequential `DeleteByFilterAsync` → `UpsertBatchAsync`). Qdrant does not support server-side transactions, so sequential execution is the best available behavior.

---

## v2.1.0

### Added

- `VerifyConnectionAsync` — sends a lightweight gRPC `ListCollections` call to verify connectivity to the Qdrant server.
  - Allows callers to verify connectivity before issuing queries or claiming "connected" in UI.
  - Implements the `IVectorStore.VerifyConnectionAsync` contract introduced in Abstractions v2.2.0.

### Compatibility

- Fully backward compatible with v2.0.0. No breaking changes.

---

## v2.0.0

### Added

- Native hybrid retrieval via `IVectorStore.HybridSearchAsync`.
  - Uses Qdrant server-side prefetch + fusion.
  - Stores BM25 sparse vectors alongside dense vectors.
- Hybrid-capable collection provisioning is now the default behavior for `QdrantStore`.
- `Mythosia.VectorDb.Tools` support for Qdrant collection workflows.
  - `migrate qdrant` upgrades dense-only collections to hybrid schema.
  - `copy qdrant` copies a source collection as-is, including payload, vectors, and schema marker.
- Qdrant schema marker support for reliable schema detection and migration skipping.
- Default target naming for tooling.
  - Migration: `<source>_migrate`, `<source>_migrate2`, `<source>_migrate3`, ...
  - Copy: `<source>_copy`, `<source>_copy2`, `<source>_copy3`, ...

### Behavior Changes

- Collections are treated as hybrid-capable by default.
- `EnableHybridSearch` option has been removed. Collections are always hybrid-capable.
- Migration tooling prints explicit warnings, progress stages, and clear success / no-op output.

### Migrating from v1.0.0

If you are already on `v1.0.0`, your existing Qdrant collections are dense-only and do not contain the hybrid schema marker.
To move to `v2.0.0`, migrate each collection once.

Install the migration tool first:

```powershell
Install-Package Mythosia.VectorDb.Tools
```

If `docs` is the collection you want to migrate, run:

```bash
mythosia-vectordb migrate qdrant --endpoint localhost:6334 --source docs --replace
```

This migrates through a staging collection, then recreates `docs` with the new schema and copies the migrated data back into `docs`.

If your Qdrant deployment is remote or authenticated, add `--api-key your-api-key` and use your remote endpoint URL.

Stop all writes before migration or replacement for consistency-sensitive data.

### Notes for v1.0.0 Users

- Dense-only collections remain readable, but they will not use native hybrid search until migrated.
- Already-migrated hybrid collections are detected by the schema marker and will be skipped.
- `copy qdrant` does not change schema. It performs a raw collection copy.

---

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
