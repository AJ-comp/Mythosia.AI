using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        // One import boundary defines the raw blocks the serializer will actually
        // replay. Compaction and token counting must inspect this same source, with
        // the same precedence; metadata hidden by a typed result is not wire history.
        private static bool TryReadClaudeAssistantContent(Message message, out JsonElement blocks)
        {
            blocks = default;
            object? raw;
            if (message.FunctionCallBatch != null)
            {
                if (message.FunctionCallBatch.Metadata?.TryGetValue(MessageMetadataKeys.OriginalContent, out raw) != true ||
                    string.IsNullOrWhiteSpace(raw?.ToString()))
                    return false;
            }
            else
            {
                if (message.FunctionCallResultBatch != null || message.Role != ActorRole.Assistant)
                    return false;
                var key = IsFunctionCallMessage(message)
                    ? MessageMetadataKeys.OriginalContent : ClaudeNativeContentKey;
                if (message.Metadata?.TryGetValue(key, out raw) != true) return false;
            }

            // Clone JsonElement values so neither a disposed import document nor
            // property-order normalization can alter the retained wire snapshot.
            blocks = raw is JsonElement element && element.ValueKind != JsonValueKind.String
                ? element.Clone()
                : JsonSerializer.Deserialize<JsonElement>(raw?.ToString() ?? string.Empty);
            if (blocks.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Preserved Claude assistant content must be a JSON array.");
            return true;
        }

        // Legacy metadata is an import boundary: persistence can represent the same
        // enum as an enum, integer, JSON number or JSON/string enum name. Interpret it
        // once here for both assistant IDs and result IDs, never by a boxed-type cast.
        private static bool TryReadClaudeLegacySource(Message message, out IdSource source)
        {
            var value = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionSource);
            var text = value is JsonElement element
                ? element.ValueKind == JsonValueKind.String ? element.GetString()
                    : element.ValueKind == JsonValueKind.Number ? element.GetRawText() : null
                : value?.ToString();
            if (int.TryParse(text, out var numeric))
                source = (IdSource)numeric;
            else if (!Enum.TryParse(text, ignoreCase: true, out source) ||
                !string.Equals(text?.Trim(), source.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;
            return Enum.IsDefined(typeof(IdSource), source);
        }

        // Keep logical records before grouping. An accepted input owns its override,
        // attachments and instructions; grouping must not erase their edit boundaries
        // or depend on later mutations to caller-owned metadata.
        private sealed class ClaudeWireRecord
        {
            public JsonElement Wire { get; }
            public string? LegacyCallId { get; }
            public bool LegacyResult { get; }
            public bool BreakBefore { get; }
            public bool PersistentInstruction { get; }

            public ClaudeWireRecord(JsonElement wire, string? legacyCallId = null,
                bool legacyResult = false, bool breakBefore = false, bool persistentInstruction = false)
            {
                Wire = wire; LegacyCallId = legacyCallId;
                LegacyResult = legacyResult; BreakBefore = breakBefore;
                PersistentInstruction = persistentInstruction;
            }

            public static ClaudeWireRecord FromMessage(Message message, object wire, bool breakBefore = false)
            {
                var snapshot = JsonSerializer.SerializeToElement(wire);
                var callId = ClaudeWireProjection.TryGetLegacyAssistantCallId(message, snapshot, out var id) ? id : null;
                var result = message.Role == ActorRole.Function && message.FunctionCallBatch == null &&
                    message.FunctionCallResultBatch == null;
                return new ClaudeWireRecord(snapshot, callId, result, breakBefore);
            }

            public ClaudeWireRecord WithBreakBefore() => new ClaudeWireRecord(Wire, LegacyCallId, LegacyResult, true, PersistentInstruction);
        }

        /// <summary>
        /// Projects validated message records to provider turns. Both ordinary and
        /// preserved conversations use this grouping policy; cache retention and
        /// request-scoped instructions are resolved before projection.
        /// </summary>
        private sealed class ClaudeWireProjection
        {
            public List<object> Messages { get; } = new List<object>();
            private JsonElement? _legacyAssistant;
            private readonly HashSet<string> _legacyCallIds = new HashSet<string>(StringComparer.Ordinal);
            private bool _previousWasLegacyResult;

            public void Append(Message message, JsonElement[] wire, bool allowLegacyGrouping = true)
            {
                foreach (var item in wire)
                    Append(allowLegacyGrouping && wire.Length == 1
                        ? ClaudeWireRecord.FromMessage(message, item)
                        : new ClaudeWireRecord(item));
            }

            public void Append(ClaudeWireRecord record)
            {
                if (record.BreakBefore)
                {
                    _legacyAssistant = null;
                    _legacyCallIds.Clear();
                    _previousWasLegacyResult = false;
                }
                var wire = record.Wire;
                if (record.LegacyCallId is string callId)
                {
                    // OriginalContent is the authoritative complete response, not
                    // the in-memory representation of redundant per-call metadata.
                    // Compare values but emit the original first snapshot unchanged:
                    // signatures, block order and unknown provider fields survive.
                    if (_legacyAssistant.HasValue &&
                        JsonElement.DeepEquals(_legacyAssistant.Value, wire) &&
                        _legacyCallIds.Add(callId))
                        return;
                    _legacyAssistant = wire;
                    _legacyCallIds.Clear();
                    _legacyCallIds.Add(callId);
                }
                else
                {
                    _legacyAssistant = null;
                    _legacyCallIds.Clear();
                }

                var legacyResult = record.LegacyResult;
                if (legacyResult && _previousWasLegacyResult && Messages.Count > 0)
                {
                    var previous = (JsonElement)Messages[Messages.Count - 1];
                    Messages[Messages.Count - 1] = JsonSerializer.SerializeToElement(new
                    {
                        role = "user",
                        content = previous.GetProperty("content").EnumerateArray()
                            .Concat(wire.GetProperty("content").EnumerateArray()).ToArray()
                    });
                }
                else Messages.Add(wire);
                // Effort markers, instructions, typed batches and other attached
                // messages are boundaries. Never group records across these turns.
                _previousWasLegacyResult = legacyResult;
            }

            internal static bool TryGetLegacyAssistantCallId(Message message, JsonElement wire, out string callId)
            {
                callId = string.Empty;
                if (message.Role != ActorRole.Assistant || message.FunctionCallBatch != null ||
                    message.FunctionCallResultBatch != null || !IsFunctionCallMessage(message) ||
                    message.Metadata?.ContainsKey(MessageMetadataKeys.OriginalContent) != true ||
                    !(message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString() is string id) ||
                    string.IsNullOrEmpty(id) || !wire.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Array)
                    return false;

                // Native raw content already carries the wire ID. Imported IDs from
                // another provider need the same conversion as their result records.
                var candidate = id;
                if (!ContainsToolId(content, candidate))
                {
                    if (!TryReadClaudeLegacySource(message, out var source)) return false;
                    candidate = FunctionIdConverter.ToClaudeId(id, source);
                    if (!ContainsToolId(content, candidate)) return false;
                }
                callId = candidate;
                return true;
            }

            private static bool ContainsToolId(JsonElement content, string id)
                => content.EnumerateArray().Any(block => ReadClaudeString(block, "type") == "tool_use" &&
                    ReadClaudeString(block, "id") == id);
        }
    }
}
