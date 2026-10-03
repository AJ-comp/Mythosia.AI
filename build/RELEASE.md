# Validate a release before committing

A successful build does not prove that a package can be published. A target version may already exist on NuGet, a changed dependency may be missing from the publication list, or the package may differ from the source-project tests. Run the complete local check while the release edits are still uncommitted:

```powershell
pwsh -NoProfile -File build/test-release.ps1
```

This command does not commit, push, rewrite Git history, or publish packages. It stops at the first failure and keeps logs under a unique `artifacts/release-check-*` directory. Only a fully successful run writes `result.json` with `status: passed`.

Keep the 12 translated `docs/<locale>/README.md` files aligned with the root README in the same change. The documentation check compares heading and example order, API/command/dependency structure, guide links and the playable video URL. Comments and prompt strings may be translated. This catches structural omissions, but provider descriptions, requirements and translation meaning still need manual review.

The same structural, example and link checks apply to the 12 translated `serving.md` guides against the English guide. This supplements the per-language public-contract and navigation checks; translated prose and support tables still require semantic review.

The check performs:

1. Release-plan, project, documentation and dependency checks, including changed production packages omitted from the publication list and target versions already present on NuGet.
2. Solution restore, vulnerability checks, publication safeguards, deterministic tests, DocFX and documentation validation.
3. Real local PIXIE inference and the offline retrieval regression baseline.
4. Creation of every planned package with warnings treated as errors except the explicitly reviewed, visible Lucene prerelease-dependency warning.
5. Installation and execution of isolated NuGet consumers, followed by a second version-availability check.

The default run uses public package feeds and official NuGet metadata, but does not call paid LLM APIs. Live provider, account-specific and external-database validation remains separate. PostgreSQL tests require `MYTHOSIA_PG_CONN`; their skipped cases are visible in the test reports and do not become a claim of database validation.

The ordinary GitHub CI workflow provisions a disposable `pgvector/pgvector:pg17` service and supplies `MYTHOSIA_PG_CONN` to the VectorDb suite, including the HNSW candidate-budget and IVFFlat transaction-setting regressions. It checks the TRX report to reject skipped or inconclusive tests and missing runtime-setting regressions, then uploads the report as `vectordb-regressions`. For local release validation, supply your own disposable database through the same environment variable to execute those checks instead of skipping them.

## Prerequisites

Use the .NET SDK in `global.json`, PowerShell 7, Node.js 24 with npm, and DocFX 2.78.5. The release check installs the locked test-only UI dependencies from the public npm registry with lifecycle scripts disabled, then checks provider controls, cancellation, streamed output, safe Markdown rendering and console navigation. PIXIE requires the exact pinned model assets described in [model preparation](../docs/rag-pixie-search.md). Ordinary builds do not download them; the complete release check rejects missing assets or skipped real-model tests.

If the assets need preparation, supply a Python 3.12 executable from an isolated environment with `build/pixie-model-requirements.txt` installed:

```powershell
pwsh -NoProfile -File build/test-release.ps1 -PixiePython /path/to/pixie-venv/python
```

This runs the existing hash-verifying preparation script without regenerating tracked parity fixtures. Never replace the pinned hashes just to make a package pass.

## One explicit release plan

[release-plan.psd1](release-plan.psd1) defines the package versions, dependency order, framework, license and release-note URL. Packing, isolated consumers and release checks use that plan. A package outside the plan is never automatically published merely because its project version changed.

The coverage check compares production source and project changes against `PreviousReleaseCommit`, including uncommitted and untracked changes. Advance that baseline only when preparing the next release, after verifying the preceding publication. README-only edits do not require an unrelated package release. The current baseline is `0471906`, confirmed in the official NuGet package repository metadata for Serving.Abstractions, Serving.Ollama and Serving.LlamaCpp 1.0.0 and Serving.Vllm 1.1.0. Older unpublished changes discovered by comparing actual packages must still be reviewed explicitly.

