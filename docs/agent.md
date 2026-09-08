# Agent (ReAct Loop)

## Why an Agent Loop?

Regular function calling can execute **multiple functions from one model response as an ordered batch** and continue through tool rounds. The agent API packages that mechanism as a goal-oriented ReAct loop with an explicit **step limit**, returning each batch's results to the model until it produces a final answer:

- "Research the top 3 AI companies and compare their stock prices" — requires multiple web searches and stock lookups
- "Find the relevant policy, check the order status, then tell me if I qualify for a refund" — requires chaining different tools in a logical sequence
- The model might need to **retry or refine** a search if the first result is insufficient

`GetCompletionAsync` and `StartRunAsync` already execute the shared model/tool loop. The legacy agent helpers add a per-call round limit and agent-specific error translation; they do not introduce an independent planner or a separate execution engine.

## Run with progress and control

Keep the handle returned by `StartRunAsync` to display or stop a task that uses several tools. Supported models can also accept another instruction while working. See the [Run guide](execution-api-transition.md) for choosing an execution API.

```csharp
// Register functions on service before starting the task.
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Find the policy, check the order, and explain the outcome.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

## Legacy agent compatibility

`RunAgentAsync` and `RunAgentStreamAsync` retain their behavior with `[Obsolete]` warnings. Use the examples below when maintaining or migrating existing calls.

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
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// Legacy RunAgentAsync uses DefaultPolicy and the explicit maxSteps argument.
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "Research and summarize...", maxSteps: 15);
```

Predefined policies:

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Low timeout, fewer rounds — quick tasks
var fastResult = await service.RunAgentAsync(
    "Research and summarize...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Higher timeout, more rounds — deep research
var complexResult = await service.RunAgentAsync(
    "Research and summarize...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## Per-Call Request Context

`RunAgentAsync` and `RunAgentStreamAsync` accept an optional `AIRequestContext` so you can inject a dynamic system message prefix/suffix, reference documents, or replace the goal message — **scoped to a single agent run**, without mutating the service's system message or conversation history.

```csharp
string result = await service.RunAgentAsync(
    goal: "Find the refund policy and check if order #1234 qualifies.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Today's date is {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nAlways cite the policy section you used."
    });
```

The streaming variant takes the same parameter:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Research the top 3 AI companies' stock prices.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"User timezone: {userTz}\n"
    }))
{
    // handle content
}
```

`AIRequestContext` uses `AsyncLocal` for context propagation. This does not make the mutable service, its conversation history, or execution policies safe for overlapping operations. Use separate service instances for independent concurrent tasks.

See [AIRequestContext](request-contexts.md) for the full list of available properties (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Available in Mythosia.AI v6.3.0+.

## How It Works

Each step:

1. LLM receives the goal + conversation history + function definitions
2. If LLM calls a function → execute it, append result to history
3. If LLM returns a text response → loop ends, return that response
4. If step count reaches `maxSteps` → throw `AgentMaxStepsExceededException`
