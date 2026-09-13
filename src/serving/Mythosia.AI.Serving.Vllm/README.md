# Mythosia.AI.Serving.Vllm

Find out which model a running vLLM server exposes, check whether it is healthy, and inspect request load before diagnosing an inference problem. `VllmServer` provides a read-only management client for model cards, server version, health and Prometheus metrics.

Use this package for the server's management endpoints. For chat/completions, use `QwenService` with `EndpointPlatform.Vllm` in [Mythosia.AI.Providers.Alibaba](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/core/Mythosia.AI.Providers.Alibaba). The `Serving.*` family inspects running servers; `Providers.*` supplies AI conversation adapters.

## Current release: 1.0.0

Version 1.0.0 promotes the published 1.0.0-preview client to a stable release. The public API and runtime behavior remain unchanged, so existing preview callers can upgrade without source changes. Stable package metadata includes the MIT license, repository information, symbol package and packaged release notes. See the [v1.0.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100).

The package targets **.NET Standard 2.1** and depends only on **Newtonsoft.Json 13.0.4**. It has no dependency on the Mythosia.AI core and does not start or host a server. Field and metric availability still depends on the deployed vLLM version; the handling of optional fields is explained below.

## Installation

```bash
dotnet add package Mythosia.AI.Serving.Vllm
```

## Quick Start

```csharp
using Mythosia.AI.Serving.Vllm;

// Accepts the server root OR the /v1-suffixed URL you already store for chat clients.
var server = new VllmServer("http://localhost:8000/v1", httpClient);

// What model is ACTUALLY running behind the served alias?
var card = await server.GetModelAsync("my-served-alias");
Console.WriteLine(card?.DisplayModel);   // e.g. "Lorbus/Qwen3.6-27B-int4-AutoRound"
Console.WriteLine(card?.MaxModelLen);    // engine-effective context window, e.g. 50000

// Diagnostics
var version = await server.GetVersionAsync();        // e.g. "0.25.0"
var health  = await server.GetHealthAsync();         // Healthy / EngineDead / Unauthorized / Unreachable / Unexpected
var metrics = await server.GetMetricsAsync();
Console.WriteLine(metrics.KvCacheUsage);             // 0..1
Console.WriteLine(metrics.WaitingRequests);
```

## API

| Member | Endpoint | Notes |
| --- | --- | --- |
| `GetModelsAsync()` | `GET /v1/models` | One card per `--served-model-name` alias (identical `Root`) + one per loaded LoRA adapter. `data[0].Id` is the canonical served name. |
| `GetModelAsync(servedName)` | `GET /v1/models` | Convenience filter by alias; `card.DisplayModel` = `Root ?? Id`. |
| `GetVersionAsync()` | `GET /version` | e.g. `"0.25.0"`; `null` when the response has no `version` field. |
| `IsHealthyAsync()` | `GET /health` | `bool`, never throws on server/network failures. |
| `GetHealthAsync()` | `GET /health` | Classified: `Healthy` / `EngineDead` (503) / `Unauthorized` (401·403) / `Unreachable` / `Unexpected` — tells a dead engine from a wrong API key from a network problem. |
| `GetMetricsAsync()` | `GET /metrics` | Label-preserving Prometheus families + typed getters (`RunningRequests`, `WaitingRequests`, `KvCacheUsage`, token counters) + `RawText`. |

Server errors carry vLLM's OpenAI-style error body as a typed `VllmException` (`StatusCode`, `ErrorType`, `ErrorCode`, `ResponseBody`).

### Endpoint normalization

Management routes (`/health`, `/version`, `/metrics`) live at the server **root** while `/v1/models` lives under `/v1`.
The constructor therefore accepts either form — `http://host:8000` or `http://host:8000/v1` — and normalizes to the root, so you can pass the same endpoint string your chat client uses.

### The `root` field caveat

The headline feature — resolving a served alias to the **actually loaded model** — reads vLLM's `root` field, which is stable since 2023 but **undocumented** (absent from vLLM's docs and its own OpenAPI schema; it mirrors fields OpenAI removed from its API in 2023). Accordingly:

- `VllmModelCard.Root` is nullable; display `DisplayModel` (= `Root ?? Id`) instead of assuming it.
- `root` is the raw `--model` CLI value, verbatim. When a model is served from local disk this is a **host filesystem path** — consider masking path-like values in end-user-visible UI.
- `Created` is regenerated per request — it is not a load timestamp.

### Metrics stability

Typed metric getters are bound to today's stable v1 names. vLLM has renamed metrics across versions before (`gpu_cache_usage_perc` → `kv_cache_usage_perc`); after such a rename a typed getter returns `null` while `Families` / `RawText` still carry everything the server exposed.

## Scope (deliberate)

In: read-only control-plane — models, version, health, metrics.
Out: chat/embeddings/rerank (stay on `Mythosia.AI` / `Mythosia.AI.Rag`), tokenize/detokenize, LoRA load/unload (env-gated), all `VLLM_SERVER_DEV_MODE` endpoints (sleep/wake etc.).

A serving-runtime abstraction (`Mythosia.AI.Serving.Abstractions`) is planned to be **extracted, not invented** — once a second runtime implementation (Ollama) exists. Method names and DTO prefixes here are already chosen to make that extraction additive and non-breaking.
