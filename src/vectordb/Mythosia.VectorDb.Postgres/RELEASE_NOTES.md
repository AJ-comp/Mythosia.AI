# Mythosia.VectorDb.Postgres - Release Notes

## v10.7.0

### Breaking Changes — Schema

- **`namespace` and `scope` columns removed** — all values are now stored in the `metadata` JSONB column.
  - Primary key changed from `(namespace, id)` to `(id)`.
  - `namespace` and `scope` columns no longer exist in the table schema.
  - Namespace/scope filtering uses the standard metadata JSONB operators (`metadata @> '{"namespace":"docs"}'::jsonb`), leveraging the existing GIN index on `metadata`.
  - **Existing tables are migrated automatically** on first operation.

### Changed

- **Schema auto-provisioning** — `CreateSchemaAsync` now creates tables without `namespace`/`scope` columns and with `PRIMARY KEY (id)`.
- **Automatic legacy schema migration** — regardless of `EnsureSchema` setting, the store detects the legacy `namespace` column on first operation and automatically migrates the schema in a single transaction:
  1. Merges `namespace`/`scope` column values into `metadata` JSONB.
  2. Resolves duplicate IDs across namespaces by prefixing with `{namespace}:` (e.g., `chunk-1` in namespace `docs` → `docs:chunk-1`). Non-duplicate IDs are unchanged.
  3. Changes the primary key from `(namespace, id)` to `(id)`.
  4. Drops the `namespace` and `scope` columns.
  5. Drops the `idx_ns_scope` index.
  - On failure, the transaction rolls back and the schema remains unchanged.
- **SQL simplification** — all query methods (`SearchAsync`, `HybridSearchAsync`, `GetAsync`, `GetBatchAsync`, `CountAsync`, `DeleteAsync`, `DeleteByFilterAsync`, `ReplaceByFilterAsync`) no longer have special namespace/scope column handling. All conditions are processed uniformly via JSONB metadata filtering.
- **`BuildFilterWhere`** — removed `GetNonReservedConditions` (namespace/scope bypass). All `VectorFilter.Where(...)` conditions are now treated as standard metadata conditions.
- **`ReadRecord`** — reads namespace/scope from `metadata` JSONB directly (no column injection).
- **Hybrid search SQL** — RRF join key simplified from `(namespace, id)` to `(id)`.

### Compatibility

- Requires `Mythosia.VectorDb.Abstractions` v4.0.0.
- **Fully automatic migration** from v10.6.x — no manual steps required.

---

## v10.6.1

### Changed

- **Namespace filtering is now optional** — when `VectorFilter.Namespace` is null, the `WHERE namespace = @ns` clause is omitted entirely. Previously, a null namespace was silently replaced with `"default"`, forcing every query to filter on the `"default"` namespace even when namespace partitioning was not in use.
  - Affected methods: `SearchAsync`, `HybridSearchAsync`, `GetAsync`, `GetBatchAsync`, `DeleteAsync`, `DeleteByFilterAsync`, `ReplaceByFilterAsync`.
  - `BuildTextCandidatesCte` (used by `HybridSearchAsync`) also updated to accept a conditional namespace clause.
  - **Upsert unchanged** — `UpsertAsync` / `UpsertBatchAsync` still fall back to `"default"` when `record.Namespace` is null because the DB column is `NOT NULL` and part of the primary key.
  - `CountAsync` was already correct (no change needed).

### Deprecated

- **`VectorRecord.Namespace`**, **`VectorRecord.Scope`**, **`VectorFilter.Namespace`**, **`VectorFilter.Scope`**, **`VectorFilter.WithNamespace()`**, **`INamespaceContext`**, **`IScopeContext`**, and **`InNamespace()` / `InScope()`** are now marked `[Obsolete]`.
  - These will be removed in a future major version.
  - Use `Metadata` entries (e.g. `Metadata["namespace"]`) and `VectorFilter.Where("namespace", value)` for logical isolation instead.
  - This aligns with industry-standard vector database designs (Qdrant payload, Pinecone metadata, LangChain PGVector).

### Compatibility

- Backward compatible with v10.6.0. No schema changes. Existing records stored with namespace `"default"` remain accessible.
- Deprecated APIs still function but produce `CS0618` compiler warnings.

---

## v10.6.0

### Breaking Changes

`VectorFilter` construction API changed (see `Mythosia.VectorDb.Abstractions` v3.0.0). Any code that builds a `VectorFilter` to pass to `SearchAsync`, `HybridSearchAsync`, `GetAsync`, `GetBatchAsync`, `CountAsync`, `DeleteAsync`, `DeleteByFilterAsync`, or `ReplaceByFilterAsync` must be updated:

```csharp
// Before — compile error in v10.6.0
store.SearchAsync(vector, filter: VectorFilter.ByMetadata("k", "v"));
store.CountAsync(new VectorFilter { MetadataMatch = new Dictionary<string, string> { ["k"] = "v" } });

// After
store.SearchAsync(vector, filter: new VectorFilter().Where("k", "v"));
store.CountAsync(new VectorFilter().Where("k", "v"));
```

### Changed

