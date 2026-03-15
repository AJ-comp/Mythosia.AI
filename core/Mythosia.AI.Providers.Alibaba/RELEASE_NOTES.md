# Mythosia.AI.Providers.Alibaba - Release Notes

## 🚀 v5.0.0 - Package Documentation, Qwen 3.5 Request Handling, and Request Profile Integration

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

- Package version advanced to `v5.0.0`
- Compatible with `Mythosia.AI` v5.0.0
- No breaking changes
