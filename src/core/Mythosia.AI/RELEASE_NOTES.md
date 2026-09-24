# Mythosia.AI - Release Notes

## v8.1.0

> This minor release requires Mythosia.AI.Abstractions 4.1.0 and retains the v8 public contracts. Existing service defaults remain unchanged.

### Added

- **DeepSeek Responses, Files and V4 Pro:** select text-only `AIModels.DeepSeek.V4Pro`, or retain the default vision-capable Flash. Opt-in `UseResponsesApi` reuses completion, streaming, Run, typed output and local tools with captured full-history replay. Image upload, metadata, listing and deletion plus immutable `DeepSeekImageFileContent` enable Flash image reuse through either transport. Chat UI and RAG rewriting use the current model catalogue and migrate the former `DeepSeekChat` UI label without rewriting arbitrary model IDs. Chat Completions remains the default; background storage, hosted search, native asynchronous tools, steering and image generation are outside this addition.

- **Grok 4.7:** `AIModels.xAI.Grok4_7` selects `grok-4.7` through existing completion, streaming, Run, local function rounds, structured output and image input. Native/common reasoning accepts Low/Medium/High/XHigh; native Auto retains the provider High default and reasoning-off requests are rejected. Capability inspection and Chat UI expose the same choices. Common speed maps Standard to default and Fast to paid priority, preserving reported processing observations. This is not the separate Cursor/Grok Build-only Grok 4.7 Fast variant. Responses-only features, native asynchronous tools and steering are outside this addition. Existing model constants and the Grok 4.5 service default remain. See [configuration and scope](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#grok-47).

- **GPT-6 Sol and Luna:** `AIModels.OpenAI.Gpt6Sol` / `Gpt6Luna` select the new models through completion, streaming, structured output, image input, tools and Run. Sol/Luna support `None` through `Max` (excluding `Minimal`), default to `Medium`, and send sampling only with `None`; Astra retains mandatory reasoning. Standard/Pro use the same selected ID. Native async tools, WebSocket steering, Standard single-agent cache-preserving updates, paid Fast processing, local capabilities and sample UI selection are connected for both models. `DisableReasoning` selects `None` on Sol/Luna without lowering Astra's reasoning requirement. The service default remains unchanged. See [model selection and settings](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#gpt-6-sol-luna).

- **Per-request processing speed:** immutable `WithSpeed(InferenceSpeed.ProviderDefault/Standard/Fast)` builder branches and next-request service extensions select provider processing without changing model or reasoning effort. Supported Anthropic, OpenAI, xAI and Gemini Developer API combinations map to native fast/priority modes; capability inspection retains Supported/Unsupported/Unknown and does not prove account access. Explicit unknown or unsupported modes fail without a client fallback. Fast can cost more and the provider may report a lower tier. `AIService.LastProcessing` and `AIRunResult.Processing` retain immutable per-inference-attempt `AIProcessingInfo` (including server continuations), with requested/applied modes, raw mode and response ID; absent reporting stays unknown. Internal auxiliary calls remain isolated. See [speed selection and observed results](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md#inference-speed).
- **Claude Opus 5.5:** `AIModels.Anthropic.ClaudeOpus5_5` selects `claude-opus-5-5` through existing completion, streaming, Run, tools and common reasoning paths. Untouched settings use medium adaptive effort with readable thinking omitted; low/medium/high/xhigh/max, explicit summarized/progress display, per-message effort, turn instructions and binding diagnostics use existing contracts. Preserve signed thinking, including empty blocks, across turns and tool results. Direct edits to stored Opus 5.5 assistant response content fail before HTTP; send corrections as new user input or start a fresh conversation. None/Minimal, forced tools and assistant prefills are unsupported; sampling is omitted. The service default model is unchanged. Native computer toolsets, task budgets, inline tool changes, compaction and automatic server fallback are outside this addition. See [usage and migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#claude-opus-55).

### Fixed

- **Google image options by model:** Capability lists and generation/editing validation now agree: Flash Image accepts 512/1K/2K/4K and 14 ratios, Flash-Lite Image conservatively accepts 1K and 14 ratios, and Pro Image accepts 1K/2K/4K and 10 standard ratios. All retain `Auto`, which omits the corresponding selector. Unsupported explicit choices throw `NotSupportedException` before HTTP without resizing or fallback. Unknown custom image models retain `Unknown` capabilities and provider-wide option validation. The Flash-Lite 512 documentation discrepancy remains unverified; see the [model matrix and source note](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#google-image-options).
- **Preserve tool results in RAG completion:** The core request-message override stays attached to the initial input of a logical request instead of replacing the newest message after each tool round. Ordinary RAG `GetCompletionAsync` requests keep retrieved context, assistant tool calls and tool outputs together, including image-bearing inputs. Original history and public APIs are unchanged.
- Stateless requests omit the stored conversation summary from both common and Anthropic system-message construction. Existing summaries and stateful requests retain their behavior; internal RAG reranking can isolate evaluations without forwarding prior conversation content.

## v8.0.0

> This coordinated major release changes public contracts. See the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) before upgrading the package family.

### Added

- **Shared model capability definitions:** `AIService.GetCapabilities()` inspects service defaults without consuming next-call options; `AIRequestBuilder.GetCapabilities()` inspects captured request settings. Immutable tri-state capabilities share provider/model/protocol definitions with execution validation and sample UI controls. Common/native reasoning, tools, search, steering, image input, sampling and known output limits remain distinct from enabled options or live account access. `GetImageCapabilities(model)` separately describes image generation/editing/masks and typed choices. Unknown deployments and server-routed models remain Unknown; option combinations retain execution validation. No network, history changes or context callbacks occur during inspection. See [capability inspection](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/model-capabilities.md).

- **Rich Run result (major migration):** `AIRun.Result` now returns `Task<AIRunResult>`. Read `.Text` for the previous accumulated string, or keep the completed snapshot for reported usage, citations, provider/requested/actual model, library round count, and normalized/raw finish reason. Collection does not require observing the stream and survives filtering or observation overflow. Usage is counted once per reported round and excludes separate auxiliary summary requests; missing information remains null/Unknown. Execution and cleanup still finish before the result; errors and cancellation remain exceptions. `GetCompletionAsync` and `StructuredStreamRun<T>.Result` retain their return types. See [before/after migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result). `RequestedModel` is nullable: it captures the explicit model sent, including Perplexity model overrides, and remains null when presets, profiles, or server routing send no single explicit model. It is independent from the actual final response `Model`.
- **Cancelable ordinary completion:** string/Message service overloads, typed completion, immutable request builders, MessageChain send methods, and one-off helpers accept caller cancellation. The token flows through preparation, summaries, actual HTTP send/read, cooperative tools and later rounds. Caller cancellation is propagated as `OperationCanceledException` after cleanup; existing request-policy timeout errors remain distinct. Recorded tool calls and results stay paired, queued calls are skipped, and started non-cooperative tools can delay cleanup. Provider inference/billing cancellation is not promised; normal requests are not converted to background tasks. See [usage and implementation migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/completions.md#completion-cancellation).

- **Consistent local tool execution:** attributed `Task<T>` and `ValueTask<T>` results now preserve the returned object and serialize it like synchronous returns. String results stay raw, null becomes `"Done"`, and no-result tasks are awaited and return `"Success"`. Optional injected `CancellationToken` parameters are excluded from tool schemas. `FunctionBuilder.WithHandler((args, token) => ...)` and `FunctionDefinition.HandlerWithCancellation` support cancellable string handlers while existing handlers remain callable. Exceptions become error results; cancellation sets `IsCancelled` and `IsError`, skips queued calls, and waits for started handlers to keep history paired. This is independent of provider-native `AllowAsync`. See [examples](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/function-calling.md#tool-execution-contract).

- **Independent request configuration:** `AIService.CreateRequest(string/Message)` captures service defaults and returns an immutable `AIRequestBuilder`. Each `With...` returns a new builder; completion and Run execute an internal request snapshot without temporarily overwriting service defaults. Existing profiles, context, function definitions/policies, reasoning, and hosted search can be captured per request. See [Before/After and migration guidance](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/request-building.md).

- **GPT Image 2.5 Sunburst and Flare:** select `AIModels.OpenAI.GptImage2_5Sunburst` or `GptImage2_5Flare` on `ImageGenerationRequest` / `ImageEditRequest`, with `_260908` constants for fixed snapshots. Existing Image API generation and multipart editing support these models' `xhigh` / `max` quality, transparent PNG/WebP, validated size bounds, and input/mask limits. The default stays `GptImage2`; Responses image tools and partial-image streaming are outside this addition. Actual image API tests are prepared for later execution and have not been run. See the [image guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#gpt-image-25).

- **Perplexity Agent API:** research presets, hosted tools, local function rounds, common reasoning/web search/citations, structured output and image input through completion, streaming, and Run. Background response management supports longer provider tasks. A separate Search client returns ranked sources without generating an answer. See the [Perplexity guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/perplexity.md).

- **Grok Imagine Image 2.0:** `XAIService` implements the existing `IImageGenerationService` with `AIModels.xAI.GrokImagineImage2_0` as its independent image default. `GenerateImagesAsync` supports 1–10 outputs; `EditImagesAsync` accepts 1–5 ordered JPEG/PNG/WebP references. Inline base64 responses populate `GeneratedImage.Data`, `MediaType`, and existing result provenance without downloading external image URLs.
- **Typed image sizing:** `ImageSize.Auto`, `Pixels(width, height)`, and `Preset(resolution, aspectRatio)` distinguish exact pixels from resolution classes. OpenAI accepts Auto/Pixels; Google and xAI accept Auto/Preset and reject exact pixels instead of approximating them.

- **Grok 4.6:** explicit model selection and Chat UI settings with `Low` / `Medium` / `High` / `XHigh` effort. `AIModels.xAI.Grok4_6` sends `grok-4.6`; the new `WithGrokReasoning(...)` convenience method retains the existing `WithGrokParameters(...)` API. Common `WithReasoning(...)` supports one logical Grok 4.6 request through completion, streaming, Run, and structured-output repair.

- **Gemini 3.7 Flash and 3.8 Flash:** select `AIModels.Google.Gemini3_7Flash` or `Gemini3_8Flash` for long document and repeated-tool tasks using the existing completion, streaming, Run, function-call, and native search paths. Both models accept `Low`, `Medium`, and `High` effort and have provider limits of 1,048,576 input and 65,536 output tokens.
- **DeepSeek Flash (V4.1 Flash):** default model `AIModels.DeepSeek.Flash` (`deepseek-flash`) with native user image input, local function rounds, reasoning events, and reported cache/reasoning usage through existing completion, streaming, Run, and RAG paths. `WithDeepSeekReasoning(...)` enables thinking and sets persistent `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`). Common `WithReasoning(...)` applies to one logical request and its tool rounds. See [DeepSeek examples](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#deepseek-deepseekservice).

- **Claude Fable 5.1 and Mythos 5.1:** explicit model registration and request handling for `claude-fable-5-1` and the limited-access `claude-mythos-5-1`. Existing Fable 5 and Mythos 5 public model constants remain available.
- **Progress updates:** `ClaudeThinkingDisplay.Updates` requests readable status updates through the provider's beta while keeping reasoning summaries hidden. Non-streaming callers use `LastThinkingContent`; streaming and Run callers receive existing reasoning events.
- **Instructions for an ongoing conversation:** `WithTurnInstruction` captures a reminder for the next logical request and its tool rounds. `WithConversationInstruction` appends a persistent instruction. Their native system messages remain in history; neither method steers an already-running response.
- **Thinking-binding controls:** `WithThinkingBinding` and `ThinkingPrefixMismatchBehavior` select the provider's `Error` or `DropBlock` behavior. `LastInputTransformations` reports dropped thinking blocks, their request-local paths, reasons, and response identity when supplied.

### Changed

- **Stream completion and retry validation:** OpenAI-compatible and DeepSeek streaming reject text, reasoning or tool data after an explicit terminal event; OpenAI-compatible streams also reject changed terminal reasons. The first terminal event may still carry its final delta, and trailing usage-only events remain valid. Invalid rounds are not saved or executed as tools. Typed completion and structured streaming reject `int.MaxValue` repair budgets before provider work and avoid loop-counter overflow. Rejected retry budgets still consume their one-call settings so they cannot affect a later request. Standard read-only snapshots and non-generic dictionary comparers are preserved through the abstractions package.

- **Usage and image validation:** accumulated token counters use checked Int32 arithmetic; overflow fails the stream/Run and settles `Result` after cleanup. Google image parsing rejects malformed inline image data or missing/invalid/non-image MIME instead of returning partial success or guessing PNG. Request snapshots preserve array dimensions/bounds, standard typed-dictionary comparers and Undefined JSON values through the abstractions package.

- **Request ownership and tool cleanup:** request capture rejects cyclic or excessively nested function schemas, and JSON metadata/arguments are detached from their source document or mutable node. Runtime `Task<T>` / `ValueTask<T>` tool results are awaited even when declared as `Task` or `object`; no-result tasks retain their existing success result. Startup cleanup still disposes created sessions when cancellation callbacks throw, preserving the original failure together with cleanup errors.
- **Run result and observation:** explicitly reported total-token counts are preserved through snapshots and round aggregation, including partial usage reports. A returned Run output sequence rejects a second enumeration as well as a second reader registration.
- **Image response and ownership checks:** OpenAI edit requests capture image/mask bytes before delayed multipart uploads; later caller mutations cannot alter the submitted payload. Google image responses reject incomplete additional candidates instead of including them in a successful result.

- Capability inspection avoids execution profile hooks, output-token reservations, and serialization of function defaults or hosted-tool parameter graphs. Custom providers can apply native inspection flags through the side-effect-free `ApplyCapabilityRequestProfile` hook; temporary request settings are restored even after nested queries or exceptions.
- Chat UI controls follow the active connection, registered tools and current reasoning settings, preserving Unknown support distinctly from Unsupported. Sampling choices are refreshed from the shared provider definitions after settings changes.

- DeepSeek anchors request-message overrides to the original user input through tool rounds. The shared legacy streaming loop restores captured request context on every iterator advance and disposal, preserving RAG and dynamic context after the first streamed event.
- Grok 4.6 ordinary and client-function requests omit unsupported penalties; client-function requests also carry the configured output budget and `top_p`.
- Grok 4.6 `Auto` omits `reasoning_effort`, retaining the provider's `High` default; `None` is rejected. Internal reasoning-disabled profiles use `Low`. Streaming and Run expose optional provider reasoning summaries through existing reasoning events. Earlier Grok models reject the newly added `XHigh` level before sending.

- Gemini 3.7/3.8 Flash omit `temperature`, `topP`, and `topK` and reject unsupported `Minimal` / `None` effort. `ThinkingLevel` remains the service baseline; `WithReasoning(...)` applies to one logical request. Without an override, Gemini 3.8 uses the provider's `Medium` default.
- Active model recommendations and Chat UI choices omit retired `deepseek-chat` / `deepseek-reasoner` and the GPT-5/Mini/Nano/Pro and o3/o3 Pro aliases whose underlying snapshots retire on 2026-12-11. Other supported model generations remain available.
- `UseReasonerModel()` retains its signature and selects Flash with thinking enabled at High. Set `ThinkingEnabled = false` to disable thinking; new services keep this default. Common `None` also disables thinking; `Minimal`/`Low` maps to Low, `Medium`/`High`/`XHigh` to High, and `Max` to Max. Common Auto preserves the captured baseline; native Auto omits effort and uses the provider's High default.
- `TranscribeAudioAsync` sends `gpt-transcribe` instead of `whisper-1`; its public signature is unchanged.
- DeepSeek preserves native `reasoning_content` and function-call IDs for later tool rounds and accepts image bytes or public HTTP(S) URLs in user messages. The adapter uses the provider's 393216-token output ceiling while retaining an 8,000-token default request budget. Thinking requests omit unsupported temperature/penalty fields and use `top_p` of at least 0.95; non-thinking requests omit `top_p`.

- Provider request options now share the existing logical request lifetime across Run, RAG, and structured-output repair calls. Internal summarization and query rewriting do not consume pending instructions.
- Claude conversation serialization retains native content and previously transmitted context. Dynamic request-context instructions are represented by appended system messages rather than rewriting the previous prefix.
- NuGet search tags now include `GPT6`, `GPT-6`, `Astra`, `Fable`, and `Fable5.1`.

### Internal

- Final architecture validation passed **2,462 AI unit**, **203 RAG**, and **38 MCP** tests (**2,703 total**, no failures or skips), including 43 cases from the third adversarial review. Release builds, two actual-module UI checks, 13-language documentation validation and DocFX passed. This local suite does not imply that every new feature was tested against a live provider; earlier targeted API runs and prepared-only tests are listed in the [validation record](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Test/README.md#third-major-architecture-adversarial-validation).

- DeepSeek validation on 2026-09-11: the full AI Unit suite passed 1,147/1,147 and RAG passed 109/109; the final strict live suite passed 14/14 with 21 HTTP 200 responses, native reasoning efforts, four client-tool calls, PNG/JPEG inputs, JSON output, and RAG. Release builds and DocFX completed with zero warnings/errors. Initial model arithmetic errors and corrected test assumptions are retained in the [test record](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Test/README.md#initial-validation--2026-09-11-asiaseoul).

### Compatibility

- Token totals now preserve explicit provider-reported values instead of normalizing them to the input/output sum. Consumers and custom providers must not assume equality when the component breakdown is incomplete; final stream and Run totals sum the reported per-round totals.

- `async void` tool methods now fail during registration; return `Task` or `ValueTask` so completion, exceptions, and cancellation cleanup can be observed. Existing synchronous object returns and string handlers remain supported; intentionally returned error text is still a successful string result.

- Request builders are available on `AIService` and its provider classes, with no new mandatory `IAIService` members. Existing completion and service entry points remain. `MessageChain` keeps mutable message construction and delegates execution to the request path. Conversation history is still shared at execution time, and the existing one-active-run guard remains; request isolation does not promise concurrent use of one service. Custom message content and executable delegates retain their references.

- **Intentional Perplexity breaking migration:** the service now calls `/v1/agent`, and `AIModels.Perplexity.Sonar` sends `perplexity/sonar`. Former Sonar-specific helpers and model selections are removed. Use Agent presets or explicit supported models and common citations; use the independent Search API when only retrieval is needed. Old Sonar endpoints are scheduled to retire on 2026-09-27. Background execution is separate from native steering or asynchronous client tools. [Official announcement](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

- DeepSeek `V4Flash`, `Chat`, and `Reasoner` retain their wire IDs with warning-only obsolete annotations. The provider temporarily routes retired `deepseek-v4-flash` to V4.1 Flash; the library does not rewrite it. Thinking-mode forced/required tool choice is rejected; automatic local tools remain supported. Chat Completions is the integrated transport; Responses, Files API/`file_id`, image generation, hosted search, `CachePreservation.Required`, native asynchronous tools, and `SteerAsync` are not added.

- `XAIService` still defaults chat to Grok 4.5; callers opt into 4.6 explicitly. Earlier model settings remain available. This addition does not integrate xAI cache-preserving updates, hosted web/file search, or multi-agent APIs.
- **Breaking image-option migration:** `Quality`, `Background`, and `OutputFormat` now use enums; `Size` uses immutable `ImageSize`; the separate request `AspectRatio` property is removed. The shared output default changes from PNG to `ImageOutputFormat.Auto`. OpenAI resolves Auto to PNG; Google supports Auto or explicit Jpeg, and xAI supports Auto only. Unsupported or undefined enum values fail before HTTP. Choose saved extensions from `GeneratedImage.MediaType`; no transcoding is introduced. Generation/editing methods and image-model defaults remain. See [migration examples](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#image-options-migration).

- Google services still default to Gemini 3.6 Flash. Earlier Gemini model settings remain supported; the new model entries do not add every Google API capability or remove existing web/file-search combination limits. See the [Google configuration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/providers.md#google-googleaiservice).
- Retired DeepSeek and retiring GPT-5/o3 model constants remain with warning-only `[Obsolete]` annotations and unchanged wire values. Direct use of an old constant still sends its old ID. Builds treating warnings as errors may need to migrate the reference.

- Requires `Mythosia.AI.Abstractions` v4.0.0. Completion appends optional cancellation parameters; existing source calls remain valid, but custom interface implementations and compiled consumers require migration or a rebuild. Existing streaming entry points remain. Perplexity-specific API and model changes require the migration described above. Subclasses that override changed public virtual overloads for string/profile/context completion, image helpers, or `RunAgentAsync` must also append and forward the new `CancellationToken`; only the single-`Message` provider override retains its old signature. Method-group delegates targeting a changed signature may need an explicit lambda that passes or omits the token.
- Fable 5.1 and Mythos 5.1 reject forced tool selection; unsupported `any`/named-tool settings fail explicitly. Use automatic tool choice with clear task instructions. Always-on adaptive thinking and model-specific sampling limits remain enforced.
- A caller's explicit history, system-prompt, or tool edits are sent to the provider. `Error` surfaces invalid thinking as an API error; `DropBlock` permits the provider to discard it and report diagnostics. Required cache preservation retains its stricter existing history contract.
- Mythos 5.1 requires Project Glasswing access. Its model-binding behavior matches Fable 5.1, but its provider does not enforce Fable's conversation-prefix check.
- Content provenance and changed cache-read pricing are automatic provider behavior. This release does not add a custom watermark, guarantee cache hits, or implement every hosted Claude tool.
- See the [Fable 5.1 guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/fable-5-1.md) for supported controls, history handling, and examples.

---

## v7.1.0

### Added

- **GPT-6 Astra** — Responses API routing, model-aware output limits and sampling validation, and `WithGpt6Parameters` configuration for reasoning effort, Standard/Pro mode, verbosity, and reasoning summaries.
- **Controllable runs** — `StartRunAsync` returns an `AIRun` with `Result`, output-only `StreamAsync`, `Cancel`, and asynchronous disposal. The startup `onText` callback can accompany one event reader. Registered tools use the existing function-round policy, including `WithMaxRounds`.
- **Mid-turn steering** — supported Astra runs use the Responses WebSocket transport for `SteerAsync`; `CanSteer` reports availability. Other providers run through their existing streaming implementations. Successful steering submission means the instruction was accepted for delivery, not that it has already changed the output.
- **Native asynchronous tools** — `FunctionBuilder.WithAsync()`, `FunctionDefinition.AllowAsync`, and the matching attribute option permit supported Astra calls to overlap model work. A request-owned dispatcher tracks pending calls, bounded handler concurrency, and outputs with their original call IDs.
- **Common reasoning and hosted search** — `WithReasoning`, `WithWebSearch`, and `WithFileSearch` configure completed answers, structured-output requests, and runs. Supported native web tools are available through OpenAI, Anthropic, and Google; existing provider-owned file-search stores are supported through OpenAI and Google.
- **Cache-preserving effort changes** — `CachePreservation.Required` uses configuration updates for supported Astra Standard runs and requests, or the per-message effort protocol on Claude Opus 5, Fable 5.1, and Mythos 5.1. Accepted changes persist in the tracked conversation; ordinary `CachePreservation.None` overrides remain request-scoped.
- **Provider source references** — `LastCitations`, `GetLastCitations()`, `run.Citations`, and citation events retain native web and file references independently of text observation.

### Changed

- `RunAgentAsync` and `RunAgentStreamAsync` now emit non-error `[Obsolete]` warnings pointing to `StartRunAsync` and the existing function-round policy. Their signatures, default ten-step limit, and legacy behavior are retained.
- Common request features are captured once for a logical request, including tool continuations and structured-output repairs. Internal summarization and query rewriting do not inherit the final answer's hosted tools or consume its pending settings.
- Provider-native reasoning, hosted-tool results, and citations are preserved through HTTP, streaming, and supported WebSocket continuations. Anthropic hosted-tool `pause_turn` responses continue through the native protocol.
- Run startup captures built-in message content and per-request settings. Its timeout covers preparation and all rounds; cancellation and disposal wait for provider cleanup and already-started handlers.

### Fixed

- Failed or cancelled, unaccepted OpenAI and Anthropic requests no longer leave persistent reasoning changes or an unverified cache baseline in the conversation. State accepted in earlier successful rounds is preserved.
- OpenAI-compatible completion overrides now enter the common request-feature scope so unsupported options are rejected and consumed instead of bypassing validation or affecting a later request.

### Compatibility

- Requires `Mythosia.AI.Abstractions` v3.1.0. Existing provider-specific configuration APIs remain available.
- `GetCompletionAsync`, including typed overloads, remains public and supported. Existing completion and streaming entry points are not removed. Input-taking service `StreamAsync` methods remain supported during the minor transition; their documentation records planned public withdrawal in the next major release while retaining implementation hooks.
- Native asynchronous tools remain opt-in, with `AllowAsync` defaulting to `false`. Unsupported providers use ordinary handler execution. Enabling permission does not force the model to return an asynchronous call.
- One service supports one active run. A single event reader can join the text callback; the 1,024-event observation buffer may fail if it overflows, while execution, the callback, and `Result` continue. Ending an event reader stops observation; use `Cancel`, the startup token, or run disposal to cancel execution.
- `Result` concatenates all text emitted during the run, including intermediate output and text produced before steering. Steering does not undo output or cancel already-started tools. Citation offsets are local to their provider response/content part, not offsets into the concatenated result.
- Unsupported model, provider, transport, and option combinations fail explicitly. Google web and file search cannot be combined, and Google domain allowlists are unsupported. Anthropic has no native file-store search adapter; use RAG. Hosted file search uses existing stores and does not create stores or upload files.
- Required cache preservation needs tracked, stateful history and a compatible model and endpoint; it blocks incompatible history compaction and does not guarantee a cache hit. Start a new conversation when changing that baseline.
- Custom message-content types retain their original references and must remain unchanged until the run ends. Existing handlers do not accept cancellation tokens, so cancellation does not interrupt an already-started handler. Astra runs use a WebSocket transport; an injected `HttpClient` handler does not replace that transport.

---

## v7.0.0

> This is a breaking release. Follow the [v7 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v7-migration.md) before upgrading production applications.

### Added

- **Complete Claude adaptive-thinking controls** expose low/medium/high/xhigh/max through `ClaudeReasoningEffort`, while `ClaudeThinkingDisplay` selects omitted or summarized reasoning output.

- **OpenAI GPT-5.6 family** — `gpt-5.6` (Sol alias), `gpt-5.6-sol`, `gpt-5.6-terra`, and `gpt-5.6-luna` are available through `AIModels.OpenAI`. `WithGpt5_6Parameters(...)` supports `none`/`low`/`medium`/`high`/`xhigh`/`max`, verbosity, reasoning summaries, and `reasoning.mode: "pro"` through the Responses API. There is intentionally no `gpt-5.6-pro` model ID.
- **Claude Opus 5 and Sonnet 5** — `AIModels.Anthropic` now exposes the generally available `claude-opus-5` and `claude-sonnet-5` model IDs with 128K output ceilings, vision, adaptive thinking, and tool use.
- **Claude Mythos 5** — `AIModels.Anthropic.ClaudeMythos5` exposes the limited-availability `claude-mythos-5` Project Glasswing model with a 128K output ceiling, vision, tool use, and the same always-on adaptive-thinking contract as Fable 5. Live validation requires an approved account.
- **Current xAI Grok support** — `XAIService` now defaults to Grok 4.5 and exposes the `grok-4.5-latest`, `grok-build-latest`, `grok-4.3-latest`, and `grok-latest` aliases. `UseGrok4Model()` selects 4.5 while `UseGrok4FastModel()` selects 4.3.

- **Provider-neutral image generation** — `OpenAIService` now implements `IImageGenerationService`. `GenerateImagesAsync` supports multiple images and provider-supported size, quality, background, output-format, and compression controls; results include all returned images plus model and provider, with optional request-ID and token-usage metadata when returned by the provider.
- **Reference and mask editing** — `EditImagesAsync` sends ordered reference images and an optional mask through the OpenAI image-editing endpoint.
- **Current OpenAI image models** — `AIModels.OpenAI.GptImage2` exposes the default `gpt-image-2` alias and `GptImage2_260421` exposes its current `2026-04-21` snapshot. Image selection remains independent from the chat `Model`.
- **Current Gemini models and images** — `GoogleAIService` defaults chat requests to Gemini 3.6 Flash, adds Gemini 3.5 Flash-Lite, and implements `IImageGenerationService` with Gemini 3.1 Flash Image as its independent default. Reference-image editing, provider-selected inline output, image size/aspect ratio, and JPEG selection are supported without changing the chat model.
- **Gemini safety controls** — each supported harm category can retain the provider default or explicitly select `Off`, `BlockNone`, `BlockOnlyHigh`, `BlockMediumAndAbove`, or `BlockLowAndAbove`.
- **Ordered multi-function batches** preserve every function call returned in one assistant response, its provider ID/index/metadata, and the matching ordered result batch across OpenAI, Anthropic, Google, and xAI in both non-streaming and streaming flows.
- **Configurable handler scheduling** adds `FunctionExecutionMode.Sequential` (the compatibility default) and bounded `Parallel` execution through `FunctionCallingPolicy.ExecutionMode` and `MaxConcurrency`. Parallel handlers may finish out of order, but results are returned to the provider in its original call order and one handler failure is isolated to that call's result.
- **Explicit o3 reasoning-summary control** adds `O3ReasoningSummary` and `WithO3Parameters(...)`. Summaries remain opt-in because OpenAI requires a verified organization to generate them; ordinary o3 reasoning requests do not request a summary.

### Changed

- **Conversation history is no longer silently windowed by message count.** Outgoing requests use the full active history unless `ConversationPolicy` summarizes or trims it. Token-aware policy is now the single context-management mechanism.
- **Image requests use image-workload timeouts.** The default timeout now follows `FunctionCallingPolicy.Vision` instead of the independently selected chat model. Explicit non-default policy timeouts are still respected, and caller cancellation remains distinguishable from an internal timeout.
- **Dependency maintenance** updates `System.Threading.Channels` to the current stable 10.0.10 patch.
- **OpenAI reasoning-model timeouts** allow 300 seconds for the `gpt-5` alias and 600 seconds for Pro model IDs or GPT-5.6 Pro mode when the standard 100-second policy value is in effect. Any non-default `TimeoutSeconds` value remains an explicit override.
- **Nullable annotations now describe real optional state.** Request-scoped policies, forced function selection, streamed usage, provider metadata, and keyless OpenAI-compatible endpoints no longer pretend to be always populated. Null provider fields are omitted from metadata rather than stored as non-null values.

### Fixed

- **Current Claude thinking semantics** request summarized reasoning when adaptive thinking is enabled and accurately treat Fable 5 and Mythos 5 as always-on. Their closest reasoning-off profile uses low effort and omits readable reasoning; Opus 5 and Sonnet 5 use the API's explicit disabled form.
- **Anthropic tool execution safety** requires a complete stream committed with `stop_reason: "tool_use"`. Truncated streams, malformed arguments, SSE errors, refusals, and mismatched stop reasons terminate without executing collected tools.
- **Anthropic terminal responses** return an empty `end_turn` once instead of retrying it, and surface `max_tokens`, `model_context_window_exceeded`, and unsupported `pause_turn` outcomes as failures instead of successful partial completions. Simple string streaming throws on provider Error events instead of yielding the error as ordinary text.
- **Anthropic sampling validation** caps every temperature preset at the API maximum of 1.0. The Chat UI disables controls that the selected Claude model or current integration does not serialize.

- **GPT-5.6 reasoning continuity** uses `reasoning.context: "current_turn"` because Mythosia reconstructs history locally rather than using `previous_response_id`. Tool continuations replay the Responses API's original reasoning and function-call output items before the matching function results in both non-streaming and streaming round loops. Function requests retain `instructions` and enable `parallel_tool_calls`; the returned calls are preserved as one ordered batch while `FunctionExecutionMode` independently controls whether local handlers run sequentially or concurrently. Streaming summaries no longer replay the completed snapshot after its text was already emitted as deltas.
- **OpenAI Responses terminal safety** consumes output and permits tool execution only for a top-level completed response. Failed, incomplete, error, refusal, malformed, and prematurely ended responses now terminate as errors in non-streaming and streaming paths; a failed round neither executes collected tools nor synthesizes a successful completion.
- **OpenAI function request contracts** preserve multimodal text/image parts and image detail through the tool path, keep structured-output `text.format`, honor forced function selection, and build `required` from the declared parameter contract. Fully required tools use strict schemas; tools with optional parameters remain non-strict until the abstraction can express OpenAI's nullable-union form. Empty, malformed, or non-object function arguments fail before handler execution.
- **OpenAI model parameter fidelity** preserves caller-specified output limits, merges verbosity with structured-output settings, resolves GPT-5.5 `Auto` to Medium for the base model and High for Pro, and honors explicit o3 reasoning effort.
- **OpenAI o3 vision and summary routing** keeps image requests on o3 instead of silently switching to GPT-4.1, serializes an explicitly selected o3 reasoning-summary mode, and leaves summary generation disabled unless requested.
- **GPT-5 Pro internal request budgets** reserve enough output space for mandatory high reasoning during library-owned summarization and query-rewrite calls, then restore the caller's setting. Custom request profiles retain their explicit output limit.
- **OpenAI usage details** normalize Responses and legacy cached-input, cache-write, and reasoning-token details into `CachedInputTokens`, `CacheCreationTokens`, and `ReasoningTokens`.
- **Claude 5 request compatibility** sends adaptive thinking instead of the rejected manual budget form and omits unsupported custom temperature. Opus 5 and Sonnet 5 send an explicit thinking opt-out when `ThinkingBudget` is disabled; Fable 5 and Mythos 5 remain always-on at low effort as described above.
- **Anthropic signed tool continuation** preserves and replays complete assistant content blocks, including thinking signatures and redacted-thinking data, in non-streaming and streaming tool rounds. Parallel tool calls now serialize as one assistant turn followed by one user turn containing all tool results.
- **Anthropic GA tool requests** no longer send the retired `tools-2024-04-04` beta header. User-defined tools remain in the ordinary `tools` request body with the current required Anthropic headers.
- **Anthropic refusal handling** no longer treats HTTP 200 responses with `stop_reason: "refusal"` as empty successful completions or executes tools collected from a refused stream. Streaming surfaces an error event with the provider's category, explanation, and usage when present.
- **Current Anthropic output ceilings** now use the API-reported 128K maximum for Sonnet 4.6 and 64K maximum for Opus 4.5, Sonnet 4.5, and Haiku 4.5.
- **Reasoning summary accuracy** — `LastReasoningSummary` is reset for each non-streaming completion and is populated from normal and function-calling Responses output even when the convenience `output_text` field is present.

- **Image failure diagnostics** now use the shared HTTP error translation path and preserve OpenAI's `x-request-id` in `AIServiceException.Data` when the response provides one.
- **Gemini request and terminal fidelity** follows current model-specific sampling/candidate rules, sends API keys only through `x-goog-api-key`, uses native text response schemas, preserves configured safety settings across ordinary and function requests, and treats prompt blocks, malformed payloads, non-`STOP` finishes, or prematurely ended streams as failures before saving output or executing a function. Usage now includes tool-use prompt and thinking tokens in the provider-neutral totals.
- **Gemini batched-function continuation** preserves every `functionCall` part, provider order, call ID, and thought signature; returns all matching `functionResponse` parts together; honors forced function selection; and completes the follow-up model round in stateless mode instead of returning raw handler results.
- **xAI model-specific reasoning requests** serialize `none`/`low`/`medium`/`high` for Grok 4.3 and `low`/`medium`/`high` for Grok 4.5. Grok 4.5 cannot disable reasoning, defaults to high when the parameter is omitted, and reasoning requests omit provider-forbidden frequency and presence penalties. Function and ordinary requests share the same validation path.

### Known limitations

- Anthropic fine-grained tool streaming is not enabled. If malformed tool JSON is received anyway, Mythosia terminates safely instead of executing the tool; it does not yet send the optional `is_error: true` recovery result back to Claude.
- A validated function-call batch is completed after handler execution begins so conversation history cannot contain calls without a matching result batch. The current `FunctionDefinition.Handler` contract does not accept a `CancellationToken`; therefore caller cancellation and `TimeoutSeconds` stop provider requests and prevent a not-yet-started batch, but do not interrupt handlers already running or waiting inside that started batch.

### Removed

- **Unavailable xAI Grok 3 Mini support** — `AIModels.xAI.Grok3Mini`, `XAIService.UseMiniModel()`, model-specific routing, Chat UI exposure, and current tests were removed after `grok-3-mini` disappeared from the account model catalogue. Use Grok 4.3 for configurable reasoning or Grok 4.5 for the current flagship model.
- **`AIService.MaxMessageCount`** and its message-count sliding-window behavior.
- **`ChatBlock.RemoveFunctionMessages()`**, which could leave function results without their originating calls.
- **`AIService.GenerateImageAsync` and `AIService.GenerateImageUrlAsync`**, including every provider override. Use `IImageGenerationService.GenerateImagesAsync` or `EditImagesAsync` and read `GeneratedImage.Data` or `GeneratedImage.Url` from the result.
- **The legacy `gpt-image-1` default and compatibility path.** Image requests now default to GPT Image 2. Callers can still select another provider-supported image model explicitly through the request contract.
- **Retired or deprecated OpenAI model IDs** — `Gpt4Vision`, `Gpt4oLatest`, `Gpt5ChatLatest`, `Gpt5_2Codex`, the three `2025-08-07` GPT-5 snapshot constants, and `Gpt4_1Nano` were removed from the public catalog, Chat UI, examples, and tests. Use a current GPT-5.6 model, the GPT-5/Mini/Nano aliases, GPT-4.1/Mini, or GPT-4o/Mini as appropriate.
- **Retired Claude Opus snapshots** — `ClaudeOpus4_250514` (`claude-opus-4-20250514`) and `ClaudeOpus4_1_250805` (`claude-opus-4-1-20250805`) plus their Chat UI/live-test exposure were removed. Opus 4 retired on June 15, 2026, and Opus 4.1 retired on August 5, 2026. Use `ClaudeOpus4_8` as the official direct replacement.
- **`GrokReasoning.Off`** — use `Auto` to omit the xAI parameter or `None` for an explicit non-reasoning Grok 4.3 request. Grok 4.5 rejects `None` because its reasoning cannot be disabled.

### Compatibility

- Requires `Mythosia.AI.Abstractions` v3.0.0.
- This is a source-breaking release for callers of the removed APIs, retired or deprecated model constants, `GrokReasoning.Off`, and custom `AIService` or `CompletionProtocol` implementations. Function extraction now returns `FunctionCallBatch`, and the handler extension point receives a typed `FunctionCall` instead of a name/argument pair.

---

## v6.8.0

### Added

- **Reactive context-overflow recovery.** When the server rejects a request for exceeding the model's context window, the conversation is compacted and the request is sent again. The limit belongs to the server, so being told "that did not fit" is the only authoritative signal there is — more reliable than any client-side token estimate, and it follows the deployment automatically when its limit changes. Providers translate the rejection into `ContextLengthExceededException` (non-streaming) or an error chunk flagged `context_length_exceeded` (streaming).
- **Streaming recovers inside the round loop.** A round that overflows has emitted nothing yet — the server rejects before inference, so the error is that round's first chunk. That round alone is compacted and replayed: no duplicate output, and the tool results earlier rounds produced are kept rather than discarded.
- **`AIService.ContextRecoveryMaxRetries`** (default `1`, `0` disables). The budget is per attempt unit and the two paths count differently: non-streaming counts whole turns, streaming counts rounds.
- **`AIHttpErrorFactory`** (Abstractions) centralises "did the prompt fit?" across OpenAI, vLLM, Anthropic and Google. Gated to HTTP 400/413 — a rate limit or a server error is never mistaken for an overflow, because a false positive would delete conversation history in response to an unrelated failure.

### Fixed

- **Compaction that could not shrink the request discovered it too late.** The "did this actually shrink?" test ran *after* the summary call and *after* messages were deleted. At the default `MaxMessageCount` of 20 a long agentic run reaches this every time: each tool round appends two messages, so the window fills with material the cut point is not allowed to touch, and everything deletable sits outside the window — where it was never being sent and so cannot shrink anything. Recovery therefore destroyed conversation history, paid for a summary, and returned the original error anyway. The test is pure arithmetic and now runs before either.
- **The summarization request inherited the caller's `AIRequestContext`.** `RequestMessageOverride` replaces the last message of every outgoing request, and a summarization request is exactly one message long — the prompt. The summary that came back was an ordinary answer to the caller's own question, stored as the conversation summary while the messages it was meant to summarize were deleted. Internally-issued requests now run with the ambient context detached.
- **Recovery no longer retries a turn whose tools already ran.** A retry re-enters the provider's round loop from zero, so a tool that sent a mail or created a record would do it again. Nothing at that layer can tell a read-only tool from a destructive one, so it stops and reports `tool-side-effects`.
- **The rewind baseline is captured per attempt, not per turn.** Compaction shortens the history between attempts, so a baseline taken once sat above the message count from then on and rewound nothing — leaving the failed attempt's user message in place for the next attempt to duplicate. Reachable with `ContextRecoveryMaxRetries` of 2 or more.
- **A stream that gave up on recovery ended without its terminating `Completion` chunk** (and its accumulated usage, and the end-of-turn summary policy), which made the termination contract depend on whether recovery happened to be enabled. It now ends the same way regardless.
- **Anthropic's combined overflow wording is detected**: `input length and \`max_tokens\` exceed context limit: X + Y > Z`. Because `max_tokens` rides on every request, the sum crosses the window before the input does on its own — so this, not `prompt is too long`, is the message that actually arrives, and recovery never engaged on Anthropic without it.
- **`MessageChain.SendAsync()` and `RetryLastMessageAsync()` bypassed recovery**, binding to the raw provider overload through ordinary overload resolution. Both now route explicitly.
- **Gemini's legacy streaming path** was the last HTTP failure site still throwing an untranslated `AIServiceException`.
- **`RecoverySkipReason` distinguishes its causes.** `recovery-disabled`, `retries-exhausted`, `stateless` and `summarizing` previously all reported null. Compaction failures now preserve the provider's original stack trace instead of resetting it.

### Known limitations

- **DeepSeek and Perplexity do not recover while streaming.** Both replace `StreamCoreAsync` wholesale — neither supports function calling, so neither needs a round loop — and recovery lives in that loop. An overflow still surfaces as an error chunk carrying the `context_length_exceeded` flag, so a consuming app can identify it. Their non-streaming path recovers normally: both override only the raw provider overload, so the request still travels through the recovery wrapper.
- **A summarization request still consults `SystemMessageProvider`.** Detaching the caller's per-request context does not stop the service-level provider from being asked again for the summarization call, so a service configured with one contributes its system message to the summary prompt too. Harmless for a provider that returns a system prefix or suffix; a provider that statically supplies a `RequestMessageOverride` would still displace the prompt.
- **Non-streaming stops instead of replaying completed tool rounds**, per the fix above. Making it replay per round the way streaming does would mean moving the round loop out of each provider; that is a v7.0-scale change.
- **The window can still make recovery impossible.** When `MaxMessageCount` clips everything the cut point may remove, there is nothing to compact and recovery reports `window-clipped` — correctly, and now for free. Removing the window in v7.0 removes the situation; until then, setting `MaxMessageCount` to a large value avoids it.

### Deprecated

- **`ChatBlock.RemoveFunctionMessages()`** joins `AIService.MaxMessageCount` as `[Obsolete]` for removal in **v7.0**, and its message now says which version. Dropping function messages leaves function results without their originating call, which the chat/completions wire format rejects.

### Internal

- Offline unit tests for the whole feature (`Common/ContextLengthRecoveryTests.cs`, `ContextLengthStreamingRecoveryTests.cs`, `ContextLengthErrorTranslationTests.cs`) — no API key needed. The non-streaming fixture now appends the user message *before* failing, the way every real provider does, so the rewind is actually observable; the streaming fixture snapshots the summary-call count at the moment the error surfaces, so the end-of-turn summary cannot be mistaken for a recovery compaction.

### Compatibility

- Additive. Recovery engages only on failures that previously propagated as errors; set `ContextRecoveryMaxRetries = 0` for the pre-6.8 behaviour.
- **One behaviour change beyond recovery, for services configured with `WithSystemMessageProvider`.** Routing `MessageChain.SendAsync()`, `RetryLastMessageAsync()` and `GetCompletionWithImage*Async()` through the three-argument overload also makes them consult `SystemMessageProvider`, which they previously skipped. `GetCompletionAsync(string)` has always consulted it, so this removes an asymmetry rather than introducing one — but a caller who relied on those paths *not* receiving the provider's system message will see different system content. Services without a provider are unaffected.
- Requires `Mythosia.AI.Abstractions` v2.5.0.

---

## v6.7.0

### Fixed

- **The message-count window can no longer drop the anchoring user message.** `GetLatestMessages()`'s sliding window (`MaxMessageCount`, default 20) sliced off the oldest messages purely by count. Agentic runs (`RunAgentAsync` / `RunAgentStreamAsync`) append two messages per tool round, so a long run could push the originating user query out of the window — producing a request with **no user message at all**, which some OpenAI-compatible servers reject outright (e.g. vLLM/Qwen: `400 "No user query found in messages"`). The window now re-anchors the most recent cut-off user message at the front whenever the sliced window contains no user turn, so the model never loses the query it is working on. (A related guard, `EnsureUserFirstMessage`, existed but only ran on the Anthropic/Google paths and inserted a synthetic placeholder rather than the real query.)

### Deprecated

- **`AIService.MaxMessageCount` is now `[Obsolete]` and will be removed in v7.0.** A count-based window is a poor proxy for context size and silently interferes with token-based management — two competing sources of truth. From v7.0 the full conversation history is sent unless a `ConversationPolicy` (token-based summary/trim) manages it, matching mainstream SDK behavior. To opt out of windowing today, set the property to a large value.

### Internal

- Offline unit tests for the window behavior (`Common/MessageWindowTests.cs`, no API key): re-anchoring when the user turn is sliced off, most-recent-user selection, and no-op behavior for normal/untruncated/user-less conversations.

### Compatibility

- No API surface changes other than the `[Obsolete]` attribute (compile-time warning only). Behavior change is limited to the previously-broken case where the window emitted a request without any user message.

---

## v6.6.0

### Added

- **Claude Fable 5** (`claude-fable-5`, constant in Mythosia.AI.Abstractions v2.4.0) — Anthropic's new top model tier above Opus (1M context window, 128K max output). Its API contract is handled automatically:
  - `temperature` is omitted (Fable 5 rejects any non-default temperature, like Opus 4.7/4.8).
  - Extended thinking uses adaptive mode (`thinking.type=adaptive` + `output_config.effort`); the legacy `budget_tokens` form is rejected.
  - When thinking is disabled the `thinking` parameter is omitted entirely — Fable 5 rejects an explicit `thinking.type=disabled` (Opus 4.7/4.8 accept it).
  - Vision, extended-thinking support detection, and the 128K max-output ceiling are wired into the existing model gates.

### Fixed

- **Opus 4.7 / 4.8 max output tokens** — `GetModelMaxOutputTokens()` now returns 128K for these models; they previously fell into the generic `opus-4` 32K bucket, which capped the thinking-budget `max_tokens` auto-adjustment.
- **`QuickAskAsync` / `QuickAskWithImageAsync` provider routing** — `GetProviderFromModel` compared model ids against uppercase prefixes (`"Claude"`, `"Gpt"`, …) while real model ids are lowercase, so every model except `o3*` threw `ArgumentException`. Matching is now case-insensitive on real id prefixes (`claude`, `gpt`/`chatgpt`/`o3`, `grok`, `gemini`, `deepseek`, `sonar`). In addition, `CreateService` now applies the requested model via `ChangeModel` — previously the created service silently ran on its provider default model.
- **Vision gate silently swapped Sonnet/Haiku models** — `GetCompletionWithImageAsync`'s vision-capability check only recognized `claude-3` / `claude-4` / `opus-4` patterns, so vision calls on `claude-sonnet-4-x` / `claude-haiku-4-5` (and Fable 5 before this release) were silently switched to Sonnet 4.6. The gate now recognizes all vision-capable families (`sonnet-4`, `haiku-4`, `fable-5` added).

### Internal

- **Offline unit tests added** (`tests/.../Common`, `[TestCategory("Unit")]`, no API key required): Anthropic request-shape tests pin the per-model API contract via a fake `HttpMessageHandler` (temperature omission, adaptive-vs-manual thinking, effort mapping, max_tokens ceilings, auto-adjust, vision model integrity), and a reflection sweep asserts every `AIModels` constant routes to its provider through `QuickAskAsync`'s factory. Live tests gained `ClaudeModelIntegrityTest` (detects silent model substitution) and a `QuickAskAsync` smoke test. `GetProviderFromModel` / `CreateService` are now `internal` (visible to the test assembly) to make the factory path testable.

### Compatibility

- Requires **Mythosia.AI.Abstractions v2.4.0**.
- Additive minor release; no breaking changes.

---

## v6.5.0

### Added

- **New models** (constants live in Mythosia.AI.Abstractions v2.3.0):
  - OpenAI: GPT-5.5, GPT-5.5 Pro (+ dated snapshots), GPT-5 Pro
  - Anthropic: Claude Opus 4.8, Claude Opus 4.7
  - Google: Gemini 3.1 Pro (preview), Gemini 3.5 Flash, Gemini 3.1 Flash-Lite
  - xAI: Grok 4.3, Grok 4.20 (reasoning / non-reasoning), Grok Build 0.1
  - Perplexity: Sonar Reasoning Pro
- **GPT-5.5 parameter support** — `WithGpt5_5Parameters(reasoningEffort, verbosity, reasoningSummary)` plus the `Gpt5_5Reasoning` effort enum (none/low/medium/high/xhigh; the `-pro` variant clamps to medium/high/xhigh).
- **Unified request-timeout control point** — `ResolveRequestTimeoutSeconds` / `CreateRequestTimeoutCts` on the base `AIService`. The `FunctionCallingPolicy` timeout is now authoritative across the completion, streaming, audio, and image paths.

### Fixed

- **Anthropic — Opus 4.7 / 4.8 API-contract changes** (both previously returned HTTP 400):
  - `temperature` is omitted for these models (they reject any non-default temperature).
  - Extended thinking now uses adaptive mode (`thinking.type=adaptive` + `output_config.effort`) instead of the rejected `type=enabled` + `budget_tokens`; `ThinkingBudget` is mapped to an effort level (≥100000 → max, ≥32768 → xhigh, else high).
- **OpenAI — `*-pro` reasoning effort** (HTTP 400 fixes): `gpt-5-pro` is sent its only supported effort `high`; `gpt-5.2-pro` / `gpt-5.4-pro` / `gpt-5.5-pro` clamp `none`/`low` up to `medium`.
- **OpenAI — image generation**: switched from the retired `dall-e-3` to `gpt-image-1` and removed the unsupported `response_format`. `GenerateImageUrlAsync` now returns a base64 `data:` URI, since gpt-image models do not return hosted URLs.
- **OpenAI — pro-model timeouts**: slow "pro" reasoning models (`gpt-5-pro`, `o3-pro`, `gpt-5.x-pro`) get a 300s default and the per-request policy timeout is no longer silently capped by `HttpClient.Timeout`.
- **xAI — `grok-build`** rejects `frequency_penalty` / `presence_penalty`; these are now omitted for it.
- **Google — Gemini 3 "pro"** rejects the `MINIMAL` thinking level; disabling reasoning now uses `Low` (its floor) for those models.

### Changed

- **Default models updated to current flagships**:
  - Anthropic: Claude Sonnet 4.6 (was `claude-sonnet-4-20250514`, retiring 2026-06-15); vision fallback also moves to Sonnet 4.6.
  - xAI: Grok 4.3 (was `grok-3`, retired).
  - Google: Gemini 3.1 Pro preview (was `gemini-2.5-pro`).
- **Helper repoints**: xAI `UseGrok4Model()` / `UseGrok4FastModel()` now select `grok-4.3`; Perplexity `UseSonarReasoning()` now selects `sonar-reasoning-pro`.

### Removed

- Retired / incompatible model constants (details in Mythosia.AI.Abstractions v2.3.0): `grok-3`, `grok-4-0709`, `grok-4-1-fast`, `grok-4.20-multi-agent-0309`, `claude-sonnet-4-20250514`, `sonar-reasoning`, `gemini-3-pro-preview`, `gpt-5.3-codex-spark`.

### Internal

- Bumped `System.Threading.Channels` 10.0.7 → 10.0.8 (.NET 10 servicing patch).

### Compatibility

- Requires **Mythosia.AI.Abstractions v2.3.0**.
- Minor release. The removed model constants reference models that no longer function at runtime; any code referencing them by name needs a one-line update to a current model id.

---

## v6.4.0

### Added

- **Streaming diagnostics — service-level `WithStreamDiagnostics`**
  - New fluent extension on `AIService`: `service.WithStreamDiagnostics(d => d.OnRawLine(...).OnComplete(...))`. Same builder pattern as `WithRag` — register once and every subsequent `StreamAsync` call automatically invokes the hooks.
  - `OnRawLine(Action<string>)` fires for every SSE line received from the response, before any provider-specific parsing. Wire to a Debug-level logger to see exactly what the server sent.
  - `OnComplete(Action<StreamDiagnostics>)` fires exactly once on stream exit (success or failure) with a snapshot: `LinesRead`, `DataLinesProcessed`, `ParseFailures`, `AccumulatedTextLength`, `LastRawLine`, `Elapsed`.
  - Each `On*` method is independent — call only the ones you need. Re-applying replaces; pass `_ => { }` to clear.
  - Especially useful against self-hosted vLLM/ollama and unstable proxies, where "the stream just stopped" needs to be told apart from "transport error after N chunks".

- **`StreamReadException` for read-time failures**
  - When SSE reading throws (`IOException`, `NotSupportedException`, `ObjectDisposedException`, etc.), the library now wraps it in `StreamReadException` with the original exception preserved as `InnerException` and a `StreamDiagnostics` snapshot attached as `Diagnostics`. Works regardless of whether `WithStreamDiagnostics` is registered.
  - `OperationCanceledException` is intentionally not wrapped, so cancellation semantics remain intact for callers using `CancellationToken`.

- **`AIService.ReadSseLinesAsync` instance helper** (`protected internal`)
  - Extension point for custom provider implementations. Yields raw SSE lines line-by-line with diagnostics tracking, async stream disposal, and structured exception wrapping built in.

### Fixed

- **`NotSupportedException` at `await foreach ... DisposeAsync()`**
  - Replaced the synchronous `using (var stream = ...)` pattern across all 5 providers (10 SSE-reading loops) with async stream disposal via the new `ReadSseLinesAsync` helper. Eliminates the `NotSupportedException` thrown by HTTP transports whose stream rejects synchronous `Dispose`.
  - The helper's `finally` block now guards every disposal step with `try/catch` so a Dispose-time failure cannot mask the real read-side exception, and `OnComplete` is guaranteed to fire even when disposal fails.

- **`CopyFrom` now propagates service-level callbacks**
  - `AIService.CopyFrom(source)` was silently dropping `SystemMessageProvider` (declared in v6.3.0 but missing from `CopyFrom`), `StreamRawLineCallback`, and `StreamCompleteCallback`.
  - These delegates are now propagated by reference, so cross-provider switches (e.g., `new AnthropicService(...).CopyFrom(openaiService)` in a multi-provider chat UI) keep the registered diagnostics and system-message provider working without re-registration.
  - Caveat documented in XML doc: closures that capture the source service itself (e.g., `line => Log(source.Provider, line)`) will still reference the original service in the copy. Capturing only external sinks (`logger`, `metrics`, `telemetry`) is the safe pattern.

### Internal

- **5 provider streaming implementations consolidated** to a single `ReadSseLinesAsync` helper:
  - `OpenAICompatibleService` (covers OpenAI, Grok, Qwen, vLLM)
  - `AnthropicService.Streaming`
  - `GoogleAIService.Streaming` (3 loops + removed obsolete private `ReadSseLines(StreamReader)` helper)
  - `DeepSeekService.Streaming` (3 loops)
  - `PerplexityService.Streaming` (3 loops)
- Eliminates 9 duplicate SSE-reading loops and centralizes future SSE behavior changes (timeout, retry, keep-alive) to one place.

### Tests

- 16 new diagnostics unit tests covering: read-time `Exception` wrapping into `StreamReadException`, `OnRawLine`-only and `OnComplete`-only registrations, callback exception isolation (a faulty logger cannot break the stream), `CopyFrom` callback propagation, disposal-failure scenarios where `OnComplete` must still fire, and cancellation-token honoring between reads.

### Compatibility

- Additive public API — `WithStreamDiagnostics` is a new extension; `StreamReadException` is a new type. Existing callers compile and run unchanged.
- `IAIService` contract unchanged.
- Requires `Mythosia.AI.Abstractions` v2.2.0 (which adds `StreamDiagnostics`, `StreamDiagnosticsBuilder`, `StreamReadException`).

---

## v6.3.0

### Added

- **Agent path per-call `AIRequestContext`**
  - `RunAgentAsync(string, int, AIRequestContext?)` and `RunAgentStreamAsync(string, int, StreamOptions?, AIRequestContext?, CancellationToken)` now accept an optional per-call `AIRequestContext`.
  - Context flows through `AsyncLocal`, so concurrent agent runs on the same service instance don't compete on a shared field.
  - Closes a gap where `GetCompletionAsync`/`StreamAsync` already accepted `AIRequestContext` but the agent-loop entry points did not, forcing callers to mutate `ChatBlock.SystemMessage` between calls.

- **`AIService.SystemMessageProvider` for automatic per-request context injection**
  - New `Func<CancellationToken, ValueTask<AIRequestContext?>>?` property on `AIService`, invoked automatically before every outbound call (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`).
  - Eliminates the boilerplate of building and passing an `AIRequestContext` at every entry point — register once at service construction and every subsequent call picks up the latest dynamic context (date, session, folder selection, etc.).
  - Async-shaped signature so providers that need IO (database, cache, HTTP) can run natively without blocking on `.Result`/`.GetAwaiter().GetResult()`. For streaming paths the caller's `CancellationToken` flows through; for non-streaming paths it is `CancellationToken.None`.
  - When a call also supplies an explicit `AIRequestContext`, the two are merged field-by-field: explicit wins on `SystemMessagePrefix`/`SystemMessageSuffix`/`RequestMessageOverride` when non-null; `AdditionalMessages` is concatenated (provider first, then explicit).
  - Companion fluent helpers with arity-based overload resolution — `service.WithSystemMessageProvider(() => ctx)` for the sync 90% case and `service.WithSystemMessageProvider(async ct => ctx)` for the async case. The property itself exposes `internal set`, so fluent helpers are the canonical assignment path and direct property assignment is reserved for the library's own composition.
  - Zero new types — one async delegate, two extension overloads.

### Fixed

- **`AIRequestContext.SystemMessagePrefix` / `SystemMessageSuffix` now actually apply**
  - These fields have existed on `AIRequestContext` since v5.0.0 but were never read by any provider or by `GetEffectiveSystemMessage()`. Setting them had no effect on the request.
  - `GetEffectiveSystemMessage()` now composes the effective system message in the order **prefix + summary + base + suffix**, honoring values from the current `AIRequestContext` (if any) on top of the existing summary/base behavior.
  - Null-safe at every layer: a null context, null prefix, or null suffix all fall through without allocation or joining.

### Tests

- Added 14 unit tests covering the new agent context and provider behavior:
  - Agent context threading (7): prefix prepending, suffix appending, no-context baseline, per-call scope leak prevention, covering both `RunAgentAsync` and `RunAgentStreamAsync`.
  - `SystemMessageProvider` (7): auto-injection without explicit context, field-level merge with explicit context, null-return no-op, per-call re-invocation, async-overload auto-inject, plus streaming-path equivalents.

### Compatibility

- Additive public API — each agent method gains one optional parameter; existing positional/named callers compile and run unchanged. `SystemMessageProvider` defaults to `null` so services that never set it behave identically to v6.2.0.
- No `IAIService` or `Mythosia.AI.Abstractions` contract changes.
- `Mythosia.AI.Abstractions` remains at v2.1.0.
- Behavior change is strictly additive: callers that never set `SystemMessagePrefix`/`SystemMessageSuffix` or `SystemMessageProvider` see byte-identical effective system messages to v6.2.0.

---

## v6.2.0

### Added

- **`StreamingContentType.RoundUsage`**
  - Emits per-LLM-round token usage as a separate stream event.
  - Adds `StreamingContent.RoundIndex` and `StreamingContent.IsFinalRound` for consumers that need stable round metadata without parsing dictionaries.

### Behavior

- **Completion usage remains cumulative**
  - `Completion.Usage` keeps its existing meaning as total usage across the whole streaming run.
  - `RoundUsage.Usage` is scoped to one LLM round and normalizes `TotalTokens` to `InputTokens + OutputTokens`.

- **Gemini usage handling**
  - Gemini streaming now keeps reading after a function-call chunk so late `usageMetadata` can be captured before tool execution.
  - Usage capture is no longer tied to metadata-only chunks.

### Tests

- Added provider-level `Token` test coverage for round usage events and final cumulative usage.

### Compatibility

- Additive public API update.
- Requires `Mythosia.AI.Abstractions` v2.1.0.
- `Completion.Usage` keeps its existing cumulative meaning.

---

## v6.1.0

### Added

- **`AIService.RunAgentStreamAsync(...)`**
  - Adds a streaming counterpart to `RunAgentAsync(...)` for agent-style function-calling loops.
  - Accepts `goal`, `maxSteps`, optional `StreamOptions`, and `CancellationToken`.
  - Preserves token streaming while still allowing tool-use events to flow through the stream.

### Behavior

- **Agent-safe stream option normalization**
  - Forces `IncludeFunctionCalls = true` so agent tool calls remain available during streaming.
  - Forces `TextOnly = false` so the stream can emit the final `Completion` event required for agent completion tracking.

- **Consistent max-step failure semantics**
  - Converts max-round exhaustion into `AgentMaxStepsExceededException`, matching `RunAgentAsync(...)`.
  - Includes the last assistant response as partial output when available.

### Tests

- Added mock/unit coverage for `RunAgentStreamAsync(...)`.
- Added provider-level shared integration coverage for streaming agent execution with function calling.

### Compatibility

- No `IAIService` or `Mythosia.AI.Abstractions` contract changes in this release.
- `Mythosia.AI.Abstractions` remains at v2.0.0.

---

## v6.0.0

### Changed

- **`AIService.GetCompletionAsync` — 8 overloads → 2** (breaking change)
  - Replaces all positional overloads with two methods using optional parameters.
  - All concrete service implementations (`OpenAIService`, `AnthropicService`, etc.) are unaffected — they still override the internal `GetCompletionAsync(Message message)`.
  - Migration: callers using `GetCompletionAsync(message, context)` must switch to `GetCompletionAsync(message, context: context)`.

- **`AIService.StreamAsync` — 6 overloads → 4** (breaking change)
  - `AIRequestContext` parameter moved to optional position in all `Message`-based overloads.
  - Migration: callers using `StreamAsync(message, context, ct)` must switch to `StreamAsync(message, context: context, ct)`.

- **`StreamCoreAsync` protected virtual introduced**
  - Provider-level full streaming pipeline overrides (previously `override StreamAsync(Message, StreamOptions, CancellationToken)`) must now override `StreamCoreAsync` instead.
  - Affects: `DeepSeekService`, `PerplexityService`.

### Compatibility

- Requires `Mythosia.AI.Abstractions` v2.0.0.

### Migration from v5.x

#### Service class renames (from v5.2 or earlier)

The old names still compile but are marked `[Obsolete]` — update when convenient.

| Old name | New name |
|---|---|
| `ChatGptService` | `OpenAIService` |
| `ClaudeService` | `AnthropicService` |
| `GeminiService` | `GoogleAIService` |
| `GrokService` | `XAIService` |
| `SonarService` | `PerplexityService` |

```csharp
// before (v5.2 and earlier)
var service = new ChatGptService(apiKey, httpClient);

// after
var service = new OpenAIService(apiKey, httpClient);
```

#### GetCompletionAsync — one pattern requires a fix

```csharp
// before — compile error in v6.0
await service.GetCompletionAsync(message, myContext);

// after
await service.GetCompletionAsync(message, context: myContext);
```

All other patterns (`GetCompletionAsync("hello")`, `GetCompletionAsync(message, profile)`, etc.) compile unchanged.

#### StreamAsync — one pattern requires a fix

```csharp
// before — compile error in v6.0
await foreach (var chunk in service.StreamAsync(message, options, cancellationToken)) ...

// after
await foreach (var chunk in service.StreamAsync(message, options, cancellationToken: cancellationToken)) ...
```

#### Custom AIService subclasses — streaming override rename

```csharp
// before
public override async IAsyncEnumerable<StreamingContent> StreamAsync(
    Message message, StreamOptions options, CancellationToken cancellationToken = default) { ... }

// after
protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(
    Message message, StreamOptions options, CancellationToken cancellationToken = default) { ... }
```

---

## v5.3.0

### Added

- **`IFunctionRegisterable` implementation on `AIService`** — `AIService` now implements `IFunctionRegisterable` (from `Mythosia.AI.Abstractions` v1.1.0) via explicit interface implementation.
  - `IFunctionRegisterable.AddFunction(FunctionDefinition)` delegates to the existing `Functions.Add()` call.
  - No changes to existing `Functions` property or `WithFunction` / `WithFunctionAsync` extension method API — fully backward compatible.
  - Enables any extension package that depends only on `Mythosia.AI.Abstractions` to register tools on `AIService` at runtime without a direct dependency on this package.

### Changed

- **Service classes renamed to match provider names:**

  | Previous name | New name |
  |---|---|
  | `ChatGptService` | `OpenAIService` |
  | `ClaudeService` | `AnthropicService` |
  | `GeminiService` | `GoogleAIService` |
  | `GrokService` | `XAIService` |
  | `SonarService` | `PerplexityService` |

  Previous names are retained as `[Obsolete]` subclasses and will be removed in a future major version. No code changes are required at this time; a compiler warning will appear at usages of the old names.

### Removed

- Empty Alibaba stub files from `Mythosia.AI` core package (`Services/Alibaba/QwenService*.cs`). The Alibaba/Qwen implementation remains available in the separate `Mythosia.AI.Providers.Alibaba` package, which is unchanged.

### Internal

- `GetCompletionAsync(string)` overloads in `AIService` refactored to eliminate the repeated `ApplySummaryPolicyIfNeededAsync` + `new Message(ActorRole.User, prompt)` pattern via a private `PreparePromptAsync` helper. No behavioral change.

### Compatibility

- No breaking changes. All existing API surface is preserved via `[Obsolete]` shims.
- Requires `Mythosia.AI.Abstractions` v1.1.0.

---

## 🚀 v5.2.0 - Abstractions Extraction & IAIService Interface

### **Abstractions Package Split** 📦

All shared model/contract types have been extracted into the new `Mythosia.AI.Abstractions` package (zero heavy dependencies). `Mythosia.AI` now depends on `Mythosia.AI.Abstractions` and re-exports all types through the same namespaces.

Moved types include:
- **Models**: `Message`, `MessageContent`, `ChatBlock`, `ActorRole`, `AIRequestContext`, `AIRequestProfile`, `AIModels`, `AIProvider`, `RoundResult`, `SummaryConversationPolicy`, `StructuredOutputPolicy`
- **Streaming**: `StreamingContent`, `StreamingContentType`, `StreamOptions`, `TokenUsage`
- **Functions**: `FunctionDefinition`, `FunctionCallingPolicy`, `FunctionCallMode`
- **Attributes**: `AiFunctionAttribute`, `AiParameterAttribute`
- **Enums**: `ReasoningEffort`, `ReasoningSummary`, `GeminiThinkingLevel`, `Verbosity`
- **Exceptions**: `AIServiceException`, `AgentMaxStepsExceededException`

### **`IAIService` Interface** 🔌

`AIService` now implements `IAIService` (defined in `Mythosia.AI.Abstractions`). This enables downstream libraries like `Mythosia.AI.Rag` to depend on the lightweight abstractions package instead of the full implementation.

```csharp
public interface IAIService
{
    string Model { get; }
    string Provider { get; }
    string SystemMessage { get; set; }
    bool StatelessMode { get; set; }
    ChatBlock ActivateChat { get; }

    Task<string> GetCompletionAsync(string prompt);
    Task<string> GetCompletionAsync(Message message, AIRequestContext context);
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct = default);
    IAsyncEnumerable<StreamingContent> StreamAsync(Message message, StreamOptions options, CancellationToken ct = default);
    // ... additional overloads
}
```

### **Dependency Cleanup** 🧹

- `System.Threading.Channels` updated from 10.0.3 to 10.0.5
- Direct `Mythosia` package dependency removed from `Mythosia.AI` (now transitive through `Mythosia.AI.Abstractions`)

### ✅ Compatibility

- **Source-level fully compatible** — all namespaces unchanged, no code changes needed
- `AIService : IAIService` is additive — existing subclasses unaffected
- `Mythosia.AI.Rag` now depends on `Mythosia.AI.Abstractions` instead of `Mythosia.AI`

---

## 🚀 v5.1.0 - Unified Token Usage & Accurate Summary Trigger

### **Unified Token Usage in Streaming** 📊

`StreamingContent` now includes a `Usage` property (`TokenUsage`) on `Completion` events, providing unified token usage information across all providers.

`TokenUsage` fields:
- `InputTokens` / `OutputTokens` / `TotalTokens` — basic token counts
- `CachedInputTokens` — tokens served from cache (OpenAI, Claude, DeepSeek, Gemini)
- `CacheCreationTokens` — tokens written to cache (Anthropic)
- `ReasoningTokens` — tokens used for internal reasoning (OpenAI, Gemini)
- Computed: `NonCachedInputTokens`, `CacheHitRatio`, `HasCacheActivity`, `VisibleOutputTokens`

When function calling spans multiple rounds, token usage is accumulated across all rounds and reported in the final `Completion` event.

```csharp
await foreach (var content in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"Input: {content.Usage.InputTokens}");
        Console.WriteLine($"Output: {content.Usage.OutputTokens}");
        Console.WriteLine($"Cached: {content.Usage.CachedInputTokens}");
        Console.WriteLine($"Cache hit: {content.Usage.CacheHitRatio:P1}");
    }
}
```

### **Accurate API-Based Summary Trigger** 🎯

`SummaryConversationPolicy` now uses the real input token count from the API response (when available) instead of local estimation for trigger decisions. This results in more accurate and reliable summarization timing.

### **Summary Timing Improvement** ⏱️

Summary is now applied after streaming completes (preparing context for the next turn), rather than before each streaming round. This avoids unnecessary latency during active streaming.

### ⚠️ Breaking Changes

- **Removed `StreamOptions.IncludeTokenInfo`** and `StreamOptions.WithTokenInfo()`
  - Token usage is now always available via `StreamingContent.Usage` on `Completion` events
  - **Migration:** Remove any `.WithTokenInfo()` calls and access `content.Usage` directly on Completion events

### ✅ Compatibility

- Breaking: `StreamOptions.IncludeTokenInfo` / `WithTokenInfo()` removed
- All other APIs are backward compatible with v5.0.x

---

## 🚀 v5.0.1 - GPT-5.4 Mini/Nano & Streaming Reliability

### **GPT-5.4 Mini & Nano Support** 🤖

Added `AIModels.OpenAI.Gpt5_4Mini` (`gpt-5.4-mini`) and `AIModels.OpenAI.Gpt5_4Nano` (`gpt-5.4-nano`) model constants.

### **Internal Streaming Refactor** 🔧

- Refactored `StreamAsync` into a Template Method pattern: the base class now owns the round loop, `StatelessMode` handling, and `ApplySummaryPolicyIfNeededAsync()` invocation. Providers override `StreamRoundAsync` for single-round logic.
- Fixed Gemini streaming to support multi-round function chaining (previously used recursion, now iterative).
- Fixed `Stream` flag not being restored after `ApplySummaryPolicyIfNeededAsync()` triggers during streaming loops, by adding `Stream` backup/restore to `ApplyRequestProfile`.

### ✅ Compatibility

- Fully backward compatible with v5.0.0
- No breaking changes to public API

---

## 🚀 v5.0.0 - Request Profiles, Request Contexts, and AIModels Catalog

### **Model Catalog Shift to `AIModels` String Constants** 🔤

The core package now exposes provider-organized model IDs through the `AIModels` static class.

This gives consumers a direct string-based model catalog grouped by provider:

- `AIModels.OpenAI.*`
- `AIModels.Anthropic.*`
- `AIModels.Google.*`
- `AIModels.xAI.*`
- `AIModels.DeepSeek.*`
- `AIModels.Perplexity.*`

This makes it easier to select exact model IDs, including dated variants and provider-specific naming, without depending on enum-style usage in application code.

### **Per-Request Runtime Overrides with `AIRequestProfile`** ⚙️

`AIService` now supports per-call runtime overrides through `AIRequestProfile` overloads on `GetCompletionAsync(...)`.

Supported one-shot overrides include:

- `Stateless`
- `DisableFunctions`
- `DisableReasoning`
- `Temperature`
- `MaxTokens`
- `Purpose`

Included built-in presets via `RequestProfiles`:

- `RequestProfiles.QueryRewrite`
- `RequestProfiles.Summarization`

This enables targeted request shaping without mutating long-lived service configuration.

```csharp
var rewritten = await service.GetCompletionAsync(
    "Rewrite this question for document retrieval.",
    RequestProfiles.QueryRewrite);

var concise = await service.GetCompletionAsync(
    "Summarize this incident report.",
    new AIRequestProfile
    {
        Stateless = true,
        DisableFunctions = true,
        Temperature = 0.2f,
        MaxTokens = 200
    });
```

### **Request-Scoped Prompt Injection with `AIRequestContext`** 🧩

`AIRequestContext` adds request-scoped prompt composition hooks on top of the existing conversation model.

New context capabilities include:

- `SystemMessagePrefix`
- `SystemMessageSuffix`
- `RequestMessageOverride`
- `AdditionalMessages`

These APIs are especially useful when you need to pass derived prompt data only for the current call without polluting the real conversation history.

One common use case is a query rewriter pipeline:

- keep the user's original question in chat history
- rewrite that question into a retrieval-friendly form
- send only the rewritten form for the current retrieval or answer-generation request
- avoid storing the rewritten query as if it were the user's actual message

```csharp
var rewrittenQuery = await service.GetCompletionAsync(
    "Rewrite this user question for document retrieval.",
    RequestProfiles.QueryRewrite);

var response = await service.GetCompletionAsync(
    originalUserQuestion,
    new AIRequestContext
    {
        RequestMessageOverride = new Message(ActorRole.User, rewrittenQuery)
    });
```

In this pattern, the service can use the rewritten query for the current request while the stored chat history still preserves the user's original message.

### **Static Quick Helpers for Stateless Calls** ⚡

Added static helper entry points on `AIService` for simple stateless usage:

- `QuickAskAsync(...)`
- `QuickAskWithImageAsync(...)`

These helpers automatically create the appropriate provider service from the selected model and execute the request in `StatelessMode`.

### ✅ Compatibility

- Package version advanced to `v5.0.0`
- Existing service-level configuration APIs remain available
- The new request profile/context APIs are additive

---

## 🚀 v4.7.1 - AIService Copy Reliability Fix

### **AIService Internal Bug Fix** 🐛

Fixed `AIService.CopyFrom(...)` so copied service instances now preserve service-level runtime configuration from the source instance more reliably.

#### What was fixed

- `Functions`, function-calling mode, and related execution policy values are copied consistently
- Runtime tuning settings (temperature/top-p/max tokens/penalties/stream flags) are preserved
- `ConversationPolicy` values and current summary state are copied safely

### **Gemini Completion Path Flag Fix** 🐛

Fixed `GeminiService.GetCompletionAsync(...)` to set `Stream = false` before the `StatelessMode` early-return path.

- `StatelessMode` requests now also force non-streaming mode consistently
- Prevents stale stream-state leakage from previous streaming calls into completion requests

### ✅ Compatibility

- Fully backward compatible with v4.7.0
- No breaking changes

---

## 🚀 v4.7.0 - GPT-5.3/5.4 Expansion & Reasoning Streaming Reliability

### **OpenAI Model Lineup Expanded** 🤖

Added new OpenAI models to `AIModel`:

- `AIModel.Gpt5_3Codex` → `gpt-5.3-codex`
- `AIModel.Gpt5_4` → `gpt-5.4`
- `AIModel.Gpt5_4Pro` → `gpt-5.4-pro`

### **New GPT-5.3 / GPT-5.4 Reasoning Configuration** 🧠

Added dedicated reasoning enums and fluent configuration APIs for newer GPT-5 variants:

- `Gpt5_3Reasoning` enum (`Auto`, `None`, `Low`, `Medium`, `High`, `XHigh`)
- `Gpt5_4Reasoning` enum (`Auto`, `None`, `Low`, `Medium`, `High`, `XHigh`)
- `WithGpt5_3Parameters(...)`
- `WithGpt5_4Parameters(...)`

Model-specific defaults and guard behavior:

- GPT-5.3 Codex defaults to `Medium`
- GPT-5.4 defaults to `None`
- GPT-5.4 Pro defaults to `Medium`
- Codex models auto-adjust unsupported `None` reasoning effort to `Low`
- GPT-5.3/5.4 request builders ensure `max_output_tokens` minimum for reasoning scenarios

### **OpenAI Streaming Reasoning Parsing Improvements** 🌊

`ChatGptService` streaming now recognizes and parses additional reasoning events for more complete real-time output:

- `response.reasoning_text.delta`
- `response.reasoning_text.done`
- `response.reasoning_summary_text.done`
- summary array payloads (`summary[].text`) and reasoning/text item variants

This improves reliability when providers emit reasoning content in mixed delta/done/summary formats.

### **Gemini Reasoning Streaming Improvements** ✨

Gemini streaming now requests and handles thought content more consistently when reasoning is enabled:

- Streaming requests propagate reasoning intent into generation config (`includeThoughts`)
- Function-calling streaming path also propagates `includeThoughts`
- Parser prioritizes thought parts when `StreamOptions.WithReasoning()` is enabled
- Reasoning chunks are yielded in streaming responses without being dropped by text-only filters

### ✅ Compatibility

- Fully backward compatible with v4.6.2
- No breaking changes

---

## 🚀 v4.6.2 - Grok Reasoning Support & SummaryConversationPolicy Improvements

### **Grok Reasoning Support** 🧠

Added `GrokReasoning` enum and `reasoning_effort` parameter support for xAI Grok reasoning models.

#### GrokReasoning Enum

| Value | Description |
|-------|-------------|
| `Off` | No `reasoning_effort` parameter sent (default) |
| `Low` | Low reasoning effort |
| `High` | High reasoning effort |

> **Note:** Only `grok-3-mini` supports the `reasoning_effort` API parameter. `grok-3`, `grok-4`, and `grok-4-fast-reasoning` do **not** support it.

#### Reasoning Content Streaming

Grok reasoning models (`grok-3-mini`, `grok-4`, `grok-4-1-fast`) now stream `reasoning_content` deltas. When `StreamOptions.WithReasoning()` is enabled, reasoning chunks are emitted as `StreamingContentType.Reasoning`.

```csharp
var grokService = new GrokService(apiKey, httpClient);
grokService.ChangeModel(AIModel.Grok3Mini);
grokService.WithGrokParameters(reasoningEffort: GrokReasoning.High);

await foreach (var content in grokService.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Think] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

#### New API

- **`GrokReasoning`** enum (`Off` / `Low` / `High`)
- **`GrokService.ReasoningEffort`** property (default: `Off`)
- **`GrokService.WithGrokParameters()`** — Builder method to configure reasoning effort
- **`SupportsReasoningEffort()`** — Internal helper; returns `true` only for `grok-3-mini`

### **SummaryConversationPolicy Improvements** 🔧

#### Auto-Adjust `keepRecentCount`

`ByMessage()` and `ByBoth()` now automatically adjust `keepRecentCount` when the default value would be greater than or equal to `triggerCount`:

```csharp
// Before v4.6.2: ByMessage(triggerCount: 3) with default keepRecentCount=5 → invalid (5 >= 3)
// After v4.6.2: auto-adjusted to keepRecentCount=2 (triggerCount - 1)
var policy = SummaryConversationPolicy.ByMessage(triggerCount: 3);
```

When `keepRecentCount` is explicitly provided and is `>= triggerCount`, an `ArgumentException` is thrown.

#### `ApplySummaryPolicyIfNeededAsync()` Now Public

Changed from `protected` to `public` so that streaming scenarios can explicitly call summarization before `StreamAsync()`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();
await foreach (var chunk in service.StreamAsync("Continue..."))
{
    Console.Write(chunk.Content);
}
```

### **Streaming Fixes** 🐛

#### Claude — `function_call_arguments.done` Handling

Claude streaming now correctly handles the `response.function_call_arguments.done` SSE event, capturing the complete arguments JSON in addition to the incremental deltas.

#### ChatGPT — `function_call_arguments.done` Handling

ChatGPT (Responses API) streaming now correctly handles the `response.function_call_arguments.done` event, ensuring complete function call arguments are captured when the done event arrives.

### 🧪 New Tests

- **`ByMessage_DefaultKeepRecent_AutoAdjusted`** — Verifies auto-adjustment when default `keepRecentCount` would exceed `triggerCount`
- **`ByBoth_DefaultKeepRecent_AutoAdjusted`** — Same for `ByBoth()` factory
- **`ByMessage_KeepRecentGreaterOrEqual_Throws`** — Explicit `keepRecentCount >= triggerCount` throws `ArgumentException`
- **`ByBoth_KeepRecentGreaterOrEqual_Throws`** — Same for `ByBoth()`
- **`Streaming_StatelessMode_SkipsSummarization`** — Verifies `ApplySummaryPolicyIfNeededAsync()` is no-op in `StatelessMode`
- **`GrokServiceTests.SupportsReasoning()`** — Enables reasoning for `grok-3-mini` tests

### ✅ Compatibility

- Fully backward compatible with v4.6.1
- No breaking changes
- `ApplySummaryPolicyIfNeededAsync()` visibility changed from `protected` to `public` (non-breaking)
- `ByMessage()` / `ByBoth()` default `keepRecentCount` may now be auto-adjusted instead of silently producing an invalid policy

---

## 🚀 v4.6.1 - Streaming Error Content & Claude Thinking Budget Fix

### **Streaming Error Content** 🐛

`StreamingContent.Content` was `null` when an API error occurred during streaming, making it difficult for consumers to display or handle error messages. Now all providers populate `Content` with a descriptive message:

```
API error (429): {"error":{"type":"rate_limit_error","message":"..."}}
```

#### Affected Providers
- **Claude** (`ClaudeService.Streaming.cs`)
- **ChatGPT** (`ChatGptService.Streaming.cs`)
- **Grok** (`GrokService.Streaming.cs`)
- **DeepSeek** (`DeepSeekService.Streaming.cs`)
- **Gemini** (`GeminiService.Streaming.cs`)
- **Sonar** (`SonarService.Streaming.cs`)

The error body is still available in `Metadata["error"]` as before — `Content` is now an additional, consumer-friendly surface.

### **Claude Thinking Budget Auto-Adjust** 🧠

Claude API requires `budget_tokens < max_tokens`. When `ThinkingBudget >= MaxTokens`, `ApplyThinkingConfig` now automatically increases `max_tokens` to `ThinkingBudget + 1024` (capped at the model's maximum), preventing `400 Bad Request` errors.

```csharp
var claude = new ClaudeService(apiKey, httpClient);
claude.MaxTokens = 8192;
claude.ThinkingBudget = 8192;  // budget_tokens == max_tokens → auto-adjusted to 9216
```

### **Claude Opus 4.6 Max Output Tokens Correction** 🔧

- `opus-4-6` max output tokens corrected from `131072` to `128000` to match the Anthropic API specification

### 🧪 New Tests

- **`ClaudeThinkingBudgetAutoAdjustTest`** — Verifies that thinking budget auto-adjust produces a valid response without API errors
- **`ReasoningStreamingTest`** — Validates reasoning + text chunk streaming with early error detection via `StreamingContent.Content`

### 🗑️ Chore

- Removed internal design docs (`docs/` directory) and related `.csproj` folder entry

### ✅ Compatibility

- Fully backward compatible with v4.6.0
- No breaking changes
- `StreamingContent.Content` on error is now non-null (previously `null`) — consumers relying on `Content == null` to detect errors should update to check `Type == StreamingContentType.Error`

---

## 🚀 v4.6.0 - Conversation Summary Policy & Real-Time Streaming Fix

### **SummaryConversationPolicy** 🧠

Automatically summarize old conversation messages when the conversation exceeds a configured threshold. The summary is stored as a string and injected into the system message on each subsequent LLM request.

#### Configuration

```csharp
// Token-based: summarize when total tokens exceed 3000, keep recent ~1000 tokens
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// Message-count-based: summarize when messages exceed 20, keep last 5
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// Combined (OR condition): triggers when either threshold is exceeded
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 3000,
    triggerCount: 20
);
```

#### How It Works

1. `GetCompletionAsync()` checks `ConversationPolicy.ShouldSummarize(Messages)`
2. If triggered, extracts old messages via `GetMessagesToSummarize()`
3. Calls the LLM in `StatelessMode = true` to generate a concise summary
4. Stores the summary in `ConversationPolicy.CurrentSummary`
5. Removes summarized messages from `ChatBlock`
6. Prepends `[Previous conversation summary]` to the system message on every request

#### Session Persistence

```csharp
// Save summary for later
string saved = service.ConversationPolicy.CurrentSummary;

// Restore in a new session
policy.LoadSummary(saved);
```

#### Key Design Decisions

- **StatelessMode protection** — Summary LLM calls use `StatelessMode = true` to prevent polluting the main conversation history
- **Backward compatible** — `ConversationPolicy` defaults to `null`; existing behavior is unchanged
- **Provider-agnostic** — Works with all providers (OpenAI, Claude, Gemini, Grok, DeepSeek, Perplexity)
- **Incremental summarization** — When re-summarizing, existing summary is included as context for the new summary
- **`GetEffectiveSystemMessage()`** — All provider request builders now use this method to include the summary prefix

### 🧪 Test Coverage

- 28 unit tests covering: factory methods, ShouldSummarize (token/count/both), GetMessagesToSummarize (count-based, token-based, budget overflow), LoadSummary, GetEffectiveSystemMessage (null policy, no summary, with summary, summary-only), integration (null policy multi-turn, below threshold, exceeds threshold, StatelessMode protection, message removal, summary prompt content, incremental summary, token-based trigger, StatelessMode skip), serialization round-trip

### 🔧 Internal Improvements

- **`HashSet<ChatBlock>` → `List<ChatBlock>`** — `ChatBlock` has no `Equals`/`GetHashCode` override, so `HashSet` provided no deduplication benefit. `SetActivateChat()` already used LINQ O(n) lookup. `List` is simpler and more predictable.
- **Default `ChatBlock` initialization in base constructor** — `AddNewChat()` is now called in `AIService` base constructor, guaranteeing `ActivateChat` is never `null`. Previously each concrete service (ChatGpt, Claude, Grok, Gemini, DeepSeek, Sonar) had to call `AddNewChat()` individually — forgetting this would cause `NullReferenceException` in `GetLatestMessages()`, `WithSystemMessage()`, etc.
- **Removed redundant null checks** — `SystemMessage` property getter/setter no longer needs null-conditional access since `ActivateChat` is always initialized.

### 🐛 Real-Time Streaming Fix

`StreamAsync()` in 3 providers was collecting **all** API response chunks into a list/queue before yielding them to the caller. This meant consumers received all chunks at once after the full API response completed, instead of progressively as each chunk arrived.

#### Root Cause

- **Claude** (`ClaudeService.Streaming.cs`) — `ProcessClaudeStreamResponse()` read the entire SSE stream into a `Queue<StreamingContent>`, then `StreamAsync()` dequeued and yielded after the method returned.
- **OpenAI** (`ChatGptService.Streaming.cs`) — `ProcessStreamRoundAsync()` → `ReadStreamAsync()` collected all chunks into `StreamData.Contents` list, then yielded from the list.
- **Grok** (`GrokService.Streaming.cs`) — Same pattern as OpenAI via `ProcessStreamRoundAsync()` → `ReadGrokStreamAsync()`.

#### Fix

Inlined the stream reading loop directly into `StreamAsync()` with a 2-phase structure:

1. **Phase 1 (real-time)**: Read HTTP stream line-by-line, parse each SSE event, and **`yield return` text/reasoning chunks immediately**
2. **Phase 2 (post-processing)**: After stream completes, execute any detected function calls and continue to next round if needed

#### Not Affected

- **DeepSeek**, **Gemini**, **Sonar** — These providers already had correct real-time streaming implementations.

#### New Regression Test

**`StreamingIsRealTimeNotBatchedTest`** — Measures timestamps of each chunk arrival and asserts that the spread (last chunk time − first chunk time) exceeds 200ms. Batched implementations would show near-zero spread.

```csharp
var sw = Stopwatch.StartNew();
var timestamps = new List<long>();

await foreach (var chunk in AI.StreamAsync("Write a short paragraph..."))
{
    timestamps.Add(sw.ElapsedMilliseconds);
}

var spread = timestamps.Last() - timestamps.First();
Assert.IsTrue(spread > 200, "Streaming may be batched instead of real-time");
```

### ✅ Compatibility

- Fully backward compatible with v4.5.0
- No breaking changes
- No API changes — same `StreamAsync()` / `StreamCompletionAsync()` signatures
- New class: `SummaryConversationPolicy`
- New partial class: `AIService.Summary.cs` (`ConversationPolicy`, `GetEffectiveSystemMessage()`, `ApplySummaryPolicyIfNeededAsync()`)

---

## 🚀 v4.5.0 - Structured Output with Auto-Recovery, Streaming & Collection Support

### **Structured Output: `GetCompletionAsync<T>()`** 🎯

Deserialize LLM responses directly into C# POCOs with automatic JSON recovery:

```csharp
var result = await service.GetCompletionAsync<WeatherResponse>("What's the weather in Seoul?");
Console.WriteLine($"{result.City}: {result.Temperature}°C, {result.Condition}");
```

#### Auto-Recovery Retry
When the LLM returns invalid JSON, Mythosia.AI automatically sends a correction prompt asking the model to fix its output. This is **not** a network retry — it's an output quality/format correction loop.

- Configurable via `StructuredOutputMaxRetries` (default: 2, meaning up to 3 total attempts)
- Correction prompt includes the previous invalid response and the parse error
- On final failure, throws `StructuredOutputException` with rich diagnostics

#### `StructuredOutputException`
Contains all context needed for debugging:

| Property | Description |
|----------|-------------|
| `TargetTypeName` | The C# type that deserialization was attempted for |
| `FirstRawResponse` | Raw LLM response from the first attempt |
| `LastRawResponse` | Raw LLM response from the last attempt |
| `ParseError` | Last JSON parse/deserialization error message |
| `AttemptCount` | Total number of attempts made |
| `SchemaJson` | The JSON schema that was sent to the LLM |

#### OpenAI-Strict JSON Schema Generation
`JsonSchemaGenerator` now produces schemas compliant with OpenAI Structured Outputs:
- All properties listed in `required` array
- `additionalProperties: false` at every level
- `definitions` → `$defs`, `$ref` paths updated
- `$schema` field removed

### **Per-Call Structured Output Policy** 🔧

Override retry behavior for a single request without changing service defaults:

```csharp
// Custom policy — applies only to this call
var result = await service
    .WithStructuredOutputPolicy(new StructuredOutputPolicy { MaxRepairAttempts = 5 })
    .GetCompletionAsync<MyDto>(prompt);

// Preset: no retry (1 attempt only)
var result = await service
    .WithNoRetryStructuredOutput()
    .GetCompletionAsync<MyDto>(prompt);

// Preset: strict mode (up to 3 retries = 4 total attempts)
var result = await service
    .WithStrictStructuredOutput()
    .GetCompletionAsync<MyDto>(prompt);
```

Policy is consumed after one `GetCompletionAsync<T>()` call and automatically cleared.

#### `StructuredOutputPolicy` Presets

| Preset | MaxRepairAttempts | Description |
|--------|-------------------|-------------|
| `Default` | `null` (service default) | Uses `StructuredOutputMaxRetries` |
| `NoRetry` | `0` | Single attempt, no retry |
| `Strict` | `3` | Up to 3 correction retries |

### **Streaming Structured Output: `BeginStream().As<T>()`** 🌊

Stream text chunks in real-time to the UI while getting a final deserialized object with auto-repair:

```csharp
var run = service.BeginStream(prompt)
    .WithStructuredOutput(new StructuredOutputPolicy { MaxRepairAttempts = 2 })
    .As<MyDto>();

// Optional: observe chunks in real-time
await foreach (var chunk in run.Stream(cancellationToken))
{
    Console.Write(chunk); // UI display
}

// Final deserialized result (waits for stream + parse/repair)
MyDto dto = await run.Result;
```

#### Key Behaviors

- **`Result` works without `Stream()`** — just `await run.Result` internally consumes the stream and parses
- **`Stream()` is single-use** — second call throws `InvalidOperationException`
- **`Result` waits for stream completion** — even if awaited mid-stream, it won't resolve early
- **Repair retries are non-streaming** — correction prompts use `GetCompletionAsync()` for efficiency
- **Throws `StructuredOutputException`** on final failure with full diagnostics

### **Collection Support: `List<T>`, `T[]`** 📋

Both `GetCompletionAsync<T>()` and streaming support collection types as `T`:

```csharp
// Non-streaming
var items = await service.GetCompletionAsync<List<ItemDto>>(prompt);

// Streaming
var run = service.BeginStream(prompt).As<List<ItemDto>>();
await foreach (var chunk in run.Stream()) Console.Write(chunk);
List<ItemDto> items = await run.Result;
```

- `List<T>`, `T[]`, `IReadOnlyList<T>` all supported — no wrapper DTO needed
- JSON array schema auto-generated from element type
- Array extraction from markdown code blocks supported
- Empty arrays (`[]`) handled correctly

### 🧪 Test Coverage

- 32 unit tests for structured output (11 retry/policy + 4 List<T> non-streaming + 14 streaming + 3 List<T> streaming)
- Tests cover: success on first attempt, retry then success, all attempts fail, zero retries, markdown-wrapped JSON extraction, schema cleanup, policy override/consumption, stream single-use guard, result-only mode, chunk ordering, List<T> array parsing/repair

### ✅ Compatibility

- Fully backward compatible with v4.4.0
- No breaking changes
- New classes: `StructuredOutputPolicy`, `StructuredOutputException`, `StreamBuilder`, `StructuredStreamRun<T>`
- New extension methods: `WithStructuredOutputPolicy()`, `WithNoRetryStructuredOutput()`, `WithStrictStructuredOutput()`
- New entry point: `AIService.BeginStream(prompt)`

---

## 🚀 v4.4.0 - xAI Grok Provider & AIModel Enum Reordering

### **New Provider: xAI (Grok)** 🤖

Added full support for xAI's Grok models via `GrokService`:

| Model | Enum | API Identifier |
|-------|------|----------------|
| **Grok 4** | `Grok4` | `grok-4-0709` |
| **Grok 4.1 Fast** | `Grok4_1Fast` | `grok-4-1-fast` |
| **Grok 3** | `Grok3` | `grok-3` |
| **Grok 3 Mini** | `Grok3Mini` | `grok-3-mini` |

#### Features
- **Function calling** via OpenAI-compatible `tools`/`tool_calls` format
- **Streaming** with multi-round function call support
- **Vision/multimodal** support for Grok 4 models (Grok 3/3 Mini do not support image inputs)
- **Reasoning model handling** — `grok-3-mini` and `grok-4*` are reasoning models that reject `frequency_penalty`, `presence_penalty`, `stop`, and `temperature` parameters; these are automatically excluded

#### Usage Example

```csharp
var grokService = new GrokService(apiKey, new HttpClient());

// Default model: Grok 3
var response = await grokService.GetCompletionAsync("Hello from Grok!");

// Switch to Grok 4
grokService.ChangeModel(AIModel.Grok4);

// Function calling
grokService.WithFunction("get_weather", "Get current weather",
    ("city", "City name", true),
    (string city) => $"Weather in {city}: 22°C, sunny");

var result = await grokService.GetCompletionAsync("What's the weather in Seoul?");

// Streaming with function calls
await foreach (var content in grokService.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (content.Type == StreamingContentType.FunctionCall)
        Console.WriteLine($"[Calling] {content.Metadata["function_name"]}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### **AIModel Enum Reordering** 📋

- OpenAI models moved to the top of the `AIModel` enum for consistency
- Added `AIProvider.xAI` to the `AIProvider` enum
- New enum values: `Grok4`, `Grok4_1Fast`, `Grok3`, `Grok3Mini`

### 🧪 Test Updates

- **New test classes**: `xAI_Grok4_Tests`, `xAI_Grok4_1Fast_Tests`, `xAI_Grok3_Tests`, `xAI_Grok3Mini_Tests`
- Per-model `SupportsMultimodal()` — only Grok 4 models return `true`
- Per-model `GetAlternativeModel()` for conversation management tests

### ✅ Compatibility

- Fully backward compatible with v4.3.0
- **Breaking change**: `AIModel` enum values have been reordered (OpenAI first). If you persist enum integer values, update your mappings.
- New enum values: `AIModel.Grok4`, `AIModel.Grok4_1Fast`, `AIModel.Grok3`, `AIModel.Grok3Mini`
- New enum value: `AIProvider.xAI`

---

## 🚀 v4.3.0 - GPT-5.2 Codex & Claude Haiku 4.5 Extended Thinking

### **New Model: GPT-5.2 Codex** 🤖

- Added `Gpt5_2Codex` (`gpt-5.2-codex`) — Coding-optimized model for agentic coding tasks
- Reasoning effort: `low` / `medium` (default) / `high` / `xhigh` — **`none` is not supported**
- If `none` is set, automatically adjusted to `low` with a console warning
- `IsGpt5_2CodexModel()` added for Codex-specific parameter routing

### **Claude Haiku 4.5 Extended Thinking** 🧠

- Extended thinking is now supported on **Claude Haiku 4.5** (`haiku-4` model detection added to `IsExtendedThinkingModel()`)
- New **`SupportsExtendedThinking`** public property on `ClaudeService` for external callers to check thinking capability
- `ApplyThinkingConfig()` reordered: temperature set before thinking block

### 🧪 Test Improvements

- **`RunIfSupported()` error logging** — Now catches `AIServiceException` and logs `ErrorDetails` before rethrowing, improving test diagnostics
- **Streaming assertion relaxed** — `StreamingTest` now accepts Korean number words (하나, 셋, 삼) alongside digits for multilingual model responses
- **`SupportsReasoning()` refactored** — `ClaudeServiceTests` now uses `SupportsExtendedThinking` property instead of manual model string checking
- **New test class**: `OpenAI_Gpt5_2Codex_Tests`

### ✅ Compatibility

- Fully backward compatible with v4.2.0
- No breaking changes
- New enum value: `AIModel.Gpt5_2Codex`

---

## 🚀 v4.2.0 - Claude Sonnet 4.6 & Deprecated Model Cleanup

### **New Model: Claude Sonnet 4.6** ✨

- Added `ClaudeSonnet4_6` (`claude-sonnet-4-6`) with **64K max output tokens** (65,536)
- Extended thinking supported (Sonnet 4+)

### **Deprecated Claude 3.x Models Removed** 🗑️

All Claude 3.x models have been retired by Anthropic and are removed from the library:

| Model | API ID | Status |
|-------|--------|--------|
| Claude 3.7 Sonnet | `claude-3-7-sonnet-latest` | EOL (Feb 2026) |
| Claude 3.5 Haiku | `claude-3-5-haiku-20241022` | Retired (404) |
| Claude 3 Opus | `claude-3-opus-20240229` | Retired |
| Claude 3 Haiku | `claude-3-haiku-20240307` | Retired |

**Breaking changes:**
- `AIModel.Claude3_7SonnetLatest` — removed, use `AIModel.ClaudeSonnet4_250514` or newer
- `AIModel.Claude3_5Haiku241022` — removed, use `AIModel.ClaudeHaiku4_5_251001`
- `AIModel.Claude3Opus240229` — removed, no direct replacement
- `AIModel.Claude3Haiku240307` — removed, use `AIModel.ClaudeHaiku4_5_251001`
- `GetModelMaxOutputTokens()` entries for 3.x models removed
- `IsExtendedThinkingModel()` no longer references 3.7 Sonnet

---

## 🔧 v4.1.0 - Error Reporting, New Claude Models & Code Quality

### **Enhanced Error Reporting** 🚨

All AI services now include **HTTP status code and error body** in `AIServiceException`, replacing the unreliable `ReasonPhrase` which is null on HTTP/2 connections.

#### Before (v4.0.x)
```
API request failed: <none>
```

#### After (v4.1.0)
```
API request failed (400): {"type":"error","error":{"type":"invalid_request_error","message":"..."}}
```

- Applied to all services: **Claude**, **ChatGPT**, **Gemini**, **DeepSeek**, **Sonar**
- Covers both non-streaming and streaming endpoints, plus Audio, Images, TokenCount, and Search
- `AIServiceException.ErrorDetails` provides the raw API error body for programmatic inspection

### **Function Argument Conversion Fix** 🐛

Fixed `ConvertValue` to handle non-string `JsonElement` when target type is `string`. Some models (e.g., Claude Opus 4.6) send function arguments as raw JSON arrays instead of stringified JSON, which previously caused `InvalidOperationException`.

```csharp
// Before: jsonElement.GetString() throws on arrays/objects
// After: falls back to GetRawText() for non-string JsonElement
return jsonElement.ValueKind == JsonValueKind.String
    ? jsonElement.GetString()
    : jsonElement.GetRawText();
```

### **New Claude Models** 🤖

| Model | Enum | API Identifier |
|-------|------|----------------|
| **Claude Opus 4.6** | `ClaudeOpus4_6` | `claude-opus-4-6` |
| **Claude Opus 4.5** | `ClaudeOpus4_5_251101` | `claude-opus-4-5-20251101` |
| **Claude Sonnet 4.5** | `ClaudeSonnet4_5_250929` | `claude-sonnet-4-5-20250929` |
| **Claude Haiku 4.5** | `ClaudeHaiku4_5_251001` | `claude-haiku-4-5-20251001` |

### **Code Refactoring** 🏗️

#### ClaudeService
- Extracted constants: `AnthropicApiVersion`, `DefaultImageMimeType`, `SseDataPrefix`, `SseEventPrefix`
- Helper methods: `AddClaudeHeaders()`, `CreateFunctionCallMessage()`, `CreateFunctionResultMessage()`, `ApplySystemMessage()`
- Unified `ProcessMultipleToolUses` loop (removed first/rest duplication)

#### GeminiService
- `SendAndReadAsync()` — Unified HTTP send + error handling
- `ProcessFunctionCallLoopAsync()` — Extracted function call loop from `GetCompletionAsync`
- SSE helpers: `ReadSseLines()`, `TryExtractSseData()`, `SendStreamingRequestAsync()`
- `AddAssistantMessage()`, `AddFunctionCallMessage()`, `AddFunctionResultMessage()` helpers
- `CreateCompletionContent()`, `CreateErrorContent()` factory methods

#### ChatGptService
- General code cleanup and simplification

### 🧪 Test Improvements

- **CrossProvider test diagnostics** — Debug message history dump before Phase 2 API call, `ErrorDetails` output in catch blocks
- **New test classes**: `Claude_Opus4_6_Tests`, `Claude_Opus4_5_Tests`, `Claude_Sonnet4_5_Tests`, `Claude_Haiku4_5_Tests`

### ✅ Compatibility

- Fully backward compatible with v4.0.x
- No breaking changes
- Error message format changed (more detailed), but `AIServiceException` API unchanged

---

## 🛠️ v4.0.1 - MaxTokens Auto-Capping & Cross-Provider Function Fallback

### **Automatic MaxTokens Capping** 🔒

`MaxTokens` is now automatically capped at each model's maximum allowed output tokens before sending API requests. This prevents errors when `MaxTokens` is set higher than a model supports (e.g., after `CopyFrom()` transfers settings between providers).

#### How It Works

- **`GetModelMaxOutputTokens()`** — New virtual method in `AIService`, overridden per service with model-specific limits
- **`GetEffectiveMaxTokens()`** — Returns `Math.Min(MaxTokens, GetModelMaxOutputTokens())`, used in all `BuildRequestBody()` methods

#### Model-Specific Output Token Limits

| Provider | Model | Max Output Tokens |
|----------|-------|-------------------|
| **Claude** | opus-4 / opus-4-1 | 32,768 |
| **Claude** | sonnet-4 | 16,384 |
| **OpenAI** | gpt-5 family | 128,000 |
| **OpenAI** | o3 / o3-pro | 100,000 |
| **OpenAI** | gpt-4.1 family | 32,768 |
| **OpenAI** | gpt-4o / 4o-mini | 16,384 |
| **OpenAI** | gpt-4-vision | 4,096 |
| **Gemini** | all current models | 65,536 |
| **DeepSeek** | all models | 8,192 |
| **Perplexity** | all models | 8,192 |

#### Usage Example

```csharp
// Before v4.0.1: CopyFrom could cause API errors
var gptService = new ChatGptService(apiKey, httpClient);
gptService.MaxTokens = 16000;  // fine for GPT-4o

var claudeService = new ClaudeService(claudeKey, httpClient);
claudeService.CopyFrom(gptService);
claudeService.ChangeModel(AIModel.ClaudeHaiku4_5_251001);
// MaxTokens=16000 > Haiku limit 65536 → no issue (Haiku 4.5 supports 64K)

// After v4.0.1: automatically capped
// MaxTokens stays 16000 but GetEffectiveMaxTokens() returns 8192
// No API error, no manual adjustment needed
```

### **Cross-Provider Function History Fallback** 🔄

When function calling is disabled (`FunctionsDisabled = true`), function-related messages in conversation history are now automatically converted to plain text, preventing API errors from unsupported message formats.

- **`GetLatestMessagesWithFunctionFallback()`** — New helper in `AIService`
  - `function_call` assistant messages → `"[Called funcName(args)]"`
  - `Function` role messages → `User` role with `"[Function funcName returned: result]"`
- Applied in non-function `BuildRequestBody()` for Claude, OpenAI, and Gemini

### 🧪 Test

- **`CrossProvider_FunctionOff_WithFunctionHistory`** — New test verifying that function call history transfers correctly between providers with function calling disabled

### ✅ Compatibility

- Fully backward compatible with v4.0.0
- No breaking changes

---

## 🚀 What's New in v4.0.0

### **Architecture: Configuration moved from ChatBlock to AIService** 🏗️

All configuration settings are now managed at the **service level** instead of per-ChatBlock:

- **`AIService`** holds: `Model`, `Temperature`, `TopP`, `MaxTokens`, `Functions`, `EnableFunctions`, `MaxMessageCount`, `Stream`, etc.
- **`ChatBlock`** now only holds: `Messages`, `SystemMessage`, `Id`

This simplifies the API — configure once on the service, and all conversations share the same settings:

```csharp
var service = new ChatGptService(apiKey, httpClient);
service.Temperature = 0.9f;
service.MaxTokens = 2048;
service.SystemMessage = "You are a helpful assistant."; // delegates to ActivateChat.SystemMessage
```

**`CopyFrom`** now copies both conversation data and service-level settings (except `Model`, which stays provider-specific):

```csharp
var claudeService = new ClaudeService(claudeKey, httpClient).CopyFrom(gptService);
claudeService.ChangeModel(AIModel.ClaudeSonnet4_250514);
// Messages, Functions, Temperature, etc. are all preserved
```

### Migration Guide from v3.2.x to v4.0.0

#### Configuration properties moved to AIService
```csharp
// v3.2.x - Settings on ChatBlock via ActivateChat
service.ActivateChat.Temperature = 0.9f;
service.ActivateChat.MaxTokens = 2048;
service.ActivateChat.ChangeModel(AIModel.Gpt4oMini);
service.ActivateChat.AddFunction(functionDef);
service.ActivateChat.EnableFunctions = true;
service.ActivateChat.MaxMessageCount = 30;

// v4.0.0 - Settings directly on AIService
service.Temperature = 0.9f;
service.MaxTokens = 2048;
service.ChangeModel(AIModel.Gpt4oMini);
service.Functions.Add(functionDef);
service.EnableFunctions = true;
service.MaxMessageCount = 30;
```

#### ChatBlock is now conversation-only
```csharp
// v3.2.x - ChatBlock held everything
var chat = service.ActivateChat;
chat.Model;           // ❌ Removed
chat.Temperature;     // ❌ Removed
chat.Functions;       // ❌ Removed

// v4.0.0 - ChatBlock holds only conversation state
var chat = service.ActivateChat;
chat.Messages;        // ✅ Conversation history
chat.SystemMessage;   // ✅ System prompt
chat.Id;              // ✅ Unique identifier
```

#### CopyFrom copies service settings
```csharp
// v3.2.x - CopyFrom only cloned ChatBlock (which had everything)
var newService = new ClaudeService(key, http).CopyFrom(oldService);

// v4.0.0 - CopyFrom clones ChatBlock + copies service-level settings
// (Functions, Temperature, MaxTokens, etc. are all copied)
// Model is NOT copied (stays as the new provider's default)
var newService = new ClaudeService(key, http).CopyFrom(oldService);
newService.ChangeModel(AIModel.ClaudeSonnet4_250514); // set model explicitly
```

### **Gemini 2.5 GA + Gemini 3 Flash/Pro Preview** 🌐

#### Gemini 2.5 (GA)
- **Gemini 2.5 Pro**, **Gemini 2.5 Flash**, **Gemini 2.5 Flash-Lite** now fully supported (GA)
- 🗑️ Removed deprecated Gemini 1.0, 1.5, 2.0 models

#### Gemini 3 Preview
- **Gemini 3 Flash Preview** (`gemini-3-flash-preview`) and **Gemini 3 Pro Preview** (`gemini-3-pro-preview`) models added
- **Thought Signature Circulation**: Gemini 3 function calling requires thought signatures to be sent back in follow-up requests
- **ThinkingLevel** (`GeminiThinkingLevel` enum: `Auto`/`Minimal`/`Low`/`Medium`/`High`, default: `Auto`) for Gemini 3 vs **ThinkingBudget** (int) for Gemini 2.5
- **IsGemini3Model()** helper for model-specific branching
- Function response role changed to `"user"` for Gemini 3

#### Reasoning Streaming Support
- **`StreamingContentType.Reasoning`**: Gemini thinking parts (`"thought": true`) are now classified as reasoning content
- When `StreamOptions.WithReasoning()` is enabled, thought parts are emitted as `Reasoning` type
- When reasoning is not requested, thought parts are silently skipped

#### Gemini Function Calling Improvements
- Multi-round function call loop with `policy.MaxRounds`
- Proper conversation history management during streaming function calls
- `ConvertParameterProperty` for normalized parameter serialization
- `ParameterProperty.Items` added for array parameter schemas

#### Usage Example

```csharp
var geminiService = new GeminiService(apiKey, httpClient);

// Gemini 3 with thinking level
geminiService.ChangeModel(AIModel.Gemini3FlashPreview);
geminiService.ThinkingLevel = GeminiThinkingLevel.High;  // Auto = model default (High)
var response = await geminiService.GetCompletionAsync("Explain quantum entanglement");

// Streaming with reasoning
await foreach (var content in geminiService.StreamAsync(message, new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"[Thinking] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

---

## v3.2.0

### 🧠 GPT-5.1 / GPT-5.2 Model Support

#### New Models
- **GPT-5.1** (`gpt-5.1`) — Reasoning model with effort levels (none/low/medium/high) and text verbosity control (low/medium/high)
- **GPT-5.2** (`gpt-5.2`) — Best model for complex, coding, and agentic tasks with effort levels (none/low/medium/high/xhigh)
- **GPT-5.2 Pro** (`gpt-5.2-pro`) — High-compute model for tough problems, supports medium/high/xhigh reasoning effort
- **GPT-5.2 Codex** (`gpt-5.2-codex`) — Coding-optimized model for agentic coding tasks, supports low/medium/high/xhigh reasoning effort (default: medium)

#### New Builder Methods
- **`WithGpt5_1Parameters()`** — Configure reasoning effort (`Gpt5_1Reasoning` enum), verbosity (`Verbosity` enum), and reasoning summary (`ReasoningSummary` enum) for GPT-5.1
- **`WithGpt5_2Parameters()`** — Configure reasoning effort (`Gpt5_2Reasoning` enum), verbosity (`Verbosity` enum), and reasoning summary (`ReasoningSummary` enum) for GPT-5.2
- **`WithGpt5Parameters()` updated** — Uses `Gpt5Reasoning` enum for effort and `ReasoningSummary` enum for summary

#### Usage Example

```csharp
var gptService = (ChatGptService)service;

// GPT-5.2 with high reasoning and verbose output
gptService.WithGpt5_2Parameters(reasoningEffort: Gpt5_2Reasoning.High, verbosity: Verbosity.High);
var response = await gptService.GetCompletionAsync("Solve: 15 * 17");

// GPT-5.1 with concise reasoning summary
gptService.WithGpt5_1Parameters(reasoningEffort: Gpt5_1Reasoning.Medium, verbosity: Verbosity.Low, reasoningSummary: ReasoningSummary.Concise);
var response2 = await gptService.GetCompletionAsync("Explain quantum computing");

// GPT-5 base with reasoning summary disabled
gptService.WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.High, reasoningSummary: null);
```

### 🔧 Model Detection Improvements

#### GPT-5 Family Hierarchy
- **`IsGpt5Family()`** — Unified detection for all GPT-5 variants (gpt-5, gpt-5.1, gpt-5.2), used for shared behaviors like Responses API endpoint routing and unsupported parameter removal
- **`IsGpt5Model()`** — Matches only GPT-5 base models (gpt-5, gpt-5-mini, gpt-5-nano), excludes gpt-5.1/5.2
- **`IsGpt5_1Model()`** — Matches GPT-5.1 models
- **`IsGpt5_2Model()`** — Matches GPT-5.2 models (including gpt-5.2-pro, gpt-5.2-codex)
- **`IsGpt5_2CodexModel()`** — Matches GPT-5.2 Codex models specifically (Codex does not support 'none' reasoning effort)
- **Per-model parameter routing** — `ApplyModelSpecificParameters` now routes from most specific to least specific (5.2 → 5.1 → 5)

#### GPT-5.2 Pro / Codex Defaults
- GPT-5.2 Pro automatically applies `Gpt5_2Reasoning.Medium` as default (regular GPT-5.2 defaults to `Gpt5_2Reasoning.None`)
- GPT-5.2 Codex automatically applies `Gpt5_2Reasoning.Medium` as default; 'none' is not supported and will be adjusted to 'low'

### 🗑️ Deprecated Model Removal

| Model | Status | Reason |
|-------|--------|--------|
| `o3-mini` | ❌ Removed | Deprecated by OpenAI |
| `claude-3-5-sonnet-20241022` | ❌ Removed | Deprecated by Anthropic |
| `gpt-5-pro` | ⏸️ Suspended | Temporarily unavailable (not deprecated) |

### 🧪 New Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Gpt5ReasoningEffort` | `Gpt5Reasoning` | `Auto` | GPT-5 reasoning effort (Auto/Minimal/Low/Medium/High) |
| `Gpt5ReasoningSummary` | `ReasoningSummary?` | `Auto` | GPT-5 reasoning summary mode |
| `Gpt5_1ReasoningEffort` | `Gpt5_1Reasoning` | `Auto` | GPT-5.1 reasoning effort (Auto/None/Low/Medium/High) |
| `Gpt5_1ReasoningSummary` | `ReasoningSummary?` | `Auto` | GPT-5.1 reasoning summary mode |
| `Gpt5_1Verbosity` | `Verbosity?` | `null` | GPT-5.1 text verbosity (Low/Medium/High) |
| `Gpt5_2ReasoningEffort` | `Gpt5_2Reasoning` | `Auto` | GPT-5.2 reasoning effort (Auto/None/Low/Medium/High/XHigh) |
| `Gpt5_2ReasoningSummary` | `ReasoningSummary?` | `Auto` | GPT-5.2 reasoning summary mode |
| `Gpt5_2Verbosity` | `Verbosity?` | `null` | GPT-5.2 text verbosity (Low/Medium/High) |

### 🧪 Test Updates
- **Added test classes**: `OpenAI_o3_Tests`, `OpenAI_Gpt5_1_Tests`, `OpenAI_Gpt5_2_Tests`, `OpenAI_Gpt5_2Pro_Tests`, `OpenAI_Gpt5_2Codex_Tests`
- **Removed test classes**: `OpenAI_o3MiniTests`, `OpenAI_Gpt5Pro_Tests`, `Claude_3_5Sonnet_Tests`
- **Relaxed streaming assertions** — Chunk count assertions changed from exact match to range-based (`Assert.IsTrue(count >= 1 && count <= N)`) to accommodate reasoning models that may return fewer chunks
- **Updated Claude vision fallback** — Changed from `Claude3_5Sonnet241022` to `ClaudeSonnet4_250514`

### 📋 GPT-5 Family Model Support Status

| Model | Status | Reasoning Effort | Verbosity |
|-------|--------|-----------------|-----------|
| **gpt-5** | ✅ Full Support | minimal/low/medium/high | — |
| **gpt-5-mini** | ✅ Full Support | minimal/low/medium/high | — |
| **gpt-5-nano** | ✅ Full Support | minimal/low/medium/high | — |
| **gpt-5-pro** | ⏸️ Suspended | — | — |
| **gpt-5.1** | ✅ Full Support | none/low/medium/high | low/medium/high |
| **gpt-5.2** | ✅ Full Support | none/low/medium/high/xhigh | low/medium/high |
| **gpt-5.2-pro** | ✅ Full Support | medium/high/xhigh | low/medium/high |
| **gpt-5.2-codex** | ✅ Full Support | low/medium/high/xhigh | low/medium/high |

### ✅ Compatibility
- Fully backward compatible with v3.1.x
- No breaking changes
- `WithGpt5Parameters()` model guard removed — can now be called regardless of active model
- New enum values in `AIModel`: `Gpt5_1`, `Gpt5_2`, `Gpt5_2Pro`, `Gpt5_2Codex`
- Removed enum values: `o3_mini`, `Gpt5Pro`, `Gpt5Pro_251006`, `Claude3_5Sonnet241022`

---

## v3.1.0

### 🧠 GPT-5 Reasoning Support

#### Reasoning Streaming
- **`StreamingContentType.Reasoning`** - New streaming content type for reasoning data from GPT-5 models
- **`StreamOptions.IncludeReasoning`** - Enable reasoning summary streaming via `new StreamOptions().WithReasoning()`
- **Real-time reasoning output** - Receive reasoning chunks as they arrive, separately from text content

#### Usage Example (Streaming)

```csharp
var options = new StreamOptions().WithReasoning().WithMetadata();

await foreach (var content in service.StreamAsync("Solve this step by step: 15 * 17", options))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Reasoning] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

#### Non-Streaming Reasoning
- **`LastReasoningSummary`** - Access the reasoning summary from the most recent non-streaming GPT-5 response
- Automatically extracted from the `reasoning` output item when `reasoning.summary = "auto"` is configured

#### Usage Example (Non-Streaming)

```csharp
var gptService = (ChatGptService)service;
var response = await gptService.GetCompletionAsync("What is 15 * 17?");

Console.WriteLine($"Answer: {response}");
Console.WriteLine($"Reasoning: {gptService.LastReasoningSummary}");
```

### 🔧 GPT-5 Responses API Enhancements

#### Streaming Metadata Fix
- **Fixed metadata not populating for New API format** - `response.created` and `response.done` events now correctly extract `model`, `response_id`, `usage`, and `finish_reason` into streaming metadata
- Previously, `IncludeMetadata` only worked with legacy `chat/completions` format; now fully supports the Responses API SSE format used by GPT-5 and o3 models

#### Incomplete Response Handling
- **Detects `status=incomplete` responses** - When reasoning exhausts the entire `max_output_tokens` budget before generating text, a clear warning is returned instead of an empty string
- **Reasoning-only output detection** - If the API returns only reasoning content with no text output, a descriptive message is provided

#### GPT-5 Parameter Safeguards
- **`max_output_tokens` minimum floor (4096)** - Prevents reasoning from consuming the entire output budget by enforcing a minimum, with a logged warning when the user's value is overridden
- **`reasoning.summary = "auto"`** - Automatically configured for GPT-5 models to enable reasoning summary extraction

### 🏗 Code Quality Improvements

#### Streaming Parser Refactoring
- **Decomposed `ParseNewApiStreamChunk`** into focused helper methods for better readability and maintainability:
  - `ParseStreamTextDelta` - Text delta parsing
  - `ParseStreamFunctionCallEvent` - Function call event parsing
  - `ParseStreamOutputItemEvent` - Output item event parsing
  - `ParseStreamReasoningEvent` - Reasoning summary event parsing
  - `ParseStreamCreatedEvent` - Response lifecycle event parsing
  - `ParseStreamCompletionEvent` - Stream completion event parsing

#### Test Framework Extension
- **`SupportsReasoning()`** - New virtual method in `AIServiceTestBase` for conditional reasoning test execution
- **`ReasoningSummaryTest`** - Common test verifying both streaming and non-streaming reasoning extraction, automatically skipped for non-reasoning models via `RunIfSupported` pattern

### ✅ Compatibility
- Fully backward compatible with v3.0.x
- No breaking changes
- New `StreamingContentType.Reasoning` enum value added (non-breaking)
- New `StreamOptions.IncludeReasoning` property added (default: false)

---

## v3.0.3

### 🚨 Critical Bug Fixes

#### Claude Function Calling Fix
- **Fixed "non-empty content" error** - Resolved critical issue where Claude API would reject messages with empty content during function calling sequences
- **Claude API compatibility** - Added proper handling for tool_use responses that don't include text content, ensuring all assistant messages have valid content
- **Message cloning fix** - Fixed `Message.Clone()` not properly copying metadata, which could cause function call information to be lost during conversation transfers

### ✨ Improvements

#### Enhanced CopyFrom Method
- **Automatic model preservation** - `CopyFrom` now automatically preserves the target service's model, eliminating the need to call `SwitchModel` afterwards
- **Simplified usage** - Model switching is now handled internally, making cross-provider transfers more intuitive

#### Before (v3.0.2):

```csharp
gptService.CopyFrom(claudeService);
gptService.SwitchModel("gpt-4o");  // Required extra step
```

#### After (v3.0.3):

```csharp
gptService.CopyFrom(claudeService);  // Model automatically preserved
```

### 🔧 Technical Details
- Added content validation in `ExtractFunctionCallWithMetadata` to ensure Claude assistant messages always have non-empty content
- Enhanced `Message.Clone()` to properly copy all message metadata including function call information
- Improved `CopyFrom` to maintain target service model configuration automatically

### 📋 Known Limitations
- Array parameters in function definitions have limited support - full array parameter support with proper `items` schema planned for next release

### ✅ Compatibility
- Fully backward compatible with v3.0.x
- Recommended immediate upgrade from v3.0.2 to resolve Claude function calling issues
- No breaking changes

---

## v3.0.2

### 🐛 Bug Fixes
#### Function Calling Improvements
- **Fixed Claude API function calling errors** - "unexpected tool_use_id" errors when switching to Claude models after function calls from other providers (OpenAI, etc.)
- **Unified ID system** - Implemented internal unified ID management for seamless function calling across different providers
- **Cross-provider compatibility** - Function call history now persists correctly when switching between OpenAI and Claude models

### ✨ New Features
#### Cross-Model Conversation Transfer
- **Added `CopyFrom` method** - Transfer entire conversation history between different AI service instances
- **Cross-provider migration** - Seamlessly migrate conversations from one AI provider to another (e.g., Claude to GPT, Gemini to DeepSeek)
- **Context preservation** - Maintains full chat history, system messages, and settings when switching between different AI models

#### Usage Example

```csharp
// Transfer conversation from Claude to GPT
var claudeService = new ClaudeService(apiKey1, httpClient);
// ... have conversation with Claude ...

var gptService = new ChatGptService(apiKey2, httpClient);
gptService.CopyFrom(claudeService);  // Transfer entire conversation
gptService.SwitchModel("gpt-4o");  // Required in v3.0.2
```

### 🔧 Technical Changes
- Added `MessageMetadataKeys` for standardized metadata handling
- Function messages no longer removed when switching models
- Improved provider-specific ID mapping (`call_id` for OpenAI, `tool_use_id` for Claude)
- Enhanced `ChatBlock.Clone()` method for deep copying conversation state

### ✅ Compatibility
- Fully backward compatible with v3.0.0 and v3.0.1
- No breaking changes

---

*Latest version (v3.2.0) includes GPT-5.1/5.2 model support with verbosity control and reasoning summary configuration. We strongly recommend upgrading from v3.1.x for the latest model support.*

## What's New in v3.0.0

### Function Calling
- Full function calling support for OpenAI GPT-4o and Claude 3+
- Fluent API with `WithFunction()` / `WithFunctionAsync()` / `WithFunctions()`
- Attribute-based registration with `[AiFunction]` / `[AiParameter]`
- Advanced `FunctionBuilder` for complex scenarios
- Function calling policies (`Fast`, `Complex`, `Vision`, custom)

### Enhanced Streaming
- `StreamingContent` with metadata, function call events, and completion info
- `StreamOptions` for fine-grained control (`TextOnlyOptions`, `FullOptions`, custom)

### Migration Guide from v2.x to v3.0.0

#### Function Calling (New Feature)
```csharp
// v3.0.0 - Functions are now supported!
var service = new ChatGptService(apiKey, httpClient)
    .WithFunction("my_function", "Description", 
        ("param", "Param description", true),
        (string param) => $"Result: {param}");

// AI will automatically use functions when appropriate
var response = await service.GetCompletionAsync("Use my function");
```

#### Streaming Changes
```csharp
// v2.x - Returns string chunks
await foreach (var chunk in service.StreamAsync("Hello"))
{
    Console.Write(chunk); // chunk is string
}

// v3.0.0 - Can return StreamingContent with metadata
await foreach (var content in service.StreamAsync("Hello", StreamOptions.FullOptions))
{
    Console.Write(content.Content); // Access text via .Content
    var metadata = content.Metadata; // Access metadata
}

// For backward compatibility, default behavior unchanged
await foreach (var chunk in service.StreamAsync("Hello"))
{
    Console.Write(chunk); // Still works, chunk is string
}
```

#### Policy System (New)
```csharp
// v3.0.0 - Control function execution behavior
service.DefaultPolicy = FunctionCallingPolicy.Fast;

// Per-request override
await service
    .WithTimeout(60)
    .WithMaxRounds(5)
    .GetCompletionAsync("Complex task");
```
