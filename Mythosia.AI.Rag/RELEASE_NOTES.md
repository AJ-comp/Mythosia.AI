# Mythosia.AI.Rag - Release Notes

## v2.0.0

### Breaking Changes

- Vector DB abstraction types (`IVectorStore`, `VectorRecord`, `VectorFilter`, `VectorSearchResult`) moved to `Mythosia.VectorDb` namespace.
- `InMemoryVectorStore` moved to `Mythosia.VectorDb.InMemory` package (namespace `Mythosia.VectorDb.InMemory`).
- Consumers must replace `using Mythosia.AI.VectorDB;` with `using Mythosia.VectorDb.InMemory;`.
- Consumers must add `using Mythosia.VectorDb;` for vector DB contract types.

### Changed

- Improved `MarkdownTextSplitter` behavior for large markdown tables:
  - Large table blocks are now split by row within chunk budget.
  - Table header/separator rows are preserved at the start of each split chunk.
  - Code fence blocks remain unsplit.
- `ProcessAsync` now returns the original query as-is when no references are found, instead of an empty context template that confuses the LLM.

---

## v1.2.0

### Changed

- Integrated `IDocumentParser`-based loaders for Office and PDF sources.
- Removed semantic splitter from `RagBuilder`/`RagPipeline`.

### Added

- `DocumentSourceBuilder` for per-extension routing with per-source loader/text splitter configuration.
- `MarkdownTextSplitter` — splits on markdown headers.
- `RecursiveTextSplitter` — recursive splitting with ordered separators.
- Convenience document helpers: `AddWord`, `AddExcel`, `AddPowerPoint`.
- Per-source routing: single-file sources prioritized over directory sources; deduplicated by normalized full path.

### Fixed

- `CharacterTextSplitter` overlap now aligns to separator boundaries.

---

## v1.1.0

### Added

- Convenience document helpers for Office files: AddWord, AddExcel, AddPowerPoint.
- DocumentSourceBuilder for per-extension routing with per-source loader/text splitter configuration.
- MarkdownTextSplitter (splits on markdown headers).
- RecursiveTextSplitter (recursive, ordered separators).
- Per-source routing updates: single-file sources take priority over directory sources and documents are deduplicated by normalized full path.

### Fixed

- CharacterTextSplitter overlap now aligns to separator boundaries to avoid awkward mid-paragraph splits.

### Compatibility

- Backward compatible with v1.0.0 (existing ITextSplitter usage unchanged).

### Documentation

- RAG README expanded with per-extension routing examples.

---

## v1.0.0

### Initial Release

- RagPipeline + RagBuilder orchestration for indexing and querying.
- DefaultContextBuilder for query context construction.
- CharacterTextSplitter and TokenTextSplitter.
- OpenAIEmbeddingProvider and LocalEmbeddingProvider.
- PlainTextDocumentLoader integration for RAG sources.
