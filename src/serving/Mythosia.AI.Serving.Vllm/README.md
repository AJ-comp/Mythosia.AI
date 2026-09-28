# Mythosia.AI.Serving.Vllm

Find out which model a running vLLM server exposes, check whether it is healthy, and inspect request load before diagnosing an inference problem. `VllmServer` provides a read-only management client for model cards, server version, health and Prometheus metrics.

Use this package for the server's management endpoints. For chat/completions, use `QwenService` with `EndpointPlatform.Vllm` in [Mythosia.AI.Providers.Alibaba](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Providers.Alibaba). Chat and embedding APIs remain separate from the `Serving.*` management clients.

## Version and installation

Version 1.1.0 adds the shared `IModelServer` and `IModelMetricsProvider` interfaces while preserving the existing `VllmServer` methods and vLLM-specific return types. Common callers can inspect server information, model observations, health and metrics, and check this server's endpoint capabilities. Response-body reads now honor caller cancellation. See the [v1.1.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v110).

The package targets **.NET Standard 2.1** and depends on **Mythosia.AI.Serving.Abstractions 1.0.0** and **Newtonsoft.Json 13.0.4**. It has no dependency on the Mythosia.AI core and does not start or host a server. Field and metric availability still depends on the deployed vLLM version; the handling of optional fields is explained below.

```bash
dotnet add package Mythosia.AI.Serving.Vllm --version 1.1.0
```

## Inspect model cards, health and metrics

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Vllm;

// Accepts the server root OR the /v1-suffixed URL you already store for chat clients.
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var vllm = new VllmServer("http://localhost:8000/v1", http);

var cards = await vllm.GetModelsAsync(cancellation.Token);
foreach (var card in cards)
    Console.WriteLine($"{card.Id}: {card.DisplayModel}, context={card.MaxModelLen}");

var version = await vllm.GetVersionAsync(cancellation.Token);
var health = await vllm.GetHealthAsync(cancellation.Token);
var metrics = await vllm.GetMetricsAsync(cancellation.Token);
Console.WriteLine($"vLLM {version}: {health.Status}");
Console.WriteLine(metrics.KvCacheUsage); // Fraction when the expected metric is present.
Console.WriteLine(metrics.WaitingRequests);
```

The example uses C# top-level statements and the existing concrete API. A missing optional field or typed metric returns `null`; do not interpret it as zero. Use `GetModelAsync(servedName, cancellationToken)` to look up one configured alias; it returns `null` when absent.

| Input | Configuration |
|---|---|
| `endpoint` | Use an HTTP(S) server root or trailing `/v1`. Normalization retains a reverse-proxy path prefix. |
| `httpClient` | Caller-owned client, kept alive for the serving client's lifetime. The client does not change its base address, default headers or timeout, and does not dispose it. |
| `apiKey` | Optional third argument; sends a Bearer credential per request. |
| `cancellationToken` | Optional on every asynchronous operation; propagated through headers and body reads. |

The common model, information and metrics operations below enforce `HttpClient.Timeout` across headers and body reads. Existing concrete body reads honor caller cancellation but retain their legacy timeout behavior; use a cancellation token with a deadline to bound the entire call.

## Shared serving contracts

Use the common interfaces when the application also manages other serving runtimes. They are implemented explicitly where the existing vLLM API already has a method with a runtime-specific return type. Continue the previous example with:

```csharp
IModelServer server = vllm;

var info = await server.GetInfoAsync(cancellation.Token);
var commonHealth = await server.GetHealthAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);

Console.WriteLine($"{info.Runtime}: {commonHealth.Status}");

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.DisplayName} ({model.LoadState})");

