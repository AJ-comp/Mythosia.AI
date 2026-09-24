# Choose controls the selected model supports

> Grok 4.7: Requires Mythosia.AI 8.1.0 / Abstractions 4.1.0. [model selection, reasoning and processing speed](providers.md#grok-47)

> GPT-6 Sol/Luna: Requires Mythosia.AI 8.1.0 / Abstractions 4.1.0. [model selection and requirements](providers.md#gpt-6-sol-luna)

For [Claude Opus 5.5](providers.md#claude-opus-55), capabilities expose `Low` through `Max`, including `XHigh`; `None` and `Minimal` are unsupported. `ThinkingToggle` is unsupported and `MaxOutputTokens` is 128000. Hidden display does not mean thinking is disabled. Requires Mythosia.AI 8.1.0 / Abstractions 4.1.0.

A chat screen should offer reasoning, search, tools, or image controls that match the selected connection. Keeping model-name lists in every app duplicates the library’s rules and drifts when a provider, protocol, or deployment changes. Capability snapshots let the app and execution validation use the same model definitions.

This API belongs to Mythosia.AI 8.0.0 / Abstractions 4.0.0. The snapshots are immutable local descriptions of library support, not live account or server probes. Types are in `Mythosia.AI.Models.Capabilities`.

For requests where waiting time matters, use [processing speed](request-building.md#inference-speed): `WithSpeed` keeps the model and reasoning effort, while `Processing` reports what the provider actually applied. Fast is a paid option on supported combinations.

## Before / After

Before: the application maintains its own model lists. The lists below represent application code, not library APIs.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: inspect the configured request, then choose supported options. The final completion call below performs the model request; inspecting capabilities does not.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain the documents.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` distinguishes `Supported`, `Unsupported`, and `Unknown`. Unknown means the library lacks enough information, such as a custom deployment or server-selected model; it does not mean unsupported. The example enables extra reasoning only when known to be supported. For unknown support, choose an application policy such as retaining defaults or allowing an attempted request.

`request.GetCapabilities()` reads the builder’s captured model, provider options and profile. `service.GetCapabilities()` inspects service defaults without consuming pending next-call options. Neither sends HTTP, invokes context callbacks or execution validators, changes history, nor starts work. Returned lists are read-only snapshots. The service query also peeks at pending next-call feature settings; it leaves them available for the actual request. Inspection does not serialize function defaults or hosted-tool parameters, and does not run execution profile preparation or token-budget reservations.

Capabilities describe what the connection can support, not which options you have enabled. Provider, API protocol and mode matter as well as the model name. The explicit wire model reflects provider overrides and Qwen/Ollama ID translation; when no single model is selected, model identity can be `null`. The sample Chat UI refreshes controls from the active connection and its current settings, including registered tools, rather than relying only on the model catalogue. Sampling support can change with reasoning mode or tool availability; query again after those settings change. Unknown support remains visibly distinct from unsupported support.

| API | Meaning |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | Support and levels for common `WithReasoning` settings. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | Provider-native reasoning controls and suggested budget choices. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | Streaming, tools, provider-native asynchronous tools and mid-run instructions. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | Hosted search, cache-preserving reasoning changes, input images and structured output. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | Supported sampling controls and a nullable known output-token limit. |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | Mythosia.AI 8.1.0 / Abstractions 4.1.0: Supported/Unsupported/Unknown processing modes; account access is checked separately. |
| `Provider`, `Model` | Provider and selected wire-model identity; either may be unknown. |

Common and native reasoning are separate. `ReasoningLevels` describes common `WithReasoning` values; `NativeReasoningLevels` describes the provider’s own controls. `ThinkingBudgetPresets` supplies useful UI choices, not every valid budget or an exhaustive numeric range. `AsyncFunctionCalling` means native asynchronous tool execution, not merely that a local handler returns `Task` or runs in parallel. `StructuredOutput` covers the common typed-output API, including prompt-and-repair fallback; it does not guarantee native constrained decoding. Both reasoning-level lists use `ReasoningLevel`; budget presets are integer values.

A capability snapshot does not grant account access, establish live server readiness, or make malformed option combinations valid. Execution keeps its existing validation and errors. Check `run.CanSteer` on the actual running session before steering; a supported model capability alone does not mean the run is still active.

## Inspect image generation separately

Image generation uses an independent model selection. Call `service.GetImageCapabilities(imageModel)` for a specific image model, or omit it for that provider’s image default. A chat request builder does not select an image-generation model. Use `Generation`, `Editing` and `Mask` to decide which image actions to present.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` and `AspectRatios` are typed, read-only option lists. `MaxImages` and `MaxInputImages` are nullable known limits. A listed option is not a promise that every combination is valid: existing size, format, quality, mask and model validation still applies. Custom or unknown image models retain unknown support instead of being labelled unsupported.

For Google, `Resolutions` and `AspectRatios` depend on the selected image model and also govern generation/editing validation. See the [model-specific table](providers.md#google-image-options), including the conservative Flash-Lite 1K policy. Unsupported explicit choices fail before HTTP; custom unknown models retain `Unknown` capabilities and provider-wide option validation.

For a custom `AIService`, override the protected `ResolveRequestCapabilities()` hook when the provider can supply reliable definitions. The default is `AIModelCapabilities.Unknown`. Adding a catalogue entry must not turn an unknown deployment into an unsupported one. `IAIService` consumers do not gain mandatory capability members; these query methods belong to `AIService` and its request builder.

If a custom provider profile changes native mode flags, override `ApplyCapabilityRequestProfile(AIRequestProfile)` and use `SetExecutionSetting(...)` only for the flags needed by the resolver. The default hook does nothing. Common profile overrides are already captured by the builder; inspection never calls `ApplyRequestProfile` or `ApplyProviderSpecificRequestProfile`. Keep this hook free of validation, callbacks, serialization, budget reservation and changes to service or caller-owned state. Its temporary settings are restored after inspection, including when an override throws.

[Request settings](request-building.md) · [Provider and image options](providers.md) · [Run control](execution-api-transition.md)
