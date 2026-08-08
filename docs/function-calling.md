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

Parallel handlers may finish out of order, but Mythosia preserves the provider's original call order in the `FunctionCallResultBatch`. Once a validated batch starts, its handlers run to completion so conversation history cannot contain calls without matching results; registered handlers do not currently receive a `CancellationToken`.

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
