# Project reference archive

This directory keeps the selected local reference material with the repository,
so a new cloud checkout has both the code and the evidence needed to continue
work. It is a curated snapshot collected on **2026-10-16**, not an automatic dump
of every test run or a statement that today's code passed those historical tests.

| Material | Start here | Preserved contents |
| --- | --- | --- |
| PIXIE comparison | [PIXIE evaluation](pixie-evaluation/README.md) | Two historical reports, indexed document text, questions, rankings, document vectors, and the formal run's query vectors |
| Provider verification and known issues | [API diagnostics](api-diagnostics/README.md) | OpenAI/Claude test outcomes, Opus differential diagnosis, and the accepted unresolved-issue report |
| Playground video production | [Video sources](playground-video/README.md) | Selected source recordings, captions/timelines, and reusable production scripts |

Each section has a `manifest.json` recording original repository-relative paths,
SHA-256 hashes, and any transformations. Original paths identify provenance; the
old ignored directories do **not** need to exist in a fresh checkout. Historical
metrics, failures, and limitations must remain distinguishable from new results.

## Continue in a cloud session

1. Clone `https://github.com/AJ-comp/Mythosia.AI.git`. After the archive PR is
   merged, `main` includes these files. Until then, check out
   `codex/archive-cloud-handoff` to include them.
2. Read [AGENTS.md](../AGENTS.md), [CLAUDE.md](../CLAUDE.md), and
   [CONTRIBUTING.md](../CONTRIBUTING.md). Start new work on its own task branch.
3. Install the exact .NET SDK in [global.json](../global.json) and restore with
   `dotnet restore Mythosia.AI.slnx`. The SDK currently pinned is **10.0.301**;
   the repository does not automatically select a newer installed SDK.
4. With Python 3.10 or later, run `python build/test-archive.py`. This verifies
   the preserved files without keys, model downloads, or provider API calls.
5. Prepare only the tools needed for the next task. The existing
   [CI workflow](../.github/workflows/ci.yml) records build/test setup. Retrieval
   evaluation needs the model setup described in its
   [guide](../tests/Mythosia.AI.Rag.Evaluation/README.md); video production has
   separate Node/Playwright/FFmpeg requirements in its section.

Live API tests still need network access and an Azure identity with permission
to read the existing Key Vault credentials, as described in the
[live test guide](../tests/Mythosia.AI.Test/README.md). Database tests need their
test server. Cloning a repository does not transfer credentials, installed tools,
running servers, or browser sessions.

## Keep the archive manageable

New tests and recordings write to ignored `artifacts/`, never `bin/`, `obj/`, or
this directory. Preserve a new result here only when explicitly selected for
long-term use, after checking its contents and updating its manifest. Run the
archive check before committing. `.gitattributes` preserves archive bytes across
Windows and Linux checkouts so the recorded hashes remain meaningful.

Build products, package caches, model weights, credentials, account/browser state,
intermediate recording failures, and unrelated drafts are not included. Final
published demo assets remain in [docs/assets](../docs/assets). The pre-existing
untracked root `results.json` is unrelated to this selected archive.

The archive changes no library APIs, package versions, or release metadata.
