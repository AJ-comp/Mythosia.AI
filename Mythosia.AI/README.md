# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides a unified interface for various AI models with **multimodal support**, **function calling**, **reasoning streaming**, and **advanced streaming capabilities**.

### Supported Providers

- **OpenAI** — GPT-5.2 / 5.2 Codex / 5.1 / 5 (with reasoning), GPT-4.1, GPT-4o, o3
- **Anthropic** — Claude Opus 4.6 / 4.5 / 4.1 / 4, Sonnet 4.5 / 4, Haiku 4.5 / 3.5
- **Google** — Gemini 3 Flash/Pro Preview, Gemini 2.5 Pro/Flash/Flash-Lite
- **DeepSeek** — Chat and Reasoner models
- **Perplexity** — Sonar with web search and citations

## 📚 Documentation

- **[Basic Usage Guide](https://github.com/AJ-comp/Mythosia/wiki)** — Getting started with text queries, streaming, image analysis, and more
- **[Advanced Features](https://github.com/AJ-comp/Mythosia/wiki/Advanced-Features)** — Function calling, policies, and enhanced streaming
- **[Release Notes](RELEASE_NOTES.md)** — Full version history and migration guides

## Installation

```bash
dotnet add package Mythosia.AI
```

For advanced LINQ operations with streams:

```bash
dotnet add package System.Linq.Async
```

For RAG (Retrieval-Augmented Generation) support:

```bash
dotnet add package Mythosia.AI.Rag
```

This adds `.WithRag()` to any `AIService`, enabling document-based context augmentation. See the [Mythosia.AI.Rag README](https://github.com/AJ-comp/Mythosia/tree/master/Mythosia.AI.Rag) for full usage details.

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

## Quick Start

```csharp
// OpenAI GPT
var gptService = new ChatGptService(apiKey, httpClient);
var response = await gptService.GetCompletionAsync("Hello!");

// Anthropic Claude
var claudeService = new ClaudeService(apiKey, httpClient);
var response = await claudeService.GetCompletionAsync("Hello!");

// Google Gemini
var geminiService = new GeminiService(apiKey, httpClient);
var response = await geminiService.GetCompletionAsync("Hello!");
```

## GPT-5 Family Configuration

GPT-5 family models support **type-safe reasoning configuration** with per-model enums.

### Reasoning Effort (Per-Model Enums)

Each GPT-5 variant has its own enum to ensure only valid options are available at compile time.

```csharp
var gptService = (ChatGptService)service;

// GPT-5: Gpt5Reasoning (Auto/Minimal/Low/Medium/High)
gptService.WithGpt5Parameters(
    reasoningEffort: Gpt5Reasoning.High,
    reasoningSummary: ReasoningSummary.Concise);

// GPT-5.1: Gpt5_1Reasoning (Auto/None/Low/Medium/High) + Verbosity
gptService.WithGpt5_1Parameters(
    reasoningEffort: Gpt5_1Reasoning.Medium,
    verbosity: Verbosity.Low,
    reasoningSummary: ReasoningSummary.Concise);

// GPT-5.2: Gpt5_2Reasoning (Auto/None/Low/Medium/High/XHigh) + Verbosity
gptService.WithGpt5_2Parameters(
    reasoningEffort: Gpt5_2Reasoning.XHigh,
    verbosity: Verbosity.High);
```

`Auto` uses the model-appropriate default (e.g., Medium for GPT-5, None for GPT-5.1/5.2, Medium for GPT-5.2 Pro/Codex).

### Reasoning Summary

All GPT-5 family models support `ReasoningSummary` enum (`Auto` / `Concise` / `Detailed`). Set to `null` to disable.

## Gemini Configuration

### Gemini 3 — ThinkingLevel

```csharp
var geminiService = new GeminiService(apiKey, httpClient);
geminiService.ChangeModel(AIModel.Gemini3FlashPreview);

// GeminiThinkingLevel enum: Auto / Minimal / Low / Medium / High
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;  // Auto = model default (High)
```

### Gemini 2.5 — ThinkingBudget

```csharp
geminiService.ChangeModel(AIModel.Gemini2_5Pro);
geminiService.ThinkingBudget = 8192;  // -1 = dynamic (default), 0 = disable
```

## Function Calling

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

## Structured Output

Deserialize LLM responses directly into C# POCOs with automatic JSON recovery.

### Basic Usage

```csharp
// Define your POCO
public class WeatherResponse
{
    public string City { get; set; }
    public double Temperature { get; set; }
    public string Condition { get; set; }
}

// Get typed result — schema is auto-generated and sent to the LLM
var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");
Console.WriteLine($"{result.City}: {result.Temperature}°C, {result.Condition}");
```

### Auto-Recovery Retry

When the LLM returns invalid JSON, a correction prompt is automatically sent asking the model to fix its output. This is **not** a network retry — it's an output quality/format correction loop.

```csharp
// Configure service-level retry count (default: 2)
service.StructuredOutputMaxRetries = 3;

// On final failure, StructuredOutputException is thrown with rich diagnostics:
// - FirstRawResponse, LastRawResponse
// - ParseError, AttemptCount, SchemaJson, TargetTypeName
```

### Per-Call Structured Output Policy

Override retry behavior for a single request without changing service defaults:

```csharp
// Custom policy — applies only to this call, then auto-cleared
var result = await service
    .WithStructuredOutputPolicy(new StructuredOutputPolicy { MaxRepairAttempts = 5 })
    .GetCompletionAsync<MyDto>(prompt);

// Preset: no retry (1 attempt only)
var result = await service
    .WithNoRetryStructuredOutput()
    .GetCompletionAsync<MyDto>(prompt);

// Preset: strict mode (up to 3 retries = 4 total attempts)
var result = await service
    .WithStrictStructuredOutput()
    .GetCompletionAsync<MyDto>(prompt);
```

| Preset | MaxRepairAttempts | Description |
|--------|-------------------|-------------|
| `Default` | `null` (service default) | Uses `StructuredOutputMaxRetries` |
| `NoRetry` | `0` | Single attempt, no retry |
| `Strict` | `3` | Up to 3 correction retries |

### Streaming Structured Output

Stream text chunks in real-time to the UI while getting a final deserialized object with auto-repair:

```csharp
var run = service.BeginStream(prompt)
    .WithStructuredOutput(new StructuredOutputPolicy { MaxRepairAttempts = 2 })
    .As<MyDto>();

// Optional: observe chunks in real-time
await foreach (var chunk in run.Stream(cancellationToken))
{
    Console.Write(chunk); // UI display
}

// Final deserialized result (waits for stream + parse/repair)
MyDto dto = await run.Result;
```

- **`Result` works without `Stream()`** — just `await run.Result` internally consumes the stream and parses
- **`Stream()` is single-use** — second call throws `InvalidOperationException`
- **`Result` waits for stream completion** — even if awaited mid-stream, it won't resolve early
- **Repair retries are non-streaming** — correction prompts use `GetCompletionAsync()` for efficiency

### Collection Support (`List<T>`, `T[]`)

Both `GetCompletionAsync<T>()` and streaming support collection types — no wrapper DTO needed:

```csharp
// Non-streaming: get a list directly
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "Extract all entities from this document...");

// Streaming: observe chunks + get list result
var run = service.BeginStream(prompt).As<List<ItemDto>>();
await foreach (var chunk in run.Stream()) Console.Write(chunk);
List<ItemDto> items = await run.Result;
```

`List<T>`, `T[]`, `IReadOnlyList<T>` are all supported. JSON array schema is auto-generated from the element type.

## Enhanced Streaming

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

## Reasoning Streaming

GPT-5 and Gemini 3 models support streaming reasoning (thinking) content.

```csharp
await foreach (var content in service.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"[Thinking] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

## Service Support

| Service | Function Calling | Streaming | Reasoning | Notes |
|---------|-----------------|-----------|-----------|--------|
| **OpenAI GPT-5.2 / 5.2 Pro / 5.2 Codex** | ✅ | ✅ | ✅ | Per-model reasoning enums + verbosity |
| **OpenAI GPT-5.1** | ✅ | ✅ | ✅ | Reasoning + verbosity control |
| **OpenAI GPT-5 / Mini / Nano** | ✅ | ✅ | ✅ | Reasoning streaming + summary |
| **OpenAI GPT-4.1 / GPT-4o** | ✅ | ✅ | — | Full function support |
| **OpenAI o3 / o3-pro** | ✅ | ✅ | ✅ | Advanced reasoning |
| **Claude Opus 4.6 / 4.5 / 4.1 / 4** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Sonnet 4.6 / 4.5 / 4** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Claude Haiku 4.5** | ✅ | ✅ | ✅ | Extended thinking + tool use |
| **Gemini 3 Flash/Pro** | ✅ | ✅ | ✅ | ThinkingLevel + thought signatures |
| **Gemini 2.5 Pro/Flash** | ✅ | ✅ | ✅ | ThinkingBudget control |
| **DeepSeek** | ❌ | ✅ | ✅ | Reasoner model streaming |
| **Perplexity** | ❌ | ✅ | — | Web search + citations |

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

For detailed migration instructions, see the **[Release Notes](RELEASE_NOTES.md)**.

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
- Verify the model supports function calling (see Service Support table above)

**Q: Function calling is too slow?**
- Adjust the policy timeout: `service.DefaultPolicy.TimeoutSeconds = 30`
- Use `FunctionCallingPolicy.Fast` for simple operations
- Consider using streaming for better perceived performance

**Q: How to debug function execution?**
- Enable logging: `service.DefaultPolicy.EnableLogging = true`
- Check the console output for round-by-round execution details
- Use `StreamOptions.FullOptions` to see function call metadata

**Q: Can I use functions with streaming?**
- Yes! Functions work seamlessly with streaming
- Use `StreamOptions.WithFunctions` to see function execution in real-time