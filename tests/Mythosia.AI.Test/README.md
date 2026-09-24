# Mythosia.AI tests

The `Unit` category contains deterministic tests for CI. The `Live` category sends requests to external providers and requires credentials. CI and package publishing continue to select `TestCategory=Unit`; live tests run explicitly.

See the [2026-09-24 full live API validation record (Korean)](validation/2026-09-24-live-api.md) for the 3,207-case scope, preserved initial failures, verified reruns, and account/resource limitations. Validation is complete for the runnable scope: 3,147 passed, 18 failed, 35 unsupported skips, 4 inconclusive cases, and 3 blocked resource cases. See the record for the explicitly labeled 39 console-backed results from the interrupted Pro run.

The [Claude-only follow-up](validation/2026-09-24-claude-errors.md) rechecks the three Claude failures: the unchanged context scenario passed six actual requests, while both Fast paths confirmed an account quota of zero. It adds request/refusal diagnostics and four context regression cases (70 related unit cases passed). The original full-run snapshot is retained; the initial refusal cause remains unknown.

## GPT-6 Sol and Luna validation

`OpenAIGpt6SolLunaContractTests` and the extended GPT-6 request, async-tool, Run, speed, capability and Chat UI tests cover both models and Astra regression boundaries. They check model-specific `None`, all supported efforts, immutable request settings, sampling after cache-preserving changes, Pro, Fast, and real transport payloads. `node --experimental-vm-modules build/test-model-capabilities-ui.mjs` exercises the actual settings module, including choosing None and refreshing sampling controls.

Run `./build/test-openai-gpt6-sol-luna-live.ps1` from the repository root for the strict 60-case paid suite. It uses the existing OpenAI Key Vault test credential and synthetic data. Cases cover all reasoning levels, ProviderDefault/Standard/Fast over completion and WebSocket Run, Pro with None/Medium, HTTP/SSE async tool continuations, None with tool results, steering during text and pending tools, cache-preserving transitions both into and out of None, typed structured output, synthetic image input, web search, and file search. Uploaded synthetic files and stores are cleaned up by the hosted-search fixture.

The runner requires every discovered case to pass with no skips; an unavailable account capability or a server downgrade does not count as a successful Fast check. Cache tests verify accepted configuration changes and retained history, not a guaranteed cache hit. Raw HTTP observations retain status/request IDs without recording credentials. Reports are stored in `artifacts/test-results/openai-gpt6-sol-luna-live/`.

On 2026-09-24 the final Sol/Luna matrix passed **60/60**, and the existing Astra/GPT-5.6 async-tool live regression passed **6/6**, with no skipped cases. Both new models confirmed actual Fast processing in completion and Run. Core Unit passed **2,724/2,724**; document validation covered 548 Markdown files in 13 languages, and DocFX finished with no warnings or errors. The initial async fixture exposed its obsolete service-default mutation and unconstrained repeated tool calls; its request-scoped control and single-call test setup were corrected before the final run. Evidence and the initial failure log are retained in `artifacts/gpt6-sol-luna/`.

## Processing speed validation

`InferenceSpeedContractTests`, `AuxiliaryRunProcessingTests`, `AnthropicGoogleSpeedTests`, `OpenAIXAISpeedTests` and `OpenAISpeedWebSocketTests` check immutable request branches, next-call options, early rejection, provider mappings, observed modes, tool continuations, server continuations, and internal-request isolation. `RagRequestFeaturesTests` also checks that speed reaches the answer while query rewriting remains separate.

Run `./build/test-inference-speed-live.ps1` from the repository root for the strict 24-case live matrix: four providers × ProviderDefault/Standard/Fast × completion/Run. The runner uses existing Key Vault credentials and makes paid requests containing synthetic prompts. Every case must pass without skipping; a quota error, missing explicit-mode confirmation, or downgrade fails Fast verification. Claude's ordinary non-beta ProviderDefault response can omit speed: the test requires that it stays unknown rather than inventing Standard.

Additional explicit suites:

```powershell
dotnet test --project tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj -c Release --filter "FullyQualifiedName~InferenceSpeedContinuationLiveTests&TestCategory=Live"
dotnet test --project tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj -c Release --filter "FullyQualifiedName~InferenceSpeedWireDiagnosticLiveTests&TestCategory=Live"
```

The 12-case continuation suite verifies tool results and processing observations across multiple requests, and GPT-5.6 HTTP Responses separately from Astra's WebSocket Run. Anthropic and Gemini tool tests use Standard; OpenAI and xAI use Fast. It also checks Sonnet 5 Standard: this model rejects the native speed parameter, so the adapter sends an ordinary request and keeps missing actual-mode metadata unknown. The Gemini spelling diagnostic compares `serviceTier` with the official REST example's `service_tier`, records response headers and usage, and **does not** certify Fast access.

The 2026-09-24 main matrix passed 20/24 cases with no skips. Anthropic Fast failed with HTTP 429 and an account limit of 0 Fast input tokens/minute; Gemini Fast returned Standard in both completion and Run. OpenAI and xAI confirmed Fast in both paths. Claude Standard's required beta header was corrected after the initial real-call failure and verified successfully. Reports are retained in `artifacts/test-results/inference-speed-live/` and `artifacts/speed/tests/`; the consolidated record is `artifacts/speed/RESULTS.ko.md`. Account limits were not changed, and downgrades were not relabeled as Fast successes.

Final continuation tests passed 12/12 and spelling diagnostics passed 2/2. The diagnostics confirmed that both Google request spellings returned Standard, rather than proving Priority access. The full core unit suite passed 2,651/2,651 (102 new speed cases), and RAG passed 910/910. No tests were skipped. The first continuation run exposed the Sonnet parameter restriction and an invalid forced-tool setting in the test fixture; both were corrected before the final run, and the earlier report was retained.

## Retrieval quality evaluation infrastructure

Use the [shared evaluation runner](../Mythosia.AI.Rag.Evaluation/README.md) to compare search methods on versioned document/query datasets, retain every run, inspect category metrics and gate regressions against a reviewed baseline. `Mythosia.AI.Rag.Evaluation.Tests` checks the evaluator itself; CI also executes the offline smoke dataset against its committed baseline. Real PIXIE and paid OpenAI comparisons run explicitly through `build/test-retrieval-evaluation.ps1` or the `Retrieval Evaluation` workflow. Existing PIXIE commands remain compatibility entry points.

## Retrieval architecture validation (2026-09-22)

The new `IRagRetriever` path is exercised through `RagStore`, ordinary RAG and Agentic RAG. Tests cover query-embedding avoidance in keyword mode, configured and application-level weighted fusion, inactive search legs, filter isolation, cancellation, legacy strategy compatibility, index updates and explicit failures for unsupported search modes.

The final Release checks passed with no failures or skips: **274/274 RAG tests**, **104/104 vector-store tests** and **2,468/2,468 AI Unit tests**. The vector suite includes **36 actual PostgreSQL checks**, run against a disposable localhost `pgvector/pgvector:pg17` database. These cover punctuation-safe text search, stemming, trigram search, rank scores and metadata filters, including empty `NotIn` sets with missing keys. An additional disposable **Qdrant 1.17** run passed **14 real backend checks**, including keyword/hybrid retrieval, tenant filters in new and legacy paths, missing-key semantics and quoted flat payload indexes. Qdrant/Pinecone adapter tests use recorded requests; no Pinecone cloud endpoint or model API was called.

Reports are under `artifacts/test-results/retrieval-modern-rag-final/`, `retrieval-modern-vector-final/`, `retrieval-modern-core-unit/` and `search-modernization/`; the newest RAG report contains the final 274-case run. Build and test logs use `artifacts/retrieval-modern-`. The separate Qdrant live probe is retained locally at `artifacts/qdrant-search-modernization/Program.cs`; its 14 successful checks were verified from process output and exit status, not a TRX report. Both disposable database containers and the PostgreSQL connection file were removed afterward. The full Release solution build passed with warnings treated as errors. This validates execution and contracts; it does not establish retrieval-quality gains on a production corpus or add symbol-preserving tokenization.

Documentation validation passed for six release packages, 545 Markdown files and 13 languages. DocFX regenerated the API manifest and built the reference/site with zero warnings and errors (`artifacts/retrieval-modern-docfx.log`). The test-category check and patch whitespace check also passed; published release history was preserved and no package versions were changed.

To rerun the RAG suite:

```powershell
dotnet test --project tests/Mythosia.AI.Rag.Tests/Mythosia.AI.Rag.Tests.csproj --configuration Release
```

Set `MYTHOSIA_PG_CONN` only in the test process to a disposable PostgreSQL database, then run `tests/Mythosia.VectorDb.Tests/Mythosia.VectorDb.Tests.csproj`. Without it, PostgreSQL integration cases are inconclusive and cannot be counted as a successful live validation. Do not commit connection strings. The tests create unique tables and clean them up; use a database reserved for testing.

## Run deterministic tests

From the repository root, using the .NET SDK pinned in `global.json`:

```powershell
dotnet test --project tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj --configuration Release --settings tests/Mythosia.AI.Test/serial.runsettings --filter "TestCategory=Unit"
```

## Run OpenAI asynchronous tool-calling live tests

The [dedicated runner](../../build/test-openai-async-live.ps1) selects only `OpenAIAsyncToolCallingLiveTests`, builds in Release, and uses the existing serial settings:

```powershell
./build/test-openai-async-live.ps1
```

To reuse an up-to-date Release build:

```powershell
./build/test-openai-async-live.ps1 -NoBuild
```

Each run writes a uniquely named TRX report under `artifacts/test-results/openai-async-live/` and prints its path. The runner requires at least six discovered tests, then checks the report: total, executed, and passed counts must match, with no skipped, unexecuted, or inconclusive cases. A missing report, too few tests, or any unsuccessful case fails the script even if the test process returns zero.

This suite makes billable OpenAI API calls. It requires network access to Azure Key Vault and OpenAI, an Azure identity accepted by the existing `Mythosia.Azure.SecretFetcher` authentication setup, and permission to read `momedit-openai-secret` from `https://mythosia-key-vault.vault.azure.net/`. That OpenAI account must have access to the tested models. [LiveTestSecrets](Infrastructure/LiveTestSecrets.cs) retrieves and caches the key in the test process. The runner does not add an environment-variable API key override.

The six cases cover both completion and streaming for each configuration:

| Model | Tool permission | Expected behavior |
| --- | --- | --- |
| GPT-6 Astra | `AllowAsync = true` | The server emits a native asynchronous call. The library sends another real request while its handler remains pending, then supplies the result using the original `call_id`. |
| GPT-6 Astra | `AllowAsync = false` | The request omits the native `async` tool field, and the library waits for the handler result before continuing. |
| GPT-5.6 Sol | `AllowAsync = true` | The unsupported model receives no native `async` tool field and uses the existing behavior of waiting for the result. |

The native cases require the actual server call to contain `async: true`; completing an ordinary synchronous call does not satisfy that contract. They check that the intermediate server response completes before releasing the deferred handler, that the result preserves its original call ID without duplicate execution or history entries, and that the final answer contains a value supplied only by the handler. The streaming cases also check function call/result correlation and the final completion event.

The fixture fixes its single-call policy in the actual requests: the first request forces the verification tool, and later requests use the public `FunctionCallMode.None` setting to send `tool_choice: "none"`. This prohibits additional model calls while keeping registered tools, the original pending handler, and result delivery active. The tests verify these request settings alongside the native async, pending continuation, call ID, and exactly-once execution assertions. Diagnostic output records only protocol decisions and identifiers, without credentials, prompts, tool arguments, or result content.

Authentication failures, unavailable models, unsupported API fields, and unmet native asynchronous behavior must fail the live run. Do not convert them to skipped or inconclusive tests and report the run as verified. The runner enforces that all six current cases, and any cases added later, pass without skipping. The presence of these tests alone is not a record of a successful live run.

## Run execution and steering live tests

```powershell
./build/test-openai-run-live.ps1
# With an up-to-date Release build:
./build/test-openai-run-live.ps1 -NoBuild
```

The [Run runner](../../build/test-openai-run-live.ps1) uses the same Key Vault credential source and serial settings. It requires all five cases to pass and writes a unique TRX report under `artifacts/test-results/openai-run-live/`. These tests make billable API calls and fail on authentication, model access, transport, or behavioral errors.

The cases verify GPT-6 Astra results without a stream consumer, steering during callback text output, steering while a synchronous or native asynchronous tool handler is still pending, and GPT-5.6 Sol's normal tool execution after an unsupported steering attempt. The tool cases require exactly one handler execution, the original call ID on the result, one final completion event, and output containing independently generated values delivered only through the tool result and the steering instruction. The native asynchronous case requires an actual `async: true` call from the server.

GPT-6 Astra runs use the Responses WebSocket endpoint. Existing HTTP `HttpMessageHandler` observers do not intercept that transport. The tests use the real socket and validate the shared Run result, callback output, events, and conversation history. See the [Run guide](../../docs/execution-api-transition.md) for lifecycle and cancellation behavior.

## Verify request features against real providers

Use these tests to check that a model actually accepts the common reasoning controls, performs hosted search, and returns usable citations. Payload-only tests cannot establish account access or prove that a retrieved answer came from the uploaded document.

```powershell
./build/test-request-features-live.ps1
# Select one provider while requiring every selected case to pass:
./build/test-request-features-live.ps1 -Provider OpenAI -NoBuild
```

The [dedicated runner](../../build/test-request-features-live.ps1) requires 16 cases in total: six for OpenAI, four for Anthropic, and six for Google. Each feature is exercised through both `GetCompletionAsync` and `StartRunAsync`. OpenAI uses GPT-6 Astra, Anthropic uses Sonnet 5 for web search and Opus 5 for cache-preserving effort changes, and Google uses Gemini 3.6 Flash. The existing Key Vault credentials must provide access to these models and hosted tools. Missing access is a failure, never a skipped success.

