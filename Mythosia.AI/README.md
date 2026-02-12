# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides a unified interface for various AI models with **multimodal support**, **function calling**, **reasoning streaming**, and **advanced streaming capabilities**, including **OpenAI GPT-5.2/5.1/5/GPT-4o**, **Anthropic Claude 3/4**, **Google Gemini**, **DeepSeek**, and **Perplexity Sonar**.

## 📚 Documentation

- **[Basic Usage Guide](https://github.com/AJ-comp/Mythosia/wiki)** - Getting started with text queries, streaming, image analysis, and more
- **[Advanced Features](https://github.com/AJ-comp/Mythosia/wiki/Advanced-Features)** - Function calling, policies, and enhanced streaming (v3.0.0)
- **[Release Notes](RELEASE_NOTES.md)** - Full version history and migration guides

### 🚀 Latest: v4.0.0 — Architecture overhaul (config moved to AIService) + v3.2.0 — GPT-5.1/5.2 support

👉 **[Full Release Notes & Migration Guides](RELEASE_NOTES.md)**

## Installation

```bash
dotnet add package Mythosia.AI
```

For advanced LINQ operations with streams:

```bash
dotnet add package System.Linq.Async
```

## Function Calling (New in v3.0.0)

### Quick Start with Functions

```csharp
// Define a simple function
var service = new ChatGptService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => $"The weather in {location} is sunny, 22°C"
    );

// AI will automatically call the function when needed
var response = await service.GetCompletionAsync("What's the weather in Seoul?");
// Output: "The weather in Seoul is currently sunny with a temperature of 22°C."
```

### Attribute-Based Function Registration

```csharp
public class WeatherService
{
    [AiFunction("get_current_weather", "Gets the current weather for a location")]
    public string GetWeather(
        [AiParameter("The city name", required: true)] string city,
        [AiParameter("Temperature unit", required: false)] string unit = "celsius")
    {
        // Your implementation
        return $"Weather in {city}: 22°{unit[0]}";
    }
}

// Register all functions from a class
var weatherService = new WeatherService();
var service = new ChatGptService(apiKey, httpClient)
    .WithFunctions(weatherService);
```

### Advanced Function Builder

```csharp
var service = new ChatGptService(apiKey, httpClient)
    .WithFunction(FunctionBuilder.Create("calculate")
        .WithDescription("Performs mathematical calculations")
        .AddParameter("expression", "string", "The math expression", required: true)
        .AddParameter("precision", "integer", "Decimal places", required: false, defaultValue: 2)
        .WithHandler(async (args) => 
        {
            var expr = args["expression"].ToString();
            var precision = Convert.ToInt32(args.GetValueOrDefault("precision", 2));
            // Calculate and return result
            return await CalculateAsync(expr, precision);
        })
        .Build());
```

### Multiple Functions with Different Types

```csharp
var service = new ChatGptService(apiKey, httpClient)
    // Parameterless function
    .WithFunction(
        "get_time",
        "Gets the current time",
        () => DateTime.Now.ToString("HH:mm:ss")
    )
    // Two-parameter function
    .WithFunction(
        "add_numbers",
        "Adds two numbers",
        ("a", "First number", true),
        ("b", "Second number", true),
        (double a, double b) => $"The sum is {a + b}"
    )
    // Async function
    .WithFunctionAsync(
        "fetch_data",
        "Fetches data from API",
        ("endpoint", "API endpoint", true),
        async (string endpoint) => await httpClient.GetStringAsync(endpoint)
    );

// The AI will automatically use the appropriate functions
var response = await service.GetCompletionAsync(
    "What time is it? Also, what's 15 plus 27?"
);
```

### Function Calling Policies

```csharp
// Pre-defined policies
service.DefaultPolicy = FunctionCallingPolicy.Fast;     // 30s timeout, 10 rounds
service.DefaultPolicy = FunctionCallingPolicy.Complex;   // 300s timeout, 50 rounds
service.DefaultPolicy = FunctionCallingPolicy.Vision;    // 200s timeout, for image analysis

// Custom policy
service.DefaultPolicy = new FunctionCallingPolicy
{
    MaxRounds = 25,
    TimeoutSeconds = 120,
    MaxConcurrency = 5,
    EnableLogging = true  // Enable debug output
};

// Per-request policy override
var response = await service
    .WithPolicy(FunctionCallingPolicy.Fast)
    .GetCompletionAsync("Complex task requiring functions");

// Inline policy configuration
var response = await service
    .BeginMessage()
    .AddText("Analyze this data")
    .WithMaxRounds(5)
    .WithTimeout(60)
    .SendAsync();
```

### Function Calling with Streaming

```csharp
// Stream with function calling support
await foreach (var content in service.StreamAsync(
    "What's the weather in Seoul and calculate 15% tip on $85",
    StreamOptions.WithFunctions))
{
    if (content.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {content.Metadata["function_name"]}");
    }
    else if (content.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function completed: {content.Metadata["status"]}");
    }
    else if (content.Type == StreamingContentType.Text)
    {
        Console.Write(content.Content);
    }
}
```

### Disabling Functions Temporarily

```csharp
// Disable functions for a single request
var response = await service
    .WithoutFunctions()
    .GetCompletionAsync("Don't use any functions for this");

// Or use the async helper
var response = await service.AskWithoutFunctionsAsync(
    "Process this without calling functions"
);
```

## Enhanced Streaming (v3.0.0)

### Stream Options

```csharp
// Text only - fastest, no overhead
await foreach (var chunk in service.StreamAsync("Hello", StreamOptions.TextOnlyOptions))
{
    Console.Write(chunk.Content);
}

// With metadata - includes model info, timestamps, etc.
await foreach (var content in service.StreamAsync("Hello", StreamOptions.FullOptions))
{
    if (content.Metadata != null)
    {
        Console.WriteLine($"Model: {content.Metadata["model"]}");
    }
    Console.Write(content.Content);
}

// Custom options
var options = new StreamOptions()
    .WithMetadata(true)
    .WithFunctionCalls(true)
    .WithTokenInfo(false)
    .AsTextOnly(false);

await foreach (var content in service.StreamAsync("Query", options))
{
    // Process based on content.Type
    switch (content.Type)
    {
        case StreamingContentType.Text:
            Console.Write(content.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine($"Calling: {content.Metadata["function_name"]}");
            break;
        case StreamingContentType.Completion:
            Console.WriteLine($"Total length: {content.Metadata["total_length"]}");
            break;
    }
}
```

## Service-Specific Function Support

| Service | Function Calling | Streaming Functions | Notes |
|---------|-----------------|-------------------|--------|
| **OpenAI GPT-5.2 / 5.2 Pro** | ✅ Full | ✅ Full | Best for complex, coding, agentic tasks |
| **OpenAI GPT-5.1** | ✅ Full | ✅ Full | Reasoning with verbosity control |
| **OpenAI GPT-5 / Mini / Nano** | ✅ Full | ✅ Full | Reasoning streaming + summary support |
| **OpenAI GPT-4o** | ✅ Full | ✅ Full | Best support, all features |
| **OpenAI GPT-4.1** | ✅ Full | ✅ Full | Full function support |
| **OpenAI o3** | ✅ Full | ✅ Full | Advanced reasoning with functions |
| **Claude 3/4** | ✅ Full | ✅ Full | Tool use via native API |
| **Gemini** | 🔜 Coming Soon | 🔜 Coming Soon | Support planned for future update |
| **DeepSeek** | ❌ | ❌ | Not yet available |
| **Perplexity** | ❌ | ❌ | Web search focused |

## Complete Examples

### Building a Weather Assistant

```csharp
public class WeatherAssistant
{
    private readonly ChatGptService _service;
    private readonly HttpClient _httpClient;

    public WeatherAssistant(string apiKey)
    {
        _httpClient = new HttpClient();
        _service = new ChatGptService(apiKey, _httpClient)
            .WithSystemMessage("You are a helpful weather assistant.")
            .WithFunction(
                "get_weather",
                "Gets current weather for a city",
                ("city", "City name", true),
                GetWeatherData
            )
            .WithFunction(
                "get_forecast",
                "Gets weather forecast",
                ("city", "City name", true),
                ("days", "Number of days", false),
                GetForecast
            );
        
        // Configure function calling behavior
        _service.DefaultPolicy = new FunctionCallingPolicy
        {
            MaxRounds = 10,
            TimeoutSeconds = 30,
            EnableLogging = true
        };
    }

    private string GetWeatherData(string city)
    {
        // In real implementation, call weather API
        return $"{{\"city\":\"{city}\",\"temp\":22,\"condition\":\"sunny\"}}";
    }

    private string GetForecast(string city, int days = 3)
    {
        // In real implementation, call forecast API
        return $"{{\"city\":\"{city}\",\"forecast\":\"{days} days of sun\"}}";
    }

    public async Task<string> AskAsync(string question)
    {
        return await _service.GetCompletionAsync(question);
    }

    public async IAsyncEnumerable<string> StreamAsync(string question)
    {
        await foreach (var content in _service.StreamAsync(question))
        {
            if (content.Type == StreamingContentType.Text && content.Content != null)
            {
                yield return content.Content;
            }
        }
    }
}

// Usage
var assistant = new WeatherAssistant(apiKey);

// Functions are called automatically
var response = await assistant.AskAsync("What's the weather in Tokyo?");
// AI calls get_weather("Tokyo") and responds naturally

// Streaming also supports functions
await foreach (var chunk in assistant.StreamAsync(
    "Compare weather in Seoul and Tokyo for the next 5 days"))
{
    Console.Write(chunk);
}
```

### Math Tutor with Step-by-Step Solutions

```csharp
var mathTutor = new ChatGptService(apiKey, httpClient)
    .WithSystemMessage("You are a math tutor. Always explain your reasoning.")
    .WithFunction(
        "calculate",
        "Performs calculations",
        ("expression", "Math expression", true),
        (string expr) => {
            // Using a math expression evaluator
            var result = EvaluateExpression(expr);
            return $"Result: {result}";
        }
    )
    .WithFunction(
        "solve_equation",
        "Solves equations step by step",
        ("equation", "Equation to solve", true),
        (string equation) => {
            var steps = SolveWithSteps(equation);
            return JsonSerializer.Serialize(steps);
        }
    );

// The AI will use functions and explain the process
var response = await mathTutor.GetCompletionAsync(
    "Solve the equation 2x + 5 = 13 and verify the answer"
);
// Output includes step-by-step solution with verification
```

## Migration Guides

For detailed migration instructions, see the **[Release Notes](RELEASE_NOTES.md)**:

- [v3.2.x → v4.0.0](RELEASE_NOTES.md#migration-guide-from-v32x-to-v400) — Configuration moved from ChatBlock to AIService
- [v2.x → v3.0.0](RELEASE_NOTES.md#migration-guide-from-v2x-to-v300) — Function calling & enhanced streaming

## Best Practices

1. **Function Design**: Keep functions focused and simple. Complex logic should be broken into multiple functions.

2. **Error Handling**: Functions should return meaningful error messages that the AI can understand.

3. **Performance**: Use appropriate policies for your use case (Fast for simple tasks, Complex for detailed analysis).

4. **Streaming**: Use `TextOnlyOptions` for best performance when metadata isn't needed.

5. **Testing**: Test function calling with various prompts to ensure robust behavior.

## Troubleshooting

**Q: Functions aren't being called when expected?**
- Ensure functions are registered with clear, descriptive names and descriptions
- Check that `EnableFunctions` is true on the service
- Verify the model supports function calling (GPT-4, Claude 3+, Gemini)

**Q: Function calling is too slow?**
- Adjust the policy timeout: `service.DefaultPolicy.TimeoutSeconds = 30`
- Use `FunctionCallingPolicy.Fast` for simple operations
- Consider using streaming for better perceived performance

**Q: How to debug function execution?**
- Enable logging: `service.DefaultPolicy.EnableLogging = true`
- Check the console output for round-by-round execution details
- Use `StreamOptions.FullOptions` to see function call metadata

**Q: Can I use functions with streaming?**
- Yes! Functions work seamlessly with streaming in v3.0.0
- Use `StreamOptions.WithFunctions` to see function execution in real-time