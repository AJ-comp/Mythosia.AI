# Agentic RAG

Agentic RAG registers a `RagStore` as a callable search tool inside the agent loop. Instead of running retrieval exactly once for every user message, the agent decides when to search, what query to use, whether to search again, and when to combine document results with other tools.

## Why Agentic RAG?

Standard RAG is a fixed retrieve-then-answer flow. That is simple and fast, but it can be too rigid when:

- The question requires multiple searches across different topics.
- The first search result is insufficient and the system should try a better query.
- The question does not need document retrieval at all.
- The final answer depends on documents plus live data from APIs or other tools.
- The application needs per-request permission filters or step-level diagnostics for each search.

Agentic RAG handles those cases by letting `RunAgentAsync(...)` or `RunAgentStreamAsync(...)` use RAG as one tool among the agent's registered functions.

## Quick Start

Build the `RagStore` once, register it with `WithAgenticRag(...)`, then run the agent:

```csharp
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("Summarise the refund policy.");
```

By default, `WithAgenticRag(...)` registers a tool named `search_documents`. The agent calls that tool automatically whenever it needs document context, then uses the returned excerpts to produce the final answer.

## Streaming Agentic RAG

Use `RunAgentStreamAsync(...)` when the UI should receive streamed text while still observing tool calls and tool results from the agent loop:

```csharp
service.WithAgenticRag(ragStore);

await foreach (var content in service.RunAgentStreamAsync(
    "Summarise the refund policy and mention the key eligibility rules.",
    maxSteps: 10))
{
    if (content.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Searching docs via: {content.Metadata["function_name"]}");
    }
    else if (content.Type == StreamingContentType.Text)
    {
        Console.Write(content.Content);
    }
}
```

`RunAgentStreamAsync(...)` is useful for chat UIs because the user can see progress while the agent searches documents, calls other tools, and writes the final response.

## Combining with Other Tools

Agentic RAG works best when document search is one tool beside live APIs, calculators, workflow actions, or domain-specific functions:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Look up an order status by order ID.",
           ("order_id", "The order ID to look up.", required: true),
           async id => await orderApi.GetStatusAsync(id));

var answer = await service.RunAgentAsync(
    "Order #12345: am I eligible for a refund based on the current policy?");
```

In this example, the agent can search documents for the refund rules, call the order API for live order data, and combine both pieces of context in the final answer.

## Custom Tool Description

The tool description strongly affects when the model chooses to call RAG. The default description is domain-neutral and tells the agent to use self-contained queries, but production apps should usually provide a domain-specific description:

```csharp
service.WithAgenticRag(
    ragStore,
    toolDescription:
        "Search internal HR policies, product manuals, and compliance documents. " +
        "Call this tool whenever company-specific policy or product information is needed.");
```

Avoid vague descriptions such as "Search documents" when your app has a clear document domain. Good descriptions tell the agent what the index contains and when the tool should be used.

## Custom Tool Name

The default tool name is `search_documents`. You can customize it when the service registers multiple search-like tools or when a more domain-specific name helps the agent choose correctly:

```csharp
service.WithAgenticRag(
    ragStore,
    toolName: "search_private_docs",
    toolDescription: "Search documents that the current user is allowed to access.");
```

Use a stable, descriptive, snake_case name. If you also register tracing, pass the same tool name to `WithAgenticRagTracing(...)`.

## Per-Call Query Options

Use the `queryOptions` overload when each agent search step needs fresh `RagQueryOptions`. This is the usual place to apply tenant filters, user permissions, storage scopes, dynamic `TopK`, or other retrieval policy decisions:

```csharp
service.WithAgenticRag(
    ragStore,
    queryOptions: ctx => new RagQueryOptions
    {
        StoreFilter = new VectorFilter()
            .Where("tenant", currentTenantId)
            .Where("storage_id", currentStorageId),
        FinalFilter = new RagFilter
        {
            TopK = ctx.Query.Contains("exact policy", StringComparison.OrdinalIgnoreCase)
                ? 8
                : 5
        }
    },
    toolDescription: "Search only the documents the current user is allowed to access.");
