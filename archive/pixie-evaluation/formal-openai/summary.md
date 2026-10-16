# Retrieval evaluation

Dataset: repository-docs v1. Documents: 22; queries: 32.

Hand-authored Korean developer-documentation sanity benchmark. Relevance labels identify documentation pages, not exhaustive human judgments. This is not a paper-quality held-out evaluation.

Dense: OpenAI/text-embedding-3-small/1536. Quality uses the first measured iteration; latency uses 2 iteration(s).
Local-hash is a deterministic wiring check, not neural semantic retrieval. BM25 is InMemory Lucene BM25, not PostgreSQL search.
Search timings exclude dense embedding API/network time. No-answer rate measures returned results, not answer generation.

| Method | Recall@5 | MRR@10 | nDCG@10 | No-answer FP | Median ms | P95 ms |
|---|---:|---:|---:|---:|---:|---:|
| bm25 | 0.7188 | 0.6201 | 0.6634 | n/a | 8.9519 | 19.6267 |
| pixie | 0.9375 | 0.7057 | 0.7647 | n/a | 14.9466 | 17.3207 |
| dense | 0.8906 | 0.7748 | 0.8167 | n/a | 0.7600 | 3.3939 |
| hybrid-bm25 | 0.9219 | 0.7535 | 0.7963 | n/a | 9.3715 | 22.9610 |
| hybrid-pixie | 0.9688 | 0.7865 | 0.8355 | n/a | 17.2189 | 20.5314 |

## Categories

| Method | Category | Cases | Recall | nDCG | No-answer FP |
|---|---|---:|---:|---:|---:|
| bm25 | identifier | 13 | 0.7692 | 0.7083 | n/a |
| bm25 | paraphrase | 18 | 0.6667 | 0.6191 | n/a |
| bm25 | punctuation | 1 | 1.0000 | 0.8772 | n/a |
| pixie | identifier | 13 | 1.0000 | 0.9148 | n/a |
| pixie | paraphrase | 18 | 0.8889 | 0.6432 | n/a |
| pixie | punctuation | 1 | 1.0000 | 1.0000 | n/a |
| dense | identifier | 13 | 1.0000 | 0.9148 | n/a |
| dense | paraphrase | 18 | 0.8056 | 0.7357 | n/a |
| dense | punctuation | 1 | 1.0000 | 1.0000 | n/a |
| hybrid-bm25 | identifier | 13 | 1.0000 | 0.9048 | n/a |
| hybrid-bm25 | paraphrase | 18 | 0.8611 | 0.7067 | n/a |
| hybrid-bm25 | punctuation | 1 | 1.0000 | 1.0000 | n/a |
| hybrid-pixie | identifier | 13 | 1.0000 | 0.8710 | n/a |
| hybrid-pixie | paraphrase | 18 | 0.9444 | 0.8006 | n/a |
| hybrid-pixie | punctuation | 1 | 1.0000 | 1.0000 | n/a |

See results.json for all rankings/configuration/category metrics, measurements.csv for tabular analysis, and corpus.json for the exact corpus and shared vectors. These artifacts can contain the evaluated documents.
