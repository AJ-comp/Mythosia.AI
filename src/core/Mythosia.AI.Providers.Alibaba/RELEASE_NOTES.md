# Mythosia.AI.Providers.Alibaba - Release Notes

## v3.0.0

> This coordinated major release changes public contracts. See the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) before upgrading the package family.

### Added

- Inherits pure `GetCapabilities()` inspection on Qwen services and request builders. Definitions reflect endpoint mode, captured provider settings, model overrides and Ollama wire-ID mapping; custom deployment support stays Unknown when not known. UI controls and invocation validation use the shared capability definitions, with existing request validation retained. See [capability inspection](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/model-capabilities.md).

- Inherits the major `AIRun.Result` migration to `Task<AIRunResult>`, with accumulated `.Text`, reported usage/sources, captured provider/requested model, actual model when reported, library rounds, and finish details. Stream observation is optional; ordinary `GetCompletionAsync` remains a string API. Existing Run string callers must read `(await run.Result).Text` and rebuild. See [migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result).
- `AIRunResult.RequestedModel` records the model ID actually sent: the captured `ModelIdOverride` when set, or the endpoint-specific model ID, including Ollama mapping such as `qwen3-32b` to `qwen3:32b`. A builder retains its captured override after service defaults change; `Model` remains the separate server-reported model.
- **Ordinary completion cancellation:** Qwen forwards the caller token through DashScope, vLLM and Ollama HTTP requests, local tools and later rounds. The inherited completion, typed, builder and message-chain APIs accept the token. Caller cancellation is distinct from request-policy timeouts; remote generation or billing cancellation is not guaranteed. See [the common contract](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation).

### Internal

- Inherits the shared streaming terminal guard: later content or changed finish reasons fail the round before saving its response or executing tools. A final delta in the first terminal event and trailing usage-only events remain supported.

- Qwen profile inspection shares only native mode flags with execution through the pure capability-profile hook; queries do not invoke execution preparation or serialize tool defaults.

- Inherits common tool return normalization, cooperative cancellation, and error-result handling from the core service; Qwen uses the same execution contract without requiring provider-native async tool support.

- Inherits `CreateRequest(...)` and immutable request builders from the core service. Common and Qwen provider defaults are captured per request; existing Qwen reasoning/search capability limits and shared-conversation rules remain. See the [request guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md).

- Rebuilt against `Mythosia.AI` v8.0.0 and its provider request-option capture hooks, with `Mythosia.AI.Abstractions` v4.0.0 as an indirect dependency.

### Compatibility

- Existing source callers can omit the new completion token. Qwen endpoint behavior and Run controls remain; rebuild callers for the changed completion signatures. Claude-specific options are not added to Qwen.

---

## v2.0.1

### Fixed

- Qwen's non-streaming completion override now enters the common request-feature scope. Unsupported common reasoning or hosted-search options are validated and consumed before an HTTP request, rather than bypassing validation or leaking into a later call.

### Changed

- Targets `Mythosia.AI` v7.1.0 and its request-scope implementation. Qwen inherits the core `StartRunAsync` controls for output, final results, cancellation, and disposal; existing tools continue through the common round policy.

### Compatibility

- Requires `Mythosia.AI` v7.1.0, which depends on `Mythosia.AI.Abstractions` v3.1.0. No existing Qwen public member is removed or changed.
- DashScope, vLLM, and Ollama retain their provider-specific `ThinkingMode` and endpoint settings. This adapter does not gain native steering, hosted search, or native asynchronous-tool support; Qwen runs report `CanSteer = false`. Functions with `AllowAsync` set still use ordinary execution.
- Qwen remains a chat-completion provider and does not implement `IImageGenerationService`. The v2.0.0 migration notes below still apply when upgrading from v1.x.

---

## v2.0.0

> This is the Alibaba provider release paired with Mythosia.AI v7. Follow the [v7 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v7-migration.md) before upgrading.

### Changed

- **Ordered function-call batches** — Qwen non-streaming and streaming paths now preserve every tool call returned in one assistant turn and send the matching ordered result batch back through the common Mythosia.AI v7 continuation contract.
- **Common handler scheduling** — `FunctionCallingPolicy.ExecutionMode` selects sequential compatibility behavior or bounded-parallel local handler execution; `MaxConcurrency` limits parallel work while provider call order remains stable.

