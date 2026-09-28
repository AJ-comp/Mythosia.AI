# Mythosia.AI.Serving.LlamaCpp

A llama.cpp model name can refer to an installed model, an unloaded preset, or a model that is still downloading. Applications need to distinguish these states before offering a model selector or a load button. This package provides the management client for an **already running** llama.cpp server, using the common `Mythosia.AI.Serving` contracts.

It does not install llama.cpp, host a server, or issue chat requests. Chat and embedding adapters remain separate from server management.

## Version and installation

```bash
dotnet add package Mythosia.AI.Serving.LlamaCpp --version 1.0.0
```

Version 1.0.0 targets .NET Standard 2.1 and depends on `Mythosia.AI.Serving.Abstractions` 1.0.0 and Newtonsoft.Json 13.0.4. The package has no dependency on the Mythosia.AI chat library. See the [v1.0.0 release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.LlamaCpp/RELEASE_NOTES.md#v100).

## Inspect a running server

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.LlamaCpp;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
var cancellationToken = cancellation.Token;
var llama = new LlamaCppServer("http://localhost:8080", http);
IModelServer server = llama;

var info = await server.GetInfoAsync(cancellationToken);
var health = await server.GetHealthAsync(cancellationToken);
var capabilities = await server.GetCapabilitiesAsync(cancellationToken);
var models = await server.GetModelsAsync(cancellationToken);

Console.WriteLine($"{info.Runtime} {info.Version} ({info.Mode}): {health.Status}");
Console.WriteLine($"Download support: {capabilities.ModelDownloading}");
foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

The example uses C# top-level statements. Keep the supplied `HttpClient` alive for the client's lifetime; the serving client neither changes nor disposes it.

| Input | Configuration |
|---|---|
| `endpoint` | HTTP(S) root or a trailing `/v1`; reverse-proxy path prefixes are retained. Embedded URL credentials, query strings and fragments are rejected. |
| `httpClient` | Caller-owned client. Configure handlers and timeout before use; the timeout covers each request, including streamed bodies. Allow enough time for downloads, and use a caller cancellation deadline to bound a multi-request workflow. |
| `apiKey` | Optional third argument; sends a Bearer credential per request without changing default headers. |
| `cancellationToken` | Optional on every asynchronous operation; cancellation stops local HTTP work and waiting. |

`Unknown` installation or load state is not evidence that a model is absent. Router presets may describe files that have not been downloaded. Single-model sleeping state is read through `/props`; that inspection does not wake the model. Memory size remains unknown when the server does not report it.

## Understand single-model and router modes

| Operation | Single-model server | Router |
|---|---|---|
| Model listing / identity / health | `/v1/models`, `/props`, `/health` | `/v1/models`, `/props`, `/health` |
| Load / unload | Unsupported | Explicit router commands |
| Download | Unsupported | Explicit POST plus SSE completion; availability depends on the deployed build |
| Server-wide metrics | Available with `--metrics` | Unsupported through the common method |
| Model-specific metrics | Use the server-wide method | Use `GetMetricsAsync(modelId)`; the child must already be available and expose metrics |

Both modes expose `/models` and `/v1/models`; those routes alone do not prove router support. The client uses `/props` to distinguish the modes and refuses lifecycle or download mutations if router identity cannot be verified.

`GetCapabilitiesAsync` only sends GET requests. A missing endpoint can be `Unsupported`, while authentication errors, transient failures and insufficient evidence are `Unknown`. Even a working router SSE endpoint cannot prove that an older server build implements the download POST route, so download support remains `Unknown` rather than being guessed. An application may still explicitly request a download on a verified router and handle a rejected request. `Supported` does not promise authorization or that an operation is valid for every model.

## Load and unload explicitly

On a verified router, continue the inspection example with the commands below. Use an identifier exposed by that router or a supported remote model reference:

```csharp
const string modelId = "Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M";
IModelLifecycle lifecycle = llama;
await lifecycle.LoadModelAsync(modelId, cancellationToken);

// Acknowledgement is not proof that loading has finished. Inspect the model state.
var afterLoad = await server.GetModelsAsync(cancellationToken);

// After the application's work is finished:
await lifecycle.UnloadModelAsync(modelId, cancellationToken);
```

The runtime decides how it handles already loaded models, unknown identifiers, and resource limits. The client sends one explicit command and does not automatically retry it.

## Download and observe completion

This alternative workflow uses the imports and cancellation token from the inspection example, with a longer-lived download request. A download can continue on the server after the client stops waiting.

```csharp
using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromHours(1) };
IModelDownloader downloader = new LlamaCppServer("http://localhost:8080", downloadHttp);
var progress = new Progress<ModelDownloadProgress>(p =>
    Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));

await downloader.DownloadModelAsync(
    "Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M", progress, cancellationToken);
```

The client opens `/models/sse` before posting to `/models`, then waits for the requested model's `download_finished` event. HTTP acceptance, another model's completion, a `loaded` status, or an early end of the event stream is not download success. `download_failed` raises `ServingException`. Frames use Server-Sent Events, not newline-delimited JSON; progress can describe multiple files independently. Unknown totals are `null`, not zero.

Cancellation interrupts the HTTP work and local observation, disposes the SSE subscription and preserves the caller's cancellation token. It **does not automatically send an unload command**, cancel the remote download, or undo files already written. Inspect the server after cancellation before deciding whether to retry or explicitly unload. Timeouts likewise do not imply remote rollback.

## Read metrics without loading a model

Choose the call for the mode reported by the inspection example. Metrics must be enabled on the single-model server or router child. The router child must already be loaded and ready; an unloaded child is not started by this call.

```csharp
ServerMetrics metrics;
if (info.Mode == ServerMode.SingleModel)
    metrics = await llama.GetMetricsAsync(cancellationToken);
else if (info.Mode == ServerMode.Router)
    metrics = await llama.GetMetricsAsync(
        "Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M", cancellationToken);
else
    throw new InvalidOperationException("The server mode is unknown.");

foreach (var sample in metrics.Samples)
    Console.WriteLine($"{sample.Name}: {sample.Value}");
```

The router overload sends `autoload=false`, including when the model name requires URL escaping. It never loads a model to obtain metrics. Labelled samples are preserved separately; do not blindly sum samples with different labels. Prometheus `NaN` and infinities are retained, so check them before displaying or calculating values. `RawText` retains the original metrics text.

## Errors and verification

`ServingException` exposes a status code when available and a failure kind that distinguishes HTTP, transport, timeout and response validation failures. Response bodies, API keys and request URLs are not copied into exception messages. Required JSON fields, model identifiers, counts, acknowledgement values and SSE completion are validated, while unconsumed additional fields are allowed. A single-model management mutation produces `NotSupportedException`; an unverified server mode produces `ServingException` without sending the mutation.

The offline tests use controlled HTTP responses to cover single/router modes, capability uncertainty, installation versus loading, sleeping, malformed payloads, auth isolation, encoded metrics queries, SSE ordering and both supported progress shapes, cross-model events, premature termination, cancellation and body timeouts:

```powershell
dotnet test --project tests/Mythosia.AI.Serving.LlamaCpp.Tests/Mythosia.AI.Serving.LlamaCpp.Tests.csproj -c Release
```

Separate live checks exercised **llama.cpp b11146** with **`Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M`** on one NVIDIA A40. Router checks covered discovery, download completion, load/unload, model-specific metrics without automatic loading, sanitized missing-model errors and pre-cancellation. Cancellation after partial download progress was verified separately using a larger model. Single-model checks covered mode/inventory, server-wide metrics, pre-cancellation and rejection of router-only lifecycle commands. Small native HTTP inference requests succeeded in both modes; they did not test Mythosia chat adapters.

This is a bounded compatibility observation, not a minimum supported build or a guarantee for all models and deployments. Download cancellation establishes local cancellation propagation, not remote rollback. See the [serving guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/serving.md) for the cross-runtime support and validation matrix.

Protocol references: [official server documentation](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md), [native router implementation](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/server-models.cpp), and [single-model routes](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/server-context.cpp). Runtime features depend on the deployed llama.cpp build.
