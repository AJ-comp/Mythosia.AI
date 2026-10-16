# Historical API validation and outstanding findings

These are selected **2026-10-03 records**, preserved on 2026-10-16 so a fresh
checkout has the evidence needed to continue the work. Archiving did not make
new provider requests, rerun these tests, or resolve the recorded findings.

| Evidence | What it establishes |
| --- | --- |
| [Live validation report](live-openai-claude-2026-10-03/FINAL.md) | OpenAI **128/128 passed**; Claude **74/79 passed, 5 failed**, with no skipped cases. Three refusal failures failed again in a separate retry. |
| [Counters](live-openai-claude-2026-10-03/counters.json) and five sanitized TRX files | Initial results and retry remain separate. Test identities, outcomes, timings, and failure diagnostics are retained. |
| [Tested binary hashes](live-openai-claude-2026-10-03/binaries.json) | Identifies the binaries used by that live run. It does not imply that a later commit was tested. |
| [Opus refusal diagnosis](opus-refusal-2026-10-03/FINAL.md) | Fifteen additional API calls compared library behavior, identical direct requests, and ordinary arithmetic controls. The observed refusals were reproduced outside the library. |
| [Diagnosis results](opus-refusal-2026-10-03/reports/results.json), [DropBlock control](opus-refusal-2026-10-03/reports/drop-control-results.json), and [redacted synthetic requests](opus-refusal-2026-10-03/redacted-requests.json) | Preserves outcomes, refusal categories, synthetic input structure, and server transformations. Result rows record selected observations, not one row for each of the fifteen HTTP calls. |
| [Outstanding-findings report](known-limitations-2026-10-03.md) | Records native pending-server-tool continuation and custom buffered-SSE cancellation defects as observed then, including triggers, locations, and reproduction outcomes. Their present status requires checking current code. |

The five TRX files are:

- [OpenAI focused](live-openai-claude-2026-10-03/openai/openai-live.trx): 110 passed.
- [OpenAI common](live-openai-claude-2026-10-03/openai/openai-common-live.trx): 18 passed.
- [Claude focused](live-openai-claude-2026-10-03/anthropic/anthropic-current.trx): 60 passed, 3 failed.
- [Claude common](live-openai-claude-2026-10-03/anthropic/anthropic-common.trx): 14 passed, 2 failed.
- [Claude refusal retry](live-openai-claude-2026-10-03/anthropic/anthropic-refusal-retry.trx): 3 failed; excluded from the initial 207-case total.

Two Claude failures were HTTP 429 with a Fast allowance of zero. Three were
HTTP 200 refusals. The later diagnosis supports fragile synthetic inputs
interacting with provider refusal classification; it **does not convert those
five failures into passes**, prove all original assertions ran, or certify the
implementation as defect-free.

## Public archive transformations

The [manifest](manifest.json) records each original repository-relative source,
its original SHA-256, the archived SHA-256, and applied transformations. Original
local files were not changed. Transformed copies are not byte-identical raw
evidence.

- TRX stdout/stderr and run-environment messages were removed entirely. Those
  streams can include generated output and account-specific diagnostics; test
  outcomes and error stacks remain.
- Host/user/deployment names and absolute binary paths were replaced. Checkout
  prefixes were removed from source stack traces. Organization and provider
  request IDs were removed from error messages.
- The two diagnosis result JSON files have provider request IDs removed. The
  captured requests already omitted private thinking text and signatures, and
  those omission markers remain. Model configuration named `thinking` is kept;
  it is not captured reasoning text.
- Binary hash paths are repository-relative. Reports have a notice explaining
  their historical context and evidence intentionally omitted from this archive.
- API keys, credential helpers, diagnostic executables, build outputs, raw
  console logs, generated images, and local environment dumps are not included.
  Probe source and per-case outputs cited by the outstanding-findings report
  were also not selected. Historical backtick paths in reports refer to the
  original run, not files promised by this archive.

## Continue the work

Use the maintained [OpenAI live fixtures](../../tests/Mythosia.AI.Test/Providers/OpenAI/OpenAIGpt61SolLiveTests.cs),
[Opus live fixtures](../../tests/Mythosia.AI.Test/Providers/Anthropic/AnthropicOpus55LiveTests.cs),
[Claude isolation fixtures](../../tests/Mythosia.AI.Test/Providers/Anthropic/AnthropicRequestIsolationLiveTests.cs),
and [processing-speed fixtures](../../tests/Mythosia.AI.Test/Providers/Common/InferenceSpeedLiveTests.cs)
as the executable sources. Credentials and API access must be configured in the
new environment before intentionally running live tests. New output belongs in
ignored `artifacts/`, not this dated snapshot or a build-output directory.
