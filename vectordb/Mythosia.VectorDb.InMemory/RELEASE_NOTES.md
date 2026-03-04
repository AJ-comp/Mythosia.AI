# Mythosia.VectorDb.InMemory - Release Notes

## v1.0.0

### Initial Release

- `InMemoryVectorStore` — thread-safe in-memory implementation of `IVectorStore`.
- Cosine similarity TopK search with configurable result count.
- Thread-safe concurrent access via `ConcurrentDictionary`.
- Namespace isolation and metadata key-value filtering.
- Minimum score threshold support.
- Single and batch upsert/delete operations.
- Collection management (create, check, delete).
- Diagnostic helpers: `ListAllRecordsAsync`, `ScoredListAsync`, `GetTotalRecordCount`.
