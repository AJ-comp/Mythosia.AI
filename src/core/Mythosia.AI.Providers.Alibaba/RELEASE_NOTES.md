# Mythosia.AI.Providers.Alibaba - Release Notes

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
