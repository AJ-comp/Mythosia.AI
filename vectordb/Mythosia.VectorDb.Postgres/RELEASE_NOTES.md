# Mythosia.VectorDb.Postgres - Release Notes

## v1.0.0

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
