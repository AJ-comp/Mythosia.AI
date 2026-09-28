# Release Notes

## v1.1.0

### Added

- Implements the shared `IModelServer` and `IModelMetricsProvider` contracts from `Mythosia.AI.Serving.Abstractions` 1.0.0. Explicit interface implementations preserve the existing `Task<VllmHealthReport>`, model-card list and `Task<VllmMetrics>` methods.
- Adds `GetInfoAsync()` and read-only `GetCapabilitiesAsync()`. Model-list and metrics support is established from valid responses; missing routes are unsupported, while authentication, transport and malformed-response failures remain unknown.
- Common model observations preserve aliases, display names and reported context lengths. Installation and load state remain unknown; aliases and source metadata are not proof of either. Common metric snapshots preserve individual samples, labels and raw exposition.

### Changed

- Response-body reads now honor caller cancellation as well as the initial HTTP request, including a body stream that needs disposal to release a pending read.
- Common model, information and metrics operations apply the HTTP timeout to the full response, limit response size, and reject duplicate or malformed protocol data. Valid metadata-only Prometheus exposition produces an empty snapshot; empty or unrelated text does not prove metrics support. Legacy concrete parsers remain unchanged.
- `VllmException` also derives from `ServingException`, retaining its existing constructor, concrete `int StatusCode`, error fields and response-body property. New common operations omit raw error payloads and transport messages.

### Internal

- Expand the README with version-pinned installation, complete examples, concrete/common API selection, caller-owned HTTP configuration and the different diagnostic/timeout behavior of the two surfaces.
- Live checks against vLLM 0.30.0 with `Qwen/Qwen2.5-0.5B-Instruct` cover health/info/inventory/capabilities, common metrics parsing, pre-cancellation and absence of lifecycle/download interfaces. Native HTTP inference was checked separately; the checks do not establish Mythosia chat-adapter coverage.
- Verification used a small model on one NVIDIA A40 and does not establish a minimum supported version or compatibility with all models and deployments.

### Compatibility

- Existing public methods, vLLM DTOs, endpoint normalization, authentication and tolerant legacy parsing remain available without source changes.
- Retains .NET Standard 2.1 and Newtonsoft.Json 13.0.4; adds the shared serving-contract package without referencing the Mythosia.AI core.
- No lifecycle or download interfaces are implemented, and capability discovery never loads, unloads or downloads a model.

---

## v1.0.0

### Changed

- Promotes the published `1.0.0-preview` package to the first stable release. The existing public API and runtime behavior are unchanged.
- Updates the package description and README to explain model discovery, health checks and metrics inspection before API details; current release links use absolute URLs suitable for NuGet.
- Adds MIT license, repository and `.snupkg` symbol metadata. The README and release notes remain included at the package root.

### Compatibility

- Existing preview callers can upgrade to `1.0.0` without source changes.
- Retains .NET Standard 2.1 and the sole package dependency `Newtonsoft.Json` 13.0.4.
- Optional model fields and metric names continue to depend on the deployed server version.

---

## v1.0.0-preview

### vLLM Control-Plane Client (Initial Preview)

First package of the `Mythosia.AI.Serving.*` family — the model-server **control plane**, complementing the chat data plane (`Mythosia.AI` / `Mythosia.AI.Providers.*`).

- **VllmServer** — control-plane client for one running vLLM server instance
  - `GetModelsAsync()` — `GET /v1/models` model cards: served aliases, `Root` (the actually loaded model = raw `--model` value), `Parent` (LoRA detection), `MaxModelLen`, plus `DisplayModel` (= `Root ?? Id`) fallback for the undocumented-field caveat
  - `GetModelAsync(servedName)` — alias lookup convenience
  - `GetVersionAsync()` — `GET /version`
  - `IsHealthyAsync()` / `GetHealthAsync()` — `GET /health` classified as `Healthy` / `EngineDead` (503) / `Unauthorized` (401·403) / `Unreachable` / `Unexpected`; never throws on server/network failures
  - `GetMetricsAsync()` — `GET /metrics` parsed into label-preserving Prometheus families (`VllmMetricSample` keeps `model_name`/`engine` labels) + typed convenience getters (`RunningRequests`, `WaitingRequests`, `KvCacheUsage`, `PromptTokensTotal`, `GenerationTokensTotal`, `RequestSuccessTotal`) + `RawText` passthrough
  - Endpoint normalization — accepts server root or `/v1`-suffixed URLs (management routes live at root, `/v1/models` under `/v1`)
  - Optional `apiKey` sent as `Authorization: Bearer` (vLLM `--api-key` / `VLLM_API_KEY`); shared `HttpClient` safe (no `BaseAddress`/default-header mutation)
- **VllmException** — non-success responses parsed from vLLM's uniform OpenAI-style error body (`{"error":{message,type,param,code}}`) into `StatusCode` / `ErrorType` / `ErrorCode` / `ResponseBody` (4 KB-truncated)

### Compatibility

- netstandard2.1, sole dependency Newtonsoft.Json 13.0.4 — no dependency on Mythosia.AI core
- Verified against the vLLM v0.25.0 wire surface

### Deliberate scope exclusions

- Chat/embeddings/rerank (stay on `Mythosia.AI` / `Mythosia.AI.Rag` — no duplicate source of truth)
- `/tokenize`, `/detokenize`, `/tokenizer_info`
- LoRA load/unload endpoints (env-gated), `/load`
- All `VLLM_SERVER_DEV_MODE` endpoints (sleep/wake, reset_prefix_cache, server_info, …)

### Design note

`Mythosia.AI.Serving.Abstractions` (a common serving-runtime interface) is deliberately **deferred**: it will be extracted from two working concretes when `Mythosia.AI.Serving.Ollama` lands, per Framework Design Guidelines ("do not provide abstractions unless tested by several concrete implementations"). `VllmServer`'s method names (`GetModelsAsync` / `GetVersionAsync` / `IsHealthyAsync`) are runtime-neutral and all DTOs are `Vllm`-prefixed so that extraction is additive (`VllmServer : IXxx` in a minor version) and neutral type names stay free.
