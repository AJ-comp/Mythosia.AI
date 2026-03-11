# Mythosia.VectorDb.Postgres - Release Notes

## v10.2.0

### Added

- `PostgresStore` supports native hybrid search via `IVectorStore.HybridSearchAsync`.
  - `HybridSearchAsync` runs **parallel queries** — PostgreSQL full-text search and `pgvector` similarity search — then merges results via **Reciprocal Rank Fusion (RRF)** with `k=60`.
  - Uses `ts_rank` for keyword scoring and distance-strategy-aware similarity scoring.
  - Supports all existing filters: namespace, scope, metadata, and min-score.
- **Persisted `content_tsv` column** for full-text search — hybrid search now reads from the pre-computed `content_tsv` (`tsvector`) column instead of recalculating `to_tsvector(content)` on every query.
  - `content` remains **nullable** to support deployments that prohibit original text storage; `content_tsv` is required for lexical retrieval.
  - Recommended GIN index:
    ```sql
    CREATE INDEX idx_vectors_fts ON public.vectors USING gin (content_tsv);
    ```

### Breaking Changes — Schema

- New required column `content_tsv tsvector` added to the `vectors` table.
- Existing tables must be migrated before upgrading (see migration SQL below).

### Compatibility

- **Breaking schema change** from v10.1.0 — the `content_tsv` column must exist before using hybrid search.
- Existing `SearchAsync` behavior unchanged. `HybridSearchAsync` is only invoked when `UseHybridSearch()` is configured in the RAG pipeline.

---

## v10.1.0

### Breaking Changes — Namespace Now Optional

Aligned with `IVectorStore` v2.0.0: namespace moved from method parameter to `VectorRecord.Namespace` / `VectorFilter.Namespace` properties.

- All methods no longer take `string @namespace` as a parameter.
- Namespace is read from `record.Namespace` or `filter.Namespace` (defaults to `"default"` when null).
- `NamespaceExistsAsync` / `CreateNamespaceAsync` / `DeleteNamespaceAsync` removed — use `DeleteByFilterAsync(new VectorFilter { Namespace = "ns" })`.
- `GetAsync` / `DeleteAsync` now accept optional `VectorFilter? filter` for namespace/scope narrowing.
- **`PostgresVectorStore` → `PostgresStore`**: Class renamed for shorter DX.
- **`PostgresVectorStoreOptions` → `PostgresOptions`**: Options class renamed.

### Breaking Changes — Schema

- Primary key remains `(namespace, id)`.
- Column `collection` → `namespace`, column `namespace` → `scope` (from v10.0.0 terminology).

### Migration from v10.0.0

For existing PostgreSQL databases, run the following migration **before** upgrading:

```sql
-- 1. Rename columns (order matters: rename 'namespace' first to avoid conflict)
ALTER TABLE "public"."vectors" RENAME COLUMN namespace TO scope;
ALTER TABLE "public"."vectors" RENAME COLUMN collection TO namespace;

-- 2. Recreate composite index
DROP INDEX IF EXISTS idx_vectors_collection_ns;
CREATE INDEX idx_vectors_ns_scope ON "public"."vectors" (namespace, scope);

-- 3. Recreate primary key
ALTER TABLE "public"."vectors" DROP CONSTRAINT vectors_pkey;
ALTER TABLE "public"."vectors" ADD PRIMARY KEY (namespace, id);
```

### Fluent Builder API

```csharp
var store = new PostgresStore(options);
await store.InNamespace("docs").InScope("tenant-1").UpsertAsync(record);
var results = await store.InNamespace("docs").InScope("tenant-1").SearchAsync(queryVector);
```

## v10.0.0

### Initial Release

- `PostgresVectorStore` — pgvector-based implementation of `IVectorStore`.
- Similarity search with `DistanceStrategy` support: `Cosine`, `Euclidean`, `InnerProduct`.
- Single-table design with `collection` column for logical isolation.
- Upsert with `ON CONFLICT ... DO UPDATE` (single and batch via `NpgsqlBatch`).
- Metadata filtering via jsonb containment (`@>`).
- Namespace isolation filter.
- Minimum score threshold filter.
- `EnsureSchema` option for automatic table/extension/index provisioning.
- Schema/table name validation to prevent SQL injection.
- Vector index support via typed settings:
  - `HnswIndexOptions` (`M`, `EfConstruction`, `EfSearch`)
  - `IvfFlatIndexOptions` (`Lists`, `Probes`)
  - `NoIndexOptions`
- Per-request runtime tuning via algorithm-specific options:
  - `HnswSearchRuntimeOptions`
  - `IvfFlatSearchRuntimeOptions`
  - `SearchProfile` presets (`Fast`, `Balanced`, `HighRecall`)
- `FailFastOnIndexCreationFailure` option for index provisioning behavior.
- `gin(metadata)` and `(collection, namespace)` indexes.

### Fixed

- `SearchAsync`: Refactored `NpgsqlCommand`/`NpgsqlDataReader` to block-scoped `using` to ensure disposal before `tx.CommitAsync()`, preventing Npgsql "A command is already in progress" errors.
- `ApplySearchRuntimeSettingsAsync`: Each index branch now creates its own block-scoped `NpgsqlCommand`, preventing shared-command conflicts.
- `SET LOCAL` statements changed from parameterized queries to string interpolation — PostgreSQL `SET LOCAL` does not support `$1`-style parameters.
