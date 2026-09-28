# Mythosia.AI.Serving.Abstractions

Manage different model servers without coupling your dashboard or operational tools to a runtime's response format. This package defines small, independent contracts for inspecting an **already running** server and explicitly managing its models.

Use it to keep a model selector, readiness check or operations dashboard independent of Ollama, llama.cpp and vLLM. The package contains contracts and immutable observations; install a concrete client to make HTTP requests.

## Version and installation

This README describes **1.0.0**. It targets .NET Standard 2.1, has no NuGet dependencies, and does not depend on the AI chat or RAG packages.

```bash
dotnet add package Mythosia.AI.Serving.Abstractions --version 1.0.0
```

Installing a concrete Serving package also brings in these contracts. For source development, reference the project in this repository.

## Choose the contract for the operation

| Contract | Purpose |
|---|---|
| `IModelServer` | Read runtime identity, health, model inventory and observed capabilities |
| `IModelLifecycle` | Explicitly request model load or unload |
| `IModelDownloader` | Download a model and wait for the server's completion event |
| `IModelMetricsProvider` | Read server-wide metric samples, preserving labels |

Concrete clients are implemented by [`Mythosia.AI.Serving.Ollama`](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/serving/Mythosia.AI.Serving.Ollama), [`Mythosia.AI.Serving.LlamaCpp`](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/serving/Mythosia.AI.Serving.LlamaCpp) and [`Mythosia.AI.Serving.Vllm`](https://github.com/AJ-comp/Mythosia.AI/tree/main/src/serving/Mythosia.AI.Serving.Vllm). Their namespaces are specific to each runtime; the contracts use `Mythosia.AI.Serving`.

The optional interfaces identify operations a client can express. They do not promise that the connected server supports those operations. For example, `LlamaCppServer` implements `IModelLifecycle`, but a single-model llama.cpp server cannot accept router lifecycle commands.

## Inspect any supported runtime

This helper accepts a concrete client through `IModelServer`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Serving;

public static class ServerInspector
{
    public static async Task InspectAsync(
        IModelServer server, CancellationToken cancellationToken = default)
    {
        var info = await server.GetInfoAsync(cancellationToken);
        var health = await server.GetHealthAsync(cancellationToken);
        Console.WriteLine($"{info.Runtime} {info.Version}: {health.Status}");

        var models = await server.GetModelsAsync(cancellationToken);
        foreach (var model in models)
            Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");

        var capabilities = await server.GetCapabilitiesAsync(cancellationToken);
        if (server is IModelLifecycle &&
            capabilities.ModelLoading == ServingFeatureSupport.Supported)
            Console.WriteLine("An explicit load command is available.");
    }
}
```

## Interpret observations and completion

| Value or result | Interpretation |
|---|---|
| `ServingFeatureSupport.Unknown` | The probe was inconclusive; it is not evidence of unsupported functionality. |
| `ServingFeatureSupport.Supported` | Endpoint support was observed or inferred from the runtime protocol; mutation permissions and every model were not tested. |
| `ModelInstallationState.Unknown` / `ModelLoadState.Unknown` | The server did not establish that state. Do not render it as missing or unloaded. |
| Nullable size, memory, context length or locality | No measurement was reported; `null` is not zero or local. |
| `ServerHealthStatus.Healthy` | The health probe succeeded; a particular model can still be unavailable. |
| Successful load / unload | The server acknowledged the command; inspect inventory separately for resulting state. |
| Successful download | The client observed the runtime's terminal success condition. |

Capability discovery is read-only and never loads or downloads a model. Inventory gathered through separate native requests is not an atomic snapshot. Capabilities can change and cannot guarantee sufficient memory or success for every request.

Load/unload completion acknowledges the command, not indefinite residency. Download completion requires a terminal success response. Cancelling a call interrupts this client's HTTP work and wait; it cannot promise to reverse a command already accepted by the server. All asynchronous operations accept cancellation tokens.

`ServingException` exposes a nullable HTTP status and `ServingFailureKind` (`Unknown`, `Http`, `Transport`, `Timeout`, `InvalidResponse`). Server-declared operation errors can have no HTTP status or more specific classification. Caller cancellation propagates as `OperationCanceledException`. Ollama, llama.cpp and the new common vLLM operations omit raw error bodies and credentials; existing concrete vLLM APIs retain their diagnostic fields for compatibility and need filtering before logging.

Metric samples preserve labels and can contain Prometheus `NaN` or infinite values. Validate values and select labels before calculating totals across models or engines. `ServerMetrics.RawText` is the original exposition and can contain deployment-specific labels.

## Scope and validation

The contracts have been exercised through the concrete clients against Ollama 0.34.4, llama.cpp b11146 in router and single-model modes, and vLLM 0.30.0. Those small-model checks on one NVIDIA A40 are a bounded compatibility observation, not a guarantee for all runtime versions, models or deployments. See each client's README for its verified operations and limits.

This package does not host engines, start processes, infer text, or implement embeddings. Continue using AI provider and embedding APIs for those operations. See the [serving guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/serving.md) and [release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/serving/Mythosia.AI.Serving.Abstractions/RELEASE_NOTES.md#v100).