### Removed

- **Legacy image-generation overrides** — `QwenService.GenerateImageAsync` and `GenerateImageUrlAsync` were unsupported stubs inherited from the old core abstraction and have been removed with the Mythosia.AI v7 API surface. Qwen remains a chat-completion provider and does not implement `IImageGenerationService`.

### Compatibility

- Recompiled against and requires `Mythosia.AI` v7.0.0.
- Breaking release for callers that referenced the removed public overrides or derived from the former single-function extraction contract.

---

## v1.2.8

### Fixed

- **Context-overflow rejections reach the core's recovery.** `QwenService` builds and throws its own HTTP failure, so it did not produce the `ContextLengthExceededException` that Mythosia.AI v6.8.0 reacts to — a Qwen model, or any vLLM deployment served through this provider, would have been refused for exceeding the context window and never compacted or re-sent, while every other provider recovered. The rejection now goes through `AIHttpErrorFactory`, which is also where vLLM's wording is recognised.

### Compatibility

- Requires `Mythosia.AI` v6.8.0. No API changes.

---

## v1.2.7

### Fixed

- **Thinking-off was silently dropped for models whose id does not literally contain `qwen3`.** The request builder gated the "thinking off" signal behind a model-name check (`modelId.Contains("qwen3")`), while "thinking on" was always sent. Because a served model name is chosen freely by the operator (vLLM `--served-model-name`, aliases), a Qwen 3 model served under any other name never received `enable_thinking = false` — the caller believed reasoning was disabled while the server kept its default (reasoning **on**). This surfaced as summarization requests emitting long reasoning traces and hitting request timeouts.
- **`enable_thinking` was sent in the wrong shape on vLLM for models outside the `qwen3.5` name path.** It was emitted as a top-level parameter instead of `chat_template_kwargs.enable_thinking`, so vLLM never applied it. Both the on and off signals are now sent in the documented per-platform format for every model.

### Changed

- Thinking parameters are now derived solely from the configured `ThinkingMode` and translated per platform (DashScope / vLLM / Ollama). The provider no longer inspects the model id to infer capability — an unsupported model is expected to ignore the parameter or surface an error, which is preferable to a directive disappearing silently.

### Internal

- Removed the duplicated Qwen 3.5-specific request path and the `IsQwen35` / `IsQwen3ThinkingCapable` name heuristics; both request paths are unified into a single `ApplyThinkingParameters` step. No public API change.

### Compatibility

- No API changes. Callers that set `ThinkingMode` (directly or via `AIRequestProfile.DisableReasoning`) will now actually have that setting reach the server; this can change model behavior where the directive was previously being dropped.

---

## v1.2.6

### Compatibility

- Recompiled for the `Mythosia.AI` v6.4.0 release line. No API changes.

---

## v1.2.5

### Compatibility

- Recompiled for the `Mythosia.AI` v6.3.0 release line. No API changes.

---

## v1.2.4

### Compatibility

- Recompiled for the `Mythosia.AI` v6.2.0 release line. No API changes.

---

## v1.2.3

### Compatibility

- Recompiled for the `Mythosia.AI` v6.1.0 release line. No API changes.

---

## v1.2.2

### Compatibility

- Recompiled against `Mythosia.AI` v6.0.0. No API changes.

---

## v1.2.1

### Compatibility

- Recompiled against `Mythosia.AI` v5.3.0. No API changes.

---

## v1.2.0 - Mythosia.AI v5.2.0 Binary Compatibility

### ✅ Compatibility

- Recompiled against `Mythosia.AI` v5.2.0 (Abstractions split: `AIService` now implements `IAIService`)
- No API changes — fixes `TypeLoadException` when used alongside `Mythosia.AI.Abstractions` v1.0.0

---

## 🚀 v1.1.0 - Mythosia.AI v5.1.0 Compatibility & Token Usage Support

### **Token Usage in Streaming**

`QwenService` streaming now reports token usage (input, output, cached, reasoning tokens) on `Completion` events via `StreamingContent.Usage`, inherited from the core package.

### ✅ Compatibility