Web-search cases require actual URL citations. File-search cases upload only a small, newly generated synthetic document; a random clearance code exists solely in that document, so the answer must retrieve it and identify the source. [HostedSearchLiveFixture](Infrastructure/HostedSearchLiveFixture.cs) records the resources it creates and deletes them on completion or failure. It separately deletes OpenAI's source file and vector store; Google cleanup deletes the fixture store and its documents. Cleanup failures fail the test and report the affected resource IDs. No user documents are uploaded.

Reasoning cases change the level on a second turn; OpenAI and Anthropic additionally require the provider's cache-preserving transition to be accepted. This verifies the request contract, not a guaranteed billing cache hit. Google tests supported thinking levels without requesting cache preservation. Run cases also verify callback/result consistency and a single final completion event.

These are billable model, search, and indexing operations. `LiveTestSecrets` remains the credential source; tests do not print keys or add environment-variable overrides. Unique TRX reports are written under `artifacts/test-results/request-features-live/`. The runner checks discovery, execution, and pass counts, rejecting skipped, unexecuted, or inconclusive cases. Test code being present does not by itself mean the live checks have passed.

## Verify Claude Opus 5.5 reasoning and conversation contracts

Use this suite before adopting `claude-opus-5-5`: thinking is always on, its default effort is **Medium**, and signed thinking must survive tool calls without changing the conversation prefix. Successful HTTP responses alone do not prove that progress events or preserved thinking reach library callers.

```powershell
./build/test-anthropic-opus55-live.ps1
# With an up-to-date Release build:
./build/test-anthropic-opus55-live.ps1 -NoBuild
```

The [dedicated runner](../../build/test-anthropic-opus55-live.ps1) selects `AnthropicOpus55LiveTests` and the `Live` category, builds in Release unless `-NoBuild` is set, and uses the existing serial settings. It requires at least the **12 current cases** to be discovered, executed and passed, with zero skipped, unexecuted or inconclusive results. Every future case selected by the same filter must also pass. A unique TRX report is written under `artifacts/test-results/anthropic-opus55-live/`; a failed process, missing report or incomplete result fails the runner.

| Contract | Cases | Required evidence |
| --- | ---: | --- |
| Explicit effort | 5 | The real Messages API accepts Low, Medium, High, XHigh and Max, with adaptive thinking and no manual `budget_tokens`. |
| Default multi-turn response and token counting | 1 | Medium is used without explicit effort; correct answers and unchanged signed thinking continue across turns; the real token-count endpoint accepts completed history without changing it. |
| Automatic tools and readable Updates | 3 | Completion, Run and legacy streaming each execute three dependent tools once, calculate the correct allocation and replay actual signed thinking. Readable updates reach `LastThinkingContent`; streaming paths also emit reasoning events. |
| Edited user prefix | 2 | `Error` produces the actual binding HTTP 400; `DropBlock` succeeds and reports the real dropped-thinking transformation. |
| Cache-preserving effort and temporary instructions | 1 | High changes to Low through a per-message marker without rewriting the existing prefix or top-level configuration; a temporary instruction affects the first reply and clears on the next turn. |

The tool cases reuse the [allocation scenario](Infrastructure/AnthropicFableAllocationLiveScenario.cs): read stock, fetch rules using the returned snapshot ID, then calculate and validate whole-carton quantities with a rules ID supplied only by the preceding handler. The final report reference also exists only in a handler result. This task requires real calculation between tool results. A trivial lookup can succeed without any thinking block, so it cannot establish signed-thinking replay or readable Updates delivery. These checks require the actual blocks and public events; accepted request fields, synthetic responses or a plausible final answer are insufficient. The scenario only calculates an allocation and does not modify external inventory.

These are billable Anthropic API calls. The existing [LiveTestSecrets](Infrastructure/LiveTestSecrets.cs) retrieves `momedit-antropic-secret` from `https://mythosia-key-vault.vault.azure.net/` using the existing Azure authentication setup. The account must have Opus 5.5 access, and network access to Key Vault and Anthropic is required. No API-key environment-variable override is added. Authentication, model-access and behavioral failures are not converted to skipped or inconclusive successes.

The shared observer tees real SSE reads through the production parser. Logs contain protocol metadata such as model, request ID, status, block types, stop reason and transformation counts; credentials, prompts, thinking text, signatures and handler-only values are not printed. The suite uses synthetic conversations and creates no hosted resources requiring cleanup. Cache-preservation checks verify the request and conversation contract, not a guaranteed billing cache hit.

Deterministic `AnthropicOpus55Tests` additionally cover rejected reasoning settings, forced tools and prefills, builder isolation, legacy budget-to-effort mapping, and explicit rejection of public assistant-history edits that would otherwise be hidden by native content replay. Their presence, and the live runner's presence, do not establish a successful live run; retain the generated TRX report as execution evidence.

### Recorded Opus 5.5 validation on 2026-09-24

The final Release core Unit run passed **2,549/2,549**, including **71 Opus 5.5 cases**. The final live run passed **12/12**, with no failures or skips, in **1 minute 38.641 seconds**. Its report is `artifacts/opus55/tests/opus55-live-final.trx`; the accompanying log records 26 actual API requests: 25 HTTP 200 responses and one expected HTTP 400 for explicit prefix-binding rejection.

All three allocation execution paths produced two actual readable thinking blocks and populated `LastThinkingContent`; Run and legacy streaming also passed the public reasoning-event assertions. The earlier trivial-lookup run is retained as a failed diagnostic: its tools and HTTP requests succeeded, but three cases lacked actual thinking. Replacing that task with the dependent calculation scenario exercised the intended feature without weakening the assertions. Only the final 12-case report is the passing live evidence.

The dedicated runner was checked without additional API calls: PowerShell parsing succeeded, its counter gate accepted the final TRX report, and nine offline counter fixtures verified acceptance and rejection conditions. No extra live run was made just to validate the runner.

## Verify Claude Fable 5.1 thinking and instruction lifecycles

Use these tests before relying on preserved thinking across tool calls or applying an instruction to just one logical task. Accepted JSON fields alone do not prove that signed thinking can be continued, that progress reaches the caller, or that the server actually drops an incompatible block.

```powershell
./build/test-anthropic-fable-live.ps1
# With an up-to-date Release build:
./build/test-anthropic-fable-live.ps1 -NoBuild
```

The [dedicated runner](../../build/test-anthropic-fable-live.ps1) requires all 16 live cases to pass with no skipped, unexecuted, or inconclusive results. It writes a unique TRX report under `artifacts/test-results/anthropic-fable-live/`. The suite uses `claude-fable-5-1` through the real Anthropic Messages API and the existing `LiveTestSecrets` credential named `momedit-antropic-secret`. Network access, accepted Azure credentials, and model access are required; these are billable API calls. Missing credentials, model access, or protocol support fail validation.

| Contract | Execution paths | Required evidence |
| --- | --- | --- |
| Ordinary multi-turn responses | Completion, Run, legacy streaming | Correct answers, a real signed thinking block replayed in the next request, and unchanged previously sent message prefixes. |
| Automatic tools and Updates | Completion, Run, legacy streaming | Three dependent tools execute once each: inventory, allocation rules, and validation of a calculated plan. The final report reference exists only in a handler. Actual readable thinking reaches `LastThinkingContent`, and streaming paths also emit public reasoning events. |
| Cache-preserving effort | Completion, Run | Two successful turns change effort using a system message while preserving the existing prefix and top-level configuration. This does not assert a billing cache hit. |
| Turn and conversation instructions | Completion, Run | A synthetic persistent marker remains on the next turn, a temporary marker clears, and the original `clear_at` message remains unchanged in history. |
| Explicit prefix rejection | Completion, Run | Editing public user history with `Error` produces one real HTTP 400 binding error. The test does not corrupt transport data or retry after silently removing thinking. |
| Dropping an incompatible block | Completion, Run, legacy streaming | The same public edit with `DropBlock` succeeds, and actual server `input_transformations` with the dropped block's path and reason reach `LastInputTransformations`. |
| Counting completed conversation input | Token counting after Completion | The real `messages/count_tokens` endpoint accepts history ending with an assistant message, returns a positive count, and leaves public history unchanged. |

The tool fixture uses automatic selection throughout; Fable 5.1 does not support forced tool selection. It subtracts inventory reservations, applies kit composition and whole-carton rules, prioritizes two orders, and validates the calculated allocation without modifying external stock. Each downstream handler requires an independently generated value returned by the preceding handler, so a plausible text answer cannot satisfy the test. The [shared allocation scenario](Infrastructure/AnthropicFableAllocationLiveScenario.cs) is used by both the release suite and separate bounded diagnostics. Updates cases explicitly ask for useful findings at tool-result boundaries and require actual delivery, not just acceptance of `display: "updates"`. An API response with no readable progress therefore does not count as proof of that delivery contract.

Requests verify the relevant beta headers for binding controls, Updates, per-message effort, and turn-scoped system messages. The response observer tees the stream as the production parser reads it, preserving actual streaming behavior. Diagnostics expose only request sequence numbers, HTTP status, request IDs, selected protocol settings, response block types, stop reasons, transformation counts, and byte counts. Credentials, full prompts, thinking text, signatures, and handler secrets are not printed. The suite creates only synthetic conversations and no uploaded files or hosted resources requiring deletion.

Deterministic `AnthropicFable51Tests` additionally cover unsupported forced selection before transport/history mutation, Claude 5.0 compatibility, typed repair, RAG request overrides, instruction isolation during query rewriting and summarization, failed retrieval, Run option snapshots, explicit content edits, and preservation of wire history when only local metadata changes. Presence of this suite is not by itself a record of a successful live run; retain the generated TRX report for execution evidence.

### Recorded validation on 2026-09-08

The final Release build completed with zero warnings and errors. All 903 deterministic core tests passed with no skips, including 42 Fable 5.1 regression cases. The unit report is `artifacts/test-results/fable51-unit/fable51-unit-903.trx`.

The final strict live runner passed **16/16**, with no failures or skips, in 3 minutes 30 seconds. Its report is `artifacts/test-results/anthropic-fable-live/anthropic-fable-live-20260908-095147-339-51f4e0fb77b54f1b961826878388f841.trx`. It made 38 real requests, including the expected HTTP 400 responses for explicit prefix rejection. The allocation cases received actual readable thinking through all three execution paths, including the public `LastThinkingContent` property and streaming reasoning events.

Earlier results remain preserved so the final success does not hide the diagnostic history:

- The initial verification-code fixture passed 12/15; all three tool cases received a first-response `stop_reason=refusal`. The reason for those server refusals was not established. Report: `artifacts/test-results/anthropic-fable-live/anthropic-fable-live-20260908-093459-056-15b6efa18ca44d6c90754b5b869d5903.trx`.
- A two-step inventory lookup passed 13/16. All three automatic tool cases executed correctly, but no readable thinking blocks were emitted, so their strict Updates assertions failed. Report: `artifacts/test-results/anthropic-fable-live/anthropic-fable-live-20260908-094105-617-0ab3e4aa1eac476ba4561ecf3aa8ef15.trx`.
- The substantive allocation scenario passed a bounded Completion diagnostic (1/1), followed by Run and legacy-streaming diagnostics (2/2), before its shared implementation was included in the final 16-case runner. Reports: `artifacts/test-results/anthropic-fable-updates-diagnostic/fable51-updates-allocation-diagnostic.trx` and `artifacts/test-results/anthropic-fable-updates-diagnostic/fable51-updates-allocation-streaming.trx`.

These results establish actual Updates delivery for the tested allocation task. Readable progress remains optional server output; neither enabling Updates nor asking for progress guarantees a thinking event on every request. The tests retained their actual-thinking, handler execution, data-correlation, and prefix-preservation assertions throughout. All test processes finished, and these synthetic conversations created no external files or hosted stores requiring cleanup.

## Verify Gemini 3.7 and 3.8 Flash against the real API

Run this suite before deploying an upgrade that changes the model's thinking or sampling contract. A successful mock response cannot prove that the real model accepts those settings, preserves Google's signed function calls, or retrieves a document through a hosted tool.

```powershell
./build/test-google-flash-live.ps1
# With an up-to-date Release build:
./build/test-google-flash-live.ps1 -NoBuild
```

The [strict runner](../../build/test-google-flash-live.ps1) requires all 24 cases to execute and pass. It also checks evidence of exactly 30 successful real model requests, 15 per model, and cleanup of all four synthetic Google file-search stores. Authentication failures, unavailable models, quota errors, missing citations, skipped tests, and cleanup failures fail validation. Credentials come from the existing `LiveTestSecrets` Key Vault entry `gemini-secret`; model generation, Google Search, and file indexing are billable operations. Reports use unique names under `artifacts/test-results/google-flash-live/`.

| Contract | Coverage for each model | Required evidence |
| --- | --- | --- |
| Completion and thinking | Completion with Low, legacy callback with Medium, Run with High | Correct answer, documented thinking level, omitted sampling fields, actual successful model endpoint, and Run callback/event/result consistency. Requested thought summaries are compared with real provider output; the suite records observed reasoning counts without requiring discretionary summaries on every task. |
| Automatic function calling | Completion and Run | A random dispatch reference enters only through one tool handler. The provider-issued ID, full function-call part, and real thought signature are replayed unchanged; the response retains the same ID. |
| Typed JSON | `GetCompletionAsync<T>` and `BeginStream(...).As<T>()` | A correctly typed object on the first request, real native schema configuration, no repair calls, and matching streamed JSON. |
| Google Search | Run | An actual provider URL citation and a registered Google Search tool. |
| File Search | Completion and Run | A random value existing only in the fixture document, a citation identifying that document, and deletion of the owned store and documents after success or failure. |
| Image input | Run | Recognition of a red PNG generated entirely in memory and the exact inline image payload. No user images or external image URLs are used. |
| RAG internal requests | Query rewriting followed by completion | The rewrite uses supported Low thinking, the answer restores High, and the final response contains the value from locally embedded synthetic context. |

