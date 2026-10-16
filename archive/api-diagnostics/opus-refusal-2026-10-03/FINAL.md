# Opus 5.5 refusal diagnosis — 2026-10-03

> Historical report archived on 2026-10-16. Executable and console logs mentioned below were not selected for this archive. The reports/ JSON and redacted-requests.json are included. The storage-correction commands describe the former local project, not an executable supplied here. No new provider calls were made.

User asked whether the five live failures came from implementation or tests. This bounded diagnosis investigates only the three HTTP-200 refusal cases. The two Fast failures remain independently confirmed account restrictions (0 Fast input tokens/minute).

## Findings

- Identical request JSON sent directly to the official Messages API, bypassing Mythosia response/execution code, reproduced refusals for all three scenarios.
- Every observed refusal reported `stop_details.category=reasoning_extraction`. The provider explanation identified its restrictions on reverse engineering or duplicating model outputs. These synthetic fixture inputs did not intentionally request private reasoning; the policy classification is a provider outcome, not evidence of an actual harmful request.
- In the reused-message scenario, the original library call succeeded on this additional observation, while the identical raw replay refused. Replacing only the final user marker request with ordinary arithmetic succeeded with unchanged history/settings. This rules out a deterministic parser failure as the observed cause, without certifying the unexecuted second-turn assertions.
- In the auxiliary-Run scenario, the original parent continuation refused through both library and direct HTTP. With identical history/settings and only the final question changed to arithmetic, direct HTTP returned the correct answer.
- In the DropBlock scenario, changing only the final marker question to arithmetic was insufficient: the artificial replacement of the prior user message remained. A final control used a natural change to the earlier calculation while retaining signed assistant history and the same DropBlock mechanism. Both the library and direct HTTP returned the correct new arithmetic answer, and both reported `thinking_dropped` with reason `prefix_binding_mismatch`.

The evidence supports fragile test inputs interacting with Opus's refusal classifier as the cause of these observed failures. No execution/serialization defect was confirmed in this diagnosis. It does not prove that the entire implementation is defect-free or that all original workflow assertions passed.

## Implication

First revise the synthetic test scenarios to use ordinary tasks while retaining their structural assertions: history ownership and prefix equality, auxiliary isolation, and real server thinking-drop transformations. Do not merely accept refusals, weaken assertions, or relabel previous failures as successes. The original 79-case Claude result (74 pass / 5 fail) and three failed retries are preserved unchanged.

The request-reuse test reuses the original Message unchanged and varies RequestMessageOverride and temporary instructions; it does not modify the original Message object's Content. This corrects an imprecise earlier user-facing description.

## Evidence

- `Program.cs` / `Probe.csproj`: ignored diagnostic executable, links existing probe/credential helpers; no production or maintained test edits.
- `run.log` and `reports/results.json`: original-scenario, identical-raw and arithmetic-control outcomes.
- `drop-control.log` and `reports/drop-control-results.json`: natural-prefix DropBlock control, including real input-transformations.
- `redacted-requests.json`: captured synthetic requests; signatures and thinking text omitted. No API keys recorded.
- 15 official Anthropic API calls in total: first diagnostic run 12 (6 library, 6 direct HTTP), final DropBlock control 3 (2 library, 1 direct HTTP).
- Official references: https://platform.claude.com/docs/en/build-with-claude/refusals-and-fallback and https://platform.claude.com/docs/en/build-with-claude/handling-stop-reasons . These describe refusal as HTTP 200 and stop_details as the provider's category/explanation.

No library/test/documentation source changes, commits, pushes or publication were performed. Existing failures are not converted to passes. Future scenario revisions must rerun the original structural assertions before they can be considered validated.

## Local report storage correction — 2026-10-16

The two result files were moved byte-for-byte from the build output to `reports/`. Their SHA-256 hashes and original paths are recorded in `reports/storage-migration.json`. The historical API results above were not rerun or changed.

`Program.cs` now resolves `reports/` beside `Probe.csproj`, regardless of the working directory or Debug/Release configuration. It fails before credential access if the diagnostic project cannot be located. `bin/` and `obj/` contain disposable build output; keep `reports/` when preserving this diagnosis.

To inspect the destination without accessing credentials or making API calls:

```powershell
dotnet run --project artifacts/opus-refusal-diagnosis-oct3/Probe.csproj --configuration Release -- --print-report-path
dotnet run --project artifacts/opus-refusal-diagnosis-oct3/Probe.csproj --configuration Release -- --drop-control --print-report-path
```
