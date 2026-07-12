# Release Notes

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
