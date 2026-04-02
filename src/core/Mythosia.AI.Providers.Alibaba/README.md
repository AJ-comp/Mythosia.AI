# Mythosia.AI.Providers.Alibaba

## Package Summary

`Mythosia.AI.Providers.Alibaba` adds Alibaba Cloud / Qwen provider support for `Mythosia.AI` through `QwenService`.

It is intended for projects that want to keep using the common `AIService` abstraction while calling Qwen-compatible chat completion endpoints through `DashScope`, `vLLM`, or `Ollama`.

## Features

- Qwen chat completion support through `QwenService`
- Streaming response support with token usage reporting (`TokenUsage`)
- Function calling support
- Shared `Mythosia.AI` conversation and message abstractions
- Optional thinking-mode control for supported Qwen models
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

`QwenService` applies platform-specific thinking request formatting for Qwen 3-family models.

| Platform | Thinking On | Thinking Off |
|---|---|---|
| DashScope | `enable_thinking = true` | `enable_thinking = false` |
| vLLM | `chat_template_kwargs.enable_thinking = true` | `chat_template_kwargs.enable_thinking = false` |
| Ollama | `reasoning.effort = "high"` | _(파라미터 생략)_ |

Thinking off 시 DashScope / vLLM에는 명시적으로 `enable_thinking = false`가 전송되어 서버 기본값에 의한 의도치 않은 thinking 활성화를 방지합니다.

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
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:8000", EndpointPlatform.Vllm, httpClient)
    .UseQwen3_32BModel();

var response = await service.GetCompletionAsync("Hello, Qwen!");
Console.WriteLine(response);
```

## Quick Start with Ollama

```csharp
using Mythosia.AI.Providers.Alibaba;

var httpClient = new HttpClient();
var service = new QwenService("http://localhost:11434", EndpointPlatform.Ollama, httpClient)
    .UseQwen3_32BModel();

var response = await service.GetCompletionAsync("Hello, Qwen!");
Console.WriteLine(response);
```

## Configure Thinking Mode

```csharp
using Mythosia.AI.Providers.Alibaba;

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
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService("http://localhost:11434", EndpointPlatform.Ollama, httpClient)
{
    ThinkingMode = QwenThinking.On,
    ModelIdOverride = "qwen3:32b-q4_K_M"
};

var response = await service.GetCompletionAsync("Summarize this document.");
```

You can also combine a built-in base model selection with a different runtime model ID:

```csharp
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
var service = new QwenService("http://localhost:8000", EndpointPlatform.Vllm, httpClient)
    .UseQwen3_32BModel();

await foreach (var chunk in service.StreamAsync("Explain transformers simply."))
{
    if (!string.IsNullOrWhiteSpace(chunk.Content))
        Console.Write(chunk.Content);
}
```

## Function Calling Example

```csharp
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
- Core package docs: [Mythosia.AI Core Package](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI)
- Release notes: [RELEASE_NOTES.md](RELEASE_NOTES.md)
