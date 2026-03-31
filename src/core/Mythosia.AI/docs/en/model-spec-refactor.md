# [To-Be] Model Metadata (ModelSpec) Refactor Proposal

## Background

- `AIService.Model` is currently a `string`.
- Each service uses string comparisons to determine model capabilities and token limits.
- Adding new models is fast, but logic is scattered and maintenance cost is rising.

## Goals

- **Flexibility**: accept unknown/custom model strings as-is
- **Centralization**: manage known model metadata in one place
- **Compatibility**: minimize breaking changes to existing APIs/behavior

## Proposed Structure (Hybrid)

- Keep `Model` as **string**
- Introduce **ModelSpec (metadata)** as optional
- Resolve **known** models to ModelSpec
- **Unknown** strings fall back to existing logic

### Example ModelSpec Fields (Draft)

- ModelId (string)
- Provider
- MaxOutputTokens (or MaxTokensLimit)
- Capabilities
  - Vision support
  - Reasoning/Thinking support
  - Function Calling support
- Optional defaults: DefaultMaxTokens, etc.

> Note: provider-specific settings (ReasoningEffort, ThinkingBudget, etc.) remain in the **service classes**.

## Resolution Rules

1. When `Model` is set, attempt resolve via **ModelSpecRegistry**
2. **Resolved** ¡æ apply capability/limit from ModelSpec
3. **Not resolved** ¡æ use existing per-service string parsing
4. Unknown strings must still work reliably

## Expected Benefits

- Centralized metadata for known models ¡æ **better maintainability**
- Keeps **flexibility** for custom models
- Enables **gradual migration**

## Migration Sketch

1. Add `ModelSpec` / `ModelSpecRegistry` skeleton
2. Register known models incrementally
3. Apply "Spec-first + fallback" in service parsing
4. Add tests (known model metadata, unknown model path)

## Open Questions

- How to allow user-defined metadata for unknown models (optional)
- Alias/renamed model policy
- Minimum metadata set to include in ModelSpec
