# PIXIE evaluation evidence

These are two runs from **2026-09-22 UTC** on a small hand-authored Korean
developer-documentation benchmark: **22 pages, 364 indexed chunks, 32 questions**.
Both recorded Dense+BM25 Recall@5 **0.9219** and Dense+PIXIE **0.9688**, an increase
of **4.69 percentage points**. This is a sanity benchmark, not a held-out research
evaluation or proof of production superiority.

| Run | Summary | Full results | Indexed corpus |
| --- | --- | --- | --- |
| Original comparison | [summary.md](original-openai/summary.md) | [results.json](original-openai/results.json) | [corpus.json](original-openai/corpus.json) |
| Formal evaluation infrastructure | [summary.md](formal-openai/summary.md) | [results.json](formal-openai/results.json) · [measurements.csv](formal-openai/measurements.csv) | [corpus.json](formal-openai/corpus.json) |

The dense model was `text-embedding-3-small`, 1,536 dimensions. Each run shared its
dense vectors across compared methods. BM25 means **InMemory Lucene BM25**, not
PostgreSQL full-text/trigram search. Query rewriting and reranking were disabled.
Timings exclude dense embedding API/network time; the two runs have different
report schemas and their timings should not be presented as a controlled change.

The corpus files preserve the **actual indexed chunk text** and document vectors,
not a newly generated snapshot of today's documentation. The original corpus has
questions and relevance labels but **no query vectors**. The formal corpus also
preserves all 32 query vectors and its dataset definition. Full unchunked document
files are not separately stored in this archive. Historical source paths and
hashes, where recorded, identify the inputs; do not silently substitute today's
documents when reproducing a historical comparison.

The formal report records source revision
`f12955458f921c31cc36e10fadc99bb0d2ded803` and implementation assembly hashes. A
revision alone does not prove that every local input matched that Git tree.
Preserved corpus text and vectors are the authoritative indexed inputs.

## Reuse

Read [the current evaluation guide](../../tests/Mythosia.AI.Rag.Evaluation/README.md).
The reusable runner and datasets already live in the repository. Start with its
`local-hash` smoke mode to check wiring without a model or API key. Prepare PIXIE
model files separately when evaluating neural sparse search.

These are historical reports, not plug-and-play current cache directories. The
original schema is not accepted as a modern baseline. Even the formal report can
be incompatible with current chunking, fingerprint formatting, or corpus changes;
the evaluator deliberately rejects mismatched conditions. Preserve the archived
results and create a new run under `artifacts/retrieval-evaluation/` when changing
code or datasets. Using `-Dense openai` explicitly makes paid API calls for any
uncached inputs; simply opening or checking the archive makes none.

`manifest.json` records source and archive hashes. Only the formal report's six
machine-specific options paths were converted to repository-relative paths, with
JSON formatting normalized. Metrics, rankings and vector values were unchanged;
both corpus files and the other reports are byte-for-byte copies.
