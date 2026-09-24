# Mythosia.AI.Rag.Search.Pixie

Compare local neural sparse search with your existing RAG retrieval when questions and documents use different wording. PIXIE encodes both documents and queries, while `PixieInMemoryStore` combines its results with your current dense embeddings through the existing RAG builder.

**0.1.0-preview · .NET 8+ · local ONNX inference.** The optional package includes a pinned tokenizer and derived dynamic 8-bit PIXIE model. No Python server or API key is required for PIXIE. Existing embedding, reranking and answer providers keep their own execution requirements.

## Installation

Install the preview with `Mythosia.AI.Rag` 8.1.0. Its neural search contracts require `Mythosia.VectorDb.Abstractions` 4.1.0 or later. For local builds, follow the [model preparation and package validation instructions](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-pixie-search.md#building-the-model-bundled-package-from-source).

```bash
dotnet add package Mythosia.AI.Rag --version 8.1.0
dotnet add package Mythosia.AI.Rag.Search.Pixie --version 0.1.0-preview
```

## Connect an existing embedding provider

Here, `embeddings` is your existing `IEmbeddingProvider`.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);

RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

Use `.UseKeywordSearch()` for PIXIE sparse-only query retrieval, or `.UseVectorSearch()` for dense-only queries. RAG document ingestion still generates dense embeddings in either mode. Query sparse encoding is neural inference even when no dense query embedding is requested.

## Deployment and ownership

- The approximately 190 MB derived 8-bit model, tokenizer, manifest and license are copied to `models/pixie` in the application output. Runtime memory use is not the model file size. The original approximately 752 MB FP32 ONNX file is not bundled.
- `PixieOptions.ModelDirectory` overrides the default `AppContext.BaseDirectory/models/pixie`. No runtime downloads or Python installation are performed.
- `MaxSequenceLength` defaults to 512 tokens including two boundary tokens; up to 5,632 is permitted. Longer text is rejected. Split documents into suitable chunks instead of relying on silent truncation.
- The caller owns and disposes `PixieSparseEncoder` after all store operations finish. The store does not dispose it.
- Both indexes are memory-only and must be rebuilt after restart or model/configuration changes. Writes rebuild the index snapshot, so this preview is intended for comparison and small corpora. It does not add PIXIE to PostgreSQL, Qdrant or Pinecone and does not migrate existing indexes.

Existing search remains the default. Compare judged documents and rank metrics before selecting a replacement; raw BM25, sparse, cosine and fused scores are not directly comparable. Exact identifiers and natural-language exclusions require evaluation; enforce hard restrictions with metadata filters. Quantized-model quality is not established by the original model's published benchmark numbers.

For repeatable comparisons, use the [shared retrieval evaluation infrastructure](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Rag.Evaluation/README.md): add versioned datasets or search adapters, retain each run, reuse dense embeddings and compare quality against an earlier compatible baseline. The original PIXIE benchmark command remains supported.

See the [full guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/rag-pixie-search.md), [한국어 안내](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/rag-pixie-search.md), and [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/rag/Mythosia.AI.Rag.Search.Pixie/RELEASE_NOTES.md#v010-preview).

Package code uses MIT; the [TelePIX model](https://huggingface.co/telepix/PIXIE-Splade-v1.0) uses Apache-2.0. Model attribution, its license and the derivation manifest are included in the package.

The coordinated release includes PIXIE 0.1.0-preview and `Mythosia.VectorDb.Abstractions` 4.1.0. Package validation checks the bundled model, tokenizer and licenses before publication; ordinary runtime use never downloads model assets.