The [transport probe](Infrastructure/GeminiFlashLiveProbe.cs) tees actual response reads, so it does not pre-buffer streaming responses. It verifies HTTPS provider routing and header authentication without recording credentials in output. Logs contain model IDs, status codes, selected configuration fields, counts of native calls/signatures/thought parts, and response sizes. Full prompts, thought text, signatures, image bytes, and random fixture values stay in memory. Presence of the suite is not evidence of a successful live run; retain its generated TRX report.

### Recorded Gemini Flash validation on 2026-09-08

The strict runner passed **24/24**, with no failed, skipped, unexecuted, or inconclusive cases, in 2 minutes 16 seconds. Report: `artifacts/test-results/google-flash-live/google-flash-live-20260908-120506-417-5bdc6b7056d84e1998d02800045d4b54.trx`. All 30 real model requests returned HTTP 200: 15 for Gemini 3.7 Flash and 15 for Gemini 3.8 Flash. All four synthetic file-search stores were deleted after validation.

Both models accepted Low, Medium, and High thinking without legacy sampling fields. Completion and Run each completed a real tool round trip with the original provider-issued ID and thought signature preserved. Native JSON schema output succeeded through both typed completion and typed streaming on the first request, without repair. Web and file retrieval returned provider citations, inline image input was recognized, and the RAG rewrite used Low before the final response restored High.

Gemini 3.7 Flash emitted one actual thought-summary part in the High-thinking Run case, which reached the public reasoning stream. Gemini 3.8 Flash emitted no thought-summary parts in this suite, although its Run requests accepted `includeThoughts`. The tests verified every summary actually returned by the provider; this run does not establish actual summary delivery for 3.8 or guarantee a reasoning event for every request.

The same implementation passed all 987 deterministic core tests and 109 RAG regression tests. These results are separate from the earlier Fable validation recorded above.

## Verify Grok 4.7 against the real API

Use this suite before switching an application to Grok 4.7 or enabling its paid priority processing. It tests actual model and option acceptance through the library instead of inferring support from a model identifier.

```powershell
./build/test-xai-grok47-live.ps1
# With an up-to-date Release build:
./build/test-xai-grok47-live.ps1 -NoBuild
```

The [strict runner](../../build/test-xai-grok47-live.ps1) requires all **25 cases** and **29 real requests** to pass with `grok-4.7`. The existing `LiveTestSecrets` entry `xai-secret` supplies credentials. Calls incur model API charges, including priority processing. Reports are written to `artifacts/test-results/xai-grok47-live/`. Skipped cases, substituted models, missing applied-tier reports, and server downgrades in explicit Fast cases fail validation. The earlier Grok 4.6 suite and its recorded results remain separate.

The [live fixture](Providers/xAI/Grok47LiveTests.cs) covers all supported native/common reasoning levels and omitted Auto, callback/rich streaming and Run, real local-tool round trips with provider call IDs, RAG rewrite Low-to-High restoration, typed JSON completion/streaming without repair, synthetic PNG image input, and ProviderDefault/Standard/Fast processing across completion and Run. Optional reasoning summaries are checked only when the provider actually emits them. JSON tests verify the adapter's JSON mode, not strict native JSON-schema enforcement. Synthetic text, images and local RAG/tool values are used; credentials and content are not written to diagnostics.

This runner's existence is not evidence of a successful API run. Retain its generated TRX and request evidence to establish the result for the account being used.

### Grok 4.7 execution record — 2026-09-24 (Asia/Seoul)

The strict runner passed **25/25** with no failures or skipped cases. All **29 real requests** returned HTTP 200 from `grok-4.7`, with no model substitution or test retry. Evidence: `artifacts/test-results/xai-grok47-live/xai-grok47-live-20260923-232110-200-e687eb9371854b238f5e58fd41390263.trx` and `artifacts/grok47/LIVE-RESULTS.ko.md`.

The requests exercised two omitted Auto settings, 15 Low, six Medium, four High and two XHigh settings. Both tool round trips preserved actual provider-issued call IDs and returned handler-only values. JSON completed without repair, the synthetic image was recognized, and actual reasoning deltas reached public stream events. ProviderDefault and Standard reported `default`, while explicit Fast reported `priority`, in both completion and Run. This validates public API priority processing, not the separate Grok 4.7 Fast product variant. API access and processing results remain account- and time-dependent.

## Verify Grok 4.6 against the real API

Run these tests before relying on a new reasoning level or moving an application to Grok 4.6. They distinguish an accepted real request from a plausible mock response and verify that tools return data actually supplied by the application.

```powershell
./build/test-xai-grok-live.ps1
# With an up-to-date Release build:
./build/test-xai-grok-live.ps1 -NoBuild
```

The [strict runner](../../build/test-xai-grok-live.ps1) requires all 16 cases to pass and verifies 20 successful requests to `grok-4.6`. It rejects skipped, unexecuted, inconclusive, failed, or missing cases and checks recorded request counts for every supported effort. Authentication, model access, quota, and protocol failures fail validation. The existing `LiveTestSecrets` entry `xai-secret` supplies credentials. These calls incur model API charges; unique TRX reports are written under `artifacts/test-results/xai-grok-live/`.

| Contract | Execution paths | Required evidence |
| --- | --- | --- |
| Provider reasoning settings | Completion | Auto omits `reasoning_effort`; Low, Medium, High, and XHigh each receive a successful response with the requested wire value. |
| Common reasoning controls | Legacy callback, rich streaming, Run | `.WithReasoning(...)` accepts all five settings without mutating the provider default. Run callback, stream, and result agree; any actual `reasoning_content` deltas match public reasoning events. |
| Automatic client functions | Completion and Run | Exactly one handler supplies a random dispatch reference. The provider's actual call ID reaches history and the matching `tool_call_id` response, and the final answer contains the handler-only value. |
| RAG internal requests | Completion and Run | A real query rewrite uses Low with an explicit 1,024-token rewrite budget, then the final answer restores High and uses locally embedded synthetic context. |
| Typed JSON output | Typed completion and typed streaming | `json_object` mode returns the correct object on the first request, without repair; streamed JSON agrees with the typed result. This verifies the adapter's JSON mode rather than strict native JSON-schema enforcement. |

The [Grok transport probe](Infrastructure/Grok46LiveProbe.cs) tees real response reads without pre-buffering SSE. It checks the HTTPS Chat Completions endpoint, bearer authentication, model ID, supported effort, omitted unsupported penalties, and successful terminal reasons. It records actual reasoning-part counts instead of assuming that setting an effort guarantees readable reasoning. If xAI emits no reasoning text for a request, the result proves effort acceptance and correct answer delivery, not actual delivery of a reasoning summary.

Diagnostics contain only model IDs, HTTP status, selected configuration values, call/reasoning counts, and response sizes. Prompts, credentials, reasoning text, and random handler values remain in memory. The suite uses only synthetic conversations, in-memory tool data, and local synthetic RAG documents; it creates no external files or hosted stores. A test suite being present does not establish that its live execution passed; retain the generated TRX report as evidence.

### Execution record — 2026-09-08

The strict runner passed **16/16** cases, with none skipped or inconclusive, in 1 minute 12 seconds. Report: `artifacts/test-results/xai-grok-live/xai-grok-live-20260908-123706-913-3b9f778794974469b6d2aacab329f3cd.trx`.

All **20** real requests used `grok-4.6` and returned HTTP 200: two Auto requests omitted effort, six used Low, six Medium, four High, and two XHigh. Each request carried `max_tokens=4096` and `top_p=1`, except the two explicit RAG rewrite requests with a 1,024-token budget. Both client-function scenarios preserved the real provider call ID through the handler result and returned the independently generated handler value.

The Auto, High, and XHigh Run cases and Medium rich stream received readable reasoning summaries; the probe verified that every actual delta reached the public reasoning stream. The tool Run did so across both rounds. This records observed delivery for these requests, not a guarantee of summaries on every future response. Typed output succeeded in JSON mode without repair, and both RAG paths restored Low to High for the final answer.

The implementation also passed **1,026** deterministic core tests, **109** RAG regression tests, and **11** offline Chat UI JavaScript cases. Release builds completed with zero warnings or errors. The UI harness uses a mock DOM and transport, separately from the real API suite.

## Verify current DeepSeek models, Responses and uploaded images

Use the dedicated runner to verify V4 Pro reasoning, both current models through the Responses transport, native tool continuation, typed JSON output, and image-file reuse:

```powershell
./build/test-deepseek-current-live.ps1
# With an up-to-date Release build:
./build/test-deepseek-current-live.ps1 -NoBuild
```

The suite uses the existing `deepseek-secret` Key Vault credential and synthetic prompts/images. These are billable model calls. It requires 14 passing cases without skips: 19 successful model requests and four Files API operations. The model requests exercise completion, streaming and Run with `UseResponsesApi = true`, Pro Chat Completions at Low/High/Max effort, actual function calls and their returned values, later-turn reasoning replay, and typed schema responses. Flash receives the same uploaded image by file ID through both transports. The file lifecycle verifies upload, metadata, pagination-compatible listing and deletion; a `finally` block deletes only the test-created file, using a separate cleanup cancellation token. Upload expiration also bounds its lifetime if cleanup fails.

The runner writes a unique TRX under `artifacts/test-results/deepseek-current-live/`. Protocol diagnostics contain endpoint/model/options/status counts, without credentials or full prompts. Tests reject hidden retries, model substitution, truncated responses and mismatches between provider output and public results. Network access, account permissions, balance, API errors and cleanup errors fail verification. Presence of the suite is not a record that a live run passed.

### Recorded DeepSeek validation on 2026-09-24

The strict runner passed **14/14** cases with no skips, retries or model substitution. All **23 requests returned HTTP 200**: Flash made eight Responses requests and one Chat Completions request; V4 Pro made seven Responses requests and three Chat Completions requests. The four Files operations completed, including deletion of the synthetic image. Both models passed completion, streaming, Run, real function-result continuation and native JSON-schema output checks. Pro Low/High/Max reasoning settings reached the API. Flash correctly recognized the uploaded blue image through both transports.

Evidence: `artifacts/test-results/deepseek-current-live/deepseek-current-live-20260924-004614-216-05611154e8fa4af789280e88fab6b533.trx`. This validates the listed representative scenarios, not every possible image, tool schema or account configuration.

## Verify Google image model options

Run the [dedicated runner](../../build/test-google-image-model-options-live.ps1) from the repository root to check the selected model, resolution and aspect ratio through both `GenerateImagesAsync` and `EditImagesAsync`:

```powershell
./build/test-google-image-model-options-live.ps1
# With an up-to-date Release build:
./build/test-google-image-model-options-live.ps1 -NoBuild
```

The runner selects [GoogleImageModelOptionsLiveTests](Providers/Google/GoogleImageModelOptionsLiveTests.cs), uses the existing serial settings, and requires all six cases to pass without skipped, unexecuted or inconclusive results.

| Model | Resolution | Aspect ratio | Operations |
| --- | --- | --- | --- |
| `gemini-3.1-flash-image` | `FiveTwelve` (512) | `FourByOne` (4:1) | Generate + Edit |
| `gemini-3.1-flash-lite-image` | `OneK` (1K) | `SixteenByNine` (16:9) | Generate + Edit |
| `gemini-3-pro-image` | `TwoK` (2K) | `FourByThree` (4:3) | Generate + Edit |

Each case requires exactly one request to the selected model's `generateContent` endpoint, the exact requested resolution/ratio and JPEG selector, HTTP 200, and one output image. Editing also verifies the synthetic reference bytes sent in the request. A real JPEG decoder must decode the entire response; decoded dimensions must match the requested resolution tier and ratio within the fixture's rounding tolerances. Returned bytes must match the provider response, with no retries, fallback model or resizing. Unique TRX reports and generated JPEG evidence are retained under `artifacts/test-results/google-image-model-options-live/`.

