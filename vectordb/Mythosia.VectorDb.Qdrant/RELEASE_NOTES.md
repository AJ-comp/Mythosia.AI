# Release Notes — Mythosia.VectorDb.Qdrant

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