Explicit relative `Compile Include` files count as that package's source, including linked files outside its directory and compiled inputs under otherwise excluded documentation paths. For example, changing a shared Serving transport requires every adapter that compiles it to be covered by the release plan. Paths are normalized inside the repository; unresolved MSBuild expressions or globs in explicit includes require review instead of being silently skipped.

`Packages` is the publication allowlist. `ConsumerOnlyPackages` retains exact versions of unchanged packages for the full compatibility checks; those packages resolve from NuGet and are never packed or published by this release. A dependency supplied by the current release belongs in `Dependencies`; an unchanged published dependency keeps its exact version in `FixedDependencies`.

For a quick readiness check without rebuilding:

```powershell
pwsh -NoProfile -File build/test-release-readiness.ps1
```

The default check queries NuGet. The offline structural check used in ordinary CI cannot prove that a version is still available for publication. Both readiness and the final publication check reject network failures and malformed or inconclusive NuGet version indexes. Their version-index requests have a 30-second timeout; only HTTP 404 establishes a package ID that does not yet exist.

## Commit and publish

After the local check succeeds, review and commit the release changes on a task branch, then push and open a PR against `main`. Follow [the contribution flow](../CONTRIBUTING.md) for Codex review, bounded fixes, current-revision CI, and the maintainer's merge decision. Any later source edit requires fresh validation.

After merge, wait for ordinary CI to succeed on the exact `main` commit selected for publication. The publication workflow verifies the latest `ci.yml` push run for that SHA before its validation and again immediately before publishing. A stale green run, failed or pending rerun, or passing PR merge-ref run cannot satisfy this prerequisite. GitHub API errors stop publication instead of assuming success.

The local package manifest is marked `development-validation` and is deliberately rejected by publication. Use **Publish NuGet Packages** on `main` to build fresh artifacts from the committed source. Run it with publication disabled first to verify release readiness, then enable publication when ready. Existing-version checks and package/source-commit provenance checks still apply at the actual push step.

Partial resume is only for packages already published from the **same release commit**. It is not a way to overwrite an old version or bypass a missing version bump. NuGet availability may change after a local check, so the publication workflow repeats the checks.

The current release adds GPT-6.1 Sol, Claude Sonnet 5.5 and the diagnostics migration, together with request isolation, signed-history, tool-schema, streaming cancellation and response-cleanup fixes. It publishes these packages in dependency order:

| Package | Version | Change |
| --- | --- | --- |
| Mythosia.AI.Abstractions | 4.2.0 | Adds `AIModels.OpenAI.Gpt6_1Sol`, `AIModels.Anthropic.ClaudeSonnet5_5` and `ClaudeThinkingMode` without changing existing identifiers. |
| Mythosia.AI | 8.2.0 | Adds GPT-6.1 Sol and Sonnet 5.5; fixes request/profile isolation, signed/tool history, schemas, streaming cancellation/timeouts, response cleanup and OpenAI Run lifecycle. See the remaining limitations below. |
| Mythosia.AI.Providers.Alibaba | 3.0.2 | Uses the owned request-message snapshot in Qwen completion and targets Core 8.2.0; public APIs and endpoint defaults are unchanged. |
| Mythosia.VectorDb.Abstractions | 4.2.0 | Adds optional `IVectorStoreDiagnostics`. |
| Mythosia.AI.Rag.Abstractions | 6.5.0 | Preserves obsolete `IRagDiagnosticsStore` and bridges existing implementations to the new contract. |
| Mythosia.VectorDb.InMemory | 5.0.0 | Implements the new contract and removes the RAG dependency; old diagnostic interface casts require migration. |
| Mythosia.AI.Rag | 9.0.0 | Detects the new capability and includes the breaking InMemory dependency upgrade. |

