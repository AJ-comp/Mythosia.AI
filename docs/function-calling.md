# Function Calling

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

For ordinary calls, parallel handlers may finish out of order, but Mythosia preserves the provider's original call order in the `FunctionCallResultBatch`. Once a validated batch starts, its handlers run to completion so conversation history cannot contain calls without matching results; registered handlers do not currently receive a `CancellationToken`.

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

Mythosia sends `async: true` for GPT-6 Astra through Responses. Unsupported models and APIs omit that field and wait for the same handler's result, leaving your `AllowAsync` setting unchanged. The provider must also return an async call (`FunctionCall.IsAsync`); enabling the permission does not guarantee async execution.

`WithFunctionAsync` only accepts a .NET asynchronous handler, and `FunctionExecutionMode.Parallel` controls local handler scheduling. Neither enables this permission. `AllowAsync` lets the model continue before a function result arrives. `FunctionExecutionMode` still controls ordinary calls. Opted-in async jobs can overlap even in `Sequential` mode and share a separate pending-job limit set by `MaxConcurrency`.

Pending tool jobs belong to the `GetCompletionAsync`, existing `service.StreamAsync`, or `StartRunAsync` execution that started them. Results retain their original call IDs. A successful completion or run `Result` waits for pending results to be processed. Controlling execution through `AIRun`, as shown in the [Run guide](execution-api-transition.md), does not detach tool jobs from that execution.

When async tools are used, `GetCompletionAsync` returns the intermediate independent text and the final text accumulated in order, after the request finishes. `StreamAsync` emits text as it arrives across those rounds.

Handlers do not receive cancellation tokens, so cancellation, timeout, or execution errors wait for already-started handlers during cleanup. Early disposal of the existing input-taking `service.StreamAsync` also cleans up execution; ending a `run.StreamAsync()` reader only stops observation. Call `run.Cancel()` or dispose the run to stop its execution. See the [official API guide](https://developers.openai.com/api/docs/guides/async-tool-calling) for the protocol.

Streaming starts handlers after complete function calls and a valid response boundary have been received, then can continue another model round while async jobs run. Incomplete call events do not trigger execution. If the model returns no new calls while jobs remain pending, Mythosia waits for results before resuming. Automatic context-overflow summarization retries are disabled while calls are pending to avoid dropping unfinished calls from history.
