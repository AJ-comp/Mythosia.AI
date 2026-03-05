# Mythosia.AI.Rag.Abstractions - Release Notes

## v2.0.0

### Breaking Changes

- `IVectorStore`, `VectorRecord`, `VectorFilter`, `VectorSearchResult` moved to `Mythosia.VectorDb.Abstractions` package (namespace `Mythosia.VectorDb`).
- Consumers must add `using Mythosia.VectorDb;` to resolve these types.
- Added project dependency on `Mythosia.VectorDb.Abstractions`.

### Added

- `RagProcessedQuery.HasReferences` computed property — returns `true` when the query matched at least one vector store reference.

---

## v1.0.0

### Initial Release

- `IRagPipeline`, `IContextBuilder`, `ITextSplitter`, `IEmbeddingProvider` interfaces.
- `RagPipelineOptions`, `RagProcessedQuery` shared models.
- `IVectorStore`, `VectorRecord`, `VectorFilter`, `VectorSearchResult` contracts.