```

The callback receives an `AgenticRagQueryContext`:

- `ToolName`: the registered tool currently being executed.
- `Query`: the self-contained search query generated by the agent for this step.

Use `_ => ...` when the same options apply to every search in the request. Use `ctx.Query` or `ctx.ToolName` when filtering or retrieval settings should vary by search step.

## Structured Tracing

`WithAgenticRagTracing(...)` registers trace observers for Agentic RAG search executions. It is intentionally separate from `WithAgenticRag(...)`:

- `WithAgenticRag(...)` registers the RAG search tool and resolves per-call query options.
- `WithAgenticRagTracing(...)` registers observers for search traces produced by that tool.

```csharp
var traces = new List<AgenticRagSearchTrace>();

service
    .WithAgenticRag(
        ragStore,
        queryOptions: _ => new RagQueryOptions
        {
            StoreFilter = new VectorFilter()
                .Where("tenant", currentTenantId)
                .Where("storage_id", currentStorageId)
        },
        toolDescription: "Search only the documents the current user is allowed to access.")
    .WithAgenticRagTracing(trace =>
    {
        traces.Add(trace);
    });
```

Each `AgenticRagSearchTrace` contains:

- `ToolName`: the tool that produced the trace.
- `Query`: the self-contained query executed for this search step.
- `QueryOptions`: the resolved per-call `RagQueryOptions`, if any.
- `Result`: the structured `RagProcessedQuery` when the search succeeded.
- `Result.References`: final selected references returned to the agent.
- `Result.RetrievalCandidates` and `Result.RerankedCandidates`: candidates before final selection.
- `Result.Diagnostics`: applied retrieval settings and elapsed timings.
- `Succeeded` and `Exception`: whether the tool execution completed and what failed when it did not.
- `HasReferences`: whether the successful result contained references.

Trace observers are best for reference panels, audit logs, search-quality analysis, debugging retrieval settings, and capturing failures from permission lookup or vector search.

Trace callbacks are observability helpers. Exceptions thrown from a trace observer are swallowed so they do not break the agent run.

## Tracing with Custom Tool Names

Tracing is registered by service instance and tool name. If you customize the Agentic RAG tool name, pass the same name to tracing:

```csharp
service
    .WithAgenticRag(ragStore, toolName: "search_private_docs")
    .WithAgenticRagTracing(
        trace => traces.Add(trace),
        toolName: "search_private_docs");
```

If the names do not match, the search still works, but the observer will not receive traces for that tool.

## Query Rewriting Behavior

`QueryRewriter` is intentionally bypassed in Agentic RAG. The agent itself writes a self-contained search query before calling the RAG tool, so running a separate query-rewriting step would be redundant and could distort the agent's intent.

The `RagStore` still respects its configured retrieval strategy and pipeline options. Vector search, hybrid search, reranking, final selection, context building, `StoreFilter`, and diagnostics all continue to work as part of `RagStore.QueryAsync(...)`.

## How It Differs from Standard RAG

| | Standard RAG | Agentic RAG |
| --- | --- | --- |
| Search timing | Every message | Agent decides |
| Query formulation | `QueryRewriter` | Agent-generated self-contained query |
| Number of searches | Once per turn | One or more as needed |
| Tool combination | Document retrieval only | Any registered function or tool |
| Per-step filters | Request options | `queryOptions` callback per tool call |
| Observability | RAG result/diagnostics | `AgenticRagSearchTrace` per search step |
| Setup | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync(...)` or `RunAgentStreamAsync(...)` |

## When to Choose Which

- Use **standard RAG** when every question is document-based, single-topic, and low latency matters more than tool autonomy.
- Use **Agentic RAG** when questions may span multiple topics, require document search plus live data, need iterative retrieval, or must apply per-search permission filters and diagnostics.

## Practical Guidance

- Keep `maxSteps` high enough for the agent to search, inspect results, and retry when needed.
- Write the tool description as a usage policy, not just a label.
- Use per-call `StoreFilter` for tenant isolation and permission boundaries.
- Capture traces when you need citations, reference panels, audit logs, or retrieval diagnostics.
- Use custom tool names only when they make tool selection clearer.
