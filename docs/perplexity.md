# Perplexity: grounded answers, search, and embeddings

Use Perplexity when an answer must reflect recent information and readers need sources they can check. `PerplexityService` calls the Agent API, while independent search and embeddings let you build retrieval around your own answer model.

## Choose the job first

A current answer, a list of web pages, and vectors for your own document index solve different problems. Choose the component that owns the work instead of calling an answer model for every retrieval task.

| Need | Component |
| --- | --- |
| A researched answer with sources | `PerplexityService` |
| Ranked web pages for another model or your UI | `PerplexitySearchClient` |
| Vectors for independent passages in ordinary RAG | `PerplexityEmbeddingProvider` |
| Vectors that retain neighbouring chunks from the same document | `PerplexityContextualizedEmbeddingProvider` |

Install `Mythosia.AI`; the embedding examples also require `Mythosia.AI.Rag`. Supply your API key and an application-owned `HttpClient`. The examples use `apiKey`, `httpClient`, and `cancellationToken` from your application.

## Answer with an Agent preset

A preset selects a maintained combination of model, instructions, tools, effort, and budgets. Start with `Fast` for a quick lookup, `Low` for everyday research, `Medium` for multi-step comparison, or `High` / `XHigh` for deeper work. `WideResearch` targets broad research; use background execution when a task is expected to run for longer. These are presets, not model IDs.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "Compare the latest approaches to battery recycling and cite the sources.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Use `GetCompletionAsync` when you only need the answer, service `StreamAsync` for existing streaming code, or `StartRunAsync` to observe and cancel a live execution. `(await run.Result).Text` concatenates emitted answer text. `run.Citations` and `LastCitations` keep sources even when you do not read citation events. Reasoning events contain only provider-exposed material and depend on the selected model.

`AIRunResult.RequestedModel` is the single explicit model sent in the request, captured at startup, including a provider model override. It is `null` when a preset, profile, or server-side model routing selects the model without a single explicit model field (for example, a Perplexity `Models` list). This is independent of the actual response model in `Model`.

## Control research and tools

`WithPerplexityOptions(...)` sets persistent service options; each logical request captures a copy. Common `WithReasoning(...)` and `WithWebSearch(...)` apply to the next logical request, including client-tool rounds and typed-output repair. Internal RAG query rewriting does not inherit the final answer's search settings.

`UsePreset(...)` is a shortcut for selecting a preset. A preset/profile chooses its own model; `ModelOverride` explicitly replaces it. `DisableWebSearch` only removes the adapter's default tool and cannot promise to disable a preset's built-in search. Agent effort accepts `Minimal`, `Low`, `Medium`, `High`, `XHigh`, or `Max` when supported; `None` is rejected and direct Sonar rejects explicit effort. Internal `DisableReasoning` uses an available low effort or omits the setting, without promising reasoning is off.

| Option | Use it when |
| --- | --- |
| `Preset` / `ModelOverride` | Choose a research setup, or explicitly override its model with a provider/model ID. |
| `MaxSteps` | Bound the provider's hosted loop; zero uses the provider default. This is separate from the library's `WithMaxRounds` for client-function continuations. |
| `ReasoningEffort` | Spend more or less model reasoning. `Auto` omits an override; supported levels depend on the actual model. |
| `DisableWebSearch` / `Tools` | Control the adapter's default web tool and explicitly selected hosted tools. |
| `Models` | Supply one to five fallback models in priority order. The list overrides the single model; all selected models must support your requested features. |
| `Profile` | Use a saved server configuration and optionally pin its version. It cannot be combined with `Preset`. |
| `ServiceTier` | Request default, flex, or priority processing. The provider may ignore a tier unsupported by the selected model. |
| `Skills` | Supply built-in, inline, or previously uploaded custom skills. Custom resources belong to your Perplexity account. |
| `LanguagePreference` / `PromptCacheKey` | Set a response-language preference or cache-routing hint; a hint does not guarantee a cache hit. |
| `PreviousResponseId` / `Store` | Continue a completed provider response or control retrieval visibility. Use `StatelessMode` with continuation and send only the new turn. `Store = false` does not disable provider persistence. |

