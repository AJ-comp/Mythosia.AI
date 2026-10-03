using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Utilities;
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
            return options;
        }

        protected override void ValidateProviderRequestOptions(object? options, Message message)
        {
            ValidateClaudeHistoryOwnership();
            ValidateClaudeRequestOptions(ClaudeOptions, message);
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
        // Opus 5.5 is a fixed ID, not a dated-snapshot model family.
        private bool IsClaudeOpus55Model() => RequestModel.Equals(AIModels.Anthropic.ClaudeOpus5_5, StringComparison.OrdinalIgnoreCase);
        private bool UsesBoundClaudeThinking() => IsClaude51Model() || IsClaudeOpus55Model() || IsClaudeSonnet55Model();
        private bool SupportsClaudeSystemMessages() => UsesBoundClaudeThinking() || IsClaudeNameOrSnapshot("claude-fable-5") ||
            IsClaudeNameOrSnapshot("claude-mythos-5") || IsClaudeNameOrSnapshot("claude-opus-5") || IsClaudeNameOrSnapshot("claude-opus-4-8");

        private void ValidateClaudeRequestOptions(ClaudeRequestOptions options, Message? message = null, bool validateThinkingPhase = true)
        {
            if (!Enum.IsDefined(typeof(ClaudeThinkingMode), RequestThinkingMode))
                throw new ArgumentOutOfRangeException(nameof(ThinkingMode));
            if (RequestThinkingMode != ClaudeThinkingMode.Auto && !IsClaudeSonnet55Model())
                throw new NotSupportedException("Explicit Claude thinking phases require Sonnet 5.5. Use adaptive thinking parameters for other models.");
            if (!Enum.IsDefined(typeof(ClaudeThinkingDisplay), RequestAdaptiveThinkingDisplay))
                throw new ArgumentOutOfRangeException(nameof(AdaptiveThinkingDisplay));
            if (RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Updates && !UsesBoundClaudeThinking())
                throw new NotSupportedException("Thinking progress updates require Claude Sonnet 5.5, Opus 5.5, Fable 5.1 or Mythos 5.1.");
            if (validateThinkingPhase && UsesSonnet55BetweenTools() && EffectiveClaudeBinding.HasValue)
                throw new NotSupportedException("Sonnet 5.5 between-tools thinking does not accept binding controls. Keep history unchanged or use adaptive thinking with an explicit binding policy.");
            if (EffectiveClaudeBinding.HasValue && (!Enum.IsDefined(typeof(ClaudeThinkingPrefixMismatchBehavior), EffectiveClaudeBinding.Value) || !SupportsExtendedThinking))
                throw new NotSupportedException("Thinking binding controls require a supported thinking-capable Claude model and a defined behavior.");
            if ((options.TurnInstructions.Length > 0 || options.ConversationInstructions.Length > 0) && !SupportsClaudeSystemMessages())
                throw new NotSupportedException("This Claude model does not support mid-conversation system instructions.");
            if (UsesBoundClaudeThinking())
            {
                if (!string.IsNullOrWhiteSpace(RequestForceFunctionName))
                    throw new NotSupportedException($"Claude model '{RequestModel}' does not support forced tool selection. Use automatic tool selection.");
                if (!Enum.IsDefined(typeof(FunctionCallMode), RequestFunctionCallMode))
                    throw new NotSupportedException($"Claude model '{RequestModel}' accepts only auto or none tool choice.");
                if (message?.Role == ActorRole.Assistant)
                    throw new NotSupportedException($"Claude model '{RequestModel}' does not support assistant prefill.");
            }
        }

        private bool UsesClaudeWireHistory => UsesBoundClaudeThinking() ||
            ClaudeOptions.TurnInstructions.Length > 0 || ClaudeOptions.ConversationInstructions.Length > 0 ||
            (ActivateChat.Messages.Count > 0 && _claudeWireHistories.TryGetValue(ActivateChat, out _));

        private bool PreserveClaudeAssistantContent => UsesClaudeWireHistory || CurrentRequestFeatures.WebSearch != null;

        private void ValidateClaudeHistoryOwnership()
        {
            if (!RequestStatelessMode && _claudeWireHistories.TryGetValue(ActivateChat, out var history))
                history.ValidateRetainedAliases(ActivateChat.Messages);
        }

        internal override int ClampConversationCompactionKeepIndex(int keepFromIndex)
        {
            if (keepFromIndex <= 0 || !UsesClaudeWireHistory) return keepFromIndex;

            // Use the same effective records as generation, including accepted
            // overrides and attachments. Project a copy: a skipped, failed or
            // cancelled summary must not accept pending request state.
            var preview = _claudeWireHistories.TryGetValue(ActivateChat, out var history)
                ? history.Copy() : new ClaudeWireHistory();
            ProjectPreservedClaudeMessages(preview);
            // ChatBlock exposes a plain IList with no clear/reset notification. Keep
            // the latest user turn as an observable conversation anchor rather than
            // retaining hidden instructions after an empty-chat reset. The dependency
            // closure below still moves this boundary back for complete tool pairs.
            if (keepFromIndex >= preview.Occurrences.Count &&
                (preview.CompactedInstructions.Count > 0 || preview.Entries.Any(entry =>
                    entry.Records.Any(record => record.PersistentInstruction))))
            {
                keepFromIndex = 0;
                for (var index = preview.Occurrences.Count - 1; index >= 0; index--)
                    if (preview.Occurrences[index].Message.Role == ActorRole.User)
                    {
                        keepFromIndex = index;
                        break;
                    }
            }
            var calls = new Dictionary<string, int>(StringComparer.Ordinal);
            var dependencies = new List<(int Result, int Call)>();
            for (var index = 0; index < preview.Occurrences.Count; index++)
            {
                var entry = preview.Occurrences[index].Entry;
                if (entry == null) continue;
                foreach (var wire in entry.Wire)
                {
                    if (!wire.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var block in content.EnumerateArray())
                    {
                        var type = ReadClaudeString(block, "type");
                        if (ReadClaudeString(wire, "role") == "assistant" &&
                            (type == "tool_use" || type == "server_tool_use"))
                        {
                            var id = ReadClaudeString(block, "id");
                            if (!string.IsNullOrEmpty(id) && !calls.ContainsKey(id!)) calls.Add(id!, index);
                        }
                        else if (type == "tool_result" || type?.EndsWith("_tool_result", StringComparison.Ordinal) == true)
                        {
                            var id = ReadClaudeString(block, "tool_use_id");
                            if (id != null && calls.TryGetValue(id, out var callIndex))
                                dependencies.Add((index, callIndex));
                        }
                    }
                }
            }

            // Moving the cut can expose another retained result, including a
            // partial result or an interleaved batch. Close every dependency.
            for (var index = dependencies.Count - 1; index >= 0; index--)
                if (dependencies[index].Result >= keepFromIndex && dependencies[index].Call < keepFromIndex)
                    keepFromIndex = dependencies[index].Call;
            return keepFromIndex;
        }

        internal override Action PrepareConversationPrefixRemoval(int count)
        {
            if (!_claudeWireHistories.TryGetValue(ActivateChat, out var history))
                return base.PrepareConversationPrefixRemoval(count);
            var chat = ActivateChat;
            var retained = history.Copy();
            retained.RemovePrefix(chat.Messages, count);
            return () =>
            {
                _claudeWireHistories.Remove(chat);
                _claudeWireHistories.Add(chat, retained);
            };
        }

        private List<object> BuildPreservedClaudeMessages(bool validateGenerationPrefill = true)
        {
            var history = _claudeWireHistories.GetValue(ActivateChat, _ => new ClaudeWireHistory());
            var messages = ProjectPreservedClaudeMessages(history);
            ValidateClaudeMessagePlacement(messages, validateGenerationPrefill);
            return messages;
        }

        private void ApplyCompactedClaudeConversationInstructions(Dictionary<string, object> requestBody, string baseMessage)
        {
            if (!_claudeWireHistories.TryGetValue(ActivateChat, out var history) ||
                history.CompactedInstructions.Count == 0 || ActivateChat.Messages.Count == 0)
            {
                if (!string.IsNullOrEmpty(baseMessage)) requestBody["system"] = baseMessage;
                return;
            }

            // These are accepted system instructions, not prose for a summary model
            // to interpret. Preserve their exact text/order after the original system
            // prompt and before any newer instruction in the retained conversation.
            var blocks = new List<object>();
            if (!string.IsNullOrEmpty(baseMessage)) blocks.Add(new { type = "text", text = baseMessage });
            foreach (var record in history.CompactedInstructions)
                blocks.Add(new { type = "text", text = record.Wire.GetProperty("content").GetString() });
            requestBody["system"] = blocks;
        }

        private List<object> ProjectPreservedClaudeMessages(ClaudeWireHistory history)
        {
            var requestStartIndex = CurrentRequestInputIndex;
            var occurrences = history.Reconcile(ActivateChat.Messages, requestStartIndex, CurrentRequestOperationIdentity);
            var projection = new ClaudeWireProjection();
            var options = ClaudeOptions;
            for (var index = 0; index < occurrences.Count; index++)
            {
                var occurrence = occurrences[index];
                var message = occurrence.Message;
                var isInput = index == requestStartIndex;
                // Compare only what this message contributes to the provider request. Serializing
                // Message itself loses derived content fields and includes arbitrary app metadata.
                var sourceWire = new List<object>();
                AppendClaudeEffortMarker(sourceWire, message);
                sourceWire.Add(ConvertPreservedClaudeMessage(message));
                var sourceRecords = sourceWire.Select((item, position) => position == sourceWire.Count - 1
                    ? ClaudeWireRecord.FromMessage(message, item)
                    : new ClaudeWireRecord(JsonSerializer.SerializeToElement(item))).ToArray();
                // Legacy record identity affects grouping even when OriginalContent
                // itself is unchanged, so it is part of the effective source.
                var source = JsonSerializer.SerializeToElement(sourceRecords.Select(record => new
                    { record.Wire, record.LegacyCallId, record.LegacyResult }));
                if (occurrence.Entry is ClaudeWireEntry previous)
                {
                    // Explicit edits to a public history message remain visible to the API's
                    // Error/DropBlock policy. Keep the attached historical instructions intact.
                    var preservedRecords = previous.Records;
                    if (!JsonElement.DeepEquals(previous.Source, source))
                    {
                        if (previous.Records[0].BreakBefore)
                            sourceRecords[0] = sourceRecords[0].WithBreakBefore();
                        preservedRecords = sourceRecords.Concat(previous.Records.Skip(previous.MessageIndex + 1)).ToArray();
                        occurrences[index] = occurrence.WithEntry(new ClaudeWireEntry(source, preservedRecords, sourceRecords.Length - 1));
                    }
                    foreach (var record in preservedRecords) projection.Append(record);
                    continue;
                }

                var records = sourceRecords.Take(sourceRecords.Length - 1).ToList();
                var messageIndex = records.Count;
                var converted = isInput && CurrentRequestContext?.RequestMessageOverride != null
                    ? CurrentRequestContext.RequestMessageOverride : message;
                records.Add(ClaudeWireRecord.FromMessage(converted, ConvertPreservedClaudeMessage(converted)));
                if (isInput) records[0] = records[0].WithBreakBefore();
                if (isInput && CurrentRequestContext?.AdditionalMessages != null)
                    foreach (var additional in CurrentRequestContext.AdditionalMessages)
                    {
                        var markers = new List<object>();
                        AppendClaudeEffortMarker(markers, additional);
                        records.AddRange(markers.Select(marker => new ClaudeWireRecord(JsonSerializer.SerializeToElement(marker))));
                        records.Add(ClaudeWireRecord.FromMessage(additional, ConvertPreservedClaudeMessage(additional)));
                    }

                var isToolResult = message.FunctionCallResultBatch != null || message.Role == ActorRole.Function;
                var belongsToRequest = requestStartIndex >= 0 && index >= requestStartIndex;
                if (belongsToRequest && (message.Role == ActorRole.User || isToolResult))
                {
                    if (isInput)
                        foreach (var instruction in options.ConversationInstructions)
                            records.Add(new ClaudeWireRecord(JsonSerializer.SerializeToElement(new { role = "system", content = instruction }),
                                persistentInstruction: true));
                    var turnInstructions = new List<string>(options.TurnInstructions);
                    if (!string.IsNullOrEmpty(CurrentRequestContext?.SystemMessagePrefix)) turnInstructions.Add(CurrentRequestContext!.SystemMessagePrefix!);
                    if (!string.IsNullOrEmpty(CurrentRequestContext?.SystemMessageSuffix)) turnInstructions.Add(CurrentRequestContext!.SystemMessageSuffix!);
                    var structured = GetStructuredOutputInstruction();
                    if (!string.IsNullOrEmpty(structured)) turnInstructions.Add(structured!);
                    foreach (var instruction in turnInstructions)
                        records.Add(new ClaudeWireRecord(JsonSerializer.SerializeToElement(new { role = "system", clear_at = "next_user_message", content = instruction })));
                }

                var snapshot = records.ToArray();
                occurrences[index] = occurrence.WithEntry(new ClaudeWireEntry(source, snapshot, messageIndex));
                foreach (var record in snapshot) projection.Append(record);
            }
            return projection.Messages;
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
            if (validateGenerationPrefill && UsesBoundClaudeThinking() && ReadClaudeString(lastTurn, "role") == "assistant" && !EndsWithClaudeServerToolResult(lastTurn))
                throw new NotSupportedException($"Claude model '{RequestModel}' does not support assistant prefill, including in AdditionalMessages.");
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
            var thinking = ResolveClaudeThinkingPlan(preserveConversation: true);
            if (thinking.Display == "updates")
                request.Headers.TryAddWithoutValidation("anthropic-beta", ClaudeUpdatesBeta);
            if (thinking.Binding.HasValue) request.Headers.TryAddWithoutValidation("anthropic-beta", ClaudeBindingBeta);
            if (UsesClaudeWireHistory && _claudeWireHistories.TryGetValue(ActivateChat, out var history) &&
                history.Entries.Any(entry => entry.Wire.Any(item => item.TryGetProperty("clear_at", out _))))
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

        private bool HasClaudeThinkingHistory()
        {
            // Accepted overrides and AdditionalMessages exist only in the occurrence
            // ledger. Inspect the same wire projection as generation, including current
            // imports/edits, rather than just the public Message list. A private ledger
            // keeps this preflight read-only: it cannot accept pending attachments or
            // change ownership when compaction is skipped, fails or is cancelled.
            var preview = _claudeWireHistories.TryGetValue(ActivateChat, out var history)
                ? history.Copy() : new ClaudeWireHistory();
            return ProjectPreservedClaudeMessages(preview).Cast<JsonElement>().Any(message =>
                ReadClaudeString(message, "role") == "assistant" &&
                message.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array &&
                blocks.EnumerateArray().Any(block => ReadClaudeString(block, "type") == "thinking" ||
                    ReadClaudeString(block, "type") == "redacted_thinking"));
        }

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
            public JsonElement Source { get; }
            public ClaudeWireRecord[] Records { get; }
            public JsonElement[] Wire { get; }
            public int MessageIndex { get; }
            public ClaudeWireEntry(JsonElement source, ClaudeWireRecord[] records, int messageIndex)
            {
                Source = source; Records = records; Wire = records.Select(record => record.Wire).ToArray();
                MessageIndex = messageIndex;
            }

            public JsonElement OwnershipSnapshot() => JsonSerializer.SerializeToElement(Records.Select(record => new
            {
                record.Wire, record.LegacyCallId, record.LegacyResult,
                record.PersistentInstruction,
                // A boundary changes wire grouping only for a legacy record.
                BreakBefore = record.BreakBefore && (record.LegacyCallId != null || record.LegacyResult)
            }));
        }

        private sealed class ClaudeWireHistory
        {
            private List<ClaudeWireOccurrence> _occurrences = new List<ClaudeWireOccurrence>();
            private ClaudeWireRecord[] _compactedInstructions = Array.Empty<ClaudeWireRecord>();
            public IReadOnlyList<ClaudeWireOccurrence> Occurrences => _occurrences;
            public IReadOnlyList<ClaudeWireRecord> CompactedInstructions => _compactedInstructions;
            public IEnumerable<ClaudeWireEntry> Entries => _occurrences.Where(item => item.Entry != null).Select(item => item.Entry!);

            // A Message is editable public data, not a conversation occurrence ID.
            // Retain each list occurrence separately, even for imported duplicates.
            // The execution-owned input and operation token reserve the exact new
            // occurrence, so earlier accepted turns never acquire its attachments.
            public List<ClaudeWireOccurrence> Reconcile(IList<Message> messages, int inputIndex, object? operation)
            {
                ValidateRetainedAliases(messages);
                if (_compactedInstructions.Length > 0)
                {
                    var anchors = new HashSet<Message>(_occurrences.Select(item => item.Message), ClaudeMessageReferenceComparer.Instance);
                    // Clearing then adding new messages before the next request is a
                    // reset too. No old occurrence may carry hidden instructions into
                    // that replacement conversation, including count-only projection.
                    if (!messages.Any(anchors.Contains)) _compactedInstructions = Array.Empty<ClaudeWireRecord>();
                }
                var retained = new Dictionary<Message, Queue<ClaudeWireOccurrence>>(ClaudeMessageReferenceComparer.Instance);
                ClaudeWireOccurrence? input = null;
                foreach (var occurrence in _occurrences)
                {
                    if (operation != null && ReferenceEquals(occurrence.Operation, operation) &&
                        inputIndex >= 0 && ReferenceEquals(occurrence.Message, messages[inputIndex]))
                    {
                        input = occurrence;
                        continue;
                    }
                    if (!retained.TryGetValue(occurrence.Message, out var queue))
                        retained.Add(occurrence.Message, queue = new Queue<ClaudeWireOccurrence>());
                    queue.Enqueue(occurrence);
                }

                var current = new List<ClaudeWireOccurrence>(messages.Count);
                for (var index = 0; index < messages.Count; index++)
                {
                    var message = messages[index];
                    if (index == inputIndex)
                        current.Add(input ?? new ClaudeWireOccurrence(message, operation));
                    else if (retained.TryGetValue(message, out var queue) && queue.Count > 0)
                        current.Add(queue.Dequeue());
                    else
                        current.Add(new ClaudeWireOccurrence(message, null));
                }
                return _occurrences = current;
            }

            public void ValidateRetainedAliases(IList<Message> messages)
            {
                var currentCounts = new Dictionary<Message, int>(ClaudeMessageReferenceComparer.Instance);
                foreach (var message in messages)
                    currentCounts[message] = currentCounts.TryGetValue(message, out var count) ? count + 1 : 1;
                foreach (var group in _occurrences.GroupBy(item => item.Message, ClaudeMessageReferenceComparer.Instance))
                {
                    // IList has no identity for the deleted slot when one object
                    // occurs more than once. Only reject partial alias deletion
                    // whose cached wire attachments make the survivors ambiguous.
                    // Ordinary edits, whole removals and equivalent imports remain valid.
                    if (!currentCounts.TryGetValue(group.Key, out var remaining) || remaining >= group.Count()) continue;
                    var entries = group.Where(item => item.Entry != null).Select(item => item.Entry!).ToArray();
                    if (entries.Length < 2) continue;
                    var first = entries[0].OwnershipSnapshot();
                    if (entries.Skip(1).Any(entry => !JsonElement.DeepEquals(first, entry.OwnershipSnapshot())))
                        throw new InvalidOperationException(
                            "A repeated Claude history Message was partially removed, but its occurrences own different preserved content or instructions. " +
                            "Use distinct Message instances for independently editable turns, or clear the conversation.");
                }
            }

            public void RemovePrefix(IList<Message> messages, int count)
            {
                // A library-owned cut has an exact occurrence boundary, even when
                // callers imported the same Message reference more than once.
                Reconcile(messages, -1, null);
                _compactedInstructions = _compactedInstructions.Concat(_occurrences.Take(count)
                    .Where(item => item.Entry != null).SelectMany(item => item.Entry!.Records)
                    .Where(record => record.PersistentInstruction)).ToArray();
                _occurrences.RemoveRange(0, count);
            }

            // Entries and occurrences are immutable; a failed/count-only attempt
            // restores this independent list without leaking staged wire changes.
            public ClaudeWireHistory Copy() => new ClaudeWireHistory
            {
                _occurrences = new List<ClaudeWireOccurrence>(_occurrences),
                _compactedInstructions = _compactedInstructions
            };
        }

        private sealed class ClaudeWireOccurrence
        {
            public Message Message { get; }
            public object? Operation { get; }
            public ClaudeWireEntry? Entry { get; }
            public ClaudeWireOccurrence(Message message, object? operation, ClaudeWireEntry? entry = null)
            {
                Message = message;
                Operation = operation;
                Entry = entry;
            }
            public ClaudeWireOccurrence WithEntry(ClaudeWireEntry entry) => new ClaudeWireOccurrence(Message, Operation, entry);
        }

        private sealed class ClaudeMessageReferenceComparer : IEqualityComparer<Message>
        {
            public static readonly ClaudeMessageReferenceComparer Instance = new ClaudeMessageReferenceComparer();
            public bool Equals(Message? left, Message? right) => ReferenceEquals(left, right);
            public int GetHashCode(Message message) => RuntimeHelpers.GetHashCode(message);
        }
    }
}
