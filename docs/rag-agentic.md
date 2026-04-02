# Agentic RAG

## Why Agentic RAG?

In standard RAG, every user message triggers exactly **one** retrieval. The system searches, builds context, and generates a response — no matter what. This works fine for simple questions, but falls short when:

- The question requires **multiple searches** across different topics (e.g. "Compare the refund policy for hardware vs software products")
- The first search result is **insufficient** and the system should refine and try again
- Some questions **don't need retrieval at all** (e.g. "Summarise our conversation so far")
- The answer depends on combining **document retrieval with live data** from APIs

Agentic RAG solves all of these. Instead of a fixed retrieve-then-answer pipeline, the **agent decides autonomously** — when to search, what to search for, whether to search again, and when to call other tools — all inside a ReAct loop.

## Quick Start

Register the `RagStore` as a tool with `WithAgenticRag`, then hand off to `RunAgentAsync`:

```csharp
// Build the index once
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// Register RAG as a tool and run the agent
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("Summarise the refund policy.");
```

The agent calls `search_documents` automatically whenever it needs document context, then synthesises the final answer from the retrieved excerpts.

## Combining with Other Tools

Agentic RAG shines when combined with additional tools — the agent selects the right tool for each sub-task:

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "Look up an order status by order ID.",
           ("order_id", "The order ID to look up.", required: true),
           async id => await orderApi.GetStatusAsync(id));

// The agent searches documents for policy AND calls the API for live order data
var answer = await service.RunAgentAsync(
    "Order #12345 — am I eligible for a refund based on the current policy?");
```

In this example, the agent autonomously:

1. Searches documents for the refund policy
2. Calls the order API to get the status of order #12345
3. Combines both pieces of information to produce a final answer

## Custom Tool Description

The tool description controls when the agent decides to invoke RAG. Tailor it to your domain for more accurate tool selection:

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "Search internal HR policies, product manuals, and compliance documents. " +
        "Call this tool whenever company-specific policy or product information is needed.");
```

A vague description like "Search documents" may cause the agent to call RAG too often or not often enough. Be specific about **what kind of information** the documents contain.

## How It Differs from Standard RAG

| | Standard RAG | Agentic RAG |
| --- | --- | --- |
| Search timing | Every message | Agent decides |
| Query formulation | QueryRewriter | Agent itself |
| Number of searches | Once per turn | One or more as needed |
| Tool combination | Not applicable | Any registered tool |
| Setup | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **Note:** `QueryRewriter` is intentionally bypassed in Agentic RAG. The agent formulates its own self-contained search query, so a separate rewriting step would be redundant and could distort the agent's intent.

## When to Choose Which

- **Standard RAG** — every question is document-based, single-topic, and you want minimal latency
- **Agentic RAG** — questions span multiple topics, require combining document + live data, or need iterative retrieval