`PerplexityHostedTool` accepts a supported `Type` and documented JSON-compatible `Parameters`: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox`, or `mcp`. MCP servers and managed connectors execute through the provider; credentials, permissions, and enabled account resources must match that endpoint. Register application functions through the existing `Functions` / function builder API. Hosted steps and local handler calls have different execution owners.

Factory methods `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, and `Connector` create tool options. MCP calls execute without an approval pause; restrict `allowedTools` as needed. Connectors are a provider preview and reference an existing connected integration.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "Read the project documentation and compare the relevant capabilities.");
```

The selected model determines tool, reasoning, image, and schema compatibility. Common `WithFileSearch` is not a Perplexity vector-store adapter. Sandbox-produced files, uploaded attachments, and remote MCP data are separate resources; they do not become a shared file-search store.

## Sources, images, and structured answers

Use typed completion or typed streaming when your application needs JSON fields. The adapter sends a native schema and retains the usual repair workflow. Native response items and tool identities are retained for continuation; avoid manually deleting or reordering protocol history. Image input uses `Message` and `ImageContent` with JPEG/PNG/WebP/GIF bytes or an HTTPS URL, subject to model support. Images are inputs, not image-generation requests.

Raw response traces remain in history metadata, but follow-up requests replay only allowed `message`, `function_call`, and `function_call_output` input items; use `PreviousResponseId` when the complete provider-side hosted state must continue.

Citations may identify web results or other provider sources. Offsets belong to an individual provider response/content part, not the concatenated Run result. Keep the URL and title for display and verification; a returned source does not itself verify every generated claim.

## Keep a long task running

Use provider background execution when research must survive a temporary client disconnect or when you need to retrieve it later by ID. A local `AIRun` controls the current client execution; a provider background response has its own server lifecycle. Ending a stream reader only ends observation. Cancel a provider job explicitly when you want the remote work to stop.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "Compare the latest approaches to battery recycling and cite the sources.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` captures input without appending conversation history and rejects active local functions or `Store = false`. `GetResponseAsync` polls once; `WaitForCompletionAsync` polls until a terminal status. Save `Id` and `LastSequenceNumber`; `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` reconnects. Cancel the remote job with `CancelAsync`; cancelling a polling/reading token stops that client operation. `LastResponse` contains text, status, usage, citations, and `OutputJson`. Check the terminal status before using an answer.

For sandbox outputs, call the handle's `ListFilesAsync` and `DownloadFileAsync(fileId)`. The service also exposes `GetAgentResponseAsync`, `GetResponseFilesAsync`, and `GetResponseFileContentAsync`. These read provider response artifacts; they do not create or search a vector store.

Use the background path in this guide for built-in Office skills: `StartBackgroundAsync`, then `WaitForCompletionAsync` / `GetResponseAsync`, and the file methods. Internal tool traces in those responses may be indistinguishable from ordinary local function calls.

Background submission, retrieval, cancellation, and stream reconnection do not enable mid-response `SteerAsync` or native asynchronous client tools. Reconnection resumes observation of an existing response rather than submitting the original task again. Keep the response ID and cursor supplied by the provider.

## Search without generating an answer

Use `PerplexitySearchClient` to obtain pages for your own ranking, UI, or another LLM. It does not change `PerplexityService` conversation history or invoke an answer model.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "battery recycling methods",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` accepts one query or multiple queries. Options include Web/People search, country, domains, languages, publication/update date ranges, and recency. Choose `ContentSize` or explicit `MaxTokens` / `MaxTokensPerPage`, not both. Results include rank, title, URL, snippet, and provider date fields; rank is the returned order, not a relevance score.

`ContentSize` is supported only for Web search. Omit it for People search; the client rejects that combination before sending.

## Use Perplexity vectors in your document index

Standard embeddings treat passages independently and implement `IEmbeddingProvider`, so they fit the existing builder. Contextualized embeddings preserve the order and grouping of neighbouring chunks; they use a separate API to prevent unrelated documents being flattened into one input.

| Model constant | Provider ID | Default dimensions |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Returns are accepted within 30 days.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("When can I return a purchase?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Returns are accepted within 30 days.", "Keep your receipt when requesting a refund." },
    new[] { "Standard delivery takes three days.", "Express delivery is available on weekdays." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "When can I return a purchase?", cancellationToken);
```

Use the same model, dimensions, and encoding for indexed documents and queries. `GetQueryEmbeddingAsync` sends one query as its own document to the same contextual model. Contextualized results retain both document and chunk order; they are not automatically connected to the flat RAG builder.

Float APIs decode the provider's base64 signed-int8 vectors and normalize them for vector similarity. Explicit binary APIs return packed bits and use Hamming distance; binary data is never silently treated as float coordinates. Full dimensions are 1024 for 0.6B and 2560 for 4B; optional reduced dimensions must follow provider limits. Batch size, document length, total tokens, and account rate limits still apply.

Binary methods are `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync` and contextual `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`. `PerplexityBinaryEmbedding` exposes `Dimensions`, a copied `ToArray()`, and `HammingDistance`; smaller distance means greater similarity. Binary dimensions must be divisible by eight. Standard batches allow up to 512 texts; contextual batches allow 512 documents and 16,000 chunks. The provider checks 32K per-text/per-document and 120K total-token limits.

## Migrate existing Sonar code

This release deliberately removes the old Sonar adapter before the provider's announced September 27, 2026 endpoint retirement. `PerplexityService` now sends Agent requests to `/v1/agent`; `AIModels.Perplexity.Sonar` now means `perplexity/sonar`. Old Sonar-specific search helpers and response types are removed. Use common completion/Run/citations, Agent presets, and `PerplexitySearchClient` for independent retrieval.

The recommended mapping is Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, and Sonar Deep Research → `High`. This is a workflow migration, not a promise of identical text, costs, or model behavior. Dynamic presets can change as the provider updates them; use an explicit model or a versioned profile when that distinction matters.

Native steering, native asynchronous client tools, and `CachePreservation.Required` are not supported by this adapter. Router/Gateway APIs are outside this integration. API availability depends on the provider, model, and account; the guide does not claim every combination has passed a paid live test.

Profiles, custom skills, and connectors require pre-existing account resources. Their request shapes are covered by unit tests; successful live calls with those resources have not been verified.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
