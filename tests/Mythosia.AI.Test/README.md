# Mythosia.AI tests

The `Unit` category contains deterministic tests for CI. The `Live` category sends requests to external providers and requires credentials. CI and package publishing continue to select `TestCategory=Unit`; live tests run explicitly.

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