- Compatible with `Mythosia.AI` v5.1.0
- Breaking: `StreamOptions.IncludeTokenInfo` / `WithTokenInfo()` removed in core package (see Mythosia.AI v5.1.0 release notes for migration guide)

---

## 🔧 v1.0.2 - Mythosia.AI v5.0.1 Compatibility

### **Streaming Architecture Alignment**

- Aligned with Mythosia.AI v5.0.1 Template Method streaming refactor: `QwenService` now overrides `StreamRoundAsync` instead of `StreamAsync`, inheriting base class round-loop management, `StatelessMode` handling, and automatic conversation summary policy.

### ✅ Compatibility

- Compatible with `Mythosia.AI` v5.0.1
- No breaking changes

---

## 🐛 v1.0.1 - Thinking Request Handling Fix

### **DashScope Qwen 3.5 파라미터 포맷 수정**

DashScope 엔드포인트에서 Qwen 3.5 thinking 파라미터가 `chat_template_kwargs.enable_thinking`으로 잘못 전송되던 문제를 수정했습니다. DashScope는 top-level `enable_thinking` 파라미터를 사용합니다.

### **vLLM / DashScope 요청 경로 분리**

vLLM과 DashScope가 동일한 `chat_template_kwargs` 경로를 공유하던 문제를 수정했습니다.

| Platform | Thinking On | Thinking Off |
|---|---|---|
| DashScope | `enable_thinking = true` | `enable_thinking = false` |
| vLLM | `chat_template_kwargs.enable_thinking = true` | `chat_template_kwargs.enable_thinking = false` |
| Ollama | `reasoning.effort = "high"` | _(파라미터 생략)_ |

### **Qwen3 모델 thinking-off 명시 전송**

Qwen3 thinking-capable 모델에서 `ThinkingMode`가 off일 때 DashScope / vLLM에 `enable_thinking = false`를 명시적으로 전송하도록 수정했습니다. 이전에는 파라미터가 생략되어 서버 기본값으로 thinking이 의도치 않게 활성화될 수 있었습니다.

### ✅ Compatibility

- Compatible with `Mythosia.AI` v5.0.0
- No breaking changes

---

## 🚀 v1.0.0 - Package Documentation, Qwen 3.5 Request Handling, and Request Profile Integration

### **NuGet Packaging Metadata and Package Docs**

This release also includes the package-level documentation and NuGet metadata alignment that had previously been tracked separately.

- Added package `README.md`
- Added package `RELEASE_NOTES.md`
- Added NuGet readme metadata to the project file
- Added package tags, description, and project URL metadata
- Added packaging entries so package documentation files are included properly

### **Expanded `AlibabaModels` Catalog**

The package now exposes a broader built-in Qwen model catalog through `AlibabaModels`.

Added coverage includes Qwen 3 and Qwen 3.5 families such as:

- `AlibabaModels.Qwen3_235B`
- `AlibabaModels.Qwen3_32B`
- `AlibabaModels.Qwen3_5_397B`
- `AlibabaModels.Qwen3_5_27B`
- `AlibabaModels.Qwen3_5_0_8B`

This makes it easier to target newer Alibaba model variants without hardcoding IDs in application code.

### **Qwen 3.5 Thinking Request Handling**

`QwenService` now applies Qwen 3.5-specific request shaping when thinking mode is enabled.

- `vLLM` and DashScope-style requests use `chat_template_kwargs.enable_thinking`
- `Ollama` requests continue to map thinking mode through reasoning parameters

This keeps thinking-mode behavior aligned with how different Qwen 3.5 endpoints expect the request payload.

### **`AIRequestProfile.DisableReasoning` Integration**

With the core `Mythosia.AI` v5.0.0 request-profile APIs, `QwenService` now respects per-request reasoning disablement.

When `AIRequestProfile.DisableReasoning` is set, the provider temporarily turns `ThinkingMode` off for that call and restores the previous state afterward.

```csharp
var answer = await service.GetCompletionAsync(
    "Summarize this policy without reasoning output.",
    new AIRequestProfile
    {
        DisableReasoning = true
    });
```

### ✅ Compatibility

- Package version advanced to `v1.0.0`
- Compatible with `Mythosia.AI` v5.0.0
- No breaking changes
