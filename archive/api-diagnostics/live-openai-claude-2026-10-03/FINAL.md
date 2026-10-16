# OpenAI and Claude live API validation — 2026-10-03

> Historical report archived on 2026-10-16. Statements about local ignored files and omitted logs below describe the original run. The five sanitized TRX files, counters, and binary hashes are preserved here; see ../README.md for retained evidence and redactions. No tests were rerun while archiving.

The user postponed pushing and requested actual OpenAI and Claude API tests. The current working tree was built in Release with zero warnings/errors. Both provider lanes then ran the existing focused live fixtures and provider-selected common fixtures using that build, serially within each provider. Existing Key Vault credentials and synthetic test inputs were used; no source fixes, weakened assertions, commit, push, merge or publication occurred.

## Result

| Provider | Initial cases | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| OpenAI | 128 | 128 | 0 | 0 |
| Claude / Anthropic | 79 | 74 | 5 | 0 |
| Total | 207 | 202 | 5 | 0 |

The three Opus 5.5 refusal failures were each retried once without changes: **all three failed again**. Those additional executions are recorded separately and do not overwrite initial results. The two zero-quota Fast failures were not retried. This is not an all-pass release validation.

## OpenAI

- GPT-6.1 Sol: 29/29; GPT-6 Sol/Luna: 60/60.
- Astra asynchronous tools: 6/6; Astra Run: 5/5.
- GPT-5.6 / GPT Image 2 contract suite: 6/6; GPT Image 2.5: 4/4.
- Common processing speed, tool continuations, parallel tools, web/file search and cache-preserving reasoning: 18/18.
- Actual HTTP/SSE/WebSocket features include reasoning levels, Standard/Fast/Pro where selected, asynchronous tool result delivery, in-progress steering, typed output, image input and generation/editing, citations and hosted file retrieval.
- Five synthetic hosted-search stores were created. Five matching deletion records confirm cleanup of those stores and their source files; the creation/deletion identifiers match in the logs. Generated images remain only in the existing ignored test-artifact directory.

## Claude

- Sonnet 5.5: 21/21; Fable 5.1: 16/16; progress diagnostics: 3/3.
- Opus 5.5 dedicated suite: 11/12; request isolation across Claude models: 9/11.
- Common processing speed, tool continuations, parallel tools, web citations and cached reasoning: 14/16.

Five unsuccessful cases:

1. **Opus 5.5 edited-prefix DropBlock continuation:** the second response returned HTTP 200 with `stop_reason=refusal`, after one input transformation. Reproduced once unchanged.
2. **Opus 5.5 reused-message scenario:** the initial response returned HTTP 200 with `stop_reason=refusal`, before the message-reuse assertions could be exercised. Reproduced once unchanged.
3. **Opus 5.5 parent continuation after an isolated helper Run:** the initial parent request, helper Run and intervening isolation checks completed, but the subsequent parent response returned HTTP 200 with `stop_reason=refusal`. Reproduced once unchanged.
4. **Opus 5.5 Fast completion:** HTTP 429 explicitly reports an account allowance of 0 Fast-mode input tokens per minute.
5. **Opus 5.5 Fast Run:** the same HTTP 429 / zero Fast quota.

For the three refusals, the API outcome is confirmed; this does not determine whether request construction contributed or establish a provider-only root cause. No new implementation audit, raw-API differential experiment or fix was performed. The two Fast failures are confirmed account-limit restrictions, not successful Fast verification.

## Evidence and scope

- Root re-read all five TRX files: `counters.json` records exact counts, including the separate three-case retry.
- OpenAI: `openai/openai-live.trx`, `openai/openai-common-live.trx`; logs `openai/live.log`, `openai/common-live.log`.
- Claude: `anthropic/anthropic-current.trx`, `anthropic/anthropic-common.trx`, `anthropic/anthropic-refusal-retry.trx`; detailed phases and request IDs in `anthropic/FINAL.md`.
- Build output: `build.log`; exact binary hashes: `binaries.json`.
- All 323 preexisting modified/untracked source files match their pre-run hashes; staged changes also remain identical. See `scope-check.json`, `source-before.json`, and staged before/after diffs.
- Initial sandbox attempts could not connect to Key Vault; they were stopped before provider calls and retained separately as environment failures. Authorized network executions produced the reported live results.
- This run covers the selected current-feature and common live fixtures. It does not execute the entire inherited legacy-model/performance/oversized-context matrix, test other providers, or rerun package creation/isolated installation/CI.
- The two already documented native pause-turn and custom buffered-SSE limitations remain unfixed; passing other live scenarios does not resolve them.

All execution reports are under ignored `artifacts/` and are not added to Git.
