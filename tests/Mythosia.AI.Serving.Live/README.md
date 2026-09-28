# Live serving verification

Run these checks against an **existing test server**. Unit tests with fake HTTP handlers cannot establish that an installed runtime implements the documented protocol. This runner is opt-in, never part of offline CI, and reports connection failures as failures rather than successful skipped tests.

Requires .NET 10 SDK and an Ollama, llama.cpp or vLLM server. It does not install engines or download model weights unless explicitly requested. Use an HTTP(S) server root URL without user info, query or fragment; reverse-proxy path prefixes are supported.

## Inspect an endpoint

```powershell
pwsh -File build/test-serving-live.ps1 -Runtime ollama -Endpoint http://localhost:11434
pwsh -File build/test-serving-live.ps1 -Runtime llamacpp -Endpoint http://localhost:8080
pwsh -File build/test-serving-live.ps1 -Runtime vllm -Endpoint http://localhost:8000
```

By default, these calls only inspect health, runtime info, capabilities, inventory and supported server-wide metrics. A missing or inconclusive metrics capability is explicitly recorded as not verified. Enable the runtime's metrics endpoint to validate it. For an already loaded llama.cpp Router model, `-Model YOUR_ROUTER_MODEL -ModelMetrics` explicitly checks model-specific metrics with `autoload=false`; a sleeping, unavailable or unknown-state model fails the precondition, never automatically loaded for inspection. This read-only check cannot be combined with lifecycle or download verification.

An exact inventory ID can be supplied with `-Model`. Model names, endpoint addresses, response bodies, labels and credentials are not written to the result. For an authenticated server, provide `MYTHOSIA_SERVING_API_KEY` through a secure environment source; do not put a key in command history. Redirect output into ignored `artifacts/` if retaining a report.

## Choose the checks

| Option | What it checks | Required setup |
| --- | --- | --- |
| No operation flags | Health, runtime info, capabilities, inventory and supported server-wide metrics | Running endpoint; enable metrics if you want to verify them. |
| `-Model` | Exact model ID is present | Use the reported inventory ID, not a display name. |
| `-Download` | Native terminal download success, then inventory | Explicit test model and a download-capable server. |
| `-Lifecycle` | Load, observe `Loaded`, unload, observe `Unloaded` | Dedicated model initially `Unloaded`; supported by Ollama or llama.cpp Router. |
| `-ModelMetrics` | Model-specific metrics with `autoload=false` | llama.cpp Router with that exact model already loaded and metrics enabled. Run separately from download/lifecycle. |
| `-InventoryModel` | Resulting inventory ID when a server normalizes the download ID | Only valid with `-Download`. |
| `-TimeoutSeconds` | Overall request/check deadline | Default 120; range 1–86400. Downloads generally need longer. |
| `-NoBuild` | Reuse the local Release build | Build first; omit this after changing code. |

`Unknown` capability is not proof of support. The runner permits an explicitly selected download with `Unknown` support so a configured llama.cpp Router can be tested; it still requires the operation and its completion signal to succeed. Lifecycle verification requires observed support for both loading and unloading.

## Verify model residency

Lifecycle checks are **explicit mutations**. Use a dedicated, initially unloaded model without other concurrent clients. Ollama preload uses its empty generate command, so select a generate-capable model. llama.cpp requires Router mode. The runner waits for loaded state, unloads, and waits for unloaded state. Cleanup has a separate 30-second deadline even after cancellation; remote rollback is not guaranteed if the server fails.

```powershell
pwsh -File build/test-serving-live.ps1 -Runtime ollama -Endpoint http://localhost:11434 -Model YOUR_INSTALLED_MODEL -Lifecycle -TimeoutSeconds 300
pwsh -File build/test-serving-live.ps1 -Runtime llamacpp -Endpoint http://localhost:8080 -Model YOUR_ROUTER_MODEL -Lifecycle -TimeoutSeconds 300
```

## Verify downloads

Downloading is another explicit mutation, may consume bandwidth and disk, and leaves downloaded files on the server. `-Download` waits for native terminal success and checks inventory afterward. It can be combined with `-Lifecycle` when the resulting model starts unloaded. llama.cpp downloading requires the Router's download endpoint and model events; older routers can reject the operation even when the client exposes the interface.

Some servers normalize download IDs: Ollama can add `:latest`, while llama.cpp's cache inventory uses the actual GGUF quantization tag. Prefer an explicit complete ID. When the download request ID and resulting inventory ID differ, supply `-InventoryModel` with the expected exact inventory ID. The runner uses `-Model` for the download, then `-InventoryModel` for inventory verification and optional lifecycle operations. It does not guess aliases or accept an unrelated inventory entry as proof of a successful download.

