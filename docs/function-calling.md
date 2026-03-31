# Function Calling

Function calling lets the model invoke your C# code when it needs to retrieve information or perform an action.

## Quick Example

```csharp
var service = new ChatGptService(apiKey, http)
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

[AiFunction("search_products", "Search the product catalog")]
public static string SearchProducts(
    [AiParameter("Search query", required: true)] string query,
    [AiParameter("Maximum results to return")] int limit = 5)
{
    // ... your implementation
    return JsonSerializer.Serialize(results);
}
```

Then register it:

```csharp
service.AddFunction(SearchProducts);
```

## Function Calling Policy

Control when the model is allowed to call functions:

```csharp
using Mythosia.AI.Models.Functions;

// Let the model decide (default)
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// Force the model to always call a function
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// Disable function calling
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
```

## Using FunctionBuilder

Build function definitions programmatically:

```csharp
using Mythosia.AI.Builders;

var fn = FunctionBuilder
    .Create("get_stock_price", "Returns the current stock price")
    .AddParameter("ticker", "Stock ticker symbol", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
