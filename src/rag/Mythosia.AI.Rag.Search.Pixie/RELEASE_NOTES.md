# Mythosia.AI.Rag.Search.Pixie - Release Notes

## v0.1.0-preview

### Added

- Local PIXIE document and query sparse encoding through ONNX Runtime with a pinned tokenizer and a derived dynamic 8-bit model. Model inference does not require a Python server, API key or runtime download.
- `PixieOptions`, `PixieSparseEncoder` and `PixieSparseVector` expose local inference configuration and sparse representations. Overlong input is rejected instead of silently truncated; callers own encoder disposal.
- `PixieInMemoryStore` supports dense cosine retrieval, sparse dot-product text retrieval, metadata filtering and configurable weighted RRF through existing vector-store contracts. Connect it with RAG `.UseStore(...)` and retain the existing `.UseEmbedding(...)` provider.

### Compatibility

- New optional .NET 8+ preview package. Requires `Mythosia.VectorDb.Abstractions` 4.1.0; use `Mythosia.AI.Rag` 8.1.0 for the builder examples. Existing RAG defaults remain unchanged; this preview does not remove BM25 or trigram search.
- The index is memory-only and must be rebuilt after process restart or encoding-configuration changes. This package does not add PIXIE encoding to persistent PostgreSQL, Qdrant or Pinecone stores or migrate existing indexes.
- The bundled model is quantized; original publisher benchmarks are not validation of this package's derived model. Evaluate your own corpus before changing the retrieval default.