```powershell
pwsh -File build/test-serving-live.ps1 -Runtime ollama -Endpoint http://localhost:11434 -Model YOUR_TEST_MODEL -Download -TimeoutSeconds 1800
# If downloading an untagged Ollama name, specify its resulting tag explicitly:
pwsh -File build/test-serving-live.ps1 -Runtime ollama -Endpoint http://localhost:11434 -Model YOUR_TEST_MODEL -InventoryModel YOUR_TEST_MODEL:latest -Download -TimeoutSeconds 1800
```

## Test a remote GPU server from your own machine

The library and runner can stay local while the actual engine runs on a rented GPU. Forward the native endpoints through SSH so the client sees the engine's streaming protocol without relying on an HTTP hosting proxy. Replace the SSH port, user and host with your test server's connection details; bind the engine endpoints to loopback on that server.

```bash
ssh -N -p SSH_PORT -L 15434:127.0.0.1:11434 -L 15808:127.0.0.1:8080 -L 15800:127.0.0.1:8000 USER@HOST
```

In a second terminal, select the forwarded endpoint:

```powershell
pwsh -File build/test-serving-live.ps1 -Runtime ollama -Endpoint http://127.0.0.1:15434 -Model qwen2.5:0.5b -Download -Lifecycle -TimeoutSeconds 900
pwsh -File build/test-serving-live.ps1 -Runtime llamacpp -Endpoint http://127.0.0.1:15808 -Model Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M -Download -Lifecycle -TimeoutSeconds 900
pwsh -File build/test-serving-live.ps1 -Runtime vllm -Endpoint http://127.0.0.1:15800 -Model serving-qwen
```

The llama.cpp command requires Router mode with its download endpoint and SSE events; the vLLM example assumes the model was started with the served alias `serving-qwen`. Run engines sequentially if they share a GPU. After testing, save the reports locally, stop the SSH tunnel and delete the temporary cloud resources you created. The runner does not provision servers, enforce a rental budget or delete rented resources.

## Tested configurations and coverage limits

Real management checks passed on one NVIDIA A40 with these configurations. These are tested builds, not minimum supported versions or a guarantee for every deployment.

| Engine | Configuration and model | Committed runner coverage |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b`, initially absent/unloaded | Inspection, fresh download, load/unload with observed state changes. |
| llama.cpp b11146 | Router, `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M`, metrics enabled | Inspection, download, load/unload; separate model metrics after explicit load. |
| llama.cpp b11146 | Single model, same GGUF, alias `serving-qwen`, metrics enabled | Inspection and server metrics. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct`, alias `serving-qwen` | Inspection and server metrics. |

Additional verification used separate probes for pre-cancellation on the public methods, sanitized missing-model errors on Ollama and Router, and cancellation after observable partial download progress. Download cancellation used separate larger models and verified local `OperationCanceledException` propagation and subsequent server health, not remote rollback. Short synthetic native HTTP inference requests returned generated text in each configuration. Those probes are **not additional flags of this runner**, and a passing runner report alone does not establish their coverage.

The first Ollama download attempt failed; a retry and another fresh download after removing the model passed. Its initial failure's exact cause was not established. vLLM's first startup required correcting the virtual environment's executable search path before any library verification. Preserve failed attempts and distinguish engine setup failures from library results when recording a run.

Ollama metrics, Router-wide metrics, and vLLM lifecycle/download remain outside their applicable client contracts. No engine embedding, application chat-adapter, throughput or retrieval-quality benchmark is implied. Keep execution reports and engine logs under ignored `artifacts/` or CI artifacts, without committing credentials, account identifiers or local report links into public guides.

## Direct invocation and result codes

Equivalent cross-platform commands use `dotnet run --project tests/Mythosia.AI.Serving.Live --configuration Release -- [--lifecycle] [--download] [--model-metrics]` with `MYTHOSIA_SERVING_RUNTIME`, `MYTHOSIA_SERVING_ENDPOINT`, optional `MYTHOSIA_SERVING_MODEL`, `MYTHOSIA_SERVING_INVENTORY_MODEL` (download only) and `MYTHOSIA_SERVING_TIMEOUT_SECONDS` environment variables. Omit the bracketed options for read-only inspection. Ctrl+C cancels local work. Missing or invalid configuration exits with code 2; failed checks exit with code 1; successful requested checks exit with code 0. Reports identify the verification mode and whether healthy-server contact actually occurred; an unreachable target is never described as an observed live server. A passing inspection is not a chat, embedding, performance or GPU-capacity test.
