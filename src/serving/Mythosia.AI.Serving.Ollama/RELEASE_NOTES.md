## v1.0.0

### Added

- `OllamaServer` implements the shared `IModelServer`, `IModelLifecycle` and `IModelDownloader` contracts for an existing Ollama server.
- Server version and classified health checks through `/api/version`, plus installed and running model observations combining `/api/tags` and `/api/ps` without assuming remote-model residency.
- Explicit model preloading through an empty `/api/generate` request and unloading through `keep_alive: 0`; these operations do not delete model files or host the server.
- `/api/pull` downloads with per-artifact progress, explicit terminal-success verification, streaming error handling and cancellation through HTTP response reads.
- Per-request authentication, reverse-proxy endpoint-prefix preservation, validated identifiers and known numeric response fields, and errors that omit server bodies and API keys.

### Compatibility

- Targets .NET Standard 2.1, references Mythosia.AI.Serving.Abstractions 1.0.0 and Newtonsoft.Json 13.0.4, and has no Mythosia.AI core dependency.
- Model preloading requires compatibility with Ollama's generation endpoint. Capability flags do not promise permission, availability or support for every model.
- Installed means registered in Ollama, including possible remote manifests; loaded state comes from the running-model endpoint. Separate observations do not form an atomic snapshot.
- Cancellation does not roll back server state or delete downloaded layers. Chat, embeddings, model deletion and process hosting remain outside this management client.

### Internal

- Adds offline HTTP regression tests for discovery, lifecycle requests, download completion/error validation, authentication isolation, safe failures and request/body cancellation.
- Live checks against Ollama 0.34.4 with `qwen2.5:0.5b` cover inspection, download completion (including a cold download), preload/unload, sanitized errors and pre-cancellation. Cancellation after partial download progress used a separate larger model. Native HTTP inference was checked separately from the management client.
- An initial server-declared pull failure had no established root cause; subsequent library retry and cold-download checks passed. Testing used a small model on one NVIDIA A40 and does not guarantee every version, model or deployment, or remote rollback after cancellation.
