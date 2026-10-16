# Adversarial re-audit: Claude end-to-end request lifecycle

> Historical reproduction report from 2026-10-03, archived on 2026-10-16. The two findings were unresolved at that review; this archive does not establish their status in later code. Probe source and individual outputs named below are not included. Source line numbers and HEAD refer to the original review context.

Date: 2026-10-03. Branch: `codex/sonnet-5-5`.

## Result

Two reproducible P2 findings remain. Neither was introduced by the immediately preceding response-cleanup fix. Both relevant restrictions/waits also exist in HEAD. The most recent cleanup fix continues to pass its regression controls.

### P2: Valid pending server-tool continuation is rejected as assistant prefill

- Location: `src/core/Mythosia.AI/Services/Anthropic/AnthropicService.PreservedThinking.cs:360-371`, especially the generation-prefill check at line 361.
- Trigger: Sonnet 5.5 or Opus 5.5 with web search receives `stop_reason=pause_turn` and a final `server_tool_use` block whose server tool has not run yet.
- Expected: send the returned assistant content unchanged with the same tool definitions, allowing the server-side turn to continue.
- Actual: `NotSupportedException` saying assistant prefill is unsupported occurs before the second HTTP request. The validator exempts only final `*_tool_result` blocks, so it rejects this legitimate pending-call shape.
- Root independently confirmed six failures (two models × completion/text streaming/Run), with three successful older Opus 5 controls. All use the same ordinary injected HTTP response fixture. Streaming fixtures include proper text and input-JSON delta events.
- Primary contract: [Anthropic server tools — the server-side loop and pause_turn](https://platform.claude.com/docs/en/agents-and-tools/tool-use/server-tools#the-server-side-loop-and-pause_turn), verified 2026-10-03. It explicitly permits a paused response ending with an unexecuted server tool call and requires resending unchanged content with the same tools.
- Scope: existing prefill heuristic, including current Sonnet applicability; not a cleanup regression. Completed-server-result continuation controls still pass.
- Fix direction: distinguish validated native continuation from user-authored assistant prefill; preserve the pending call, original block order, and captured tool configuration without broadly disabling prefill validation.
- Evidence: `native-confirmation/Program.cs`, `native-confirmation/results.json`; independent reviewer `execution/FINAL.md`.

### P2: Successful SSE body acquisition can hold a canceled Run and its service guard

- Location: `src/core/Mythosia.AI/Services/AIService.SseHelper.cs:62`.
- Trigger: an injected delegating handler wraps real network content with a forwarding `HttpContent` whose normal `ReadAsStreamAsync` fallback materializes the body through `SerializeToStreamAsync`, while the peer stalls after headers and one body byte.
- Actual: on HTTP 200 SSE, caller cancellation and the one-second request policy timeout leave `Run.Result` pending and original content undisposed; the next Run is rejected as already active. Only allowing the peer to finish makes cancellation/timeout settle and frees the guard.
- Control: the same wrapper with HTTP 503 settles cancellation and disposes its owner before the peer finishes. Default `SocketsHttpHandler` without the wrapper passed all normal transport scenarios.
- Root independently reproduced caller cancellation and policy timeout over real local TCP. The wrapper forwards normal content and cooperates with disposal; it does not supply a fake stuck stream or a never-completing cleanup method. This is acquisition before cleanup begins, distinct from the documented possibility that custom cleanup itself takes time.
- Scope: existing conditional custom-handler/content defect, not a default transport failure or a cleanup regression. The direct unconditional acquisition await also exists in HEAD.
- Fix direction: make successful SSE acquisition honor the same cancellation/ownership boundary as HTTP error acquisition; observe late tasks and dispose acquired/late bodies without unsafe disposal callbacks or masking the primary outcome.
- Evidence: `transport/Acquisition.cs`, `transport/acquisition-run.log`, and independently built `acquisition-confirmation/Program.cs` / `root-run.log`.

## Verification performed

- Fresh Core/RAG Release builds: zero warnings and errors.
- Full maintained Core unit suite: 4,326 passed, zero failed/skipped.
- Full maintained RAG suite: 1,158 passed, zero failed/skipped.
- Combined maintained total: 5,484 passed.
- Fresh public external-consumer fragmentation/decoding/reuse matrix: 120/120 passed, 720 assertions.
- Fresh default-transport real local TCP matrix: 19/19 passed, including cancellation, policy/HTTP timeouts, truncation, gzip/charset, and reuse.
- Fresh signed-history/tool/native/structured-repair combinations: 14/14 passed across 46 synthetic exchanges.
- Fresh execution/wrapper/lifecycle matrix: 15 passed and one pending-server-tool finding. Six cleanup-time cancellation observations are recorded separately and not promoted to findings without a stronger contract basis.
- Native continuation root confirmation: six affected failures and three passing older-model controls.
- Acquisition root confirmation: two affected cases and one passing HTTP-error control.
- Documentation/metadata checks: six release packages and 569 Markdown files passed; 13 documentation languages covered.
- `git diff --check`: passed.
- Independent reviewers ran with requested `gpt-6-astra` / `ultra` configuration through the current login.

All candidate consumer outputs use Core SHA256:
`681C08ABE516D13D057C53917FFCE9D49274CCDAD1FDBCBE75430DC00AE8FE2A`.

## Scope and limits

This was a review, not a repair. All 320 preexisting maintained modified/untracked files remain byte-identical to the starting snapshot; the staged diff is also preserved. Probe code and reports are ignored artifacts. No commit, push, merge, release, live provider API call, or external DB call was performed. Primary provider documentation was read to verify the native-pause contract. Passing maintained tests does not negate the independently reproduced failures above.
