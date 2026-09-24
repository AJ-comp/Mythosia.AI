# Mythosia.AI.Providers.Alibaba

Call Qwen-compatible chat endpoints on DashScope, vLLM or Ollama while keeping the shared `AIService` conversation, streaming and tool workflows. `QwenService` adds provider-specific thinking controls and custom deployment names.

## Current release: 3.0.1

This patch rebuilds the adapter against **Mythosia.AI 8.1.0**, which brings **Mythosia.AI.Abstractions 4.1.0** transitively. Qwen public APIs and endpoint defaults remain unchanged; no additional source migration is required from 3.0.0. When upgrading from 2.x or earlier, review the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) for the inherited completion and Run contract changes.

Prepare different settings with an immutable `CreateRequest(...)` builder, pass cancellation to stop cooperative client work, and read `(await run.Result).Text` for a Run answer. The completed `AIRunResult` also retains reported usage, sources, requested/actual model, rounds and finish details without requiring a stream reader. Ordinary `GetCompletionAsync` still returns a string. [Request settings](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md) · [Run migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result) · [Completion cancellation](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation).

Qwen uses the shared local tool executor: asynchronous methods can return objects, cancellation-aware handlers receive the execution token, and failures become failed tool results. Cleanup can wait for tools that ignore cancellation; remote inference or billing cancellation is not guaranteed. See the [tool contract](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/function-calling.md#tool-execution-contract).

Inspect `service.GetCapabilities()` or `request.GetCapabilities()` before showing model controls. Snapshots use captured Qwen settings and the endpoint's actual model ID; unknown custom deployments remain `Unknown`. Inspection does not send requests or consume pending settings. This adapter retains provider-specific `ThinkingMode` controls; native steering, hosted search and image generation are not integrated. See [capability inspection](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/model-capabilities.md).

Shared stream validation rejects content or changed finish reasons after an explicit terminal event before saving history or executing tools. The final delta in the first terminal event and trailing usage-only events remain valid. See the [v3.0.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300).

## Features

- Qwen chat completion support through `QwenService`
- Streaming response support with token usage reporting (`TokenUsage`)
- Ordered multi-function calling in non-streaming and streaming flows, with sequential or bounded-parallel local handler execution inherited from the core policy
- Shared `Mythosia.AI` conversation and message abstractions
- Thinking-mode control that is sent as configured, without model-name guessing
- Compatible endpoint handling for `DashScope`, `vLLM`, and `Ollama`

## Installation

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

## Model Catalog

The provider now includes a broader built-in model catalog for Qwen 3 and Qwen 3.5 families.

```csharp
service.ChangeModel(AlibabaModels.Qwen3_32B);
service.ChangeModel(AlibabaModels.Qwen3_5_27B);
service.ChangeModel(AlibabaModels.Qwen3_5_397B);
```

## Thinking Mode Behavior

`QwenService` sends whatever `ThinkingMode` you configured, translated into the platform's request format.

| Platform | Thinking On | Thinking Off |
| --- | --- | --- |
| DashScope | `enable_thinking = true` | `enable_thinking = false` |
| vLLM | `chat_template_kwargs.enable_thinking = true` | `chat_template_kwargs.enable_thinking = false` |
| Ollama | `reasoning.effort = "high"` | _(parameter omitted)_ |

When thinking is off, DashScope and vLLM receive an explicit `enable_thinking = false`, preventing the server default from enabling reasoning unexpectedly.

**The model name is not treated as a capability signal.** Operators can choose any served name through vLLM `--served-model-name`, aliases, or a gateway. `QwenService` therefore sends the configured `ThinkingMode` without guessing from the model ID; an unsupported endpoint can ignore or reject the setting instead of the caller's instruction disappearing silently.

## Request-Scoped Reasoning Control

When you are using the shared `AIRequestProfile` APIs from `Mythosia.AI`, `QwenService` can disable reasoning for a single call without changing the long-lived service configuration.

```csharp
var answer = await service.GetCompletionAsync(
    "Summarize this policy without reasoning output.",
    new AIRequestProfile
    {
        DisableReasoning = true
    });
```

## Quick Start with vLLM

```csharp
using System.Net.Http;
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:8000", EndpointPlatform.Vllm, httpClient)
    .UseQwen3_32BModel();

var response = await service.GetCompletionAsync("Hello, Qwen!");
Console.WriteLine(response);
```

## Quick Start with Ollama

```csharp
using System.Net.Http;
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:11434", EndpointPlatform.Ollama, httpClient)
    .UseQwen3_32BModel();

var response = await service.GetCompletionAsync("Hello, Qwen!");
Console.WriteLine(response);
```

## Configure Thinking Mode

```csharp
using System.Net.Http;
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:11434", EndpointPlatform.Ollama, httpClient)
{
    ThinkingMode = QwenThinking.On
};
```

## Using Quantized or Custom Model Names

Some Qwen deployments do not use the default public model identifier.

Examples:

- Quantized variants such as `qwen3:32b-q4_K_M`
- Custom deployment names from a gateway or self-hosted endpoint
- Provider-specific aliases that differ from the built-in `AlibabaModels` constants

In those cases, keep the service configured normally and set `ModelIdOverride` to the exact deployed model name that your endpoint expects.

```csharp
using System.Net.Http;
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:11434", EndpointPlatform.Ollama, httpClient)
{
    ThinkingMode = QwenThinking.On,
    ModelIdOverride = "qwen3:32b-q4_K_M"
};

var response = await service.GetCompletionAsync("Summarize this document.");
```

You can also combine a built-in base model selection with a different runtime model ID:

```csharp
using System.Net.Http;
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:8000", EndpointPlatform.Vllm, httpClient)
    .UseQwen3_32BModel();

service.ModelIdOverride = "my-qwen3-32b-awq";

var response = await service.GetCompletionAsync("Explain this code.");
```

This is useful when:

- The displayed deployment name is different from the public Qwen model name
- You are routing through Ollama, vLLM, or a custom proxy
- You want to use a quantized build while keeping the general service configuration readable

## How Model Names Behave on Ollama

When `EndpointPlatform.Ollama` is used, built-in model names are automatically converted to Ollama-style IDs.

Example:

- `qwen3-32b` -> `qwen3:32b`

If your Ollama model name is not the default converted name, set `ModelIdOverride` explicitly.

## Streaming Example

```csharp
using System.Net.Http;
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:8000", EndpointPlatform.Vllm, httpClient)
    .UseQwen3_32BModel();

await foreach (var chunk in service.StreamAsync("Explain transformers simply."))
{
    if (!string.IsNullOrWhiteSpace(chunk))
        Console.Write(chunk);
}
```

## Function Calling Example

```csharp
using System.Net.Http;
using Mythosia.AI.Extensions;
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:8000", EndpointPlatform.Vllm, httpClient)
    .UseQwen3_32BModel()
    .WithFunction(
        "get_weather",
        "Gets the current weather for a city",
        ("city", "City name", true),
        (string city) => $"Weather in {city}: sunny, 24°C");

var result = await service.GetCompletionAsync("What's the weather in Seoul?");
```

## Notes

- Use `EndpointPlatform.DashScope` for Alibaba Cloud DashScope endpoints (default)
- Use `EndpointPlatform.Vllm` for OpenAI-compatible `vLLM` endpoints
- Use `EndpointPlatform.Ollama` for local Ollama servers
- Model selection can be changed with provider model constants or `ModelIdOverride`
- For the shared core API surface and advanced features, see the main `Mythosia.AI` package documentation

## Documentation

- Main package: [GitHub Repository](https://github.com/AJ-comp/Mythosia.AI)
- Core documentation: [Mythosia.AI Provider Guide](https://aj-comp.github.io/Mythosia.AI/docs/providers.html)
- Release notes: [Mythosia.AI.Providers.Alibaba v3.0.1 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v301)
