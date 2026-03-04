# Mythosia.VectorDb.Postgres - Release Notes

## v1.0.0

### Initial Release

- `PostgresVectorStore` — pgvector-based implementation of `IVectorStore`.
- Cosine similarity search via `<=>` operator with `score = 1 - distance`.
- Single-table design with `collection` column for logical isolation.
- Upsert with `ON CONFLICT ... DO UPDATE` (single and batch via `NpgsqlBatch`).
- Metadata filtering via jsonb containment (`@>`).
- Namespace isolation filter.
- Minimum score threshold filter.
- `EnsureSchema` option for automatic table/extension/index provisioning.
- Schema/table name validation to prevent SQL injection.
- `ivfflat` index with configurable lists count.
- `gin(metadata)` and `(collection, namespace)` indexes.
