# Migrating to Mythosia.AI 8

Use this release when requests need independent settings, users need to stop ongoing work, or an application needs answers together with usage and sources. It brings the six architecture changes below into one major upgrade, with provider/model updates and fixes verified through three adversarial reviews.

Upgrade only the packages your application uses, and rebuild consumers. Mythosia.AI brings the matching Abstractions dependency automatically. The table maps the published baseline to the compatible versions in this release.

| Package | Published baseline | Release target |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` moves from `1.0.0-preview` to stable `1.0.0` in this release. It retains the existing model, health, server-version and metrics APIs as an independent package without a dependency on the core AI package.

## Choose the change that solves your problem

| Need | Change and migration |
| --- | --- |
| Reject misspelled image options before sending | Use `ImageQuality`, `ImageBackground`, `ImageOutputFormat` and `ImageSize.Pixels(...)` / `ImageSize.Preset(...)` instead of strings. Provider support still differs. |
| Prepare several requests without changing each other | Start with `CreateRequest(...)`; each `With...` returns an independent builder. Keep the returned value. Service-level setters still change shared defaults. |
| Return application data from asynchronous tools | Attributed methods can return objects through `Task<T>` / `ValueTask<T>` and accept an injected `CancellationToken`. Exceptions are recorded as failures; existing string handlers remain supported. |
| Stop waiting when a user cancels | Pass `cancellationToken` to completion, Run and supported RAG entry points. Cancellation stops local work and cooperative tools; it does not guarantee remote provider cancellation or reverse completed actions. |
| Store the answer, usage and sources together | `AIRun.Result` returns `Task<AIRunResult>`. Replace string assignments with `(await run.Result).Text`; the snapshot is collected even without a stream reader. |
| Offer only controls appropriate to the selected model | Use `request.GetCapabilities()` or service/image capability queries. `Supported`, `Unsupported` and `Unknown` describe local library knowledge, not live account access. |

## Update callers and custom providers

Image option types, `AIRun.Result` and changed cancellation signatures are breaking contracts. Custom `IAIService` implementations and overrides of changed public overloads must add and forward the token; the provider `GetCompletionAsync(Message)` override keeps its signature and forwards `RequestCancellationToken`. Custom `AIRun` implementations must return `AIRunResult`. GetCompletionAsync keeps its string result, typed completion and `StructuredStreamRun<T>.Result` keep their typed results. Existing input-taking service/RAG StreamAsync methods remain public in v8. RunAgentAsync and RunAgentStreamAsync retain their compatibility behavior and obsolete warnings. Use Run for new progress, cancellation and supported steering flows.

## One request, a collected result and optional progress

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

Image options: choose pixels for OpenAI; Google and xAI use `ImageSize.Preset(...)`. Keep `ImageOutputFormat.Auto` unless the selected provider supports an explicit format, and save using the returned `GeneratedImage.MediaType`.

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

For registered tools, return the application object as shown below. Low-level `HandlerWithCancellation` still returns `Task<string>`; it does not add a new object wrapper.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## Provider updates and validation

This release also includes the prepared Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash and Perplexity Agent integration updates, along with shared OpenAI, Google and xAI image generation/editing. Removed model constants and Perplexity endpoint changes can require caller changes; review the provider guide and package release notes for the exact scope.

Configure Perplexity research through `PerplexityAgentOptions`; account-resource tests for Profile, Custom Skill and Connector are prepared but require registered resources to run. MCP remains a preview package. Once disposal starts, calls fail with `ObjectDisposedException`; when the reader has already stopped, new calls fail with `McpException` instead of waiting indefinitely.

Three adversarial reviews hardened request copies, tool results, cancellation/cleanup, token accounting, provider response validation and MCP lifecycle handling. The third review added 43 regression cases; 2,703 tests passed. Documentation checks covered 13 languages. No live provider API calls were made during that review; unit-test results are not a claim that every account-dependent integration was exercised.

## Detailed guides

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
