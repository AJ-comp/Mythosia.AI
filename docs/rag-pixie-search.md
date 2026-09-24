# Compare local neural search with your existing retrieval

A question can describe a concept without repeating the words in its supporting document. Learned sparse retrieval assigns weights to vocabulary items, including related terms, so retrieval can use more than literal word overlap. `Mythosia.AI.Rag.Search.Pixie` adds a local PIXIE path that you can compare with your existing search while retaining the same dense embedding provider, chunking and RAG pipeline.

This is an optional **0.1.0-preview** package for **.NET 8 or later**. Existing search remains the default. The preview provides `PixieInMemoryStore`; it does not retrofit PIXIE into PostgreSQL, Qdrant or Pinecone, and it does not migrate existing indexes.

## What runs in your application

```text
Document chunk ── existing embedding provider ── dense vector ──┐
               └─ local PIXIE model ──────────── sparse vector ┤
                                                             │
Question ──────── same embedding provider ─────── dense search ┤
         └─────── local PIXIE model ───────────── sparse search┤
                                                             ↓
                                            weighted rank fusion
                                                             ↓
                                             existing RAG reranking
                                             and context assembly
```

Both document and query sparse vectors use the same model and tokenizer. Sparse retrieval scores matching vocabulary IDs by dot product; dense retrieval uses cosine similarity. Hybrid search combines their rankings with weighted Reciprocal Rank Fusion (RRF). Sparse vectors are search representations, not generated answers or a list of manually chosen metadata keywords.

PIXIE runs in-process through ONNX Runtime and does not need a Python server, API key or inference service. It does not download files at runtime. Your selected dense embedding provider, query rewriter, reranker or answer provider may still call an external service; adding this package does not make those components local.

## Add it to an existing RAG configuration