if (capabilities.Metrics == ServingFeatureSupport.Supported)
{
    var commonMetrics = await ((IModelMetricsProvider)server).GetMetricsAsync(cancellation.Token);
    foreach (var sample in commonMetrics.Samples)
        Console.WriteLine($"{sample.Name}: {sample.Value}");
}
```

`ServerModel.Id` remains the served alias and `DisplayName` uses `root` when present. Alias cards, including LoRA cards, do not establish a local installation or independently verified load state: `InstallationState` and `LoadState` are both `Unknown`. Missing size, memory, locality and native state remain `null`; the reported context length is retained. `ServerInfo.Mode` also remains `Unknown` rather than inferring a mode from the number of aliases.

`GetCapabilitiesAsync()` makes read-only requests to `/v1/models` and `/metrics`. A correctly shaped model list (including an empty list) or valid Prometheus exposition establishes `Supported`. Metadata-only valid exposition may have no samples; empty text or arbitrary comments are not evidence of metrics support. HTTP 404/405/501 gives `Unsupported`; authentication failures, connection failures, timeouts and malformed responses give `Unknown`. Caller cancellation propagates. Capabilities are observations of the endpoint, not a promise that a specific model is healthy or authorized. vLLM exposes neither `IModelLifecycle` nor `IModelDownloader` through this client; loading, unloading and downloading are `Unsupported`.

Common health maps legacy `EngineDead` to `NotReady` and retains the other status distinctions and HTTP status code. Common metrics keep every sample and its labels; inspect labels before aggregating across models or engines. Prometheus `NaN` and infinities are retained and need checking before calculations. Unlike the tolerant legacy methods, common model and metrics reads reject an unrelated or malformed successful response.

Common model, information and metrics requests enforce response-size limits and reject duplicate JSON properties, duplicate model IDs, error envelopes and malformed metric samples or duplicate labels. Existing concrete parsing behavior remains available for compatibility.

## Concrete and common API

The rows below describe the concrete `VllmServer` API. Calls through `IModelServer` or `IModelMetricsProvider` return the common types where indicated.

| Member | Endpoint | Notes |
| --- | --- | --- |
| `GetModelsAsync()` | `GET /v1/models` | Concrete: `VllmModelCard` list. Common: `ServerModel` list with unknown installation/load state. An alias is not independent readiness evidence. |
| `GetModelAsync(servedName)` | `GET /v1/models` | Convenience filter by alias; `DisplayModel` uses non-empty `Root`, otherwise `Id`. |
| `GetVersionAsync()` | `GET /version` | Optional version string; legacy parsing can return `null` for an absent field or malformed JSON. |
| `IsHealthyAsync()` | `GET /health` | `bool`; classifies server/network failures but propagates caller cancellation. |
| `GetHealthAsync()` | `GET /health` | Concrete: `VllmHealthReport` (`EngineDead` for 503). Common: `ServerHealth` (`NotReady` for 503). Both distinguish unauthorized, unreachable and unexpected results. |
| `GetMetricsAsync()` | `GET /metrics` | Concrete: `VllmMetrics`, with families and typed getters. Common: `ServerMetrics`, with individual samples. Both retain labels and raw text. |
| `GetInfoAsync()` | `GET /version` | Common `ServerInfo`, with runtime `vllm`, normalized endpoint and optional version. |
| `GetCapabilitiesAsync()` | `GET /v1/models`, `GET /metrics` | Observed common capabilities; does not perform lifecycle or download operations. |

## Errors and cancellation

All asynchronous methods accept a cancellation token. Caller cancellation propagates as `OperationCanceledException`; health methods classify server/network failures and capability probes report `Unknown` for inconclusive failures.

Common operations omit raw server payloads and transport messages. Their failures use `ServingException` or its `VllmException` subtype, with status/failure classification when available. The existing concrete APIs retain legacy diagnostics: `VllmException.Message`, `ErrorType`, `ErrorCode`, `ResponseBody`, and health `Detail` can contain server or transport information. Filter those fields before logging. `VllmException` retains its concrete `int StatusCode` property while also deriving from `ServingException`.

## Endpoint normalization

Management routes (`/health`, `/version`, `/metrics`) live at the server **root** while `/v1/models` lives under `/v1`.
The constructor therefore accepts either form — `http://host:8000` or `http://host:8000/v1` — and normalizes to the root, so you can pass the same endpoint string your chat client uses.

## Model-source metadata

`root` is optional server metadata about a model's source, not an independent load-state check:

- `VllmModelCard.Root` is optional; `DisplayModel` falls back to `Id` when `Root` is null or empty.
- `Root` can contain a model repository ID, host filesystem path, or adapter source. Mask path-like values in end-user-visible UI when appropriate.
- `Created` must not be treated as a load timestamp.

## Metrics stability

Typed metric getters use specific metric names. If a deployed version omits or renames one, the getter returns `null`; inspect `Families` / `RawText` for what the server actually exposed. Request/token counters are summed over samples; `KvCacheUsage` is an unweighted average. Select labels directly when the application needs per-model or per-engine values.

## Validation and scope

The offline regression suite covers concrete API compatibility, common response validation, label preservation, capability uncertainty, authentication isolation, cancellation and body timeouts:

```bash
dotnet test --project tests/Mythosia.AI.Serving.Vllm.Tests/Mythosia.AI.Serving.Vllm.Tests.csproj --configuration Release
```

Separate live checks exercised **vLLM 0.30.0** with **`Qwen/Qwen2.5-0.5B-Instruct`** on one NVIDIA A40: health/info/inventory/capabilities, label-preserving common metrics parsing, pre-cancellation and absence of lifecycle/download interfaces. A small native HTTP inference request succeeded; that check did not exercise `QwenService` or any other Mythosia chat adapter. These checks do not establish a minimum supported runtime version or guarantee every model, metric or deployment.

This package supplies read-only model, version, health and metrics inspection. Model load/unload/download, LoRA mutations, sleep/wake, tokenization, process hosting and inference APIs are outside its scope. See the [serving guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/serving.md) for the cross-runtime support and validation matrix.
