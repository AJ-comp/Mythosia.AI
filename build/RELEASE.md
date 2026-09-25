# Validate a release before committing

A successful build does not prove that a package can be published. A target version may already exist on NuGet, a changed dependency may be missing from the publication list, or the package may differ from the source-project tests. Run the complete local check while the release edits are still uncommitted:

```powershell
pwsh -NoProfile -File build/test-release.ps1
```

This command does not commit, push, rewrite Git history, or publish packages. It stops at the first failure and keeps logs under a unique `artifacts/release-check-*` directory. Only a fully successful run writes `result.json` with `status: passed`.

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

The coverage check compares production source and project changes against `PreviousReleaseCommit`, including uncommitted and untracked changes. Advance that baseline only when preparing the next release, after verifying the preceding publication. README-only edits do not require an unrelated package release. The current baseline is `ce2af19`, confirmed in the preceding release's official NuGet repository metadata. Older unpublished changes discovered by comparing actual packages must still be reviewed explicitly.

`Packages` is the publication allowlist. `ConsumerOnlyPackages` retains exact versions of unchanged packages for the full compatibility checks; those packages resolve from NuGet and are never packed or published by this release. A dependency supplied by the current release belongs in `Dependencies`; an unchanged published dependency keeps its exact version in `FixedDependencies`.

For a quick readiness check without rebuilding:

```powershell
pwsh -NoProfile -File build/test-release-readiness.ps1
```

The default check queries NuGet. The offline structural check used in ordinary CI cannot prove that a version is still available for publication. Both readiness and the final publication check reject network failures and malformed or inconclusive NuGet version indexes. Their version-index requests have a 30-second timeout; only HTTP 404 establishes a package ID that does not yet exist.

## Commit and publish

After the local check succeeds, review and commit the release changes, then push. GitHub rebuilds the committed source and runs its checks. Any later source edit requires fresh validation.

The local package manifest is marked `development-validation` and is deliberately rejected by publication. Use **Publish NuGet Packages** on `main` to build fresh artifacts from the committed source. Run it with publication disabled first to verify release readiness, then enable publication when ready. Existing-version checks and package/source-commit provenance checks still apply at the actual push step.

Partial resume is only for packages already published from the **same release commit**. It is not a way to overwrite an old version or bypass a missing version bump. NuGet availability may change after a local check, so the publication workflow repeats the checks.

The current patch publishes only **Mythosia.AI.Rag 8.1.1** and **Mythosia.VectorDb.Postgres 10.8.1**. Their dependency versions stay unchanged. All existing isolated consumer probes still run, including the unchanged core, loaders, other stores, MCP, vLLM and PIXIE packages from NuGet. The preceding fifteen-package v8.1 release is historical and must not be republished. See the [patch release notes](../RELEASE_NOTES.md#v811).