- **SQL filter builder** — rewrote `BuildFilterWhere` / `AppendConditionGroup` / `AppendMetadataCondition` to support the `VectorFilter` fluent condition tree introduced in `Mythosia.VectorDb.Abstractions` v3.0.0.
  - **`Eq`** — `metadata @> @val::jsonb` (JSONB containment — preserves GIN index).
  - **`Ne`** — `metadata->>@key != @val`
  - **`Gt / Gte / Lt / Lte`** — `metadata->>@key > @val` (lexicographic string comparison).
  - **`In`** — `metadata->>@key = ANY(@vals)` (Npgsql array binding).
  - **`NotIn`** — `NOT (metadata->>@key = ANY(@vals))`.
  - **`Like`** — `metadata->>@key LIKE @val`.
  - **`Exists`** — `jsonb_exists(metadata, @key)`.
  - **`NotExists`** — `NOT jsonb_exists(metadata, @key)`.
  - **`And / Or` groups** — wrapped in `(...)` with `AND` / `OR` joins.
  - Key names are parameterized (`@mf_k{idx}`) for all non-Eq operators. Values are always parameterized. No SQL injection surface.
- **`CountAsync`** — updated to `WHERE 1=1` pattern, appending filter conditions via `BuildFilterWhere`.

---

## v10.5.0

### Added

- **`PostgresStore.GetBatchAsync`** — fetches multiple records in a single query using `WHERE id = ANY(@ids)` with Npgsql array binding. Applies full filter conditions (namespace, scope, metadata) via `BuildFilterWhere`.
- **`PostgresStore.CountAsync`** — `SELECT COUNT(*)` with optional `WHERE` clauses for namespace, scope, and metadata jsonb containment (`@>`). Returns the total record count when filter is null.

### Changed

- **`PostgresStore.GetAsync`** — now applies the full filter (scope, metadata) via `BuildFilterWhere` in addition to the existing `namespace = @ns AND id = @id` condition. Previously, only namespace was checked.
- **`PostgresStore.DeleteAsync`** — now applies the full filter (scope, metadata) via `BuildFilterWhere`. Previously, only namespace was used in the `WHERE` clause.
- **Dependency updates**: `Npgsql` → 10.0.2, `System.Text.Json` → 10.0.5.

### Compatibility

- Fully backward compatible with v10.4.0. The `GetAsync`/`DeleteAsync` behavior changes only affect callers that pass a scope or metadata filter; plain calls without a filter behave identically to before.

---

## v10.4.0

### Added

- **`ReplaceByFilterAsync` transactional override** — wraps DELETE + INSERT in a single PostgreSQL transaction, eliminating the query gap that occurs when re-embedding modified files.
  - Heavy work (document loading, embedding generation) happens outside the transaction.
  - Transaction scope covers only the DB I/O (DELETE by filter → INSERT new records), minimizing lock duration.
  - On failure, the transaction rolls back and existing vectors remain intact.

### Compatibility

- Fully backward compatible with v10.3.0. No breaking changes — overrides the default interface method from Abstractions v2.3.0.

---

## v10.3.0

### Added

- `VerifyConnectionAsync` — opens a real TCP connection to the PostgreSQL server and authenticates, throwing on failure.
  - Allows callers to verify connectivity before issuing queries or claiming "connected" in UI.
  - Implements the `IVectorStore.VerifyConnectionAsync` contract introduced in Abstractions v2.2.0.
- **`TextSearchMode`** — configurable text search strategy for hybrid search (`TsVector` | `Trigram`).
  - `TsVector` (default): PostgreSQL `tsvector / tsquery` full-text search. Works well for European languages.
  - `Trigram`: `pg_trgm` `word_similarity` matching. Better for CJK languages (Korean, Japanese, Chinese) and agglutinative languages where PostgreSQL lacks built-in morphological analysis.
- **`TextSearchConfig`** — configurable PostgreSQL text search configuration (default: `"simple"`). Only used in `TsVector` mode.
- **Trigram index auto-provisioning** — when `TextSearchMode = Trigram` and `EnsureSchema = true`, automatically creates `pg_trgm` extension and GIN trigram index (`gin_trgm_ops`) on the `content` column.
- Hybrid search `text_candidates` CTE is now generated by `BuildTextCandidatesCte`, supporting both `TsVector` and `Trigram` modes.

### Changed

- **TsVector mode: OR-based `to_tsquery`** — replaced `plainto_tsquery` (AND logic) with `to_tsquery` using OR (`|`) token joining.
  - `plainto_tsquery('simple', 'OPM 이벤트 코드')` → `'opm' & '이벤트' & '코드'` (AND — too restrictive, requires all terms to match)
  - New approach → `'opm' | '이벤트' | '코드'` (OR — standard BM25 behavior; documents matching more terms still rank higher via `ts_rank`)
- **Script boundary normalization (`NormalizeScriptBoundaries`)** — new internal helper that inserts spaces at script boundaries (Latin↔Hangul, Latin↔CJK, Hiragana↔Katakana, etc.) so PostgreSQL's `to_tsvector` tokenises mixed-script words correctly.
  - e.g. `"event테이블에"` → `"event 테이블에"`, `"データ테이블"` → `"データ 테이블"`
  - Applied during **upsert** (`content_tsv` is computed from the normalised text) and **schema migration** (`EnsureSchemaAsync` backfills existing rows with `regexp_replace`).

### Compatibility

- Fully backward compatible with v10.2.x. Default `TextSearchMode.TsVector` preserves existing behavior.
- Existing `content_tsv` data will be re-normalised when `EnsureSchemaAsync` runs on upgrade.

---

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
