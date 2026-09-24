# Keep long Claude Fable 5.1 tasks observable

[Claude Opus 5.5](providers.md#claude-opus-55) is an unreleased addition with always-on thinking, default medium effort and omitted display. Explicitly request readable progress; its defaults and model-binding rules differ from Fable 5.1.

> Fable 5.1 controls require `Mythosia.AI` 8.0.0 and `Mythosia.AI.Abstractions` 4.0.0 or later. Existing Run, reasoning/search, and GPT-6 Astra APIs retain their 7.1.0 / 3.1.0 minimum versions.

## Why use these controls?

A document investigation may involve several searches and tool calls before it produces an answer. Your application may need to show what is happening, require a check for just the current turn, or continue after changing earlier conversation content. Fable 5.1 adds controls for these cases, but preserved thinking also makes the conversation history part of the request contract.

Use the [Run API](execution-api-transition.md) to observe and cancel the task, [common reasoning and search options](reasoning-and-search.md) to choose its effort and sources, and the Claude-specific settings below for progress and history handling. A model's native capabilities do not imply that every provider API is exposed by Mythosia.

## Select the model and effort explicitly

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` selects `claude-fable-5-1`. `ClaudeMythos5_1` selects `claude-mythos-5-1` and requires Project Glasswing access. The existing Fable 5 and Mythos 5 constants remain available. Both 5.1 models accept text and images and produce text, with a 1M-token context window and up to 128K output tokens. [Model overview](https://platform.claude.com/docs/en/models/fable-5-1/overview).

The native model default is `high`, while Mythosia preserves its existing `ClaudeReasoningEffort.Auto` mapping from `ThinkingBudget`: enabled budgets map to `High`, `XHigh` at 32,768, and `Max` at 100,000. A reasoning-off request uses low adaptive effort with readable thinking omitted. Choose `High` explicitly when that is the behavior you want; `Auto` does not mean that the library always omits effort and delegates to the model default.

## Show progress between tool calls

`ClaudeThinkingDisplay.Updates` requests readable progress updates while keeping reasoning hidden. `Summarized` includes summarized reasoning as well; `Omitted` suppresses readable thinking blocks. Updates depend on the model producing them and are not a fixed-interval heartbeat. [Progress updates](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta).

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

Updates use the existing `StreamingContentType.Reasoning` event; enable reasoning observation with `StreamOptions.FullOptions` or `StreamOptions.Default.WithReasoning()`. For a non-streaming completion, read `service.LastThinkingContent` after the call. Progress text is separate from the final answer and does not expose raw chain of thought.

## Keep per-turn changes out of earlier history

Fable 5.1 thinking blocks are bound to the system prompt, tools, and earlier messages that produced them. Rewriting those inputs while retaining later thinking can invalidate it. A turn-scoped instruction is useful for a requirement such as checking the support policy before answering this turn: it is appended to the conversation and kept there, then stops applying after a later user message. This avoids repeatedly rewriting a top-level system prompt. Changing effort and appending turn instructions are separate controls.

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

Both helpers capture instructions for the next logical request. Mythosia appends a system message after the user input or tool results, retaining earlier messages. `WithTurnInstruction` uses `clear_at: "next_user_message"`; within one logical request the library appends the instruction again after each tool-result turn so it remains effective until that request ends. `WithConversationInstruction` persists for later turns. Configure either helper before starting work; neither is `run.SteerAsync` or an instruction injected into an already running response.

To adjust effort between requests while preserving an eligible cache prefix, use `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` from `Mythosia.AI.Extensions`. The library sends a per-message effort update and keeps its history. Supported combinations are described in the [common guide](reasoning-and-search.md). Per-request system prefixes/suffixes from `AIRequestContext` become appended turn instructions for 5.1, rather than rewriting an earlier system prompt.

Fable 5.1 can consume earlier Claude thinking, but earlier models cannot consume its thinking. Mythos 5.1 shares the 5.1 capabilities and does not enforce Fable's prefix-binding check. Treat history edits, model switches, and dropped thinking as observable changes rather than assuming that the same reasoning survived. [Migration guide](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## Diagnose a deliberate history change

`ThinkingPrefixMismatchBehavior = null` leaves enforcement to the provider's account policy. `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` explicitly requests server validation. User edits to history, `SystemMessage`, or tools are sent to Anthropic; a mismatched prefix with `Error` produces the provider's 400 response. Retrying the same invalid request does not fix it.

If your application intentionally changes earlier content and accepts losing the affected reasoning, opt into `DropBlock`. Mythosia sends the control to Anthropic; it does not silently strip thinking before the request.

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` exposes the provider's reported `Type`, `Path`, and `Reason`, with `ResponseId` and `Model` for attribution. `prefix_binding_mismatch` indicates a changed prefix; `model_binding_mismatch` indicates thinking that the target model cannot read. A drop means reasoning was discarded, not repaired. Keep the transcript intact when preservation is required, and use a fresh conversation when you want to reset it.

Mythosia preserves wire history to avoid incidental changes from internal RAG/context processing. Automatic local compaction is blocked for ordinary Fable 5.1 conversations in the default/`Error` path. `DropBlock` allows it but can discard reasoning and does not guarantee cache hits. The separate `CachePreservation.Required` option retains its stricter history guards. Common options such as `WithWebSearch()` are consumed after each request. Omitting them on the next turn changes the native tools array and can cause a prefix mismatch. Reapply the same tool/search settings when preserving history; use `DropBlock` or a new conversation for an intentional change. These options are not automatically carried forward.

The preserved wire snapshot belongs to the service and its `ChatBlock`. Copying only the `ChatBlock` into a new service does not transfer earlier RAG/context or turn-system snapshots. Continue with the same service and chat when preserving reasoning; if you moved only raw history, start a new conversation rather than assuming preservation.

## Use ordinary tool selection

Fable 5.1 and Mythos 5.1 reject forced tool selection. Leave `ForceFunctionName` unset and describe when the registered tool should be used in the request. `FunctionsDisabled` remains available when a turn must not call tools. For a typed response, use the existing structured-output API instead of forcing a function only to obtain JSON.

## Know which changes belong to the server

| Native option | Required Anthropic beta |
| --- | --- |
| Per-message effort | `mid-conversation-output-config-2026-07-01` |
| Turn-scoped system message | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Thinking binding controls and `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia adds the relevant header when the corresponding supported setting is enabled. Enabling one beta does not opt into all the others. The integration does not introduce server-side compaction, native tool-addition/removal blocks, or automatic model fallback.

Both models require the provider's applicable 30-day retention arrangement; ZDR requires explicit Anthropic authorization. Adaptive thinking is always enabled, manual `budget_tokens` and disabled thinking are unavailable, and custom sampling parameters are not sent. Account access and retention are server requirements. [Migration requirements](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

Text watermarking, supported media provenance, and cache-read pricing are applied by Anthropic. They do not require a new Mythosia request option. This integration does not add a media-provenance creation API, a watermark switch, or a billing control. See [what changed in Fable 5.1](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1).
