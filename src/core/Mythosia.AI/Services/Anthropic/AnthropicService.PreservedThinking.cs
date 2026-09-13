using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        private const string ClaudeUpdatesBeta = "thinking-display-updates-2026-08-18";
        private const string ClaudeBindingBeta = "thinking-binding-controls-2026-08-01";
        private const string ClaudeTurnInstructionBeta = "mid-conversation-system-clear-at-2026-08-21";
        private readonly object _claudeInstructionGate = new object();
        private readonly List<string> _pendingClaudeTurnInstructions = new List<string>();
        private readonly List<string> _pendingClaudeConversationInstructions = new List<string>();
        private readonly ConditionalWeakTable<ChatBlock, ClaudeWireHistory> _claudeWireHistories =
            new ConditionalWeakTable<ChatBlock, ClaudeWireHistory>();
        private ClaudeRequestOptions? _lastClaudeRequestOptions;

        /// <summary>Controls the API's treatment of thinking blocks bound to an edited prefix. Null uses the provider default without opting into the beta.</summary>
        public ClaudeThinkingPrefixMismatchBehavior? ThinkingPrefixMismatchBehavior { get; set; }

        /// <summary>Input transformations reported across the latest logical request's responses, including streaming fallbacks.</summary>
        public IReadOnlyList<ClaudeInputTransformation> LastInputTransformations =>
            _lastClaudeRequestOptions?.SnapshotTransformations() ?? Array.Empty<ClaudeInputTransformation>();

        /// <summary>Selects explicit server-side prefix validation or dropping of incompatible thinking blocks.</summary>
        public AnthropicService WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior behavior)
        {
            if (!Enum.IsDefined(typeof(ClaudeThinkingPrefixMismatchBehavior), behavior))
                throw new ArgumentOutOfRangeException(nameof(behavior));
            ThinkingPrefixMismatchBehavior = behavior;
            return this;
        }

        /// <summary>Queues a system-authority instruction for the next logical request, repeated after its client-tool results.</summary>
        /// <remarks>The instruction clears at the next user message on the wire. Cleared messages remain in history verbatim.</remarks>
        public AnthropicService WithTurnInstruction(string instruction)
        {
            if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("An instruction is required.", nameof(instruction));
            lock (_claudeInstructionGate) _pendingClaudeTurnInstructions.Add(instruction);
            return this;
        }

        /// <summary>Queues a persistent mid-conversation system instruction after the next request's user input.</summary>
        public AnthropicService WithConversationInstruction(string instruction)
        {
            if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("An instruction is required.", nameof(instruction));
            lock (_claudeInstructionGate) _pendingClaudeConversationInstructions.Add(instruction);
            return this;
        }

        protected override object? CaptureProviderRequestOptions(Message message)
        {
            ClaudeRequestOptions options;
            lock (_claudeInstructionGate)
            {
                options = new ClaudeRequestOptions
                {
                    Display = RequestAdaptiveThinkingDisplay,
                    Binding = RequestThinkingPrefixMismatchBehavior,
                    TurnInstructions = _pendingClaudeTurnInstructions.ToArray(),
                    ConversationInstructions = _pendingClaudeConversationInstructions.ToArray()
                };
                _pendingClaudeTurnInstructions.Clear();
                _pendingClaudeConversationInstructions.Clear();
            }
            _lastClaudeRequestOptions = options;
            LastThinkingContent = null;
            ValidateClaudeRequestOptions(options, message);
            return options;
        }

        private ClaudeRequestOptions ClaudeOptions => CurrentProviderRequestOptions as ClaudeRequestOptions ??
            new ClaudeRequestOptions { Display = RequestAdaptiveThinkingDisplay, Binding = RequestThinkingPrefixMismatchBehavior };

        protected override object? CloneProviderRequestOptions(object? options)
        {
            if (!(options is ClaudeRequestOptions source))
                return base.CloneProviderRequestOptions(options);

            var copy = new ClaudeRequestOptions
            {
                Display = source.Display,
                Binding = source.Binding,
                TurnInstructions = source.TurnInstructions.ToArray(),
                ConversationInstructions = source.ConversationInstructions.ToArray()
            };
            _lastClaudeRequestOptions = copy;
            LastThinkingContent = null;
            return copy;
        }

        private bool IsClaude51Model() => IsClaudeNameOrSnapshot("claude-fable-5-1") || IsClaudeNameOrSnapshot("claude-mythos-5-1");
        private bool SupportsClaudeSystemMessages() => IsClaude51Model() || IsClaudeNameOrSnapshot("claude-fable-5") ||
            IsClaudeNameOrSnapshot("claude-mythos-5") || IsClaudeNameOrSnapshot("claude-opus-5") || IsClaudeNameOrSnapshot("claude-opus-4-8");

        private void ValidateClaudeRequestOptions(ClaudeRequestOptions options, Message? message = null)
        {
            if (!Enum.IsDefined(typeof(ClaudeThinkingDisplay), RequestAdaptiveThinkingDisplay))
                throw new ArgumentOutOfRangeException(nameof(AdaptiveThinkingDisplay));
            if (RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Updates && !IsClaude51Model())
                throw new NotSupportedException("Thinking progress updates require Claude Fable 5.1 or Mythos 5.1.");
            if (options.Binding.HasValue && (!Enum.IsDefined(typeof(ClaudeThinkingPrefixMismatchBehavior), options.Binding.Value) || !SupportsExtendedThinking))
                throw new NotSupportedException("Thinking binding controls require a supported thinking-capable Claude model and a defined behavior.");
            if ((options.TurnInstructions.Length > 0 || options.ConversationInstructions.Length > 0) && !SupportsClaudeSystemMessages())
                throw new NotSupportedException("This Claude model does not support mid-conversation system instructions.");
            if (IsClaude51Model())
            {
                if (!string.IsNullOrWhiteSpace(RequestForceFunctionName))
                    throw new NotSupportedException("Claude Fable 5.1 and Mythos 5.1 do not support forced tool selection. Use automatic tool selection.");
                if (!Enum.IsDefined(typeof(FunctionCallMode), RequestFunctionCallMode))
                    throw new NotSupportedException("Claude Fable 5.1 and Mythos 5.1 accept only auto or none tool choice.");
                if (message?.Role == ActorRole.Assistant)
                    throw new NotSupportedException("Claude Fable 5.1 and Mythos 5.1 do not support assistant prefill.");
            }
        }

        private bool UsesClaudeWireHistory => IsClaude51Model() ||
            ClaudeOptions.TurnInstructions.Length > 0 || ClaudeOptions.ConversationInstructions.Length > 0 ||
            (ActivateChat.Messages.Count > 0 && _claudeWireHistories.TryGetValue(ActivateChat, out _));

        private bool PreserveClaudeAssistantContent => UsesClaudeWireHistory || CurrentRequestFeatures.WebSearch != null;

        private List<object> BuildPreservedClaudeMessages(bool validateGenerationPrefill = true)
        {
            var history = _claudeWireHistories.GetValue(ActivateChat, _ => new ClaudeWireHistory());
            var retainedIds = new HashSet<string>(ActivateChat.Messages.Select(message => message.Id));
            foreach (var removedId in history.Entries.Keys.Where(id => !retainedIds.Contains(id)).ToArray())
                history.Entries.Remove(removedId);
            var output = new List<object>();
            var options = ClaudeOptions;
            var requestInputId = CurrentFeatureRequestMessage?.Id;
            var requestInput = ActivateChat.Messages.FirstOrDefault(message => message.Id == requestInputId);
            var requestStartIndex = requestInput == null ? -1 : ActivateChat.Messages.IndexOf(requestInput);
            foreach (var message in ActivateChat.Messages)
            {
                // Compare only what this message contributes to the provider request. Serializing
                // Message itself loses derived content fields and includes arbitrary app metadata.
                var sourceWire = new List<object>();
                AppendClaudeEffortMarker(sourceWire, message);
                sourceWire.Add(ConvertPreservedClaudeMessage(message));
                var source = JsonSerializer.Serialize(sourceWire);
                if (history.Entries.TryGetValue(message.Id, out var previous))
                {
                    // Explicit edits to a public history message remain visible to the API's
                    // Error/DropBlock policy. Keep the attached historical instructions intact.
                    var preservedWire = previous.Wire;
                    if (previous.Source != source)
                    {
                        preservedWire = sourceWire.Select(item => JsonSerializer.SerializeToElement(item))
                            .Concat(previous.Wire.Skip(previous.MessageIndex + 1)).ToArray();
                        history.Entries[message.Id] = new ClaudeWireEntry(source, preservedWire, sourceWire.Count - 1);
                    }
                    output.AddRange(preservedWire.Cast<object>());
                    continue;
                }

                var wire = new List<object>();
                AppendClaudeEffortMarker(wire, message);
                var messageIndex = wire.Count;
                var isInput = message.Id == requestInputId;
                var converted = isInput && CurrentRequestContext?.RequestMessageOverride != null
                    ? CurrentRequestContext.RequestMessageOverride : message;
                wire.Add(ConvertPreservedClaudeMessage(converted));
                if (isInput && CurrentRequestContext?.AdditionalMessages != null)
                    wire.AddRange(CurrentRequestContext.AdditionalMessages.Select(ConvertPreservedClaudeMessage));

                var isToolResult = message.FunctionCallResultBatch != null || message.Role == ActorRole.Function;
                var belongsToRequest = requestStartIndex >= 0 && ActivateChat.Messages.IndexOf(message) >= requestStartIndex;
                if (belongsToRequest && (message.Role == ActorRole.User || isToolResult))
                {
                    if (isInput)
                        foreach (var instruction in options.ConversationInstructions)
                            wire.Add(new { role = "system", content = instruction });
                    var turnInstructions = new List<string>(options.TurnInstructions);
                    if (!string.IsNullOrEmpty(CurrentRequestContext?.SystemMessagePrefix)) turnInstructions.Add(CurrentRequestContext!.SystemMessagePrefix!);
                    if (!string.IsNullOrEmpty(CurrentRequestContext?.SystemMessageSuffix)) turnInstructions.Add(CurrentRequestContext!.SystemMessageSuffix!);
                    var structured = GetStructuredOutputInstruction();
                    if (!string.IsNullOrEmpty(structured)) turnInstructions.Add(structured!);
                    foreach (var instruction in turnInstructions)
                        wire.Add(new { role = "system", clear_at = "next_user_message", content = instruction });
                }

                var snapshot = wire.Select(item => JsonSerializer.SerializeToElement(item)).ToArray();
                history.Entries[message.Id] = new ClaudeWireEntry(source, snapshot, messageIndex);
                output.AddRange(snapshot.Cast<object>());
            }
            ValidateClaudeMessagePlacement(output, validateGenerationPrefill);
            return output;
        }

        private void ValidateClaudeMessagePlacement(List<object> output, bool validateGenerationPrefill)
        {
            var messages = output.Cast<JsonElement>().ToArray();
            for (var index = 0; index < messages.Length; index++)
            {
                if (ReadClaudeString(messages[index], "role") != "system") continue;
                var start = index;
                var hasContent = false;
                do
                {
                    if (messages[index].TryGetProperty("content", out var content))
                        hasContent |= content.ValueKind == JsonValueKind.String
                            ? !string.IsNullOrEmpty(content.GetString())
                            : content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0;
                    index++;
                } while (index < messages.Length && ReadClaudeString(messages[index], "role") == "system");

                // Empty effort-only sections are accepted anywhere. A section containing text,
                // including adjacent effort markers, follows the system-message placement rules.
                if (hasContent &&
                    (start == 0 ||
                     (ReadClaudeString(messages[start - 1], "role") != "user" && !EndsWithClaudeServerToolResult(messages[start - 1])) ||
                     (index < messages.Length && ReadClaudeString(messages[index], "role") != "assistant")))
                    throw new NotSupportedException("Claude mid-conversation instructions must follow a user turn or a paused server-tool result and precede an assistant turn or the end of the request. Check AdditionalMessages and request instructions.");
                index--;
            }

            var lastTurn = messages.LastOrDefault(item => ReadClaudeString(item, "role") != "system");
            if (validateGenerationPrefill && IsClaude51Model() && ReadClaudeString(lastTurn, "role") == "assistant" && !EndsWithClaudeServerToolResult(lastTurn))
                throw new NotSupportedException("Claude Fable 5.1 and Mythos 5.1 do not support assistant prefill, including in AdditionalMessages.");
        }

        private static bool EndsWithClaudeServerToolResult(JsonElement message)
        {
            if (ReadClaudeString(message, "role") != "assistant" ||
                !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0)
                return false;
            var type = ReadClaudeString(content[content.GetArrayLength() - 1], "type");
            return type != null && type.EndsWith("_tool_result", StringComparison.Ordinal);
        }

        private object ConvertPreservedClaudeMessage(Message message)
        {
            if (message.FunctionCallBatch != null) return ConvertAssistantFunctionCallBatchMessage(message);
            if (message.FunctionCallResultBatch != null) return ConvertFunctionResultBatchMessage(message.FunctionCallResultBatch);
            return ConvertMessageForFunctionCalling(message);
        }

        private void ApplyClaudeRequestOptions(Dictionary<string, object> body)
        {
            var options = ClaudeOptions;
            ValidateClaudeRequestOptions(options);
            var hasExplicitControls = RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Updates || options.Binding.HasValue;
            if (!body.ContainsKey("thinking") && !hasExplicitControls) return;
            if (!body.ContainsKey("thinking") && !ModelSupportsAdaptiveThinking())
                throw new NotSupportedException("Enable manual thinking before using binding controls on this Claude model.");
            var thinking = body.TryGetValue("thinking", out var existing)
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(existing))!
                : new Dictionary<string, object> { ["type"] = "adaptive" };
            if (thinking.TryGetValue("type", out var type) && type.ToString() == "disabled" && hasExplicitControls)
                throw new NotSupportedException("Thinking binding controls require adaptive or enabled thinking.");
            if (type?.ToString() == "adaptive" &&
                (thinking.ContainsKey("display") || RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Updates))
                thinking["display"] = RequestAdaptiveThinkingDisplay.ToString().ToLowerInvariant();
            if (options.Binding.HasValue)
                thinking["block_binding"] = new { prefix_mismatch_behavior = options.Binding == ClaudeThinkingPrefixMismatchBehavior.Error ? "error" : "drop_block" };
            body["thinking"] = thinking;
        }

        private void RecordClaudeThinkingContent(string? content)
        {
            // Internal summarization/query-rewrite requests have no provider observation scope.
            if (!(CurrentProviderRequestOptions is ClaudeRequestOptions options) || !ReferenceEquals(options, _lastClaudeRequestOptions)) return;
            if (RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Updates)
            {
                options.ThinkingUpdates.Append(content);
                LastThinkingContent = options.ThinkingUpdates.Length == 0 ? null : options.ThinkingUpdates.ToString();
            }
            else LastThinkingContent = content;
        }

        private void AddClaudePreservedThinkingHeaders(System.Net.Http.HttpRequestMessage request)
        {
            var options = ClaudeOptions;
            if (RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Updates) request.Headers.TryAddWithoutValidation("anthropic-beta", ClaudeUpdatesBeta);
            if (options.Binding.HasValue) request.Headers.TryAddWithoutValidation("anthropic-beta", ClaudeBindingBeta);
            if (UsesClaudeWireHistory && _claudeWireHistories.TryGetValue(ActivateChat, out var history) &&
                history.Entries.Values.Any(entry => entry.Wire.Any(item => item.TryGetProperty("clear_at", out _))))
                request.Headers.TryAddWithoutValidation("anthropic-beta", ClaudeTurnInstructionBeta);
        }

        private void RecordClaudeInputTransformations(string response)
        {
            try
            {
                using var document = JsonDocument.Parse(response);
                var root = document.RootElement;
                RecordClaudeInputTransformations(root, ReadClaudeString(root, "id"), ReadClaudeString(root, "model"));
            }
            catch (JsonException) { /* The existing response parser owns malformed-response errors. */ }
        }

        private void RecordClaudeInputTransformations(JsonElement source, string? responseId = null, string? model = null)
        {
            if (source.ValueKind != JsonValueKind.Object) return;
            if (!(CurrentProviderRequestOptions is ClaudeRequestOptions options)) return;
            if (responseId != null) options.ResponseId = responseId;
            if (model != null) options.Model = model;
            if (!source.TryGetProperty("input_transformations", out var transformations) || transformations.ValueKind != JsonValueKind.Array) return;
            foreach (var item in transformations.EnumerateArray())
                options.AddTransformation(new ClaudeInputTransformation
                {
                    Type = ReadClaudeString(item, "type") ?? string.Empty,
                    Path = ReadClaudeString(item, "path") ?? string.Empty,
                    Reason = ReadClaudeString(item, "reason") ?? string.Empty,
                    ResponseId = options.ResponseId,
                    Model = options.Model ?? RequestModel
                });
        }

        private bool HasClaudeThinkingHistory() => ActivateChat.Messages.Any(message =>
        {
            object? raw = null;
            if (message.Metadata?.TryGetValue(ClaudeNativeContentKey, out raw) != true)
                message.FunctionCallBatch?.Metadata?.TryGetValue(MessageMetadataKeys.OriginalContent, out raw);
            if (raw == null) return false;
            try
            {
                using var document = JsonDocument.Parse(raw.ToString()!);
                return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.EnumerateArray().Any(block =>
                    ReadClaudeString(block, "type") == "thinking" || ReadClaudeString(block, "type") == "redacted_thinking");
            }
            catch (JsonException) { return false; }
        });

        private sealed class ClaudeRequestOptions
        {
            public ClaudeThinkingDisplay Display;
            public ClaudeThinkingPrefixMismatchBehavior? Binding;
            public string[] TurnInstructions = Array.Empty<string>();
            public string[] ConversationInstructions = Array.Empty<string>();
            public string? ResponseId;
            public string? Model;
            public readonly StringBuilder ThinkingUpdates = new StringBuilder();
            private readonly List<ClaudeInputTransformation> _transformations = new List<ClaudeInputTransformation>();
            public void AddTransformation(ClaudeInputTransformation item)
            {
                lock (_transformations)
                    if (!_transformations.Any(previous => previous.Type == item.Type && previous.Path == item.Path && previous.Reason == item.Reason && previous.ResponseId == item.ResponseId && previous.Model == item.Model))
                        _transformations.Add(item);
            }
            public IReadOnlyList<ClaudeInputTransformation> SnapshotTransformations()
            {
                lock (_transformations) return _transformations.Select(item => item.Clone()).ToArray();
            }
        }

        private sealed class ClaudeWireEntry
        {
            public string Source { get; }
            public JsonElement[] Wire { get; }
            public int MessageIndex { get; }
            public ClaudeWireEntry(string source, JsonElement[] wire, int messageIndex) { Source = source; Wire = wire; MessageIndex = messageIndex; }
        }

        private sealed class ClaudeWireHistory
        {
            public Dictionary<string, ClaudeWireEntry> Entries = new Dictionary<string, ClaudeWireEntry>();
            public ClaudeWireHistory Copy() => new ClaudeWireHistory { Entries = new Dictionary<string, ClaudeWireEntry>(Entries) };
        }
    }
}