InMemory now depends on VectorDb.Abstractions and its existing Lucene packages, with no AI or RAG dependency. Isolated consumers verify that package graph and exercise listing, scoring and full RAG diagnostics. A separate fixture compiles an explicit legacy `IRagDiagnosticsStore` implementation against published RAG.Abstractions 6.4.0, then runs it against the new contracts without recompiling it. InMemory 5.0.0 itself no longer implements the old interface; users must migrate assignments/casts to `IVectorStoreDiagnostics` and upgrade RAG to retain full diagnostics.

Published Serving packages, loaders, other vector stores, MCP and PIXIE resolve from NuGet as consumer-only compatibility checks. Their existing probes still run, including controlled Serving HTTP responses and the published MCP package against the new Core/AI Abstractions. Alibaba 3.0.2 is packed from this release because its completion override changed; its dependency resolves to the planned Core 8.2.0 package. Earlier releases must not be republished.

Two Core limitations remain in this release and must stay visible in the README, guides and NuGet release notes: Sonnet 5.5/Opus 5.5 continuation after a pause ending in a pending server-tool call can fail prefill validation, and custom buffering HTTP content can hold successful SSE acquisition past cancellation or timeout. See [known limitations](../src/core/Mythosia.AI/RELEASE_NOTES.md#known-limitations). They are not covered by a claim that all maintained tests pass; current documentation does not mark either defect fixed. The latest lifecycle validation used deterministic tests and real local HTTP connections, not live provider APIs.

The serving package probes do not start a server or establish live-runtime compatibility. Live management checks against explicitly configured endpoints remain separate; missing runtime endpoints must be reported as unexecuted, not passed.

The package-consumer gate also runs `test-vector-diagnostics-compatibility.ps1`. It compiles implicit and explicit custom diagnostic implementations, a store combining public helpers with explicit legacy methods, and callers of the original methods against the published RAG Abstractions **6.4.0** package. A separate consumer then references that unchanged DLL and the exact contracts/RAG versions from the release plan. Only publication targets resolve from the local artifact feed; unchanged `ConsumerOnlyPackages` resolve from NuGet. A later RAG-only patch or an unrelated-package release therefore keeps this check without requiring the four diagnostics packages to be republished. Missing, duplicate or mismatched publication entries still fail validation. It checks interface dispatch, cancellation, RAG's preservation of the explicit legacy methods and InMemory's absence of RAG assembly references. The old binary is never recompiled against the new contract; a copied-DLL hash check verifies this. This check covers those legacy custom implementations, not old InMemory casts or arbitrary mixed RAG/InMemory versions.

`test-vector-diagnostics-release-plan.ps1`, included in publication safety checks, verifies full and partial release plans, rejected manifests and package-source selection. Its simulated published feed is local and makes no publication claim; the normal package-consumer gate continues to restore unchanged packages from NuGet.

## GPT-6.1 Sol live validation

```powershell
pwsh -NoProfile -File build/test-openai-gpt61-sol-live.ps1
```

This opt-in suite uses the existing `momedit-openai-secret` Key Vault credential and synthetic inputs; API calls incur charges. It covers every supported reasoning level, Standard/Fast and provider-default processing, Pro, function calls, asynchronous tools in Standard and Pro, native Run steering in Standard, cache-preserving reasoning changes, structured output, image input and hosted web/file search. Pro Run reports `CanSteer = false` and rejects an additional instruction locally while completing the original request. It requires all 29 cases to pass without skips; a Fast downgrade remains visible as a failed Fast execution check. Cache-update acceptance does not prove a cache hit. Reports stay under ignored `artifacts/test-results/openai-gpt61-sol-live`. Use `-NoBuild` only after building the current source.

## Claude Sonnet 5.5 live validation

```powershell
pwsh -NoProfile -File build/test-anthropic-sonnet55-live.ps1
```

This opt-in suite uses the existing Anthropic Key Vault credential and synthetic inputs; API calls incur charges. All 19 cases must execute and pass without skips. It covers adaptive effort levels, between-tools effort levels, the common None mapping, prompt-based typed structured output, signed multi-turn history, token counting, automatic tools and readable progress through completion/legacy streaming/Run, and cache-preserving effort changes with temporary turn instructions. It verifies the returned model identity and replays actual signed blocks; adaptive thinking is not required to emit a new block on every simple turn. These checks do not establish a cache hit or exercise image input, hosted web search, computer tools, native compaction or server fallback. Reports remain under ignored `artifacts/test-results/anthropic-sonnet55-live`. Use `-NoBuild` only after building the current source.

## Serving live validation

Use the [Serving live runner](../tests/Mythosia.AI.Serving.Live/README.md) against an existing local server or an SSH-forwarded remote endpoint. Its default mode inspects the server. Explicit `-Download`, `-Lifecycle` and `-ModelMetrics` options select additional operations; the last option is a separate llama.cpp Router check against an already loaded model. The runner neither rents nor deletes GPU servers.

```powershell
pwsh -NoProfile -File build/test-serving-live.ps1 -Runtime ollama -Endpoint http://localhost:11434 -Model qwen2.5:0.5b -Download -Lifecycle -TimeoutSeconds 900
pwsh -NoProfile -File build/test-serving-live.ps1 -Runtime llamacpp -Endpoint http://localhost:8080 -Model YOUR_LOADED_ROUTER_MODEL -ModelMetrics
pwsh -NoProfile -File build/test-serving-live.ps1 -Runtime vllm -Endpoint http://localhost:8000 -Model YOUR_SERVED_ALIAS
```

The documented live profiles cover Ollama 0.34.4, llama.cpp b11146 in Router and single-model modes, and vLLM 0.30.0 with small public Qwen models. They supplement the offline and isolated-package checks; they do not replace the complete release gate or establish every runtime version/model combination. The [shared guide](../docs/serving.md) separates management checks from additional native inference and cancellation probes. Keep failed attempts, unverified operations and setup failures visible in local reports; preserve logs under ignored `artifacts/` and clean up any temporary rented resources.

## Retrieval embedding live validation

Use the same extraction, indexing and query pipeline with synthetic TXT, Markdown and PDF documents:

```powershell
$env:MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE = '1'
pwsh -NoProfile -File build/test-retrieval-embedding-live.ps1 -Provider All
```

Select `-Provider Gemini` or `-Provider Voyage` to validate one provider. Calls incur API charges. The runner requires all selected cases and expected HTTP requests to succeed; missing credentials, skipped tests and inconclusive results are failures, not proof of live validation. Voyage also indexes one document containing 101 tiny chunks to verify context survives the default generic batch limit. These checks validate integration rather than retrieval-quality superiority on a benchmark.

For a Voyage account with a low request-rate limit, add `-VoyageRequestIntervalSeconds 22` to space request starts across test cases. This optional integer ranges from 0 to 120 seconds and defaults to 0 (no pacing). The runner temporarily sets `MYTHOSIA_VOYAGE_REQUEST_INTERVAL_SECONDS` for its test process and restores the previous value afterward. Pacing honors cancellation and applies only to the live-test handler; it adds no production retries and retains the eleven expected Voyage requests. It does not reset an existing account rate-limit window or manage token quotas.

- Voyage credentials: `VOYAGE_API_KEY`, then `MYTHOSIA_VOYAGE_API_KEY`, then the existing Key Vault secret named by optional `MYTHOSIA_VOYAGE_SECRET_NAME`. Create a key in the [Voyage dashboard](https://dashboard.voyageai.com/organization/api-keys); do not commit it or include it in commands saved to Git.
- Gemini credentials: `GEMINI_API_KEY`, then `GOOGLE_API_KEY`, then `MYTHOSIA_GEMINI_API_KEY`, then the existing `gemini-secret` Key Vault credential.

The explicit opt-in is checked before credentials are resolved. Explicit request log entries contain only model/dimension/operation/status metadata. TRX reports also include test diagnostics and stay under ignored `artifacts/test-results/retrieval-embedding-live`. `-NoBuild` reuses an existing isolated Release build; omit it after code changes. These probes do not log credentials or API payloads and use only synthetic documents.
