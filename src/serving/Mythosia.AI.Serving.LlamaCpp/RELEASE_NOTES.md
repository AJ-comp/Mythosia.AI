## v1.0.0

### Added

- `LlamaCppServer` implements the shared `IModelServer`, `IModelLifecycle`, `IModelDownloader` and `IModelMetricsProvider` contracts for an existing llama.cpp server.
- Native model inventory, build information, classified health and read-only capability probes distinguish single-model servers from routers without speculative load or download requests.
- Installation and load states remain separate; sleeping, downloading, failed and unknown states are preserved where reported. Preset inventory alone is not treated as evidence of installation.
- Router load/unload commands and model-matched download completion using an SSE subscription established before the download POST. Both official progress envelope shapes are accepted.
- Prometheus samples retain labels and special floating-point values. Router metrics require an explicit model and disable automatic loading.

### Internal

- Validate required response fields, duplicate model IDs, numeric counts, management acknowledgements and SSE framing while allowing unused additional fields.
- Forward caller cancellation through HTTP headers and response bodies, enforce the supplied HTTP client's timeout during body reads, and dispose failed or cancelled subscriptions.
- Use per-request credentials without modifying or disposing a shared HTTP client. Error messages omit response bodies, credentials and request URLs.
- Add deterministic fake-HTTP regression coverage for protocol validation, capabilities, authentication, cancellation and timeouts.
- Live checks against llama.cpp b11146 with `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` cover router discovery/download/load/unload, model-specific metrics without automatic loading, errors and pre-cancellation; single-model inspection, metrics and router-command rejection were also checked. Cancellation after partial download progress used a separate larger model. Native HTTP inference was verified separately in both modes.
- Live verification used a small model on one NVIDIA A40. It does not establish a minimum supported build, compatibility with all models/deployments, or server-side rollback after cancellation.

### Compatibility

- Initial stable version targets .NET Standard 2.1 and depends on `Mythosia.AI.Serving.Abstractions` 1.0.0 and Newtonsoft.Json 13.0.4; it has no chat-library dependency.
- Management requires a running server. Router-only operations are rejected locally for verified single-model servers; unverifiable mode and feature probes remain unknown.
- Download availability varies by server build. SSE support alone does not establish support for the download POST route.
- Command acknowledgement does not guarantee model readiness. Cancellation and timeout stop local waiting and HTTP work without promising cancellation or rollback of remote downloads.
