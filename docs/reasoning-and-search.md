# Choose reasoning effort and answer with sources

> These APIs require `Mythosia.AI` 7.1.0 or later, which includes `Mythosia.AI.Abstractions` 3.1.0 or later. RAG examples require `Mythosia.AI.Rag` 7.6.0 or later.

## Why use these options?

Different steps need different kinds of help. A first draft may need a quick answer; reviewing its assumptions may justify more reasoning. A question about today's events needs current information, while a question about your product needs the documents that describe it. Increasing reasoning effort alone does not give a model either source.

Use the common Fluent API to express what the next task needs. The selected provider translates supported options into its native API. Your application can keep `GetCompletionAsync` for a completed answer or use `StartRunAsync` to show progress and control the same task.

| Your task needs | Configure |
| --- | --- |
| A quick draft followed by a more careful review | `WithReasoning(...)` |
| A reasoning change that retains an eligible conversation cache prefix | `WithReasoning(..., cache: CachePreservation.Required)` |
| Current information from the web | `WithWebSearch()` |
| Answers grounded in documents already indexed by the provider | `WithFileSearch(store)` |

Examples assume an initialized service with a supported model. Import `Mythosia.AI.Extensions` and `Mythosia.AI.Models`; stream events also use `Mythosia.AI.Models.Streaming`.

## Move from a quick draft to a careful review

You can spend less reasoning on an outline, then ask the same conversation to examine difficult details:

```csharp
string outline = await service
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("Outline the migration plan.");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("Review that plan for failure scenarios and recovery steps.");
```

`ReasoningLevel` expresses a requested level, not a fixed token budget or a guarantee of answer quality. Each model accepts its own subset. `Auto` retains the provider's configured/default behavior; it does not mean that unsupported levels are automatically substituted. Provider-specific budget properties remain available for models that expose token budgets instead of named levels.

For a long conversation, changing a top-level effort setting can invalidate a reusable prompt prefix. On a supported model, require the provider's mechanism for changing effort while retaining that prefix:

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("Recheck the assumptions in the previous answer.");
```

`Required` is a contract about how the change is sent. It does **not** guarantee a cache hit, free tokens, or lower latency: the provider's cache eligibility, retention and pricing still apply. Unsupported models throw `NotSupportedException` before sending the request. Use the same tracked conversation, model and endpoint; do not truncate or reorder a conversation that contains these updates. Start a new conversation when changing those conditions. Automatic compaction is blocked while a preserved prefix is required.

The accepted cache-preserving effort becomes the conversation's active effort until another explicit change. Ordinary `WithReasoning(level)` applies to its logical request; it does not silently replace that persistent setting. This change happens **between model responses**. It does not change the effort of an already running response and is separate from `run.SteerAsync`, which sends an additional instruction to a supported active run.

## Answer questions that need current information

Enable native web search when the answer should draw on information outside the model's training data:

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("Search for the latest release announcement and cite the source.");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

The provider runs this hosted tool. There is no local function handler to register or execute. Enabling search makes it available to the model; the model may decide that a particular prompt does not require it. Source references are available when the provider returns them.

OpenAI and Anthropic also accept `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`. Google does not expose this allowlist through the integrated tool, so a restricted request is rejected instead of searching the whole web.

## Answer from documents already indexed by the provider

If your application already keeps a provider-hosted document index, use that store to ground answers without implementing your own retrieval round:

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .WithFileSearch(documents)
    .GetCompletionAsync("Search our policy documents. What is the cancellation period?");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

For Google, use `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` with a Google service. Stores belong to their provider, account and deployment; an OpenAI store ID cannot be passed to Google. Create the store and upload/index its documents through the provider's API or console before using it here. This API only searches existing stores and does not upload local files.

Hosted file search and the library's [RAG pipeline](rag.md) solve different setup needs. Choose hosted search when the provider already manages your index. Choose RAG when your application needs control of loaders, splitting, embeddings, retrieval or vector storage. A `RagEnabledService` also forwards `WithReasoning`, `WithWebSearch` and `WithFileSearch` to its final answer; its internal query rewrite does not inherit these options. RAG retrieval references remain on `RagProcessedQuery`, separate from provider-supplied `AICitation` sources.

## Display progress and retain sources

Use the same options before `StartRunAsync`. The text callback can update the screen while the run retains sources for the finished answer:

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "Search the recent announcements and compare the changes.",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` remains available without consuming the stream, with text-only observation, or after the output observation buffer fills. It contains the provider's source references collected during the run, including intermediate responses. `service.LastCitations` (or `GetLastCitations()` through `IAIService`) describes the most recent logical request; retain the run or copy its citation snapshot when displaying multiple answers.

For source events as they arrive, use a single event reader:

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "Search and explain the latest changes.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nSource: {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

Citation fields are nullable when the provider supplies no value. `ResponseId`, `OutputIndex` and `ContentIndex` identify the source response/content part. `StartIndex` and `EndIndex` retain the provider's local offsets and indexing convention; they are **not** offsets into concatenated `run.Result`. Do not place citations by blindly indexing the complete answer with these values.

## Check provider support and request scope

| Integrated provider | Named reasoning levels | Cache-preserving change | Web search | File search |
| --- | --- | --- | --- | --- |
| OpenAI | Supported reasoning models; level varies by model | GPT-6 Astra Standard, single-agent mode | Supported Responses models | Supported Responses models, existing vector stores |
| Anthropic | Models with native effort control | Supported Opus 5 / Fable 5.1 / Mythos 5.1 with the provider beta | Supported Claude models | No native store adapter; use RAG |
| Google | Gemini 3 levels; Gemini 2.5 retains provider-specific budgets | Unsupported | Supported Gemini text models | Supported Gemini text models, existing file search stores |
| Other services | Existing provider-specific settings remain available; these common options require an adapter | Unsupported by this adapter set | No common adapter | No common adapter |

Model, level, transport and combination checks happen before the request is sent. In particular, **Google web search and file search cannot be combined in one request**. The library does not silently remove a feature, lower an effort level, ignore a domain restriction or switch to an external search service. Native tools can coexist with registered client functions where supported; run tool rounds still follow the function policy and `WithMaxRounds`.

Fluent methods retain the service's concrete type and copy their input options. Non-null components merge for the next logical request, including its tool rounds and structured-output repair calls, then are consumed. Search is not enabled for later unrelated calls; add `WithWebSearch` or `WithFileSearch` again when needed. A started run keeps its captured settings. As with other mutable service configuration, do not change settings or start overlapping requests on the same service while a request is running.

Custom implementations of `IAIService` remain compatible. They opt into this feature surface through `IAIRequestFeatureService`; calling these helpers on an implementation without that capability throws explicitly. Existing completion, streaming and provider-specific configuration APIs remain available. See [Run control](execution-api-transition.md) for cancellation, observation and steering.

Provider protocols: [OpenAI reasoning changes](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [OpenAI tools](https://developers.openai.com/api/docs/guides/tools), [Anthropic effort changes](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Anthropic web search](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Google Search grounding](https://ai.google.dev/gemini-api/docs/google-search), [Google File Search](https://ai.google.dev/gemini-api/docs/file-search).
