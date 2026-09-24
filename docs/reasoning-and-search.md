# Choose reasoning effort and answer with sources

> Grok 4.7 is an unreleased addition; see [model selection, reasoning and processing speed](providers.md#grok-47).

> GPT-6 Sol/Luna are unreleased additions; see [model selection and requirements](providers.md#gpt-6-sol-luna).

[Claude Opus 5.5](providers.md#claude-opus-55) is an unreleased addition with always-on thinking, default medium effort and omitted display. Explicitly request readable progress; its defaults and model-binding rules differ from Fable 5.1.

For independent settings and reusable variations, use [the request builder](request-building.md). Call `CreateRequest(...)` before `With...`; service-level setters and fluent methods retain their existing behavior.

> These APIs require `Mythosia.AI` 7.1.0 or later, which includes `Mythosia.AI.Abstractions` 3.1.0 or later. RAG examples require `Mythosia.AI.Rag` 7.6.0 or later.

> `CreateRequest` examples require the current working release; they are not available in the earlier 7.1 release that introduced Run and common request features. Earlier packages can keep their existing service overloads.

[Claude Fable 5.1](fable-5-1.md) adds progress updates, turn-scoped instructions, and thinking-binding diagnostics from `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 requires invitation access. Both reject forced tool choice.

For requests where waiting time matters, use [processing speed](request-building.md#inference-speed): `WithSpeed` keeps the model and reasoning effort, while `Processing` reports what the provider actually applied. Fast is a paid option on supported combinations.

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
    .CreateRequest("Outline the migration plan.")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("Review that plan for failure scenarios and recovery steps.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash accept `Low`, `Medium`, and `High` through `WithReasoning`; `Minimal`, `None`, and `CachePreservation.Required` are unsupported. The same completion, streaming, Run, tool-call, and native search paths apply with the existing Google combination limits. See the [Google configuration example](providers.md#google-googleaiservice).

`ReasoningLevel` expresses a requested level, not a fixed token budget or a guarantee of answer quality. Each model accepts its own subset. `Auto` retains the provider's configured/default behavior; it does not mean that unsupported levels are automatically substituted. Provider-specific budget properties remain available for models that expose token budgets instead of named levels.

For a long conversation, changing a top-level effort setting can invalidate a reusable prompt prefix. On a supported model, require the provider's mechanism for changing effort while retaining that prefix:

```csharp
string review = await service
    .CreateRequest("Recheck the assumptions in the previous answer.")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required` is a contract about how the change is sent. It does **not** guarantee a cache hit, free tokens, or lower latency: the provider's cache eligibility, retention and pricing still apply. Unsupported models throw `NotSupportedException` before sending the request. Use the same tracked conversation, model and endpoint; do not truncate or reorder a conversation that contains these updates. Start a new conversation when changing those conditions. Automatic compaction is blocked while a preserved prefix is required.

The accepted cache-preserving effort becomes the conversation's active effort until another explicit change. Ordinary `WithReasoning(level)` applies to its logical request; it does not silently replace that persistent setting. This change happens **between model responses**. It does not change the effort of an already running response and is separate from `run.SteerAsync`, which sends an additional instruction to a supported active run.

## Answer questions that need current information

Enable native web search when the answer should draw on information outside the model's training data:

```csharp
string answer = await service
    .CreateRequest("Search for the latest release announcement and cite the source.")
    .WithWebSearch()
    .GetCompletionAsync();

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
    .CreateRequest("Search our policy documents. What is the cancellation period?")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

For Google, use `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` with a Google service. Stores belong to their provider, account and deployment; an OpenAI store ID cannot be passed to Google. Create the store and upload/index its documents through the provider's API or console before using it here. This API only searches existing stores and does not upload local files.

`CreateRequest(...).With...` stores options on an independent builder. Reusing that builder reuses its captured options for each execution and its tool rounds. Legacy `service.WithReasoning`, `service.WithWebSearch`, and `service.WithFileSearch` retain their concrete service return type and next-logical-request consumption; use those existing helpers when maintaining an `IAIRequestFeatureService` or RAG-wrapper caller. Neither API promises overlapping execution on one service.

## Display progress and retain sources

Use the same options before `StartRunAsync`. The text callback can update the screen while the run retains sources for the finished answer:

```csharp
await using var run = await service
    .CreateRequest("Search the recent announcements and compare the changes.")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` remains available without consuming the stream, with text-only observation, or after the output observation buffer fills. It contains the provider's source references collected during the run, including intermediate responses. `service.LastCitations` (or `GetLastCitations()` through `IAIService`) describes the most recent logical request; retain the run or copy its citation snapshot when displaying multiple answers.

For source events as they arrive, use a single event reader:

```csharp
await using var run = await service
    .CreateRequest("Search and explain the latest changes.")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nSource: {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

Citation fields are nullable when the provider supplies no value. `ResponseId`, `OutputIndex` and `ContentIndex` identify the source response/content part. `StartIndex` and `EndIndex` retain the provider's local offsets and indexing convention; they are **not** offsets into concatenated `(await run.Result).Text`. Do not place citations by blindly indexing the complete answer with these values.

## Check provider support and request scope

| Integrated provider | Named reasoning levels | Cache-preserving change | Web search | File search |
| --- | --- | --- | --- | --- |
| OpenAI | Supported reasoning models; level varies by model | GPT-6 Astra / Sol / Luna Standard, single-agent mode | Supported Responses models | Supported Responses models, existing vector stores |
| Anthropic | Models with native effort control | Supported Opus 5 / 5.5 / Fable 5.1 / Mythos 5.1 with the provider beta | Supported Claude models | No native store adapter; use RAG |
| Google | Gemini 3 levels; Gemini 2.5 retains provider-specific budgets | Unsupported | Supported Gemini text models | Supported Gemini text models, existing file search stores |
| xAI | Grok 4.7 / 4.6: `Auto`, `Low`, `Medium`, `High`, `XHigh` | Unsupported | No common adapter | No common adapter |
| DeepSeek | Flash / V4 Pro: `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max`; provider aliases map to native Low/High/Max | Unsupported | No common adapter | No common adapter |
| Perplexity | `Auto` or model-supported `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max`; no explicit Sonar effort | Unsupported | Agent `web_search` | No common adapter |
| Other services | Existing provider-specific settings remain available; these common options require an adapter | Unsupported by this adapter set | No common adapter | No common adapter |

The adapter checks locally known model, level, transport, and combination constraints before sending; the provider validates model-specific rules that are not known locally. In particular, **Google web search and file search cannot be combined in one request**. The library does not silently remove a feature, lower an effort level, ignore a domain restriction or switch to an external search service. Native tools can coexist with registered client functions where supported; run tool rounds still follow the function policy and `WithMaxRounds`.

`CreateRequest(...).With...` stores options on an independent builder. Reusing that builder reuses its captured options for each execution and its tool rounds. Legacy `service.WithReasoning`, `service.WithWebSearch`, and `service.WithFileSearch` retain their concrete service return type and next-logical-request consumption; use those existing helpers when maintaining an `IAIRequestFeatureService` or RAG-wrapper caller. Neither API promises overlapping execution on one service.

Custom implementations of `IAIService` remain compatible. They opt into this feature surface through `IAIRequestFeatureService`; calling these helpers on an implementation without that capability throws explicitly. Existing completion, streaming and provider-specific configuration APIs remain available. See [Run control](execution-api-transition.md) for cancellation, observation and steering.

Provider protocols: [OpenAI reasoning changes](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [OpenAI tools](https://developers.openai.com/api/docs/guides/tools), [Anthropic effort changes](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Anthropic web search](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Google Search grounding](https://ai.google.dev/gemini-api/docs/google-search), [Google File Search](https://ai.google.dev/gemini-api/docs/file-search).

Perplexity uses the actual selected model to determine effort support and may reject incompatible combinations on the server. `None` is unsupported. Its default web search and preset/profile tools are persistent provider settings; common request options do not switch those defaults off.

Perplexity: [Perplexity Agent API, Search, and Embeddings](perplexity.md).
