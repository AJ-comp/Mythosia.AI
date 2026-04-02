# Agent (ReAct Loop)

The agent loop lets the model autonomously pursue a goal by repeatedly calling functions and incorporating their results until it reaches a final answer — without you writing the loop yourself.

## Basic Usage

Register functions, then call `RunAgentAsync` with a goal:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Search the web for information",
        ("query", "Search query", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Get current stock price",
        ("ticker", "Stock ticker symbol", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "What is the current stock price of the top 3 AI companies?",
    maxSteps: 10
);

Console.WriteLine(result);
```

The model will call functions as needed, observe results, and decide the next step — until it produces a final text response.

## maxSteps

`maxSteps` caps the number of LLM→function call rounds. If the agent hasn't finished within the limit, `AgentMaxStepsExceededException` is thrown:

```csharp
try
{
    string result = await service.RunAgentAsync("Research and summarize...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponse contains whatever the model produced so far
    Console.WriteLine($"Stopped early: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Control the per-round behavior of the agent loop:

```csharp
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// Or via extension methods:
service.WithMaxRounds(15).WithTimeout(60);
```

Predefined policies:

```csharp
service.WithFastPolicy();    // Low timeout, fewer rounds — quick tasks
service.WithComplexPolicy(); // Higher timeout, more rounds — deep research
```

## How It Works

Each step:
1. LLM receives the goal + conversation history + function definitions
2. If LLM calls a function → execute it, append result to history
3. If LLM returns a text response → loop ends, return that response
4. If step count reaches `maxSteps` → throw `AgentMaxStepsExceededException`