These are six billable Google image operations using synthetic prompts and references. The existing `LiveTestSecrets` Key Vault entry `gemini-secret` supplies the credential; the account must have access to all three models, and network access to Key Vault and Google is required. Authentication, model access, quota, transport or output-validation failures fail the runner. This suite does not test Flash-Lite 512 support; see the [model options and source discrepancy](../../docs/providers.md#google-image-options). The runner and cases define the required checks; they do not by themselves establish a successful live run.

### Recorded Google image option validation on 2026-09-24

The strict runner completed successfully with **6/6 passed**, six HTTP 200 responses, and no skipped cases, retries or fallback requests. Generation and editing both returned fully decoded JPEGs at 1024×256 for Flash 512 / 4:1, 1376×768 for Flash-Lite 1K / 16:9, and 2400×1792 for Pro 2K / 4:3. The exact model and option selectors, reference inputs for editing, and unchanged response bytes passed validation. This confirms these six combinations; Flash-Lite 512 was not tested and its documentation discrepancy remains unresolved.

The TRX evidence is retained at `artifacts/test-results/google-image-model-options-live/google-image-model-options-live-20260924-001216-624-40170954871141ebba6f85222764c1bd.trx`; the six JPEGs are under `artifacts/test-results/google-image-model-options-live/images-20260924-001230-313-99e5da1052094270a13a2ba1fe0c41a0/`.

## Verify Grok Imagine image generation and editing

Use this suite to confirm that the common image interface returns real image bytes and preserves the references supplied for editing. Image endpoint acceptance alone cannot establish that a response decodes correctly, matches its MIME type, or respects the requested framing.

```powershell
./build/test-xai-images-live.ps1
# With an up-to-date Release build:
./build/test-xai-images-live.ps1 -NoBuild
```

The [strict runner](../../build/test-xai-images-live.ps1) requires eight passing cases and exactly eight successful requests returning nine images. Six requests exercise `grok-imagine-image-2.0`; two exercise `gpt-image-2` through the same `IImageGenerationService` generation and editing methods. The existing Key Vault entries `xai-secret` and `momedit-openai-secret` supply credentials. These are billable image operations, including edit inputs. Only one output uses the 2K preset; the other requests use automatic or 1K/1,024-square settings. Authentication, access, quota, decoding, missing-output, skipped, and inconclusive results fail validation.

| Contract | Live coverage |
| --- | --- |
| Independent image model | A request without a model override uses Imagine Image 2.0 while preserving the service's chat model and existing conversation. |
| Generation options | One default image, a two-image low-quality batch, and a medium-quality 2K widescreen image verify count, quality, resolution, and aspect-ratio serialization. The 2K case accepts the native output with `OutputFormat = ImageOutputFormat.Auto`; it still verifies the exact response bytes, actual MIME, complete decoding, and dimensions. |
| Single-reference editing | A generated-in-memory red-square PNG is sent through xAI's JSON `image` field; the prompt asks to preserve the square and add a blue circle. |
| Multiple-reference editing | Two and five ordered shape images use JSON `images`; the five-reference case mixes PNG, JPEG, and WebP. Byte hashes and MIME checks verify the inputs and their order, including the current five-reference limit. |
| Common provider interface | OpenAI generates and edits through the same public request/result types. Its adapter still uses multipart editing, while xAI uses JSON data URIs. |
| Actual returned bytes | Every result must match the corresponding real base64 response by SHA-256, fully decode into pixels with test-only SkiaSharp and a successful codec result, report the correct MIME type, and produce usable dimensions. Explicit ratios allow small image-grid rounding; resolution presets do not promise exact pixel dimensions. |

The [image probe](Infrastructure/GrokImageLiveProbe.cs) stores original returned bytes and synthetic input images in a unique `artifacts/test-results/xai-images-live/images-.../` directory. The runner reports this directory alongside a unique TRX file. Output files are ready for visual inspection of the requested composition and edits; protocol and decoding assertions alone do not certify visual instruction following. The suite does not download image URLs or upload user files, and it creates no separate hosted stores requiring cleanup.

Diagnostics include provider/model names, endpoint paths, HTTP status, selected configuration, input/output counts, decoded dimensions, MIME types, byte lengths, and artifact paths. Authentication, base64 payloads, and complete request bodies are never printed. SkiaSharp is a private test dependency and adds no production package dependency. The tested xAI request formats and settings follow its official [generation](https://docs.x.ai/developers/model-capabilities/images/generation), [editing](https://docs.x.ai/developers/model-capabilities/images/editing), and [multiple-reference editing](https://docs.x.ai/developers/model-capabilities/images/multi-image-editing) guides. Presence of these tests is not a record of a successful live run; retain the TRX report and inspect the returned images.

xAI does not expose an output-codec selector. The typed API accepts only `ImageOutputFormat.Auto`, rejecting explicit formats before HTTP. The probe accepts native output and chooses the saved extension from `GeneratedImage.MediaType`.

Typed `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)` also has a focused Google live regression. It requests a 1K resolution class and 16:9 ratio, completely decodes the JPEG, checks its dimensions, and saves the original under `artifacts/test-results/google-image-aspect-ratio-live/`:

```powershell
dotnet test --project tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj --configuration Release --no-build --filter "FullyQualifiedName~GeminiImage_MapsExplicitAspectRatio&TestCategory=Live" --minimum-expected-tests 1 --report-trx --results-directory artifacts/test-results/google-image-aspect-ratio-live
```

### Verified run: 2026-09-09 (Asia/Seoul)

The strict suite passed **8/8** cases with none skipped: **8 HTTP 200 requests and 9 decoded images**. The final report is `artifacts/test-results/xai-images-live/xai-images-live-20260908-150806-239-bc0beabcba374c22a8fda416de3d1b47.trx`; the original outputs and synthetic references are in `artifacts/test-results/xai-images-live/images-20260908-150817-665-030e9fdbe0e24dd7a48b55dbd4513bb4/`.

The separate Google aspect-ratio regression passed **1/1** and returned a 1376×768 JPEG. Its report is `artifacts/test-results/google-image-aspect-ratio-live/google-image-aspect-ratio.trx`. All ten final output images were visually inspected: generated shapes appeared in the requested order, single-image edits retained the red square and added the blue circle, and the five-reference edit retained all five colored shapes in order. These synthetic checks do not constitute a general model-quality benchmark.

The first image run passed 7/8 cases: its 2K request returned a non-JPEG image and correctly failed the explicit JPEG requirement. After selecting native output with `auto`, the complete final suite passed; its 2K/medium/16:9 case returned a **2816×1584 PNG**. This observation does not establish a fixed format or pixel size for future 2K responses.

Deterministic verification passed **1,092 core unit tests** (including 43 new xAI image cases and 23 Google/OpenAI aspect-ratio cases) and **109 RAG regression tests**, without skips. Reports are `artifacts/test-results/grok-image-unit/grok-image-unit.trx` and `grok-image-rag.trx`. The Release solution build completed with zero warnings or errors.

Documentation and NuGet metadata validation passed for four release packages, 491 Markdown files, and all 13 documentation languages. DocFX metadata and site builds completed with zero warnings or errors.

## Verify DeepSeek Flash reasoning, tools, and vision against the real API

Run this suite before moving an application to the current `deepseek-flash` alias. A model name resolving successfully does not establish that an application can continue a tool conversation, process an image, or keep its internal RAG rewrite independent of the final answer's thinking settings. The suite checks those behaviors through the library's public methods and observes the requests actually received by DeepSeek.

```powershell
./build/test-deepseek-flash-live.ps1
# With an up-to-date Release build:
./build/test-deepseek-flash-live.ps1 -NoBuild
```

The [strict runner](../../build/test-deepseek-flash-live.ps1) requires **14 passing cases and exactly 21 successful model requests**, with no skipped, unexecuted, inconclusive, or missing cases. The existing `LiveTestSecrets` entry `deepseek-secret` supplies credentials. Authentication, access, quota, truncated output, incorrect answers, and protocol errors fail validation. These requests incur model API charges. Each run creates a unique TRX report under `artifacts/test-results/deepseek-flash-live/`.

| Contract | Required real evidence |
| --- | --- |
| Persistent native reasoning | Completion accepts Auto, Low, High, and Max and returns the correct arithmetic answer. Auto omits effort while enabling thinking. The Max case sends a 16,384-token budget, verifying acceptance beyond the adapter's former 8,192-token limit without requiring a long answer. |
| Existing completion and streaming | Default completion and the legacy text callback explicitly disable thinking, echo a freshly generated identifier exactly, and deliver text identical to the real provider response. |
| Common reasoning and Run | `.WithReasoning(XHigh)` sends High for this provider without changing persistent defaults. Run callback, text events, and final result agree; actual reasoning deltas match the separate public reasoning stream. |
| Dependent application tools | Completion and Run each perform two dependent tool rounds. The second handler requires a random value available only from the first handler; the final provider answer must exactly match the second handler's reference. Completion returns that final answer. Run preserves all actual provider text, including intermediate progress. Actual provider call IDs survive replay and match every tool result. |
| Later user input after tools | A second user message reuses the prior result without another tool execution. The next request preserves all reasoning actually emitted by preceding assistant turns, including the final answer when it contains reasoning, and receives a successful response. |
| Native vision | The adapter sends generated-in-memory PNG and JPEG inputs through the ordinary message/Run APIs. Actual data URI bytes and MIME types are checked, and the model must identify each image's dominant color. |
| Typed output | Typed completion and typed streaming return the requested object in `json_object` mode on the first request. This validates JSON mode and parsing; it does not assert native strict JSON-schema enforcement. |
| RAG internal request isolation | A real rewrite disables thinking with a 512-token budget. The final Run restores High and returns a random reference found only in a local synthetic document. |

The [transport probe](Infrastructure/DeepSeekFlashLiveProbe.cs) tees response reads without buffering the entire SSE response first. Every request must use HTTPS, bearer authentication, the DeepSeek Chat Completions endpoint, and `deepseek-flash`; only successful terminal responses qualify. Nineteen requests carry a 4,096-token output budget, one Max request carries 16,384, and the rewrite carries 512. The runner checks the actual thinking/effort counts, four provider-issued tool IDs, and two image inputs.

Sampling and reasoning replay checks follow DeepSeek's [thinking guide](https://api-docs.deepseek.com/guides/thinking_mode/). The probe records whether the provider actually returned reasoning and verifies delivery when requested. Each dependent-tool scenario requires at least one tool turn with nonempty provider reasoning and compares every earlier assistant turn with what the provider actually emitted. Absent or empty reasoning is permitted only when that response contained none. Setting an effort alone does not constitute evidence that readable reasoning was emitted on every request; the deterministic suite separately forces nonempty final-answer reasoning to check its replay.

Diagnostics contain model IDs, HTTP status, selected configuration, image/call/reasoning counts, and response sizes. Credentials, image base64, full prompts, reasoning text, and random handler values remain in memory. Images and RAG documents are synthetic; the suite creates no hosted stores or external files requiring cleanup. SkiaSharp generates the two test images and remains a private test dependency. Presence of this suite is not evidence of a successful live execution; retain the TRX report and execution record.

The separate deterministic [DeepSeek contract tests](Common/DeepSeekFlashContractTests.cs) cover all common reasoning mappings, per-request and native-setting snapshots, sequential/parallel client tool batches, malformed tool identities and arguments, ordinary fallback for `AllowAsync`, reasoning/answer separation, final usage with empty choices, tool images and unsupported roles, cancellation during HTTP error-body reads, and context overrides across repeated tool rounds. The [existing regression suite](Common/DeepSeekCurrentContractTests.cs) also verifies helper compatibility, internal request profiles, RAG, structured repair, and unsupported hosted search. These offline tests exercise failures without consuming API quota.

### Initial validation — 2026-09-11 (Asia/Seoul)

The first strict execution passed **10/14** cases. All **21** model requests returned HTTP 200, with no skipped or inconclusive tests. Report: `artifacts/test-results/deepseek-flash-live/deepseek-flash-live-20260910-180807-414-e6646b6673ac4c24ab3afd1950fd269c.trx`.

Both non-thinking arithmetic cases returned `209` for a problem whose correct answer is `208`. This was a model-answer error; the adapter delivered the returned text. Their transport contract now uses an exact random-identifier echo and also compares library output with the captured provider text. The arithmetic checks remain in the thinking cases. This change does not establish non-thinking arithmetic accuracy or discard the initial observation.

Both tool cases completed their real requests but failed a test assumption that every assistant round would contain nonempty reasoning. DeepSeek omitted reasoning in some intermediate or final rounds. The corrected contract still requires actual nonempty reasoning in at least one tool turn and exact replay of every reasoning value the provider emitted. Empty or absent provider reasoning is handled explicitly; it is not a skip or a fabricated reasoning result. Native effort, vision, typed JSON, and RAG cases passed in this initial run.

### Second validation — 2026-09-11 (Asia/Seoul)

The second execution passed **13/14** cases, with no skipped or inconclusive tests. Its **20** real requests returned HTTP 200; the failing assertion stopped the Run case before its later user request, so this run did not meet the required 21-request total. Report: `artifacts/test-results/deepseek-flash-live/deepseek-flash-live-20260910-181110-932-43347e26599649cba2a65b568eb4c123.trx`.

The remaining failure was another test assumption: the tool Run emitted valid progress text before its final dispatch reference, while the assertion expected the entire result to contain only that reference. The existing [Run contract](../../docs/execution-api-transition.md#display-text-with-a-callback) specifies that `Result` concatenates all text events, including intermediate text between tool calls. No SDK behavior changed. The test now requires the final provider response to equal the reference and separately compares the complete Run result with the concatenation of every actual provider text part. The later user response must still equal the exact reference, and all tool IDs, reasoning replay, request counts, and callback/event consistency checks remain required.

### Final validation — 2026-09-11 (Asia/Seoul)

The final strict runner passed **14/14**, with no failed, skipped, unexecuted, or inconclusive cases. All **21** real `deepseek-flash` requests returned HTTP 200. Report: `artifacts/test-results/deepseek-flash-live/deepseek-flash-live-20260910-181425-021-73fbfaca5cd04580a22f0365a07e6045.trx` (2026-09-11 03:14 KST, about 23 seconds). This successful contract run supplements the earlier observations; it does not establish general model-answer accuracy.

The report records four actual provider tool-call IDs, two inline PNG/JPEG inputs, and 276 nonempty provider reasoning parts. Auto/Low/High/Max requests passed, including a 16,384-token Max budget that exceeds the former 8,192-token clamp. Final tool responses matched random handler values, prior reasoning survived later user requests, all streamed text matched the actual provider text, and typed JSON and RAG completed without repair retries or hosted-resource cleanup.

The Release solution and updated test project built with zero warnings/errors. The full AI Unit suite passed **1,147/1,147**, including 45 new DeepSeek contracts, nine UI contracts, and updated compatibility cases. RAG passed **109/109**. Reports: `artifacts/test-results/deepseek-flash-unit/deepseek-flash-unit.trx` and `artifacts/test-results/deepseek-flash-unit/deepseek-flash-rag.trx`. The new context/tool regression exposed a shared legacy-stream context loss after the first yield; captured context is now restored on every iterator advancement and disposal. The settings JavaScript smoke check covered default thinking-off, all four efforts, request payloads, sampling changes, and preserving the selected effort when toggling thinking. Release documentation/metadata validation passed for 491 Markdown files, 13 languages, and four release packages; DocFX metadata/build completed with zero warnings/errors.

## Verify Perplexity execution, independent search, and retrieval

Use these suites before migrating an application to Perplexity's Agent API or building retrieval with its Search and embedding APIs. A successful chat response does not demonstrate that tool results survive another round, search filters reach the correct endpoint, or encoded embeddings retain their similarity meaning in a vector store. These suites exercise each public path separately. See the [Perplexity guide](../../docs/perplexity.md) for API selection and examples.

```powershell
./build/test-perplexity-agent-live.ps1 -NoBuild
./build/test-perplexity-retrieval-live.ps1 -NoBuild
```

Omit `-NoBuild` to build before execution. Both runners use the existing `LiveTestSecrets` credential `sonar-secret2`, require Azure Key Vault and Perplexity network access, and incur API charges. Authentication, access, quota, malformed output, or a failed assertion fails validation. Each runner creates a unique TRX report and checks exact discovered, executed, and passed counts with no skipped, unexecuted, or inconclusive cases.

| Suite | Strict execution requirement | Evidence |
| --- | --- | --- |
| [Agent](Providers/Perplexity/PerplexityAgentLiveTests.cs) | 24 cases, 30 successful completed requests | Completion, legacy callback, rich stream, and Run preserve actual response text; all six presets and documented reasoning efforts reach real requests; native JSON schema works for typed completion and streaming; hosted search returns citations and supports a subsequent user turn; synthetic tools preserve native IDs; image input and RAG rewrite work through public APIs. |
| [Retrieval](Providers/Perplexity/PerplexityRetrievalLiveTests.cs) | 11 cases, 17 HTTP 200 responses, 26 returned embeddings | Independent web/people search, multi-query filters and budgets; both standard models index and query through a real in-memory RAG pipeline; both contextualized models preserve document groups and query model identity; all four models return int8 and explicit packed binary representations. |

Retrieval requests use **128 dimensions** to limit payload size and cost. Standard embeddings are decoded as signed int8 and normalized for cosine similarity; the corresponding indexed source must rank above an unrelated source. Contextualized documents use two groups with two and one chunks, followed by a query sent as its own single-chunk group to the same model. Binary requests use duplicate and distinct synthetic inputs: duplicates must have Hamming distance zero, different content must differ, and packed vectors remain separate from float-vector retrieval. No hosted stores or customer files are created.

The web-search fixture requires ranked results with usable titles, snippets, and public URLs from the allowed documentation domain. The multi-query case verifies dates, country, language, and token budgets in the actual request; an empty result set is a valid response to restrictive filters. People search requires a matching public figure. `ContentSize` is deliberately omitted for people search because the live service rejects that combination; the SDK now rejects it before HTTP instead of submitting a known-invalid request.

The probes log protocol configuration and counts rather than keys, headers, prompts, search arguments, snippets, or encoded vectors. On errors, the retrieval probe extracts only recognized provider error fields, removes credentials and request strings, and limits the diagnostic length. Reports are saved under `artifacts/test-results/perplexity-agent-live/` and `artifacts/test-results/perplexity-retrieval-live/`.

The separate deterministic Search/embedding contracts reside in `tests/Mythosia.AI.Rag.Tests/`. They cover all four models, reduced dimensions, signed decoding and normalization, response index/count/model/dimension validation, contextual group ordering, binary distances, caller-owned HTTP clients, cancellation while success/error bodies are pending, and RAG indexing/retrieval. The browser checks run the actual HTML and JavaScript modules without network access:

```powershell
node --experimental-vm-modules build/test-perplexity-agent-ui.mjs
node --experimental-vm-modules build/test-perplexity-embedding-ui.mjs
```

The embedding UI check covers both standard models, custom dimension restoration, dedicated Perplexity key storage and gating, external-store reconnection, upload form payloads, missing-key rejection, and the existing keyless Ollama/vLLM paths.

### Initial Perplexity validation — 2026-09-11 (Asia/Seoul)

The Agent runner passed **24/24**, with **29** successful completed real requests and no skipped or inconclusive cases. Report: `artifacts/test-results/perplexity-agent-live/perplexity-agent-live-20260910-185503-031-09f030f0791a446c9ca734df0345b014.trx`.

The first Retrieval run passed **10/11**. Both standard and contextualized models passed int8 and binary checks, including RAG retrieval, and both web-search cases passed. People search returned HTTP 400, so this run did **not** satisfy the strict runner. Report: `artifacts/test-results/perplexity-retrieval-live/perplexity-retrieval-live-20260910-185503-056-7c94b281a4544616b6852ec337859a42.trx`.

A targeted replay exposed only the provider's generic `invalid_request` message. Bounded direct diagnostics then isolated the option combination: a people query alone returned HTTP 200 with ten results; adding only `max_results: 3` returned HTTP 200 with three results. Explicit `search_context_size` values Low, Medium, and High each returned HTTP 400. This was an unsupported request combination, not missing account access. The Search client now rejects People with `ContentSize`, the deterministic test no longer assumes that combination is valid, and the live people fixture omits it. The initial failure remains recorded alongside the successful run below.

### Final Perplexity retrieval validation — 2026-09-11 (Asia/Seoul)

The final strict Retrieval runner passed **11/11**, with **17 HTTP 200 responses**, **26 returned embeddings**, and no failed, skipped, unexecuted, or inconclusive cases. Report: `artifacts/test-results/perplexity-retrieval-live/perplexity-retrieval-live-20260910-190101-963-62669dbdde4c4a3f99df2fa2eed71dbb.trx` (about 12 seconds).

This report includes successful independent web search, filtered multi-query search, and people search. Each of the four embedding models passed both its normalized int8 and packed binary contract at 128 dimensions. Both standard models retrieved the expected source through the RAG pipeline and in-memory vector store; both contextualized models preserved two document groups and embedded the query through the same contextual model. All four binary cases verified identical-input Hamming distance zero and differing bits for unrelated content. The runner also checked the endpoint/model/format/request counts, so a model substitution or a skipped case would fail validation.


### Agent follow-up regression — 2026-09-11 (Asia/Seoul)

Adding a follow-up user turn after hosted web search exposed a real replay error. The extended run passed **23/24**: it made **30 requests**, of which **29 returned HTTP 200** and the follow-up returned **HTTP 400**. Report: `artifacts/test-results/perplexity-agent-live/perplexity-agent-live-20260910-192454-905-1aa26834593f486ab124badcd6bb6d6b.trx`. The earlier 24-case success did not include this additional follow-up request.

The serializer replayed an output-only `search_results` item as input; the server reported `unknown item type "search_results"`. Agent requests accept `message`, `function_call`, and `function_call_output` items; hosted output traces remain available in history metadata without being sent as input. Follow-up questions can use prior answer messages. Use `PreviousResponseId` when continuation needs the provider's full hosted state.

After that correction, the full deterministic AI suite passed **1,285/1,285**, with no skips, and the Release build completed with zero warnings or errors. The seven added regression cases cover four history-replay cases and three malformed background-sequence cases. Report: `artifacts/test-results/perplexity-unit/perplexity-full-unit-replay-fix.trx`. Release metadata/documentation validation passed for four packages, 504 Markdown files, and all 13 languages.

### Final Agent validation — 2026-09-11 (Asia/Seoul)

The corrected strict Agent runner passed **24/24**, with **30 HTTP 200 responses**, all reaching `completed`, and no failed, skipped, unexecuted, or inconclusive cases. Report: `artifacts/test-results/perplexity-agent-live/perplexity-agent-live-20260910-192844-282-fdcf17801c6749da99c31e0aee0b98ba.trx`.

The additional follow-up after hosted web search succeeded through the public completion path. This verifies that output-only hosted traces remain preserved without being resubmitted as input, while the prior answer remains usable in conversation. The same full run revalidated all six presets, effort settings, four execution paths, typed outputs, citations, dependent local tools, image input, and RAG rewrite isolation.

DocFX rebuilt the latest 13-language guides and API documentation with zero warnings or errors. Log: `artifacts/docfx-perplexity-final.log`.




## Verify Perplexity background tasks and hosted platform tools

Use the [platform runner](../../build/test-perplexity-platform-live.ps1) to verify server-side task lifecycles independently of foreground Agent answers and retrieval:

```powershell
./build/test-perplexity-platform-live.ps1 -NoBuild
```

The suite requires all 11 cases to pass without skips. It covers background submission and polling, saved-cursor SSE reconnection, explicit server cancellation, stored-response continuation, selection from a valid model list, URL fetching, finance/people tools, the read-only public DeepWiki MCP `ask_wiki_question` tool, an inline skill executed in sandbox, and a built-in XLSX skill whose generated workbook is listed and downloaded. It uses the existing `sonar-secret2` credential and makes billable requests.

Saved profiles, uploaded custom skills, and connectors are excluded from this platform suite because no registered account resources were supplied. A separate [opt-in resource runner](#prepare-registered-profile-custom-skill-and-connector-tests) is now prepared for those fixtures; it has not been executed. Their request shapes have deterministic coverage, which does not establish successful live execution. Selecting a model from a valid fallback list does not establish failover during an actual provider outage. These limits are not counted as passed or skipped live cases.

### First platform validation — 2026-09-11 (Asia/Seoul)

The first run passed **5/11**, with six failures. URL fetching, finance search, people search, public MCP, and the inline skill/sandbox case passed. Report: `artifacts/test-results/perplexity-platform-live/perplexity-platform-live-20260910-190101-959-3a0cb335145a48549f0621000aa2643a.trx`.

Five remaining cases encountered `request_rate_limit_exceeded` HTTP 429 responses during background submission, observation, continuation, cancellation, or workbook retrieval. Some also failed cleanup assertions because the rate limit prevented confirmation of a known background job's final state. Subsequent diagnostics observed `Retry-After: 1` and retrieved known jobs as `completed` or `in_progress`; these failures did not establish that background execution was unsupported. The adapter added bounded retries for safe reads and cancellation, while submission is not automatically retried. Fixture pacing was also adjusted.

The sixth failure used an invalid first model ID to test fallback. The provider rejected the entire model list with HTTP 400 before selection, so the fixture's premise was wrong. The replacement case supplies valid Luna/Sonar model IDs and checks which model the provider selects. It makes no outage-failover claim.

### Second platform validation — 2026-09-11 (Asia/Seoul)

The second run passed **9/11**, with two failures. Background polling, stored continuation, valid-model-list selection, URL fetching, finance and people tools, public MCP, inline skill/sandbox execution, and actual XLSX generation/listing/download passed. Report: `artifacts/test-results/perplexity-platform-live/perplexity-platform-live-20260910-190643-602-abcfa47270b54db9b1ae34a1a32c7bbc.trx`.

The successful XLSX background response also contained a provider-internal `view_images` trace encoded as an ordinary `function_call`, with no explicit field distinguishing it from a client function call. Validation therefore covers Office skills through `StartBackgroundAsync`, response polling, and file listing/download. Background `OutputJson` retains the trace. Foreground completion/Run keeps strict handling of unregistered function calls; this result does not establish a foreground Office-skill workflow.

Saved-cursor replay failed with HTTP 400 because the original background submission used `stream: false`; a response must be created with streaming enabled for durable SSE replay. The immediate-cancellation case failed at submission with HTTP 429, so this run did not establish the cancellation contract. These failures led to the corrections and reruns recorded below.


### Targeted replay and cancellation diagnostic — 2026-09-11 (Asia/Seoul)

A two-case rerun passed **0/2**. Replay still failed with HTTP 400 because the first streaming GET omitted the provider-required `starting_after` cursor; cancellation again encountered HTTP 429. Report: `artifacts/test-results/perplexity-platform-live/perplexity-replay-cancel.trx`.

The corrected background submission now enables both `background` and `stream` and retains its initial SSE events. A handle's first `StreamAsync()` delivers those buffered events before reconnecting after their last sequence. A reattached handle with no initial buffer sends `starting_after=0`; an explicit saved cursor resumes after that sequence. `LastSequenceNumber` advances only for events delivered to the reader. Reattaching without a saved cursor does not promise recovery of sequence zero or access beyond the provider's replay window.

### Final platform validation — 2026-09-11 (Asia/Seoul)

The final strict runner passed **11/11**, with no failures, skipped, unexecuted, or inconclusive cases. All **38 HTTP requests** returned HTTP 200, including **12 acknowledged Agent submissions**. There were **zero recovered HTTP 429 responses** and **zero reported cleanup failures**. Report: `artifacts/test-results/perplexity-platform-live/perplexity-platform-live-20260910-192030-969-958a5e8f689c4710ab168c5060d84fe9.trx`.

The run verified initial background observation and saved-cursor reconnection against actual text, an explicit cancellation POST followed by a terminal `cancelled` state, and stored-response continuation using only new input. It also verified valid-model-list selection, actual hosted URL/finance/people tool results, an allowlisted read-only public MCP call, an inline skill's sandbox execution, and generation/listing/download of a valid XLSX workbook. Office-skill evidence is limited to the background path described above.

At the platform-validation stage, the full deterministic AI suite passed **1,278/1,278** with no skips, and the Release test build completed with zero warnings or errors. Report: `artifacts/test-results/perplexity-unit/perplexity-full-unit-final.trx`. The later replay correction raised the passing AI total to **1,285/1,285**, as recorded above. RAG regression tests passed **181/181**; report: `artifacts/test-results/perplexity-unit/perplexity-rag-full.trx`. Registered profiles, uploaded custom skills, connectors, and actual outage-triggered model failover remain outside successful live coverage.

## Prepare registered profile, custom skill, and connector tests

These tests answer a different question from the existing platform suite: does an actual account resource supply the instructions or document content, rather than the model merely producing a plausible answer? The [resource tests](Providers/Perplexity/PerplexityResourceLiveTests.cs) and [dedicated runner](../../build/test-perplexity-resources-live.ps1) are **prepared but have not been executed**. Their three cases are not included in the **46 previously successful live cases**: 24 Agent, 11 Retrieval, and 11 Platform.

The tests reference existing synthetic fixtures; they do not create, upload, modify, or delete profiles, skills, connectors, or connector documents. All three cases use `StartBackgroundAsync` and poll to completion with the shared platform transport and background-job cleanup. They require the existing `sonar-secret2` Key Vault credential and access to the registered fixtures. Execution makes billable API calls.

Configure a fixture specifically for each check:

| Fixture | Preparation | Required evidence |
| --- | --- | --- |
| Profile | Save a verification-only profile with no hosted tools, skills, or connectors. Its saved instructions must tell the model to return only a unique synthetic token. | The request contains the configured custom profile ID and optional version, with no model, preset, or instructions override; the completed answer exactly matches the token. |
| Custom skill | Upload a verification-only skill whose name/description invite loading for this verification task. Put the unique token only in the skill body, with instructions to return that token alone; keep it out of the name and description. | The request contains only the configured custom skill reference, a `skill_loaded` trace occurs, and the answer exactly matches the token. A load trace alone is insufficient because the provider can emit it for a failed load. |
| Connector | Use one explicitly allowed read-only tool and an existing synthetic document containing a unique token. Supply enough document location/query details to retrieve it, without revealing the token in the prompt. | Actual `mcp_call` items match the configured connector ID, server label, and single tool. Every call has no error and a nonempty result; a real tool result contains the token and the answer exactly matches it. |

The expected token exists in the fixture and in the test's local expected-value setting; it must not occur in the request, resource ID, label, tool name, or connector prompt. Each test checks the serialized request for token leakage. The custom-skill and connector cases use the shared `openai/gpt-5.6-luna` test model. The profile case uses the saved model without overriding it. Keep the saved profile free of tools: request tool settings merge with profile tools and cannot reliably disable them. See the official [Profiles](https://docs.perplexity.ai/docs/agent-api/profiles), [Skills](https://docs.perplexity.ai/docs/agent-api/skills), and [Connectors](https://docs.perplexity.ai/docs/agent-api/tools/connectors) documentation.

Environment variable names below include the full required prefix. Only the selected resource's settings are required; `All` requires all three sets.

| Selection | Required environment variables | Optional environment variable |
| --- | --- | --- |
| `Profile` | `MYTHOSIA_PERPLEXITY_PROFILE_ID`, `MYTHOSIA_PERPLEXITY_PROFILE_EXPECTED_TEXT` | `MYTHOSIA_PERPLEXITY_PROFILE_VERSION` |
| `CustomSkill` | `MYTHOSIA_PERPLEXITY_CUSTOM_SKILL_ID`, `MYTHOSIA_PERPLEXITY_CUSTOM_SKILL_EXPECTED_TEXT` | `MYTHOSIA_PERPLEXITY_CUSTOM_SKILL_VERSION` |
| `Connector` | `MYTHOSIA_PERPLEXITY_CONNECTOR_ID`, `MYTHOSIA_PERPLEXITY_CONNECTOR_SERVER_LABEL`, `MYTHOSIA_PERPLEXITY_CONNECTOR_TOOL`, `MYTHOSIA_PERPLEXITY_CONNECTOR_PROMPT`, `MYTHOSIA_PERPLEXITY_CONNECTOR_EXPECTED_TEXT` | None |

Replace the following placeholder IDs and tokens with your existing fixtures before running. An optional version must be omitted or nonblank; use a real version string to pin the fixture, or `latest` to follow its latest version.

```powershell
$env:MYTHOSIA_PERPLEXITY_PROFILE_ID = 'profile_YOUR_VERIFICATION_PROFILE'
$env:MYTHOSIA_PERPLEXITY_PROFILE_EXPECTED_TEXT = 'PROFILE_YOUR_UNIQUE_FIXTURE_TOKEN'
$env:MYTHOSIA_PERPLEXITY_PROFILE_VERSION = '1' # Optional: use an existing version.
./build/test-perplexity-resources-live.ps1 -Resource Profile -NoBuild
```

```powershell
$env:MYTHOSIA_PERPLEXITY_CUSTOM_SKILL_ID = 'skill_YOUR_VERIFICATION_SKILL'
$env:MYTHOSIA_PERPLEXITY_CUSTOM_SKILL_EXPECTED_TEXT = 'SKILL_YOUR_UNIQUE_FIXTURE_TOKEN'
$env:MYTHOSIA_PERPLEXITY_CUSTOM_SKILL_VERSION = '1' # Optional: use an existing version.
./build/test-perplexity-resources-live.ps1 -Resource CustomSkill -NoBuild
```

The connector example uses GitHub's read-only `get_file_contents` tool. Replace the repository, branch, and path with your synthetic fixture, or configure an equivalent read-only tool for another connected service.

```powershell
$env:MYTHOSIA_PERPLEXITY_CONNECTOR_ID = 'connector_github'
$env:MYTHOSIA_PERPLEXITY_CONNECTOR_SERVER_LABEL = 'verification-github'
$env:MYTHOSIA_PERPLEXITY_CONNECTOR_TOOL = 'get_file_contents'
$env:MYTHOSIA_PERPLEXITY_CONNECTOR_PROMPT = 'Read owner=YOUR_TEST_OWNER, repo=YOUR_FIXTURE_REPO, ref=main, path=fixtures/perplexity-verification.txt.'
$env:MYTHOSIA_PERPLEXITY_CONNECTOR_EXPECTED_TEXT = 'CONNECTOR_YOUR_UNIQUE_FIXTURE_TOKEN'
./build/test-perplexity-resources-live.ps1 -Resource Connector -NoBuild
```

After all three sets are configured, run `./build/test-perplexity-resources-live.ps1 -Resource All -NoBuild`. `All` is also the default. Omit `-NoBuild` when a fresh Release build is needed.

The runner checks the selected settings before starting tests, temporarily sets `MYTHOSIA_PERPLEXITY_RESOURCE_LIVE=1` in its process environment, and restores the previous value when it exits. Direct `dotnet test` execution requires the caller to set that opt-in explicitly; without it, these tests fail before retrieving a credential. Missing settings fail instead of producing skipped or inconclusive results.

A successful run requires exactly one case for a single selection or exactly three for `All`, with one acknowledged submission and verified resource behavior per case. HTTP/tool errors, missing evidence, skipped cases, and unfinished background cleanup fail the runner. Unique reports are written under `artifacts/test-results/perplexity-resources-live/`. Until that runner actually succeeds with registered fixtures, profile, custom-skill, and connector execution remain unverified.

### Preparation checks — 2026-09-11 (Asia/Seoul)

The Release test project built with zero warnings or errors. All **63/63** deterministic [resource-setting tests](Common/PerplexityResourceLiveSettingsTests.cs) passed with no skips; report: `artifacts/test-results/perplexity-unit/perplexity-resource-settings-unit.trx`. PowerShell AST parsing confirmed the runner's syntax without executing the script.

These checks made **no provider API or Key Vault calls** and did not execute the three live tests. They verify configuration and build readiness only; the registered-resource cases remain unexecuted.

## Prepare GPT Image 2.5 generation and editing tests

Selecting a new image model should preserve the same `IImageGenerationService` calling pattern, keep chat history and the default image model intact, and reject unsupported settings before a billable request. The [GPT Image 2.5 contract tests](Common/OpenAIImage25ContractTests.cs) cover the Sunburst and Flare aliases and their `2026-09-08` snapshots, all six quality settings on both endpoints, custom-size boundaries, output formats, transparency, compression, ordered multipart references, and mask metadata. The image-model default remains `gpt-image-2`. The current typed API validates its options and rejects unsupported `XHigh`/`Max` quality before sending.

The [four live cases](Providers/OpenAI/OpenAIImage25LiveTests.cs) and [dedicated runner](../../build/test-openai-images-live.ps1) are **prepared and have not been executed**. They are not included in any previously reported successful live-test total. Each model has one generation and one single-image edit, using `quality=low`, `size=1024x1024`, `output_format=png`, and `Count=1`. This is four requests and four output images; broader quality, size, mask and snapshot coverage remains deterministic contract testing until separately exercised against the API.

The live probe checks the actual requested model and endpoint, JSON or multipart body settings, HTTP 200, input-byte preservation, response request ID, token usage, and byte-for-byte response extraction. SkiaSharp must fully decode every returned PNG to exactly 1024×1024 pixels. It saves the returned images and synthetic edit references under a unique directory inside `artifacts/test-results/openai-images-live/`. No user images, URL downloads, image-judging model, or automatic provider retries are involved. Diagnostics contain protocol metadata and artifact paths, without credentials, prompts, or base64 image content.

The existing `momedit-openai-secret` Key Vault credential is read only after explicit opt-in. Both direct test execution and the runner require `MYTHOSIA_OPENAI_IMAGE25_LIVE=1`; otherwise execution fails before credential retrieval. To run the prepared suite later, after authorizing the four billable image calls:

```powershell
$env:MYTHOSIA_OPENAI_IMAGE25_LIVE = '1'
try {
    ./build/test-openai-images-live.ps1 -NoBuild
}
finally {
    Remove-Item Env:MYTHOSIA_OPENAI_IMAGE25_LIVE -ErrorAction SilentlyContinue
}
```

Omit `-NoBuild` when a fresh Release build is needed. The runner creates a unique TRX report and requires exactly **4/4 passed cases**, four matching HTTP 200 requests, and four distinct fully decoded artifacts. Authentication, quota, missing metadata, invalid image data, skipped tests, and inconclusive tests fail the run. Preparing or compiling this suite does not establish account access or API success.

The input contract follows the current [OpenAI image generation guide](https://developers.openai.com/api/docs/guides/image-generation): masks use PNG or WebP matching the first reference's format, with a size below 50MiB. Tests include a mask larger than the old 4MiB boundary. Pixel dimensions and alpha-channel validity are left to the provider; the production library does not decode or transcode references merely to validate them.

### Preparation checks — 2026-09-11 (Asia/Seoul)

The Release test project built with zero warnings or errors. The full deterministic AI suite passed **1,471/1,471**, including **123/123 GPT Image 2.5 contract cases**, with no failed or skipped cases. Report: `artifacts/test-results/openai-image25-unit/gpt-image25-unit.trx`. PowerShell AST parsing also confirmed the new runner's syntax without executing it.

These checks made **no provider API or Key Vault calls**. The four GPT Image 2.5 live cases remain prepared and unexecuted; no API success is claimed for them.


## Validate typed image options for the next major release

Use the unit suite to catch invalid combinations before billable requests, and recompile consumer examples to catch the intentional source break. `ImageQuality`, `ImageBackground`, and `ImageOutputFormat` replace strings. `ImageSize.Auto`, `Pixels(...)`, and `Preset(...)` replace the overloaded size string; the separate request `AspectRatio` property is removed. See the [migration examples](../../docs/providers.md#image-options-migration).

The contract tests cover generation and editing for OpenAI, Google, and xAI: enum serialization and undefined casts; exact-pixel versus preset dispatch; new `ImageOutputFormat.Auto` defaults; explicit codec rejection; provider/model-specific quality, size, ratio, transparency, and compression limits; unchanged image-model selection; and refusal before HTTP. Factory tests validate positive dimensions, valid resolution/ratio values, and immutable size intent. Existing error, cancellation, reference ordering, multipart, byte decoding, and result metadata checks remain in place.

```powershell
dotnet test --project tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj --configuration Release --filter "TestCategory=Unit" --report-trx --results-directory artifacts/test-results/typed-image-unit
```

The prepared Grok/OpenAI, Google aspect-ratio, and GPT Image 2.5 live cases use the new typed options. Existing live results above record the API that was tested at that time; they are not evidence of a new live run after this major migration. No provider API or Key Vault calls were made for this documentation update. GPT Image 2.5's four live cases remain prepared for later execution.

### Deterministic validation: 2026-09-12 (Asia/Seoul)

The Release test project built with **zero warnings and errors**. The complete deterministic AI suite passed **1,592/1,592**, with **zero failures and zero skipped cases**. Report: `artifacts/test-results/typed-image-options-unit/typed-image-options-unit.trx`. These results cover the typed image-option implementation; live API revalidation remains unexecuted.

This includes **354 image contract cases** across the shared options, OpenAI, Google, and xAI suites. All **73 changed documentation code blocks** from 13 languages, two package READMEs, and four package guides compiled with warnings treated as errors. The documentation and NuGet metadata validator passed for four packages and 504 Markdown files. The local compilation harness is `artifacts/typed-image-options/compile-examples.ps1`; it compiles examples without running their API calls.

DocFX regenerated the public API documentation and built the site with **zero warnings and errors**. Log: `artifacts/docfx-typed-image-options.log`.

## Validate independent request builders

Use `CreateRequest(...)` when preparing calls with different settings or branching a shared request configuration. Each `With...` returns a new builder, while execution reads a captured request without overwriting service defaults. See the [request builder guide](../../docs/request-building.md).

The three `AIRequestBuilder*Tests` suites cover seven providers using deterministic HTTP/SSE responses: independent branches, captured common/native defaults, source option mutation, copied messages and function schemas, repeated execution, profile and context behavior, multiple tool rounds, invalid options, failure cleanup, cancellation and existing MessageChain adapters. A gated Anthropic run specifically checks that profile settings survive after `StartRunAsync` returns. Test providers used by existing recovery and summary tests now read the same request accessors as production providers, and assert that public defaults remain unchanged during execution.

```powershell
dotnet test --project tests/Mythosia.AI.Test/Mythosia.AI.Test.csproj -c Release --filter "TestCategory=Unit" --report-trx --results-directory artifacts/test-results/request-builder
```

Validation on **2026-09-12 (Asia/Seoul)**: the Release build completed with zero warnings or errors; **1,643/1,643 AI unit tests** and **181/181 RAG tests** passed with no skipped tests. Reports: `artifacts/test-results/request-builder/request-builder-unit.trx` and `artifacts/test-results/request-builder/request-builder-rag.trx`.

All **221 request-builder documentation code blocks** compiled without executing their API calls. The release documentation validator passed for four packages, 517 Markdown files and 13 languages, including request-guide navigation and branching examples. DocFX regenerated the API reference and built the documentation with zero warnings or errors (`artifacts/docfx-request-builder.log`). The example harness is `artifacts/request-builder-docs/compile-examples.ps1`.

This validation made **no live provider API or Key Vault calls**. Request settings are independent; conversation ownership and the existing single-active-Run restriction remain unchanged.

## Validate tool returns, errors, and cancellation

Attributed tools can return ordinary objects through `Task<T>` and `ValueTask<T>` just as synchronous tools already could. Optional `CancellationToken` parameters receive the execution token and are excluded from the model's argument schema. The common executor records actual failures and cancellation, skips queued work after cancellation, and awaits started noncooperative handlers before releasing the conversation. See the [Before/After guide](../../docs/function-calling.md#tool-execution-contract).

The deterministic coverage includes return values and serialization, one-shot ValueTask consumption, reflected exception unwrapping, existing string handlers and erased Task declarations, handler replacement, request snapshots, sequential and parallel cancellation, SSE and WebSocket Run cancellation/disposal, native deferred-tool cleanup, throwing cancellation callbacks, and Anthropic error serialization. RAG and MCP tests verify token forwarding, failure propagation, retrieval traces, cancelled MCP request cleanup and subsequent calls.

Validation on **2026-09-13 (Asia/Seoul)**: Release builds completed with zero warnings and errors. **1,705/1,705 AI unit tests**, **186/186 RAG tests**, and **23/23 MCP tests** passed with no skipped tests. Reports are under `artifacts/test-results/tool-contract/`: `tool-contract-unit.trx`, `tool-contract-rag.trx`, and `tool-contract-mcp.trx`.

The **52 new tool documentation C# examples from 13 languages** compiled with warnings treated as errors, using `artifacts/tool-contract-docs/compile-examples.ps1`. The documentation validator also checks localized tool contracts and their Run/agent/request-builder links.

This validation used deterministic in-memory HTTP/SSE/WebSocket and MCP transports. **No live provider API or Key Vault calls were made.** MCP cancellation stops local sending/waiting; the library does not promise that a remote server has stopped its work.

DocFX regenerated the API reference and built the documentation site with **zero warnings and errors**. Log: `artifacts/docfx-tool-contract.log`.

## Validate ordinary completion cancellation

Pass `cancellationToken` when an answer is no longer needed after a user cancellation, navigation, or an application deadline. The token reaches request preparation, transport and body reads, automatic summaries, JSON repair, local tools and subsequent model rounds. RAG rewriting, reranking and answer generation forward the same token. See the [completion cancellation guide](../../docs/completions.md#completion-cancellation).

The deterministic tests cover ten transport routes across seven providers, including OpenAI Chat/Responses and Qwen vLLM/Ollama. They test cancellation before sending, pending headers, stalled success/error bodies, disposal, charset handling, caller cancellation versus policy and identifiable HTTP timeouts, paired tool histories, GPT-6 deferred-tool cleanup, waiting for started noncooperative tools, and successful reuse after cancellation. Common tests cover builder/interface/message/convenience entry points, context providers, summaries and structured-output repairs; RAG tests cover all existing cancellation-aware completion wrappers.

Validation on **2026-09-13 (Asia/Seoul)**: Release builds completed with **zero warnings and errors**. **1,827/1,827 AI unit tests**, **203/203 RAG tests**, and **23/23 MCP tests** passed with no skipped tests, including **139 new cancellation cases**. Reports are under `artifacts/test-results/completion-cancellation/`: `completion-cancellation-unit.trx`, `completion-cancellation-rag.trx`, and `completion-cancellation-mcp.trx`.

All **39 completion cancellation C# examples from 13 languages** compiled with warnings treated as errors using `artifacts/completion-cancellation-docs/compile-examples.ps1`. The release documentation validator checks the localized completion contract and migration guidance as well as existing package/documentation consistency.

These tests use deterministic local HTTP and tool substitutes. **No live provider API or Key Vault calls were made.** They verify that the library cancels its operation and cleans up; they do not establish when remote inference or billing stops. Background job cancellation remains a separate explicit operation.

The final documentation validator passed for **four release packages, 517 Markdown files and 13 languages**. DocFX regenerated the API reference and the final site build completed with **zero warnings and errors**, including the custom-provider override and method-group migration guidance. Final build log: `artifacts/docfx-completion-cancellation-verified.log`.

### Interruption recheck: 2026-09-13 (Asia/Seoul)

Independent reviews of the common execution/RAG path and all seven provider implementations found no unfinished cancellation wiring from the interrupted turn. A narrow consistency fix makes Google image-URL download cancellation preserve the caller token in `OperationCanceledException` while waiting for headers, as it already did while reading the body. A deterministic regression test verifies transport cancellation, caller-token identity, no subsequent model call, and unchanged history.

Fresh Release builds passed with zero warnings/errors. After this fix the AI unit suite passed **1,828/1,828**; the recheck also passed **203/203 RAG** and **23/23 MCP** tests, without skipped cases. Reports: `artifacts/test-results/cancellation-recheck/cancellation-recheck-unit.trx`, `cancellation-recheck-rag.trx`, and `cancellation-recheck-mcp.trx` in that directory. All **39 documentation examples** compiled again, the four-package/517-Markdown/13-language validator passed, and the documentation site rebuilt with zero warnings/errors (`artifacts/docfx-cancellation-recheck.log`). No live provider API or Key Vault calls were made.


## Validate the rich Run result

Applications that need a completed answer together with usage and sources can await `AIRun.Result` without collecting stream events. The pending major release changes its type to `Task<AIRunResult>`; the former string is now `result.Text`. See the [Before/After guide](../../docs/execution-api-transition.md#run-result).

The new deterministic coverage includes result snapshots without stream observation, stream filters and observation overflow, defensive usage/citation copies, per-round usage without duplicate aggregation, missing and partially reported metadata, requested versus actual final-round model, finish mapping, custom provider fallbacks, steering, disposal/cleanup, and unchanged failure/cancellation behavior. Provider fixtures exercise HTTP/SSE and native WebSocket paths without making external requests.

Validation on **2026-09-13 (Asia/Seoul)**: **1,896/1,896 AI unit tests**, **203/203 RAG tests**, and **23/23 MCP tests** passed, including **68 new rich-result cases** (16 common and 52 provider cases). Reports are under `artifacts/test-results/rich-run-result/`: `rich-result-unit-final.trx`, `rich-result-rag-final.trx`, and `rich-result-mcp-final.trx`.

All **26 new result C# examples from 13 languages** compiled with warnings treated as errors using `artifacts/rich-run-result-docs/compile-examples.ps1`. The earlier `Before` snippet is migration history and is excluded from this compilation. **No live provider API or Key Vault calls were made.**

### Rich result re-audit

The **2026-09-13** cross-review reproduced incorrect `RequestedModel` values when Perplexity options selected a different model or delegated selection to the server, and when Qwen used a deployment override or translated an Ollama ID. The result now uses the same captured effective-model selection as the outgoing request. `RequestedModel` is nullable when no single model ID is sent; the final provider-reported `Model` remains separate. Before correction, **16/18 Perplexity** and **4/4 Qwen** regression cases failed as expected.

Added **42 regression cases**: 18 Perplexity selector/snapshot cases, 4 Qwen override/translation cases, and 20 provider-specific missing-versus-zero usage cases. The final Release runs passed **1,938/1,938 AI unit**, **203/203 RAG**, and **23/23 MCP** tests without skips. Reports are under `artifacts/test-results/rich-run-result-reaudit/`: `rich-result-reaudit-unit.trx`, `rich-result-reaudit-rag-final.trx`, and `rich-result-reaudit-mcp-final.trx`. All 26 updated documentation examples compiled again and the four-package/517-Markdown/13-language validator passed. No live provider API or Key Vault calls were made.

## Validate model capabilities

Applications can inspect the selected request before presenting reasoning, sampling, tools, search or image controls. See the [capability guide](../../docs/model-capabilities.md) for why the common and native reasoning contracts are separate and how to handle Unknown.

Validation on **2026-09-13 (Asia/Seoul)** passed **2,271/2,271 AI unit tests**, **203/203 RAG tests**, and **23/23 MCP tests**, without skips. This includes **333 new capability and UI regression cases** over the preceding rich-result re-audit. Release builds completed with zero warnings and errors. Reports are under `artifacts/test-results/capabilities/`: `capabilities-unit-final.trx`, `capabilities-rag.trx`, and `capabilities-mcp.trx`.

Coverage checks immutable collections, no HTTP/context callbacks/history changes during inspection, preservation of pending features/policies/native options and prior Claude diagnostics, independent builder snapshots, resolver-failure cleanup, internal profiles, all seven provider adapters, unknown/custom endpoints and model selectors, common/native reasoning choices, and image option definitions shared with validation. Fake HTTP responses verify accepted wire settings and rejected settings before sending. The Chat UI uses the same definitions and preserves existing native reasoning when no locally known control is offered.

The actual settings module smoke test also passed with `node --experimental-vm-modules build/test-perplexity-agent-ui.mjs`, including unknown reasoning and independent Agent controls. The modified model-selection JavaScript passed Node syntax validation.

All **26 new guide examples across 13 languages** compiled with warnings treated as errors using `artifacts/model-capabilities-docs/compile-examples.ps1`. The documentation validator passed **four release packages and 530 Markdown files**; the DocFX site and API reference built with **zero warnings and errors** (`artifacts/docfx-model-capabilities.log`).

**No live provider API or Key Vault calls were made.** Capability queries are local descriptions of adapter support, not live account or server probes. Existing request and server validation still determines whether a complete request can execute.

### Capability re-audit: 2026-09-13 (Asia/Seoul)

The independent re-audit reproduced and corrected capability queries that invoked execution-profile hooks or serialized arbitrary tool options, incorrect sampling support for function requests, duplicate-date model IDs reported as known, and derived OpenAI image-provider identity. Claude 4.6 binding that implicitly enables adaptive thinking now applies the same temperature policy to capability metadata and outgoing requests. Query profiles use the separate, side-effect-free `ApplyCapabilityRequestProfile` hook; execution profiles retain their existing budget and preparation behavior.

The Chat UI now refreshes controls from the current connection and settings response. Server snapshots keep service and model identity together, while client revisions prevent old connection, settings and polling responses from overwriting newer controls. Deterministic regressions exercise custom endpoints, reasoning changes, reverse response order and reconnecting to the same model.

Fresh Release builds completed with **zero warnings and errors**. **2,318/2,318 AI unit tests**, **203/203 RAG tests**, and **23/23 MCP tests** passed with no skips: **2,544 total**, including **47 added re-audit cases**. Reports are under `artifacts/test-results/capability-reaudit/`: `capability-reaudit-unit.trx`, `capability-reaudit-rag.trx`, and `capability-reaudit-mcp.trx`.

Both actual-module UI regressions passed:

```powershell
node --experimental-vm-modules build/test-model-capabilities-ui.mjs
node --experimental-vm-modules build/test-perplexity-agent-ui.mjs
```

Updated capability guidance covers **13 languages** and four packaged guides. The documentation validator passed for **four release packages and 530 Markdown files**. DocFX regenerated the API reference and documentation site with **zero warnings and errors** (`artifacts/docfx-capability-reaudit.log`). This re-audit used deterministic local HTTP/SSE and browser-module substitutes; **no live provider API or Key Vault calls were made**.

## Major architecture adversarial validation

Validation on **2026-09-13 (Asia/Seoul)** deliberately challenged all six pending major changes: typed image options, independent request builders, asynchronous tool returns, cancellation, rich Run results, and capability inspection. Tests use controlled HTTP/SSE responses, delayed uploads, one-shot awaitable sources, throwing cancellation callbacks, shared JSON graphs and concurrent readers/queries. No live provider API or Key Vault calls were made.

The initial reproduction run failed **38/48 AI cases** and **2/2 MCP cases**, before production fixes. The ten passing AI cases were controls for behavior that already worked. Directly cyclic schemas were tested only after adding a recursion guard, to avoid terminating the test host with `StackOverflowException`; excessive finite nesting was reproduced before the fix.

The corrections cover:

- Function-schema cycles and excessive `Items` depth now fail with a normal `ArgumentException` during request capture. Shared acyclic children remain supported.
- Request metadata and function arguments detach `JsonElement` from its owner document and clone mutable `JsonNode` values. Reusing a builder does not reuse a previous execution's JSON mutations.
- Tools declared as `object` or `Task` preserve runtime `Task<T>` / `ValueTask<T>` results, await no-result operations, consume one-shot ValueTasks once, and retain existing failure/cancellation semantics.
- Run startup and MCP disposal attempt owned-resource cleanup even when cancellation callbacks throw, preserving the original and cleanup failures.
- Usage snapshots and round aggregation preserve explicit provider totals, including total-only reports. This intentionally replaces the earlier normalization to `InputTokens + OutputTokens`; the old agent contract test, prepared live-test assertions and documentation were updated to the reported-total contract.
- Re-enumerating one returned Run output sequence cannot bypass the single-reader contract or interfere with the original reader.
- OpenAI image and mask uploads capture their bytes before delayed transmission. This strengthens input ownership consistency with the other image adapters; image input immutability had not previously been an explicit public guarantee.
- Google image results reject an incomplete additional candidate, following the existing all-or-error behavior for the first candidate.

The final suite adds **58 regression cases**: 22 Run/usage, 13 request/capability, 13 tool/startup, 8 image, and 2 MCP cases. **2,374/2,374 AI unit tests**, **203/203 RAG tests**, and **25/25 MCP tests** passed: **2,602 total**, with no failures or skips. Release builds completed with zero warnings and errors.

Reports under `artifacts/test-results/major-adversarial/` retain the progression: `adversarial-before.trx`, `adversarial-mcp-before.trx`, `adversarial-fixed.trx`, `adversarial-unit-verified.trx`, `adversarial-rag-final.trx`, and `adversarial-mcp-final.trx`. The intermediate `adversarial-unit-final.trx` retains the single old-normalization assertion failure before its contract was updated; it is not the final passing report.

Both actual JavaScript-module regression suites passed: `build/test-model-capabilities-ui.mjs` and `build/test-perplexity-agent-ui.mjs`. Request, tool, image, Run and token-usage guidance was updated across **13 languages**, and the four-package/**530-Markdown** release documentation validator passed. These checks establish the tested local contracts, not live account availability or remote inference/billing cancellation.

DocFX regenerated the API reference and built the documentation site with **zero warnings and errors** (`artifacts/docfx-major-adversarial-verified.log`). The first build encountered a write-permission failure for generated `api/.manifest`; the authorized retry completed successfully. The Korean review summary is `artifacts/major-adversarial-review.ko.md`.

## Second major architecture adversarial validation

The second review on **2026-09-13 (Asia/Seoul)** challenged different boundaries and the preceding fixes themselves. The initial AI run failed **33/48 cases** and the MCP run failed **3/3 cases**, with production changes still withheld. Notably, the first review's JSON cloning introduced a regression for `default(JsonElement)`; the new nullable-vector test exposed it, and Undefined values now survive capture unchanged.

Request snapshots now preserve multidimensional/nonzero-bound array shape, cycles/shared children, and key comparers on standard typed `Dictionary<,>`, `SortedDictionary<,>` and `SortedList<,>` values. Disposal tests show that simultaneous asynchronous MCP disposal calls wait for the same operation, and that transport closure occurs before waiting for a read that needs closure to finish. Existing callback-error aggregation remains covered.

All six token counters are tested at `Int32.MaxValue` and one beyond it, through ordinary rounds and custom indexed-usage streams. Checked sums fail with `OverflowException` instead of wrapping. Final result assembly catches aggregation failures, settles the result task, completes cleanup, and permits service reuse. Google image generation and editing reject malformed declared inline-image data and missing/invalid/non-image MIME instead of skipping parts or guessing PNG; valid text/image mixtures and new concrete image subtypes remain accepted. This validates response structure and declarations, not image-file magic bytes or pixel decoding.

Added **58 cases**: 24 usage boundaries, 13 request snapshots, 18 Google image response cases, and 3 MCP disposal cases. The final full runs passed **2,429/2,429 AI unit tests**, **203/203 RAG tests**, and **28/28 MCP tests**: **2,660 total**, with no failures or skips. Release builds completed with zero warnings and errors. Both actual JavaScript-module UI regression suites also passed.

Reports are under `artifacts/test-results/major-adversarial-second/`: `adversarial-second-before.trx`, `adversarial-second-mcp-before.trx`, `adversarial-second-fixed.trx`, `adversarial-second-unit-final.trx`, `adversarial-second-rag-final.trx`, and `adversarial-second-mcp-final.trx`. The 13-language/four-package/**530-Markdown** documentation validator passed after updates to request, tool/MCP, image, Run and token-usage guidance. **No live provider API or Key Vault calls were made; no versions were changed or packages published.**

DocFX regenerated the API reference and built the documentation site with **zero warnings and errors** (`artifacts/docfx-major-adversarial-second.log`). The Korean review summary is `artifacts/major-adversarial-second-review.ko.md`.

## Third major architecture adversarial validation

The third cross-review on **2026-09-13 (Asia/Seoul)** assigned the preceding fixes to different reviewers. Initial tests failed **22/31 AI cases** and **8/8 MCP cases** before production changes. A further two MCP tests then reproduced calls waiting indefinitely after EOF or a receive failure, even when the transport still reported itself connected; both failed before the reader-exit fix.

The corrections preserve standard `ReadOnlyCollection<T>` and `ReadOnlyDictionary<TKey, TValue>` wrapper types in typed containers, supported backing graphs and JSON ownership, plus comparers on non-generic `Hashtable` and `SortedList`. The shared OpenAI-compatible and DeepSeek stream paths reject content after an explicit terminal response; the shared path also rejects changed terminal reasons, as DeepSeek already did. The first terminal chunk can still carry final content, and subsequent usage-only events remain valid. Invalid rounds do not save a successful assistant response or execute their tools.

MCP deserializes a matching response before removing its pending request. Disposal and reader termination seal registration through the same lifecycle gate, preventing new calls behind a stopped reader. Typed completion and structured streaming reject `int.MaxValue` repair budgets before provider execution and use an overflow-safe loop while retaining the existing negative-budget behavior (no repairs).

Added **43 regression cases**: 8 snapshot, 15 provider stream, 10 structured retry, and 10 MCP lifecycle cases. Fresh Release builds completed with **zero warnings and errors**. The final runs passed **2,462/2,462 AI unit tests**, **203/203 RAG tests**, and **38/38 MCP tests**: **2,703 total**, with no failures or skips. The focused AI run also passed **48/48 cases**, including the existing structured-stream cases. Both actual JavaScript-module UI regression suites passed.

Reports are under `artifacts/test-results/major-adversarial-third/`: `adversarial-third-unit-red.trx`, `adversarial-third-mcp-red.trx`, `adversarial-third-mcp-reader-red.trx`, `adversarial-third-unit-focused.trx`, `adversarial-third-mcp-focused.trx`, `adversarial-third-unit-final.trx`, `adversarial-third-rag-final.trx`, and `adversarial-third-mcp-final.trx`. The earlier MCP focused report covers the first eight cases; the final report includes all ten new cases.

Request, streaming, tool/MCP and structured-output guides were updated across **13 languages**, together with packaged guides and current release notes. Historical release entries and version numbers were preserved. Final rechecking exposed a side effect of the retry-budget guard: rejecting the budget before capturing one-call options left those options for a later request. Two additional tests failed before adjusting the validation order and passed afterward (`adversarial-third-retry-scope-red.trx` retains the reproduction). The original consume-on-failure policy remains intact. No additional confirmed defect was found in the cross-review of asynchronous return adaptation or Run cleanup. **No live provider API or Key Vault calls were made; no commit, push or package publication was performed.**

The four-package/**530-Markdown**/13-language documentation validator passed. DocFX regenerated the API reference and documentation site with **zero warnings and errors** (`artifacts/docfx-major-adversarial-third.log`). The Korean review summary is `artifacts/major-adversarial-third-review.ko.md`.

## v8 release package and documentation preparation

On **2026-09-13**, public NuGet version indexes confirmed the release baselines: Core 7.1.0, Abstractions 3.1.0, Alibaba 2.0.1, RAG 7.6.0 and MCP 0.0.1-preview. The prepared targets are **8.0.0 / 4.0.0 / 3.0.0 / 8.0.0 / 0.1.0-preview**, respectively. See the [v8 migration guide](../../docs/v8-migration.md).

After updating versions, descriptions, package release notes and the five-package publication set, the Release solution build passed with zero warnings/errors. The fresh AI/RAG/MCP runs again passed **2,703 tests** without failures or skips. Publication safety and test-category checks passed. Five packages and five symbol packages passed metadata, dependency and provenance checks; **nine isolated consumers** then compiled or ran successfully, including the new MCP tool/error/lifecycle consumer.

All **39 current C# examples from 13 v8 migration guides** compiled with warnings treated as errors using `artifacts/v8-migration-docs/compile-examples.ps1`. The release documentation validator passed **five packages and 544 Markdown files**. Reports are in `artifacts/test-results/release-v8/`, `artifacts/release-v8-pack.log`, `artifacts/release-v8-consumers.log`, `artifacts/release-v8-doc-examples.log`, and `artifacts/release-v8-documentation.log`.

Artifacts under `artifacts/release-v8-validation/` are marked **development-validation** and cannot be pushed by the publication script. No commit, Git push or NuGet publication was performed. Network access was limited to public NuGet metadata and dependency restoration; no live model API or Key Vault calls were made. Repack from a clean committed checkout for actual publication. The Korean preparation record is `artifacts/release-v8-preparation.ko.md`.

The final DocFX API reference and site build, including all confirmed v8 version wording, passed with **zero warnings and errors** (`artifacts/docfx-release-v8.log`).

## vLLM stable 1.0.0 release preparation

The coordinated publication set now also includes **Mythosia.AI.Serving.Vllm 1.0.0**, promoted from the published **1.0.0-preview**. Its public API and runtime implementation are unchanged. The package retains .NET Standard 2.1 and Newtonsoft.Json 13.0.4 as its sole dependency.

The Release solution build passed with zero warnings/errors, and **40/40 vLLM tests** passed without failures or skips. All **six packages and six symbol packages** passed package validation. **Eleven isolated consumers** compiled or ran successfully, including vLLM consumers for net10.0 execution and netstandard2.1 API compilation. The new execution consumer uses a fake HTTP handler to check model cards, version, health classifications, labelled metrics, authentication and cancellation. Both consumers reject unexpected Core, Abstractions, Providers, RAG or MCP package dependencies.

Publication safety checks and release documentation validation passed for **six packages, 545 Markdown files and 13 languages**. Logs use the `artifacts/release-v8-vllm-` prefix; the test report is `artifacts/test-results/release-v8-vllm/vllm-stable.trx`. The updated Korean publication record is `artifacts/release-v8-vllm-preparation.ko.md`.

This validation used mocked vLLM HTTP responses, with no live server requests. Packages under `artifacts/release-v8-vllm-validation/` are marked **development-validation**. No commit, Git push or NuGet publication was performed.

The final DocFX API reference and site build passed with **zero warnings and errors** (`artifacts/docfx-release-v8-vllm.log`).

## RAG reranker history isolation regression (2026-09-22)

A shared `LlmReranker` now sends independent stateless evaluation requests through the Message overload, preserving the scorer's existing conversation without triggering automatic summarization. Rerankers sharing the same `IAIService` serialize their evaluations with cancellable waiting. Stored conversation summaries are omitted from stateless requests in both common and Anthropic wire-history system-message construction; stateful requests continue to include them.

Added **15 regression cases**: nine RAG cases for sequential and overlapping evaluations, separate rerankers sharing one service, ranking preservation, existing history and summary preservation, failed/cancelled evaluations and cancelled waiters; plus six AI cases covering OpenAI and Anthropic stateless/profile isolation and stateful controls. Tests use real provider request construction with fake HTTP handlers; no paid API or database was called.

The Release solution build passed with zero warnings/errors. **212/212 RAG tests** and **2,468/2,468 AI Unit tests** passed, with no failures or skips. Documentation metadata validation passed for six packages, 545 Markdown files and 13 languages. Reports are `artifacts/test-results/reranker-isolation/rag.trx` and `artifacts/test-results/reranker-isolation/ai-unit.trx`; build/test/documentation logs use the `artifacts/reranker-isolation-` prefix.

The DocFX API reference and site build passed with zero warnings/errors (`artifacts/docfx-reranker-isolation.log`), and the CI test-category validation passed.

This change addresses review finding F01. The other ten findings remain separate work. Package versions are unchanged and the fix is recorded under Unreleased; no commit, push or package publication was performed.

## RAG document identity regression (2026-09-22)

Built-in plain-text and directory loaders now use normalized absolute paths for `Source` and automatic document IDs. Files with the same relative name in different roots remain separate; relative/absolute/dot-segment references to the same normalized path reuse their identity. Explicit `RagDocument.Id`, `AddText(id)` and custom loader source rules are unchanged. The builder compares processed paths case-insensitively on Windows and ordinally elsewhere.

Added **17 regression cases** using real temporary files, `InMemoryVectorStore` and local/counting embeddings. Coverage includes separate roots, stable identity and stale-chunk removal on nonempty updates, preservation of the other root, registration-route equivalence, overlapping file/directory registrations, splitter priority, display metadata and explicit IDs. A filesystem-based case-variant test checks one-build deduplication on Windows and distinct files on case-sensitive filesystems; this local run was on Windows, while Ubuntu CI will exercise the Linux path.

The initial pre-fix run reproduced **10 failures** among the first 16 cases. After the fix, **229/229 RAG tests** passed with no failures or skips. The external report's original F02 reproduction also passed (**1/1**). The Release solution build and final RAG test build passed with zero warnings/errors. Reports are `artifacts/test-results/document-identity/before.trx`, `rag.trx` and `external-f02.trx`; logs use the `artifacts/document-identity-` prefix.

Documentation metadata validation passed for six packages, 545 Markdown files and 13 languages. The DocFX API reference and site build passed with zero warnings/errors (`artifacts/docfx-document-identity.log`).

This change addresses F02. Existing relative-path IDs are not automatically migrated or deleted; the 13-language RAG guide describes rebuilding into a new collection or cleaning up verified old IDs before reindexing. Empty-document replacement (F03) and other review findings remain separate work. No external model API or database was called. Package versions are unchanged and the fix is recorded under Unreleased; no commit, push or publication was performed.

## RAG empty document replacement regression (2026-09-22)

The default RAG storage flow now replaces a successfully processed zero-chunk document with an empty record set filtered by its existing `document_id`. It skips embeddings, removes all previous chunks of that document and preserves other IDs. Cancellation is checked before and after splitting and immediately before persistence. Loader/parser/splitter exceptions and cancellation observed before persistence preserve that document's stored data; rollback after storage starts depends on the vector store. Empty loader lists are not deletion instructions, and custom `onDocumentEmbedded` persistence retains its zero-chunk callback/store behavior.

Added **17 regression cases** covering empty/whitespace updates, repeated clearing and later reindexing, multiple documents, loader and real-file builder paths, custom zero-chunk splitters, no embedding calls, read/parser/split failures, cancellation before/during splitting and after embedding, token propagation to storage, callback ownership, and old-text removal from vector and native hybrid search. **246/246 RAG tests** passed with no failures or skips. The original external F03 reproduction failed before the change and passed afterward.

An isolated localhost `pgvector/pgvector:pg17` container passed **3/3 PostgreSQL integration checks**: clearing all target chunks while preserving another document and excluding the retired text from vector/hybrid search; pre-cancelled update preservation; and transaction rollback after an `AFTER DELETE STATEMENT` trigger deliberately raises an error. These use local embeddings, a disposable `mythosia_rag_review` database, unique validated table names and explicit cleanup. The PostgreSQL suite plus the original reproduction passed **4/4**. The container and its temporary connection file were removed after validation. No model API or cloud database was called.

The Release solution build and reproduction-project build passed with zero warnings/errors. Reports are under `artifacts/test-results/empty-document-update/`: `external-f03-before.trx`, `rag.trx`, and `postgres-and-external.trx`. Build/test logs use `artifacts/empty-document-update-`; PostgreSQL probe source is `artifacts/rag-review-confirmation-20260922/EmptyDocumentPostgresTests.cs` (category `ReviewPostgresF03`, guarded by `MYTHOSIA_REVIEW_PG_CONN`).

Documentation metadata validation passed for six packages, 545 Markdown files and 13 languages. DocFX regenerated the API reference and built the documentation site with zero warnings/errors (`artifacts/docfx-empty-document-update.log`). An independent implementation/documentation review found no further issue in this change.

This addresses F03 only, preserving F01/F02 fixes. Public signatures and package versions are unchanged, and release notes use Unreleased. No commit, push or publication was performed.
