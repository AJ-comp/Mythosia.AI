# Function Calling

> GPT-6 Sol/Luna are unreleased additions; see [model selection and requirements](providers.md#gpt-6-sol-luna).

Need only the completed answer and a Stop button? Pass `cancellationToken` to `GetCompletionAsync`. Use Run for progress events or supported steering. See [completion cancellation](completions.md#completion-cancellation).

For independent settings and reusable variations, use [the request builder](request-building.md). Call `CreateRequest(...)` before `With...`; service-level setters and fluent methods retain their existing behavior.

## Why Function Calling?

LLMs can only generate text — they cannot check the weather, query a database, or call an API on their own. **Without** function calling, you'd have to parse the model's intent manually:

```csharp
// ❌ Without function calling — manual intent parsing
var reply = await service.GetCompletionAsync("What's the weather in Seoul?");
// reply = "I'd need to check a weather service for that information."

// You have to figure out the user wants weather, extract "Seoul", call the API yourself
if (reply.Contains("weather"))
{
    var city = ExtractCity(reply); // fragile regex or keyword matching
    var weather = await weatherApi.GetAsync(city);
    // Now ask again with the weather data injected...
}
```

This is brittle, doesn't scale, and requires you to anticipate every possible user intent. **With** function calling, the model decides **when** to call your code and **what arguments** to pass:

```csharp
// ✅ With function calling — the model handles intent + extraction
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("What's the weather in Seoul?");
// The model calls get_weather("Seoul, Korea"), gets the result, and answers naturally.
```

You define **what** your code can do; the model figures out **when** and **how** to use it.

## Quick Example

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => $"The weather in {location} is sunny, 22°C"
    );

var response = await service.GetCompletionAsync("What's the weather in Seoul?");
// The model calls get_weather("Seoul, Korea") and incorporates the result.
```

## Defining Functions with Attributes

For more complex functions, use `[AiFunction]` and `[AiParameter]` attributes:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Search the product catalog")]
    public string SearchProducts(
        [AiParameter("Search query", required: true)] string query,
        [AiParameter("Maximum results to return")] int limit = 5)
    {
        // ... your implementation
        return JsonSerializer.Serialize(results);
    }
}
```

Then register it:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Function Calling Policy

Registered functions are available to the model by default. Disable them globally, or force one named function when a provider supports forced tool selection:

```csharp
using Mythosia.AI.Models.Functions;

// Let the model decide (default)
service.FunctionCallMode = FunctionCallMode.Auto;
service.ForceFunctionName = null;

// Force a specific registered function
service.ForceFunctionName = "search_products";

// Disable function calling
service.FunctionCallMode = FunctionCallMode.None;
```

[Claude Fable 5.1](fable-5-1.md) adds progress updates, turn-scoped instructions, and thinking-binding diagnostics from `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 requires invitation access. Both reject forced tool choice.

`FunctionCallingPolicy` controls the multi-round loop and local handler scheduling; it does not select whether the provider may call a function. Calls from one assistant response execute sequentially by default. Opt in to bounded parallel execution only for independent, thread-safe handlers:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    MaxRounds = 20,
    TimeoutSeconds = 120,
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 4
};
```

For ordinary calls, parallel handlers may finish out of order, but Mythosia preserves the provider's original call order in the `FunctionCallResultBatch`. Cancellation skips calls that have not started and supplies matching cancellation results. Already-started handlers receive cancellation when supported and are awaited so conversation history cannot contain calls without matching results. See [tool results and cancellation](#tool-execution-contract).

## Bulk Registration from a Class

Register all `[AiFunction]`-annotated methods from an object at once:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // scans instance methods with [AiFunction]
```

For static methods:

```csharp
service.WithStaticFunctions<MyTools>();  // scans static methods with [AiFunction]
```

## Async Function Handlers

All `WithFunction` overloads have `WithFunctionAsync` counterparts that accept `Func<..., Task<string>>`:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Fetches data from an external API",
    ("url", "The URL to fetch", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

Supports 0 to 3 parameters, same as the sync variants.

## Temporarily Disabling Functions

Disable function calling for a single request without removing registrations:

```csharp
// Extension method — returns result with functions disabled
string answer = await service.AskWithoutFunctionsAsync("Just answer directly");

// Or toggle the property
service.WithoutFunctions();  // sets FunctionsDisabled = true
```

## Using FunctionBuilder

Build function definitions programmatically:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Returns the current stock price")
    .AddParameter("ticker", "string", "Stock ticker symbol", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

<a id="tool-execution-contract"></a>

## Return objects from async tools and stop work when cancelled

A file or database tool often returns an object after asynchronous I/O. You should be able to return that object directly, and a Stop button should reach the operation that is still running. Synchronous object returns were already supported; this update makes asynchronous returns consistent and records exceptions as failures.

Before: an async tool had to serialize its result itself. Returning `Task<FileResult>` lost the value and produced `"Success"` instead.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: return the object and pass the injected cancellation token to the I/O operation. No new result wrapper or adapter is required in application code.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

`[AiFunction]` registration supports ordinary objects, `Task<T>`, and `ValueTask<T>`: non-string values are serialized to JSON. `string`, `Task<string>`, and `ValueTask<string>` remain plain text, without extra JSON quotes. `Task` and `ValueTask` with no result are awaited. Existing synchronous object returns keep working. A null return becomes `"Done"`; completed `Task` / `ValueTask` methods with no result become `"Success"`.

The library also recognizes awaitables at runtime: `Task<T>` returned as `Task` or `object`, and `ValueTask<T>` returned as `object`, are awaited and serialized using the same rules. Each `ValueTask` is consumed only once.

The library supplies a `CancellationToken` parameter; it is excluded from the model-facing argument schema. Register these methods with `WithFunctions(...)` or `WithStaticFunctions<T>()`. The same registration works on the service and request builder.

`async void` tool methods are rejected at registration. Return `Task` or `ValueTask` instead so execution can await completion, observe errors, and finish cancellation cleanup.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Read report.txt and summarize it.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, cancellation of the token passed to `StartRunAsync`, or run disposal reaches cancellation-aware local tools. Functions must use the token: cancellation cannot forcibly interrupt code that ignores it. Calls still waiting to start are skipped and receive cancellation results; already-started functions are awaited so call/result history stays paired. Cancelling the execution leaves the run cancelled and does not start another model round.

If a cancellation callback throws during failed run startup or MCP connection disposal, cleanup still attempts to dispose the session or transport. The original failure and any cleanup failures are preserved, together in an `AggregateException` when necessary. Concurrent asynchronous calls to `McpConnection.DisposeAsync()` wait for the same cleanup. The transport is closed before waiting for the read loop to exit, allowing reads that need connection closure to finish.

To avoid leaving late tool calls waiting during shutdown, once connection disposal starts, new `InitializeAsync`, `RefreshToolsAsync` and `CallToolAsync` operations are rejected with `ObjectDisposedException`. If a response has the matching request ID but a malformed body, it is skipped while the request remains pending: a later valid response, caller cancellation or connection cleanup can still settle the call. If the reader has already stopped because the server closed the stream or a transport read failed, new operations fail with `McpException` instead of waiting for a reply that cannot arrive; create a new connection to continue.

Let actual failures throw an exception. The executor records them as `FunctionCallResult.IsError = true` instead of a successful `"Error: ..."` string. A string deliberately returned by your function remains a normal result. Cancellation is recorded with both `IsCancelled = true` and `IsError = true`.

For programmatic registration, use the two-argument `WithHandler` overload:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Read a text file")
    .AddParameter("path", "string", "File path", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

The existing one-argument string handlers remain supported. Direct definitions can set `HandlerWithCancellation` (`Func<Dictionary<string, object>, CancellationToken, Task<string>>`). Setting `Handler` or `HandlerWithCancellation` replaces the same handler; they do not register two executions. Low-level handlers still return strings; automatic object serialization belongs to method registration.

This is cancellation and return handling for your local .NET functions. It does not require the provider-native `AllowAsync` feature. Stopping only the `run.StreamAsync(token)` reader stops observation, not the run. See the [Run guide](execution-api-transition.md) and the [provider protocol](https://developers.openai.com/api/docs/guides/async-tool-calling).

## Async Tool Calling

A slow external lookup does not always prevent useful work. While checking the weather, for example, the model can explain general packing advice that does not depend on the forecast. Async tool calling allows that independent work to continue and incorporates the lookup result when it is ready. Decisions that depend on the result still need the actual tool output.

GPT-6 Astra support and async tool calling are available from `Mythosia.AI` 7.1.0, with shared types in `Mythosia.AI.Abstractions` 3.1.0.

`FunctionDefinition.AllowAsync` defaults to `false`. Set it to `true`, or call `FunctionBuilder.WithAsync()`, only for functions whose execution may overlap further model work. `WithAsync(false)` turns the permission off. The same function definition and handler can be reused across providers.

Attribute registration supports the same permission: `[AiFunction("lookup", "Look up data", AllowAsync = true)]`.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Returns a demo weather snapshot for Seoul")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Check the demo Seoul weather. While waiting, list three packing essentials.");
```

Mythosia sends `async: true` for GPT-6 Astra / Sol / Luna through Responses. Unsupported models and APIs omit that field and wait for the same handler's result, leaving your `AllowAsync` setting unchanged. The provider must also return an async call (`FunctionCall.IsAsync`); enabling the permission does not guarantee async execution.

`WithFunctionAsync` only accepts a .NET asynchronous handler, and `FunctionExecutionMode.Parallel` controls local handler scheduling. Neither enables this permission. `AllowAsync` lets the model continue before a function result arrives. `FunctionExecutionMode` still controls ordinary calls. Opted-in async jobs can overlap even in `Sequential` mode and share a separate pending-job limit set by `MaxConcurrency`.

Pending tool jobs belong to the `GetCompletionAsync`, existing `service.StreamAsync`, or `StartRunAsync` execution that started them. Results retain their original call IDs. A successful completion or run `Result` waits for pending results to be processed. Controlling execution through `AIRun`, as shown in the [Run guide](execution-api-transition.md), does not detach tool jobs from that execution.

When async tools are used, `GetCompletionAsync` returns the intermediate independent text and the final text accumulated in order, after the request finishes. `StreamAsync` emits text as it arrives across those rounds.

Local tools can return objects from `Task<T>` / `ValueTask<T>` and accept an injected `CancellationToken`. `run.Cancel()` or the startup token reaches cooperative tools; stopping only a stream reader does not. Exceptions are failures; queued calls are skipped on cancellation, and cleanup still awaits started tools that ignore it. See [tool results, errors, and cancellation](function-calling.md#tool-execution-contract).

Streaming starts handlers after complete function calls and a valid response boundary have been received, then can continue another model round while async jobs run. Incomplete call events do not trigger execution. If the model returns no new calls while jobs remain pending, Mythosia waits for results before resuming. Automatic context-overflow summarization retries are disabled while calls are pending to avoid dropping unfinished calls from history.

Perplexity: [Control research and tools](perplexity.md).
