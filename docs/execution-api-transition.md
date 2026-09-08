# Control ongoing AI tasks with Run

> These APIs require `Mythosia.AI` 7.1.0 or later, which includes `Mythosia.AI.Abstractions` 3.1.0 or later. RAG examples require `Mythosia.AI.Rag` 7.6.0 or later.

## Why control a task while it is running?

A report can take several document searches, API calls, and writing steps to finish. During that time, a user may want to see progress, stop the work, or add a requirement such as “Only include this year's data.” Applications need a way to connect those actions to the task that is already running.

Run gives that task a handle you can keep in your application. For example, a chat screen can display incoming text, show when a tool is being used, connect a Stop button to cancellation, and send an additional instruction when the model supports it. All of these actions refer to the same execution.

| What your application needs | What to use |
| --- | --- |
| Receive a completed answer without controlling ongoing work | Keep using `GetCompletionAsync`, including typed and RAG overloads. |
| Display text as it arrives and collect it when work finishes | Start a run with `onText`, then await `run.Result`. |
| Show tool activity or await asynchronous output handling | Read events from `run.StreamAsync()`. |
| Let the user stop ongoing work | Call `run.Cancel()` on the retained handle. |
| Add a requirement before the task finishes | Check `run.CanSteer`, then use `run.SteerAsync(...)` on a supported model. |

`StartRunAsync` starts one model task and returns an `AIRun`. The task continues whether or not you observe its output. Use the same handle for streaming, the collected result, cancellation, and supported mid-turn steering. `GetCompletionAsync`, including typed and RAG overloads, remains a public convenience API for final-result callers.

## Display text with a callback

For a chat screen or console, showing the first text as soon as it arrives lets the user follow a longer answer while it is being written.

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Read the documents and write a report.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

