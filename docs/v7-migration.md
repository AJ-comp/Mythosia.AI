# Migrating to Mythosia.AI 7

Mythosia.AI 7.0.0 is a breaking release. Upgrade the related packages together:

```xml
<PackageReference Include="Mythosia.AI" Version="7.0.0" />
<PackageReference Include="Mythosia.AI.Abstractions" Version="3.0.0" />
<PackageReference Include="Mythosia.AI.Providers.Alibaba" Version="2.0.0" />
```

Install only the packages your application uses. `Mythosia.AI` already brings in the matching Abstractions dependency; the explicit Abstractions reference is mainly for libraries that compile directly against `IAIService` or the shared models.

## Breaking API replacements

| Removed in v7 | Replacement |
| --- | --- |
| `AIService.GenerateImageAsync(...)` | Cast or depend on `IImageGenerationService`, then call `GenerateImagesAsync(...)` |
| `AIService.GenerateImageUrlAsync(...)` | Read `GeneratedImage.Url` when supplied, or use the authoritative inline `GeneratedImage.Data` and `MediaType` |
| `AIService.MaxMessageCount` | Configure `ConversationPolicy`; without one, the full active history is sent |
| `ChatBlock.RemoveFunctionMessages()` | Keep call/result pairs intact, or explicitly clear and rebuild the conversation |
| `AIService.ExtractFunctionCall(...)` | Override `ExtractFunctionCalls(...)` and return `FunctionCallBatch` |
| `CompletionProtocol.ExtractFunctionCall(...)` | Override `ExtractFunctionCalls(...)` and return `FunctionCallBatch` |
| `ProcessFunctionCallAsync(string, Dictionary<string, object>)` | Override `ProcessFunctionCallAsync(FunctionCall)`; the base service schedules complete batches |
| `GrokReasoning.Off` | Use `Auto` to omit the parameter or `None` for Grok 4.3; Grok 4.5 cannot disable reasoning |

The unsupported Qwen image-method overrides were removed as part of the same image API migration. `QwenService` remains a chat-completion provider.

## Image generation

Image generation is an optional provider capability and its model is independent from the service's chat model:

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var result = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A clean architectural facade study",
    Count = 1,
    Size = "1024x1024"
});

GeneratedImage image = result.Images[0];
await File.WriteAllBytesAsync("facade.png", image.Data);
```

OpenAI supports generation plus reference/mask editing. Gemini supports generation and reference-image editing, requires `Count = 1`, and does not accept a separate mask.

## Multiple function calls

Providers can now return multiple calls in one assistant turn. Sequential handler execution remains the default, so existing registration code keeps one-at-a-time behavior:

```csharp
using Mythosia.AI.Models.Functions;

service.DefaultPolicy = new FunctionCallingPolicy
{
    ExecutionMode = FunctionExecutionMode.Sequential
};
```

Opt in to bounded parallel execution only for independent, thread-safe handlers:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 4
};
```

Handlers may finish out of order in parallel mode, but Mythosia returns results to the provider in the original call order. Custom providers must preserve `FunctionCallBatch` and `FunctionCallResultBatch` correlation data.

## Removed model constants

Constants for retired, unavailable, or deprecated models were removed instead of being kept as obsolete aliases. This includes the old GPT-5 snapshots, GPT-5.2 Codex, GPT-4 Vision/4o-latest aliases, GPT-4.1 Nano, retired Claude Opus 4/4.1 snapshots, and unavailable Grok 3 Mini support. Select a current constant from `AIModels` rather than copying a retired wire ID into application code.

GPT-5.6 Pro is a request mode selected with `Gpt5_6ReasoningMode.Pro`; there is no separate `gpt-5.6-pro` model ID.

## Conversation history

The hidden message-count window was removed. Without a `ConversationPolicy`, requests now send the complete active conversation. Applications that need bounded history should configure token-aware summarization or trimming explicitly.

## Before publishing an application update

1. Compile after replacing removed APIs and model constants.
2. Exercise both streaming and non-streaming function flows if your application registers tools.
3. Confirm whether each handler is safe for parallel execution before enabling it.
4. Treat image bytes and `GeneratedImage.MediaType` as authoritative; hosted URLs are optional.
5. Review the full [Mythosia.AI 7.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#v700) and [Abstractions 3.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v300).
