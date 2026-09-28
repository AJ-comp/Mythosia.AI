# Mythosia.AI.Serving.Ollama

Before sending a prompt, an application often needs to know whether its Ollama server is reachable, which models are registered, and which are already loaded. `OllamaServer` supplies that management layer and lets an operator explicitly download, preload or unload a model.

The client connects to an **existing server**. It does not start Ollama, install the runtime, run chat requests or generate embeddings. Chat and embedding APIs remain separate; `OllamaEmbeddingProvider` lives in `Mythosia.AI.Rag`.

## Version and installation

This initial release implements the shared `IModelServer`, `IModelLifecycle` and `IModelDownloader` contracts from `Mythosia.AI.Serving.Abstractions`. It targets .NET Standard 2.1 and references Newtonsoft.Json 13.0.4 without a Mythosia.AI core dependency. See the [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Ollama/RELEASE_NOTES.md#v100).

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

## Discover the server and its models

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
var server = new OllamaServer("http://localhost:11434", http);
IModelServer management = server;

var health = await management.GetHealthAsync(cancellation.Token);
var info = await management.GetInfoAsync(cancellation.Token);
var capabilities = await management.GetCapabilitiesAsync(cancellation.Token);
var models = await management.GetModelsAsync(cancellation.Token);

Console.WriteLine($"Ollama {info.Version}: {health.Status}");
Console.WriteLine($"Download support: {capabilities.ModelDownloading}");
foreach (var model in models)
    Console.WriteLine($"{model.Id}: registered={model.InstallationState}, loaded={model.LoadState}");
```

The example uses C# top-level statements. Keep the supplied `HttpClient` alive for the client's lifetime; the serving client neither changes nor disposes it.

| Input | Configuration |
|---|---|
| `endpoint` | HTTP(S) root, or a trailing `/api` or `/v1`. Reverse-proxy path prefixes are retained. Embedded URL credentials, query strings and fragments are rejected. |
| `httpClient` | Caller-owned client; configure timeout and HTTP handlers before use. Its timeout covers each HTTP request, including streamed response bodies. Use a caller cancellation deadline to bound a workflow containing several requests. |
| `apiKey` | Optional third argument; sends a Bearer credential per request without changing default headers. |
| `cancellationToken` | Optional on every asynchronous operation. Use a token with a deadline when a bounded wait is needed. |

| Operation | Native endpoint | Limit |
|---|---|---|
| Health / version | `GET /api/version` | Management health does not prove model readiness. |
| Inventory | `GET /api/tags` and `GET /api/ps` | Separate observations, not an atomic snapshot. |
| Preload / unload | `POST /api/generate` with an empty prompt | Requires a model compatible with that endpoint. |
| Download | `POST /api/pull` | Completes on terminal success followed by end of stream. |
| Metrics | No metrics interface | Unsupported by this client. |

Health checks read `/api/version`; a healthy management API does not establish that a particular model can perform inference. Discovery and capability checks never preload a model or issue a generation request. Capability discovery probes the version and both inventory endpoints. A valid Ollama protocol observation establishes management support; it does not verify mutation permissions or compatibility with every model. Failed authentication, unreachable endpoints or malformed responses leave affected capabilities `Unknown`. After version discovery succeeds, a missing inventory endpoint reports model listing as `Unsupported`.

## Request a model download or change its residency

These operations are explicit because they can consume network bandwidth, disk space or device memory. Continue the previous example with:

```csharp
IModelDownloader downloader = server;
var progress = new Progress<ModelDownloadProgress>(update =>
    Console.WriteLine($"{update.Stage}: {update.CompletedBytes}/{update.TotalBytes} ({update.Artifact})"));

const string modelId = "qwen2.5:0.5b";
await downloader.DownloadModelAsync(modelId, progress, cancellation.Token);

IModelLifecycle lifecycle = server;
await lifecycle.LoadModelAsync(modelId, cancellation.Token);
// Use a separate inference client while the model is resident.
// When finished, explicitly request unload:
await lifecycle.UnloadModelAsync(modelId, cancellation.Token);
```

Preloading uses Ollama's documented empty `/api/generate` request, with no user prompt. It applies the server's default keep-alive duration. This operation requires a model compatible with that endpoint; embedding-only models are not automatically redirected to another API, and a server rejection is returned to the caller. Unloading sends the same empty request with `keep_alive: 0` and does not delete model files. A completed call acknowledges the server operation; it does not guarantee indefinite residency or local execution of a remote model.

Downloads use `/api/pull` and report progress per artifact/layer. Missing byte counters remain `null`; they are not zero and are not a whole-model percentage. The task completes only after a terminal `success` message and the end of the response. HTTP errors, streaming `error` messages, invalid counters, malformed JSON and a stream ending before success all fail the task. Unknown additional response fields are allowed. Canceling the HTTP operation does not delete cached layers or guarantee that server-side work was rolled back; Ollama can reuse partial downloads.

## Interpret model state accurately

| Observation | Meaning |
| --- | --- |
| `ModelInstallationState.Installed` | Listed by `/api/tags`: registered in Ollama. A remote-model manifest can be registered without local model weights. |
| `ModelLoadState.Loaded` | Listed by `/api/ps` during the observation. |
| `ModelLoadState.Unloaded` | Registered model absent from `/api/ps`, with no explicit remote metadata. |
| Remote registration without a runner | `IsRemote == true` and `LoadState.Unknown`; remote execution cannot be inferred from the local process list. |
| Runner without a matching registration | `InstallationState.Unknown`; the lists are separate requests and server state may change between them. |
| `IsRemote == null` | No explicit remote metadata was reported. A name suffix is not used to guess locality. |

`SizeBytes` comes from the registered entry, `MemoryBytes` from `/api/ps`'s `size_vram`, and `ContextLength` from the running entry. `MemoryBytes` therefore describes reported VRAM usage, not total system memory. Model identifiers, required response shapes and known numeric fields are validated; duplicate identifiers within either list are rejected. Unused response fields are accepted for forward compatibility.

## Failures and cancellation

All methods accept a cancellation token and pass it through sending the request and reading the body. Caller cancellation is propagated as `OperationCanceledException`. Other request failures use `ServingException`, with `StatusCode` when available and a `FailureKind` distinguishing transport, timeout and invalid response failures. Exception messages exclude server response bodies, API keys and server-provided error text.

`GetHealthAsync` converts network/server failures into `ServerHealth` (`Unreachable`, `Unauthorized`, `NotReady` or `Unexpected`) but still propagates caller cancellation. `GetVersionAsync` and `IsHealthyAsync` are convenience methods on the concrete client.

## Validation and scope

The offline suite exercises installed/loaded/remote state separation, shared-client authentication, malformed/duplicate responses, lifecycle acknowledgement, streaming completion/errors, timeout and cancellation during HTTP body reads. Run it with:

```bash
dotnet test --project tests/Mythosia.AI.Serving.Ollama.Tests/Mythosia.AI.Serving.Ollama.Tests.csproj --configuration Release
```

Separate live checks exercised **Ollama 0.34.4** with **`qwen2.5:0.5b`** on one NVIDIA A40: health/version/inventory, download completion including a cold download, preload/unload observations, sanitized errors and pre-cancellation. Cancellation after partial download progress was verified separately using a larger model. A small prompt sent through native HTTP also received an inference response; that check did not test a Mythosia chat adapter.

An initial pull failed with a server-declared error whose underlying cause was not established; subsequent library retry and cold-download checks passed. The result does not establish a minimum supported version or compatibility with every model, server configuration or future release. Cancellation proves that the local call stops; it does not prove remote rollback.

Model creation/copy/deletion, publishing, process hosting and metrics are outside this release. See the [serving guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/serving.md) for the cross-runtime support and validation matrix.

Protocol references: [registered models](https://docs.ollama.com/api/tags), [running models](https://docs.ollama.com/api/ps), [model preloading and keep-alive](https://docs.ollama.com/faq), [stream errors](https://docs.ollama.com/api/errors), and [Ollama's API specification](https://github.com/ollama/ollama/blob/main/docs/api.md).