string answer = await run.Result;
```

`onText` is an optional `Action<string>` attached before work starts. It receives text in order; it does not execute tools. Omit it when only the result is needed. A callback exception cancels the run and faults `Result`. Do not pass an `async` lambda to `onText`: it would become `async void`, whose work and errors cannot be awaited by the run. Use the event stream for asynchronous delivery. Callbacks do not automatically marshal to your UI thread.

`Result` is the concatenation of the run's text events, including intermediate text between tool calls and text produced before a steering update. It is not a second model request or a freshly rewritten answer. Final-only callers can continue to use `GetCompletionAsync` when its existing completion semantics are preferred.

## Read text, tool, and usage events

When an answer needs current web information or provider-hosted documents, add [reasoning and search options](reasoning-and-search.md) before starting the run. Source events use `StreamingContentType.Citation`; `run.Citations` retains sources even without an event reader.

When a task searches documents or calls a business API, text alone may not explain the wait. Read typed events to display tool activity alongside the answer and record usage when the provider supplies it.

```csharp
await using var run = await service.StartRunAsync(
    "Search the documents and explain the result.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[Calling a tool]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[Tool result received]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[Total tokens: {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = await run.Result;
```

`run.StreamAsync()` accepts an optional observation cancellation token and no prompt. It observes the task already started by `StartRunAsync`. Registered function handlers execute inside the library; never run a tool again in response to its display event. Text display options do not disable the run's registered tools.

The startup callback and `run.StreamAsync()` can observe the same run together; the event stream supports one reader. For example, display text in `onText` and process only tool events in the stream to avoid displaying text twice. Up to 1,024 unread events are buffered, even when a callback is attached. A late reader receives the buffered events from the start while they still fit; if the limit is exceeded, stream observation fails explicitly while the callback, execution, and `Result` continue. Do not rely on the stream as an unlimited replay log. Awaiting `Result` never requires draining the event stream.

## Asynchronous output

For asynchronous output handling, await the operation inside the reader instead of using an asynchronous `onText` callback:

```csharp
using var writer = new StreamWriter("report.txt");
await using var run = await service.StartRunAsync(
    "Write a report.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = await run.Result;
```

## Cancel and dispose

- Breaking out of `await foreach` or cancelling the token passed only to `run.StreamAsync(token)` stops observation; the task continues.
- `run.Cancel()`, the token passed to `StartRunAsync`, and disposing an active run cancel execution.
- `await using` ensures `DisposeAsync()` waits for the producer and provider cleanup. Tools without cancellation support can take time to finish; disposal does not undo completed actions.
- A service permits one active `StartRunAsync` task. An overlapping start is rejected. Use a separate service for independent concurrent tasks, and do not mix legacy calls or change service settings while a run is active.

The run captures its input and pending per-request policy before background execution. Built-in text, image, and audio content and media byte arrays are copied. Custom `MessageContent` subclasses retain their identity and must remain unchanged until the run finishes.

The captured `FunctionCallingPolicy.TimeoutSeconds` sets one deadline for run preparation and all model/tool rounds together. Expiry reports `AIServiceException`; user cancellation cancels the result. Cleanup still waits for handlers that do not support cancellation.

## Send another instruction while work is running

Suppose a user starts a project plan and then realizes it must fit a two-week schedule. Steering lets the application submit that new requirement while the model is still working. It is useful for corrections and scope changes discovered during a longer task. For a new question after the task has finished, start the next request normally.

```csharp
await using var run = await service.StartRunAsync(
    "Draft a project plan.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// Call this from the UI's additional-instruction handler while the run is active.
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("This run does not support steering.");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = await run.Result;
```

Mid-turn steering is available for GPT-6 Astra over the Responses WebSocket connection. Other providers and unsupported models can use normal runs, but `CanSteer` is false and steering reports unsupported behavior instead of silently creating an ordinary next turn. `CanSteer` is not a guarantee that the run will still be active when a later call is made.

Astra runs open a dedicated socket; the supplied `HttpClient` and its message handlers continue to serve HTTP calls and do not intercept this socket. Custom transports can override `OpenAIService.ConnectRunWebSocketAsync`.

A successful `SteerAsync` means the server accepted the input into its queue, not that the model has already applied it. Continue observing the same run or awaiting its result through the continuation. Already-delivered text and completed actions are not undone, and tools that have started are not cancelled merely because steering was submitted. The library handles continuation and tool-result correlation on the same connection. See OpenAI's [mid-turn steering guide](https://developers.openai.com/api/docs/guides/steering) and [WebSocket mode](https://developers.openai.com/api/docs/guides/websocket-mode). Connection-local queued input does not survive a disconnect by assumption; do not blindly resubmit an accepted instruction.

## Tool tasks and legacy agent methods

Questions such as “Check the refund policy and this order's status” require more than one source. Register the document search and order lookup tools, and let the model choose the necessary calls. A round limit bounds how long it can keep requesting tools before it must finish or report an error.

Normal function calling already supports repeated model/tool rounds. `StartRunAsync` uses the same registered functions and execution policies; no separate agent mode, planner, or `WithAgentic` switch is required.

`RunAgentAsync` and `RunAgentStreamAsync` remain callable but now carry `[Obsolete]` warnings. Their existing signatures, default `maxSteps = 10`, and legacy max-step error behavior are retained during migration. Replace new call sites with:

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Find the policy, check the order, and explain the outcome.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

The general `FunctionCallingPolicy.MaxRounds` default is 20, so specify 10 when preserving the legacy agent limit. `WithMaxRounds` configures a one-request policy override; it does not change `DefaultPolicy`. Configure it before starting work. Legacy agent methods instead clone the current default policy and apply their per-call `maxSteps`. A new run uses the common execution error contract rather than promising the legacy `AgentMaxStepsExceededException`/`PartialResponse` translation. Keep a legacy call until its exception handling has been migrated when that contract matters.

## RAG, MCP, and package boundaries

- `RagEnabledService.StartRunAsync` supports string or `Message` input, `onText`, per-query `RagQueryOptions`, `streamOptions`, and cancellation. It performs retrieval before the underlying run, preserves image/audio content and metadata, keeps original input in conversation history, and sends augmented text through the request context. That augmentation is anchored to the original user query, so later tool results and steering inputs are not replaced by the original RAG prompt. Steering the returned run updates the model; it does not automatically repeat RAG retrieval.
- `WithAgenticRag` continues to register a search tool. Run that tool through `StartRunAsync`; the model can issue later searches as needed. MCP registration through `WithMcpServerAsync` also remains unchanged. Dispose shared MCP connections separately from runs that use them.
- `IAIRunService` is an optional capability in `Mythosia.AI.Abstractions`; `IAIService` gains no new mandatory members. A custom service must implement `IAIRunService` to support RAG run startup. Unsupported services are rejected before RAG indexing begins.
- RAG retains its dependency on Abstractions, and separately packaged providers retain their public completion overrides and accessible provider extension points. No vector-store, document-loader, or server-administration API is deprecated by this change.

## Compatibility and the next major release

| API | Current status |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Public and supported, including interface, provider, and RAG variants. |
| `StartRunAsync` / `AIRun` | Common execution and control API. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Obsolete warnings; existing behavior retained for compatibility. |
| Input-taking `service.StreamAsync` and RAG `StreamAsync` | Still callable in this minor update; planned public withdrawal in the next major release. |
| `run.StreamAsync()` | Output observation for an existing task; no request input. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | Existing typed streaming API retained; its output-only `Stream()` is not a legacy service request method. |

The next major transition changes the public streaming entry points, preserving execution and necessary provider hooks. Making a public method private or protected still breaks source and binary callers even when its body is retained. Helpers such as message chains, one-off calls, summarization, query rewriting, and reranking are not deprecated merely because they use existing execution methods.
