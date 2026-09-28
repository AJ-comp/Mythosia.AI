# Manage existing model servers

A model picker or operations screen needs server health, available models and load state before it sends a prompt. The Serving packages give these checks one interface across Ollama, llama.cpp and vLLM, while keeping runtime-specific operations explicit.

Use them to populate a model picker, show whether a server is reachable, manage model residency when the runtime supports it, or read engine metrics. Changing the runtime can leave the application's common inspection code intact.

These clients connect to an existing HTTP server. Installing or hosting the engine, renting a GPU, chat and embedding generation belong to separate components. Chat continues through the appropriate AI service, such as `QwenService` for vLLM; RAG embedding providers remain separate. Discovery does not automatically load models. SGLang is not implemented.

## Choose a package

| Package | Version | Use it for |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | Common contracts for application code or a custom management adapter. No package dependencies. |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Inspect Ollama, download models and explicitly preload or unload them. |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | Inspect llama.cpp, read metrics, and manage models in Router mode. |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | Inspect vLLM and read metrics through common or existing vLLM-specific APIs. |

All four target .NET Standard 2.1. Install the adapter you use; it brings in the abstractions package automatically. The adapters depend on the shared contracts and Newtonsoft.Json, independently of the core AI and RAG packages.

## Discover without changing server state

Install the concrete package for your runtime. This example uses Ollama; select `VllmServer` or `LlamaCppServer` from their corresponding namespaces for those servers. Discovery uses read-only requests and never issues a load, generation or download command.

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

The endpoint is the server root, optionally with a reverse-proxy path prefix. The API key is optional and sent as a Bearer credential per request. The client does not change `HttpClient.DefaultRequestHeaders` or dispose the supplied `HttpClient`; reuse and dispose it according to your application's lifetime. In this Ollama example, the timeout also covers streamed response bodies, so allow enough time for a model download.

## Common and optional contracts

| Contract | Purpose |
| --- | --- |
| `IModelServer` | Server information, health, models and observed capabilities. |
| `IModelLifecycle` | Explicit load and unload commands; optional. |
| `IModelDownloader` | Explicit download with progress; optional. |
| `IModelMetricsProvider` | Metric samples with labels; optional. |

An implemented interface says the client has an operation; `ServingCapabilities` reports what can be established for the connected endpoint. `Supported` is not a guarantee of authorization or success for every model. `Unsupported` means the operation is unavailable in the observed mode or endpoint. `Unknown` means insufficient evidence, including authentication or connection failures; it must not be treated as unsupported.

`InstallationState` and `LoadState` describe different observations. `Unknown` is neither absent nor unloaded. Missing `SizeBytes`, `MemoryBytes` or `ContextLength` remains `null`, not zero. A healthy management endpoint does not prove that a particular model is ready for inference.

## Runtime differences

| Operation | Ollama | llama.cpp single model | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| Info, health and model listing | Yes | Yes | Yes | Yes |
| Explicit load / unload | Yes, using empty generate requests | Unsupported | Yes, after Router identity is established | Unsupported by this client |
| Model download | Yes, with streamed progress | Unsupported | Explicit operation; requires download endpoint and SSE events | Unsupported by this client |
| Metrics | Not implemented | Server metrics when enabled | Concrete model-specific overload; model must already be loaded | Server metrics when available |

This table describes client operations, not a promise that every server version, permission set or model supports them. Check the connected endpoint's capabilities and handle operation failures.

**Ollama:** `/api/tags` supplies registered models and `/api/ps` supplies current runners. A registered remote model can lack local weights; without a local runner, its load state stays unknown. Preloading uses an empty `/api/generate` request and the server's default keep-alive. Embedding-only models are not redirected to another endpoint. Unload uses `keep_alive: 0` and does not delete files. Metrics are not implemented.

**llama.cpp:** `/props` must explicitly establish router mode before lifecycle or download commands. Single-model mode does not support those commands; observed sleeping state is preserved. Router downloads subscribe to `/models/sse`, then submit `POST /models`, and succeed only on that model's `download_finished` event. SSE availability alone leaves download capability unknown. Server-wide metrics apply to single-model mode; router metrics require the concrete `GetMetricsAsync(modelId, token)` overload, which sends `autoload=false` so inspection cannot load a model.

