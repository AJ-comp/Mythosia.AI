# Keep each request’s settings independent

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
