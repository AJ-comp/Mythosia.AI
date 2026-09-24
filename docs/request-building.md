# Keep each request’s settings independent

> Grok 4.7: Requires Mythosia.AI 8.1.0 / Abstractions 4.1.0. [model selection, reasoning and processing speed](providers.md#grok-47)

A summary may need a low temperature, while a creative draft needs a higher one. Preparing the draft must not silently change the settings of a summary you already prepared. Use `CreateRequest` when different calls need different settings, or when you want to reuse a base request with several variations.

Need the completed answer together with usage and sources? `await run.Result` now returns an `AIRunResult` snapshot; use `result.Text` for the string. No stream reader is required. This is an API change in Mythosia.AI 8.0.0; `GetCompletionAsync` and typed `StructuredStreamRun<T>.Result` keep their existing return types. [Run result and migration](execution-api-transition.md#run-result).

Need only the completed answer and a Stop button? Pass `cancellationToken` to `GetCompletionAsync`. Use Run for progress events or supported steering. See [completion cancellation](completions.md#completion-cancellation).

> `CreateRequest` examples require Mythosia.AI 8.0.0 / Abstractions 4.0.0; they are not available in the earlier 7.1 release that introduced Run and common request features. Earlier packages can keep their existing service overloads.

## Before: the service is shared

The existing service-level `WithTemperature` changes the service and returns the same instance. Both variables below refer to that instance; the later value applies to both. These legacy methods remain available for configuring service defaults.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Explain this document."); // 0.8
```

## After: branch from an independent request

`CreateRequest` captures the service defaults. Every builder `With...` returns a new builder without changing the original. Each execution uses its own captured settings; it does not temporarily overwrite the service defaults.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Explain this document.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Uses 0.2; the creative request and service defaults stay unchanged.
```

Keep the returned builder. Calling `basis.WithTemperature(0.2f);` and discarding the result leaves `basis` unchanged.

Builder methods validate values instead of silently clamping them: `WithTemperature` accepts 0–2, `WithTopP` 0–1, and penalties −2–2; non-finite values are rejected. Token, round, concurrency, and specified timeout limits must be positive. Invalid inputs throw `ArgumentException` / `ArgumentOutOfRangeException`. The legacy service temperature helper keeps its clamping behavior.

## What the objects do

`AIService` owns the provider connection, defaults, and existing conversation state. Public `Mythosia.AI.Builders.AIRequestBuilder` provides the fluent API. Internal `AIRequest` carries the captured input and settings into execution. You do not call `Build()` or receive an `AIRequest` as the answer: `GetCompletionAsync()` returns `Task<string>` and `StartRunAsync()` returns `Task<AIRun>`.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Use the same configuration for a controllable run

Choose `GetCompletionAsync()` for the completed answer. Choose `StartRunAsync()` to display progress or steer a supported active run. The prompt belongs to `CreateRequest`; the builder’s execution methods do not take another prompt. `run.StreamAsync()` continues to observe that run, and `run.SteerAsync(...)` keeps its existing model-dependent behavior.

```csharp
await using var run = await service
    .CreateRequest("Explain this document.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Local tools can return objects from `Task<T>` / `ValueTask<T>` and accept an injected `CancellationToken`. `run.Cancel()` or the startup token reaches cooperative tools; stopping only a stream reader does not. Exceptions are failures; queued calls are skipped on cancellation, and cleanup still awaits started tools that ignore it. See [tool results, errors, and cancellation](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Reuse profiles and context

`WithProfile` captures an existing `AIRequestProfile`; `WithContext` captures an `AIRequestContext`. Their original objects can later change without altering the prepared request. Builder methods cover sampling, system instructions, stateless mode, function policy, and supported reasoning/web/file search. Provider capability checks still apply; a builder does not make an unsupported option available.

`WithFunctions(params FunctionDefinition[])` adds copied definitions to the request. With `Mythosia.AI.Extensions`, `WithFunctions(toolInstance)` and `WithStaticFunctions<T>()` also support the existing attributed functions. Configure registration before `CreateRequest` for a service default, or after it for one request. Pending legacy next-call feature/policy options are captured and consumed by `CreateRequest`; reuse the returned builder when those options should be reused.

```csharp
var request = service
    .CreateRequest("Rewrite this question for search.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nKeep the original meaning."
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## What is captured, and what is still shared

Common and provider defaults are captured when `CreateRequest` runs. Later default changes do not alter that prepared request. Supplied built-in message content, supported option collections, profiles, contexts, and policies are copied. Function handlers, dynamic context callbacks, and custom message-content objects retain their references: keep custom content unchanged, and remember that delegates can observe external state. Dynamic context callbacks still run at execution time.

After capture, you can dispose the original `JsonDocument` or edit original `JsonNode` values without changing JSON values stored in request metadata or function-call arguments; each execution receives its own copy. A cyclic tool-schema `Items` chain or one exceeding 64 levels throws `ArgumentException` during capture (`CreateRequest` or `WithFunctions`), so an invalid schema fails before execution instead of exhausting the process stack.

Copying also preserves array dimensions and starting indices, and the key-comparison rules of standard `Dictionary<,>`, `SortedDictionary<,>` and `SortedList<,>` containers. A case-insensitive key lookup therefore stays case-insensitive in the request. The empty `default(JsonElement)` value (`Undefined`) stays unchanged. Unknown custom metadata objects retain their references; their owner remains responsible for keeping them unchanged or coordinating access.

Standard `ReadOnlyCollection<T>` and `ReadOnlyDictionary<TKey, TValue>` values keep their types inside typed arrays and dictionaries. Supported backing collections are copied while preserving read-only views, shared references and cycles. `Hashtable` and non-generic `SortedList` also retain their key-comparison rules.

A builder is not a separate conversation. The service’s active conversation at execution time remains the source of history; creating a builder does not freeze that history. Stateful calls still update the shared conversation. Use `WithStatelessMode()` when a call should neither read nor accumulate history. The existing one-active-run guard remains: independent request settings do not promise parallel execution on the same service. Use separate services for independent concurrent conversations.

## Existing callers and extensions

`GetCompletionAsync` remains supported, as do the existing service entry points. `BeginMessage()` / `MessageChain` retain their mutable message-construction behavior, while their execution uses the request path. Use `CreateRequest` when branching and reusing settings. The builder API belongs to `AIService` and its provider implementations; no member is added to `IAIService`. An abstraction-only or RAG wrapper caller keeps its existing profile/context and execution APIs.

[Choose model controls using shared capability definitions](model-capabilities.md).

<a id="inference-speed"></a>

## Choose processing speed for the current task

A customer waiting for an answer may justify premium low-latency processing, while a background report can use ordinary processing. `WithSpeed` selects that processing mode while keeping the same model and reasoning effort. Requires Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ProviderDefault` adds no override and preserves existing service/provider settings; a project default may already be Fast. `Standard` explicitly requests ordinary processing. `Fast` opts into the provider’s premium low-latency mode and can incur additional charges. Keep each returned builder: the three branches below are independent, and the base request is unchanged.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

Inspect `GetSpeedSupport(InferenceSpeed.Fast)` before offering the option. `StandardSpeed` and `FastSpeed` also expose Supported, Unsupported or Unknown. A local Supported result does not verify account entitlement, capacity or a latency guarantee. Explicit Standard/Fast requests with unsupported or unknown support fail rather than silently changing model or effort. Use `ProviderDefault` to keep the existing route.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` retains immutable `AIProcessingInfo` records even when no stream is read. `RequestIndex` starts at 1 and identifies provider inference attempts, including server continuations; it is neither a tool-round counter nor an HTTP-request count. Tool follow-ups, retries and format repairs can create additional records. `AppliedSpeed` is null when the server reports no recognized mode, including failed attempts. `RawAppliedMode` and `ResponseId` retain supplied attribution. `IsDowngraded` is true only for requested Fast explicitly reported as Standard; false does not establish that Fast was applied.

For an ordinary completion, inspect `AIService.LastProcessing` immediately after the call; a later logical request replaces that view. Captured records remain immutable. The service extension configures the next logical request, including its tool rounds; it does not set a permanent default. Auxiliary summaries, internal query rewriting and internal profiles do not inherit the main request’s speed override or mix their observations into its records.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

These fields report the provider’s processing mode, not measured tokens per second. OpenAI, xAI and Google may downgrade on the server; Mythosia does not automatically retry at a different speed. Anthropic fast mode requires access and is limited to the direct Claude API; switching speed can invalidate prompt-cache reuse. Gemini Developer API priority requires Tier 2/3 eligibility. Provider/model/API support and pricing remain separate from the common method. This option does not configure image generation, embeddings or native Batch APIs. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

When you hold an `IAIService`, use `GetLastProcessing()` from `Mythosia.AI.Extensions`; it reads the optional `IAIProcessingInfoService` and returns an empty list when diagnostics are unavailable. `IAIService` gains no required member. For RAG, `RagEnabledService.WithSpeed(...)` configures the next answer after retrieval, and `LastProcessing` describes that answer; internal query rewriting remains separate. Run results expose the same `Processing` records.

The Fast allowlist implemented here is explicit. Check Standard support separately with `GetSpeedSupport(InferenceSpeed.Standard)`. Unlisted models, third-party endpoints and OpenAI-compatible providers do not automatically inherit paid processing support.

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — other known Claude models, including Sonnet 5 | `speed` and fast-mode beta omitted | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

For these other Claude models, Standard uses the existing ordinary request. If the server omits processing metadata, `AppliedSpeed` remains null; the library does not infer Standard from the request.