**vLLM:** served aliases and the optional `root` field remain available, but common installation and load states stay unknown. Models and metrics are checked against actual responses; lifecycle and download are unsupported. Existing `VllmServer` methods and DTOs remain available through the concrete client; common health, model and metric methods use explicit interfaces.

## Run an explicit management operation

Downloads and residency changes consume network, disk or device memory. Invoke them when your application needs that action. The following continuation of the Ollama example downloads a small model and briefly loads it to observe its state. Use exact server model IDs, including the Ollama tag or the llama.cpp quantization tag.

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

The example uses a dedicated test model and unloads it afterward. A production application chooses when to release a model; do not unload a model still used by other requests. Cleanup has its own deadline and can fail if the server is unavailable.

Progress describes an individual artifact or stage. Nullable byte counters are not zero or a whole-model percentage. A successful load call acknowledges the command, not readiness or indefinite residency; observe `LoadState` with a bounded wait when readiness matters. For llama.cpp's Router download protocol and version constraints, use the concrete package guide. An explicitly requested operation can be attempted with `Unknown` support after verifying the server's configuration; capability discovery alone never initiates it.

## Cancellation and errors

Pass a cancellation token through discovery and commands. Cancellation stops this client's HTTP work and waiting; it does not guarantee remote cancellation, rollback or removal of downloaded layers. Configure the supplied `HttpClient` for the operation's duration. These clients do not take ownership of it.

Keep metric labels when comparing models or engines. Missing metrics are not zero, and values can include `NaN` or infinity. `ServingException` is the shared error type; common management errors omit raw response bodies and credentials. Existing vLLM-specific calls retain their legacy error details.

`GetHealthAsync` classifies endpoint failures as health states; it still propagates caller cancellation. Other operations can throw `ServingException`, while a known unsupported llama.cpp mode can throw `NotSupportedException`. Neither a timeout nor a failed request proves that the remote action was rolled back. A download method returns successfully only after the runtime reports completion: terminal success followed by EOF for Ollama, or the matching `download_finished` event for llama.cpp Router.

## What has been verified

Offline tests cover controlled success, malformed responses, errors and cancellation. Separate real-server checks used a single NVIDIA A40, small public Qwen models and these engine builds:

| Runtime | Tested model | Verified management operations |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | Discovery, fresh download, load/unload, sanitized missing-model errors, pre-cancellation and cancellation after partial download progress. |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | Discovery, download events, load/unload, model-specific metrics without autoload, errors and download cancellation. |
| llama.cpp b11146, single model | Same GGUF model | Discovery, server metrics, cancellation and explicit rejection of Router lifecycle commands. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | Discovery, server metrics and pre-cancellation. |

Short native HTTP inference requests also returned generated text in all four configurations. Those requests confirm engine operation, not the AI service chat adapters, model quality, throughput, or compatibility with every engine build. The profiles above are tested configurations, not minimum supported versions. Download cancellation checks used separate larger test models and did not assert remote rollback. A first Ollama download failed; retry and a fresh download after removing the model passed, without establishing the first failure's exact cause.

Use the [opt-in live verification guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md) to check your deployed endpoint. It distinguishes the committed management runner from the additional inference and cancellation probes used during verification. Detailed execution reports stay outside published documentation.

## Package guides

- [Mythosia.AI.Serving.Abstractions](../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — Shared management contracts and immutable server/model/capability snapshots.
- [Mythosia.AI.Serving.Ollama](../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Ollama inventory, health, explicit preload/unload and streamed model downloads.
- [Mythosia.AI.Serving.LlamaCpp](../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — llama.cpp inspection, guarded router lifecycle/downloads and metrics without autoload.
- [Mythosia.AI.Serving.Vllm](../src/serving/Mythosia.AI.Serving.Vllm/README.md) — vLLM model cards, health, version and label-preserving metrics; existing concrete API retained.
