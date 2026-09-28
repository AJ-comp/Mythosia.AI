# Release Notes

## v1.0.0

### Added

- Dependency-free .NET Standard 2.1 contracts: `IModelServer`, `IModelLifecycle`, `IModelDownloader`, and `IModelMetricsProvider`.
- Immutable runtime, health, model, capability, progress, and labeled metric observations, with explicit unknown states.
- Shared `ServingException` and failure classification; cancellation on every asynchronous operation.

### Internal

- Document operation selection, unknown versus unsupported states, cancellation semantics, and caller ownership of each concrete client's supplied `HttpClient`.
- Exercise the shared contracts through Ollama 0.34.4, llama.cpp b11146 (router and single-model) and vLLM 0.30.0 with small Qwen models on one NVIDIA A40. These observations do not establish a minimum runtime version or universal compatibility.

### Compatibility

- Management of existing HTTP servers is independent of chat, embeddings and RAG.
- Optional contracts avoid requiring unsupported commands from every implementation. Existing vLLM-specific APIs remain available in `Mythosia.AI.Serving.Vllm` 1.1.0.