**Package versions:** this guide targets `Mythosia.AI.Rag.Search.Pixie` 0.1.0-preview with `Mythosia.AI.Rag` 8.1.0 and `Mythosia.VectorDb.Abstractions` 4.1.0. The optional preview includes the model assets. To validate a source checkout, follow [building from source](#building-the-model-bundled-package-from-source).

Install the RAG package and the optional search preview:

```bash
dotnet add package Mythosia.AI.Rag --version 8.1.0
dotnet add package Mythosia.AI.Rag.Search.Pixie --version 0.1.0-preview
```

In this example, `embeddings` is the same `IEmbeddingProvider` used by the current application. Retaining it makes comparisons about the search change rather than a simultaneous embedding-model change.

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
    .UseHybridSearch(new HybridSearchOptions
    {
        VectorWeight = 0.7f,
        CandidateMultiplier = 4,
        RrfK = 60
    }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

Keep the encoder alive until all operations using the store have completed. The caller owns its lifetime; `PixieInMemoryStore` does not dispose the supplied encoder. In a long-lived application, retain the encoder with the store and dispose it during shutdown after outstanding requests finish.

The same `.UseStore(searchStore)` configuration works with `service.WithRag(...)`. Agentic RAG uses the selected retrieval path too. The package performs retrieval; use the existing RAG/AI APIs when you also need a generated answer.

## Choose sparse, dense or hybrid retrieval

| RAG configuration | Query work with `PixieInMemoryStore` |
| --- | --- |
| `.UseKeywordSearch()` | Runs PIXIE sparse encoding and sparse search; skips the dense query embedding provider |
| `.UseVectorSearch()` | Uses the existing dense embedding provider and dense search |
| `.UseHybridSearch(options)` | Runs both active branches and combines their ranks with configured weighted RRF |

The existing name `UseKeywordSearch()` refers to the store's text-search capability. With PIXIE, that capability is neural sparse search. It is not inference-free: PIXIE still processes the query. RAG document ingestion still generates dense embeddings, even when queries use only sparse search.

`VectorWeight = 0` skips the dense query branch, and `VectorWeight = 1` skips the sparse query branch. The `CandidateMultiplier` expands candidates per active branch; the RAG reranker's candidate multiplier is a separate setting. Native dot-product scores, cosine scores and fused RRF scores are not directly interchangeable or probabilities. Use relevance judgments and rank metrics to compare systems, not the magnitude of their raw scores.

## Model files and limits

The package includes a pinned tokenizer, license, provenance manifest and a **derived dynamic 8-bit ONNX model**, copied to `models/pixie` in the application output. Its per-channel weights use UINT8. The derived model is approximately **190 MB before package compression**; the original official FP32 ONNX model is approximately 752 MB and is not bundled. Runtime memory consumption is greater than the weight file size and depends on inputs and execution settings. Quantization may change retrieval results; upstream benchmark numbers do not measure this package's derived model.

By default, the encoder reads `AppContext.BaseDirectory/models/pixie`. If deployment moves those files, supply their directory explicitly. This relocates the same pinned assets; it does not permit an arbitrary replacement model. The encoder verifies model and tokenizer SHA-256 hashes at construction:

```csharp
using var encoder = new PixieSparseEncoder(new PixieOptions
{
    ModelDirectory = modelDirectory,
    MaxSequenceLength = 512,
    IntraOpThreads = 0,
    MinimumWeight = 0
});
```

| Option | Behavior |
| --- | --- |
| `ModelDirectory` | Local directory containing the pinned model assets; no automatic network fallback |
| `MaxSequenceLength` | Defaults to 512 tokens including two boundary tokens; may be raised up to 5,632; longer input is rejected rather than silently truncated |
| `IntraOpThreads` | `0` uses ONNX Runtime's default thread setting; tune against application concurrency |
| `MinimumWeight` | Defaults to `0`; increasing it removes low-weight sparse entries and can change recall |

Split long documents into chunks that fit the tokenizer's limit. Character counts are not token counts; increasing the limit can raise memory use and latency substantially. Build a new index when changing the model, tokenizer or sparse encoding options. Do not combine vectors produced by different model configurations.

`PixieInMemoryStore` retains the dense and sparse index only in memory. It must be rebuilt after restart. It supports filtered retrieval and document replacement through the existing vector-store contracts; it is not a durable store or a production database migration tool. Writes rebuild a snapshot of the index, so this implementation is intended for comparison and small corpora rather than high-volume concurrent ingestion. Batch writes encode all incoming records before publishing the replacement snapshot.

The encoder copies its options at construction and serializes calls per encoder to bound inference memory. Its asynchronous methods accept `CancellationToken`: cancellation interrupts queue waits and requests termination of active local ONNX work. Termination is cooperative and does not roll back already completed store operations.

## Inspect the sparse representation

The low-level encoder can be used without the RAG pipeline. `EncodeQueryAsync` and `EncodeDocumentAsync` return the same immutable `PixieSparseVector` contract: sorted vocabulary `Indices` and corresponding positive `Values`.

```csharp
PixieSparseVector vector = await encoder.EncodeQueryAsync(
    "환불 정책", cancellationToken);

var strongest = Enumerable.Range(0, vector.Indices.Count)
    .OrderByDescending(i => vector.Values[i])
    .Take(10);

foreach (int i in strongest)
    Console.WriteLine($"{encoder.GetToken(vector.Indices[i])}: {vector.Values[i]}");
```

`GetToken` shows the vocabulary spelling, which may be a subword rather than a whole human-readable keyword. Blank input returns an empty vector. Special token IDs are excluded from sparse output. The displayed weights explain part of a match; they are not relevance probabilities or evidence that a statement is true.

The underlying [TelePIX model card](https://huggingface.co/telepix/PIXIE-Splade-v1.0) describes Korean/English training and aerospace-domain specialization. The model is Apache-2.0 licensed; package code uses the repository's MIT license, and the package preserves the model license and attribution. Language coverage and publisher benchmarks are reasons to evaluate a candidate, not proof of quality on your documents.

## Compare before choosing a default

Use identical documents, chunk boundaries, dense vectors, rewritten questions, candidate counts, filters and reranking settings for both branches. First compare retrieval without reranking so a later stage cannot hide missing candidates; then compare the full RAG path with the same reranker. Where the current backend is PostgreSQL trigram/full-text, compare against that backend too: an InMemory BM25 result is a different baseline.

Record Recall@K, nDCG@K and exact-identifier/exclusion failures against manually reviewed relevant documents. Include Korean paraphrases, English terms, `C#`/`C++`, product codes, negation and questions with no relevant document. Measure indexing time, query latency and memory separately. A small synthetic smoke set can expose a regression but cannot establish general quality or replace a manuscript's held-out case study.

The shared [retrieval evaluation infrastructure](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Rag.Evaluation/README.md) evaluates versioned datasets with independently registered search methods. It records per-query rankings, category metrics, latency, corpus snapshots and regression comparisons. `--dense local-hash` is a deterministic wiring check, not neural semantic retrieval; `--dense openai` uses actual dense embeddings and requires `OPENAI_API_KEY`. Both compared paths use the same cached vectors. The original PIXIE executable and script delegate to this infrastructure.

```powershell
./build/test-pixie-search.ps1 -Dense none
./build/test-pixie-search.ps1 -Dense local-hash
# Optional actual dense API comparison; OPENAI_API_KEY is required.
./build/test-pixie-search.ps1 -Dense openai

# Shared runner: choose methods, dataset and latency repetitions.
./build/test-retrieval-evaluation.ps1 -Methods bm25,pixie -Dense none -Repeat 3
# Fail when quality drops more than 0.02 absolute points under identical conditions.
./build/test-retrieval-evaluation.ps1 -Methods bm25,pixie -Dense none -Baseline artifacts/retrieval-evaluation/PRIOR-RUN/results.json -MaxRegression 0.02
```

Each new run writes `results.json`, `summary.md`, `measurements.csv` and `corpus.json` to a unique directory under `artifacts/retrieval-evaluation`; an earlier output is never overwritten. Supply `-Dataset` to the shared script, or `-Cases` to the compatibility script, for your own reviewed corpus. Dataset content, judgments, retrieval settings and shared vectors must match for a regression comparison. The original comparison artifacts described below remain historical records and are not converted into a new baseline automatically. OpenAI mode sends uncached document chunks and questions to the API and incurs usage charges. Regular CI runs deterministic tests and a small offline regression baseline; the `Retrieval Evaluation` workflow exposes explicit offline, PIXIE and OpenAI runs.

PIXIE does not enforce natural-language exclusions and does not guarantee that punctuation-bearing identifiers remain distinct. Apply explicit metadata filters for hard constraints. Promote the neural path only after your evaluation supports it, then plan reindexing and removal of obsolete implementations separately. Installing this preview does not remove BM25 or trigram search.

### Measured repository comparison — September 23, 2026

An actual OpenAI embedding comparison used **22 public Korean documentation pages, 364 identical chunks and 32 hand-authored questions**. Both hybrid methods reused the same `text-embedding-3-small` vectors (1,536 dimensions), with `VectorWeight = 0.5`, `CandidateMultiplier = 2` and `RrfK = 60`. Query rewriting and reranking were disabled. This is a small smoke comparison with incomplete, page-level relevance labels, not a held-out study or proof of production superiority.

| Method | Recall@5 | nDCG@10 | Median retrieval ms |
| --- | ---: | ---: | ---: |
| InMemory BM25 | 0.7188 | 0.6662 | 16.57 |
| PIXIE | 0.9375 | 0.7647 | 24.67 |
| OpenAI dense | 0.8906 | 0.8167 | 1.25 |
| OpenAI dense + BM25 | 0.9219 | 0.7922 | 17.01 |
| OpenAI dense + PIXIE | 0.9688 | 0.8355 | 28.85 |

The PIXIE hybrid retrieved more of the labeled pages on this set, while median retrieval time increased from **17.01 ms to 28.85 ms**. Indexing took **2.72 seconds for BM25 versus 40.91 seconds for PIXIE**, with a separate **1.22-second model load**. Retrieval timings include sparse inference but exclude precomputed dense embeddings and network time; shared document/query embedding preparation took 8.24 seconds. Measurements used Windows, .NET 10.0.9 and four ONNX intra-op threads. They compare this library's **InMemory Lucene BM25**, not PostgreSQL trigram/full-text search. PIXIE alone also ranked labeled pages less well than dense-only retrieval by nDCG on this set, so these results do not support discarding the dense branch.

The reproducible run records are `artifacts/pixie-comparison-openai/summary.md`, `results.json` and `corpus.json`, containing source/model hashes, per-question results and shared vectors. Re-run with your own judged documents before deciding on a default.

## Building the model-bundled package from source

Model weights are not committed to Git. Ordinary source builds and deterministic unit tests do not require the model download; real inference and package creation do. Release asset preparation is a build task, separate from the application's .NET-only runtime.

On Windows, prepare an isolated Python 3.12 environment with the pinned build dependencies, validate the model derivation, then build and check a consumer package:

```powershell
py -3.12 -m venv artifacts/pixie-model-env
./artifacts/pixie-model-env/Scripts/python.exe -m pip install -r build/pixie-model-requirements.txt
./artifacts/pixie-model-env/Scripts/python.exe build/prepare-pixie-model.py --validate
./build/test-pixie-model.ps1
./build/pack-pixie.ps1
```

The preparation script downloads only the pinned official revision and checks hashes before deriving the 8-bit artifact. Packaging refuses missing or mismatched weights, checks the distribution size budget, and by default tests a fresh package consumer. It creates local packages and does not publish them.

`test-pixie-model.ps1` runs the real local ONNX tests, supplies the model directory explicitly and requires at least five executed, passing tests; skipped tests do not count as verification. It writes a TRX report under `artifacts/test-results/pixie-local-model`. This is separate from deterministic unit tests and from retrieval-quality comparisons. The current validation passed **32 deterministic tests and 5 actual local-model tests**.

The locally validated NuGet package is **184,022,040 bytes (approximately 184 MB)**. Fresh consumers using both direct and transitive package references successfully loaded the bundled assets and ran inference, including after `dotnet publish`; the direct consumer also exercised search. The validation record is `artifacts/pixie-package/pixie-package-validation.json`. This verifies package consumption, not publication to NuGet.

`Mythosia.VectorDb.Abstractions` 4.1.0 supplies the text/hybrid contracts used by this preview; the earlier 4.0.1 package does not contain them. The coordinated release plan includes PIXIE 0.1.0-preview and its versioned dependencies. `pack-pixie.ps1` remains a local model-package validation tool; official release validation and publication use the GitHub publishing workflow and its release manifest.
