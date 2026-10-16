# PIXIE repository documentation comparison

Hand-authored Korean developer-documentation sanity benchmark. Relevance labels identify documentation pages, not exhaustive human judgments. This is not a paper-quality held-out evaluation.

- Corpus: 22 Korean documentation pages, 364 chunks, 32 queries.
- Dense branch: OpenAI/text-embedding-3-small/1536. Identical cached vectors are reused by both methods.
- Same chunking and fusion settings; query rewriting and reranking are disabled for both.
- Baseline is this library's in-memory Lucene BM25, not PostgreSQL Trigram/ts_rank.
- Metrics use manually labeled pages; labels are not exhaustive. Do not treat this as production superiority or manuscript evidence.
- Query timings include sparse encoding/search but exclude precomputed dense embeddings and network time.

| Method | Recall@5 | MRR@10 | nDCG@10 | Median ms | P95 ms |
|---|---:|---:|---:|---:|---:|
| BM25 | 0.7188 | 0.6249 | 0.6662 | 16.57 | 38.86 |
| PIXIE | 0.9375 | 0.7057 | 0.7647 | 24.67 | 32.76 |
| Dense | 0.8906 | 0.7748 | 0.8167 | 1.25 | 6.40 |
| Dense+BM25 | 0.9219 | 0.7483 | 0.7922 | 17.01 | 49.23 |
| Dense+PIXIE | 0.9688 | 0.7865 | 0.8355 | 28.85 | 42.76 |

Model load: 1.22s. BM25 index: 2.72s. PIXIE index: 40.91s.

See results.json for every ranking, source/model hashes, runtime details and whole-process memory; corpus.json contains the exact corpus and shared vectors.
