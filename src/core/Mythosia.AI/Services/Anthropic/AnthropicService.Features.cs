using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        private const string ClaudeEffortMessageKey = "mythosia_claude_effort";
        private const string ClaudeNativeContentKey = "mythosia_claude_native_content";
        private const string ClaudeEffortBeta = "mid-conversation-output-config-2026-07-01";
        private readonly ConditionalWeakTable<ChatBlock, ClaudeReasoningBaseline> _claudeReasoningBaselines =
            new ConditionalWeakTable<ChatBlock, ClaudeReasoningBaseline>();
        private bool _nativeServerContinuation;
        protected override bool HasPendingRunContinuation => _nativeServerContinuation;

        private sealed class ClaudeReasoningBaseline
        {
            public string Model = string.Empty;
            public string Endpoint = string.Empty;
            public object? Thinking;
            public object? OutputConfig;
            public bool Disabled;
            public string? PersistentEffort;
            public string[] MessageIds = Array.Empty<string>();

            public ClaudeReasoningBaseline Copy() => (ClaudeReasoningBaseline)MemberwiseClone();
        }

        private sealed class ClaudeReasoningAttempt : IDisposable
        {
            private readonly AnthropicService _service;
            private readonly ChatBlock _chat;
            private readonly ClaudeReasoningBaseline? _previous;
            private readonly ClaudeWireHistory? _previousWire;
            private readonly Message? _input;
            private readonly bool _hadMetadata;
            private readonly bool _hadEffort;
            private readonly object? _previousEffort;
            private bool _accepted;

            public ClaudeReasoningAttempt(AnthropicService service)
            {
                _service = service;
                _chat = service.ActivateChat;
                _previous = service._claudeReasoningBaselines.TryGetValue(_chat, out var previous)
                    ? previous.Copy() : null;
                _previousWire = service._claudeWireHistories.TryGetValue(_chat, out var previousWire)
                    ? previousWire.Copy() : null;
                _input = service.CurrentFeatureRequestMessage;
                _hadMetadata = _input?.Metadata != null;
                if (_input?.Metadata?.TryGetValue(ClaudeEffortMessageKey, out var effort) == true)
                {
                    _hadEffort = true;
                    _previousEffort = effort;
                }
            }

            public void Accept() => _accepted = true;

            public void Dispose()
            {
                if (_accepted)
                {
                    _service.CaptureClaudePreservedHistory(_chat);
                    return;
                }

                // Serialization stages both the baseline and the user-message update. A failed
                // send/read must restore only this round, retaining earlier accepted responses.
                _service._claudeReasoningBaselines.Remove(_chat);
                if (_previous != null)
                    _service._claudeReasoningBaselines.Add(_chat, _previous);
                _service._claudeWireHistories.Remove(_chat);
                if (_previousWire != null)
                    _service._claudeWireHistories.Add(_chat, _previousWire);
                if (_input == null) return;
                if (_hadEffort)
                {
                    _input.Metadata ??= new Dictionary<string, object>();
                    _input.Metadata[ClaudeEffortMessageKey] = _previousEffort!;
                }
                else
                {
                    _input.Metadata?.Remove(ClaudeEffortMessageKey);
                    if (!_hadMetadata && _input.Metadata?.Count == 0)
                        _input.Metadata = null;
                }
            }
        }

        private bool SupportsPerMessageClaudeEffort()
        {
            return IsClaudeOpus55Model() || IsClaudeNameOrSnapshot("claude-opus-5") ||
                   IsClaudeNameOrSnapshot("claude-fable-5-1") ||
                   IsClaudeNameOrSnapshot("claude-mythos-5-1");
        }

        private bool IsClaudeNameOrSnapshot(string name) =>
            RequestModel.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            (RequestModel.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase) &&
             RequestModel.Length == name.Length + 9 && RequestModel.Substring(name.Length + 1).All(char.IsDigit));

        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            if (ActivateChat.Messages.Count == 0)
            {
                _claudeReasoningBaselines.Remove(ActivateChat);
                _claudeWireHistories.Remove(ActivateChat);
            }
            if (_claudeReasoningBaselines.TryGetValue(ActivateChat, out var preserved) && preserved.PersistentEffort != null)
            {
                ValidateClaudePreservedHistory(preserved);
                if (RequestStatelessMode)
                    throw new NotSupportedException("A cache-preserving Claude conversation cannot use stateless requests.");
                if (features.Reasoning?.Level == ReasoningLevel.None)
                    throw new NotSupportedException("Thinking cannot be disabled inside a cache-preserving Claude conversation. Start a new conversation.");
            }
            if (features.FileSearch != null)
                throw new NotSupportedException("Anthropic Messages has no supported native file-search-store adapter. Use the RAG pipeline.");
            if (features.WebSearch != null &&
                (ResolveRequestCapabilities().WebSearch == CapabilitySupport.Unsupported || !SupportsExtendedThinking))
                throw new NotSupportedException($"Claude model '{RequestModel}' is not supported by the native web search adapter.");
            if (features.WebSearch != null && RequestFunctions.Any(function => function.Name == "web_search"))
                throw new NotSupportedException("A client function named web_search conflicts with Claude's native web search tool.");
            var reasoning = features.Reasoning;
            if (reasoning == null) return;
            if (reasoning.Cache == CachePreservation.Required)
            {
                if (RequestStatelessMode)
                    throw new NotSupportedException("Cache-preserving Claude effort requires conversation history.");
                if (!_claudeReasoningBaselines.TryGetValue(ActivateChat, out _) &&
                    ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant))
                    throw new NotSupportedException("Imported Claude history has no verified reasoning baseline. Start a new conversation.");
                if (ResolveRequestCapabilities().ReasoningCachePreservation == CapabilitySupport.Unsupported ||
                    !SupportsPerMessageClaudeEffort())
                    throw new NotSupportedException($"Claude model '{RequestModel}' does not support cache-preserving per-message effort.");
                if (_claudeReasoningBaselines.TryGetValue(ActivateChat, out var previous) &&
                    (previous.Model != RequestModel || previous.Endpoint != (HttpClient.BaseAddress?.AbsoluteUri ?? string.Empty)))
                    throw new NotSupportedException("Cache preservation requires the model and endpoint of the previous Claude request.");
                if (reasoning.Level == ReasoningLevel.None)
                    throw new NotSupportedException("Cache-preserving Claude effort cannot change the thinking mode.");
                if (_claudeReasoningBaselines.TryGetValue(ActivateChat, out var baseline) &&
                    baseline.Model == RequestModel && baseline.Disabled)
                    throw new NotSupportedException(
                        "The previous Claude request disabled thinking. Enable adaptive thinking before starting cache-preserving effort changes.");
            }
            var level = reasoning.Level;
            var support = ResolveRequestCapabilities().GetReasoningSupport(level);
            var error = GetClaudeCommonReasoningError(level);
            if (support == CapabilitySupport.Unsupported || (support == CapabilitySupport.Unknown && error != null))
                throw new NotSupportedException(error ?? $"Claude model '{RequestModel}' does not support reasoning level {level}.");
        }

        protected override string? GetConversationCompactionBlockReason()
        {
            if (CurrentRequestFeatures.Reasoning?.Cache == CachePreservation.Required ||
                (_claudeReasoningBaselines.TryGetValue(ActivateChat, out var state) &&
                 state.PersistentEffort != null && ActivateChat.Messages.Count > 0))
                return "Conversation compaction would invalidate the prefix required by cache-preserving Claude effort.";
            if ((IsClaudeNameOrSnapshot("claude-fable-5-1") || IsClaudeOpus55Model()) &&
                ClaudeOptions.Binding != ClaudeThinkingPrefixMismatchBehavior.DropBlock && HasClaudeThinkingHistory())
                return "Conversation compaction would invalidate preserved Claude thinking. Start a new conversation or explicitly select DropBlock.";
            return base.GetConversationCompactionBlockReason();
        }

        private void PrepareClaudeFeatureMessage()
        {
            var input = CurrentFeatureRequestMessage;
            if (input == null || !SupportsPerMessageClaudeEffort()) return;
            var reasoning = CurrentRequestFeatures.Reasoning;
            var baseline = _claudeReasoningBaselines.GetValue(ActivateChat, _ => new ClaudeReasoningBaseline());
            if (reasoning?.Cache != CachePreservation.Required && baseline.PersistentEffort == null) return;
            var effort = reasoning == null
                ? baseline.PersistentEffort!
                : reasoning.Level == ReasoningLevel.Auto
                ? (UsesAdaptiveThinkingForRequest() && IsThinkingEnabled ? ResolveAdaptiveThinkingEffort()
                    : IsClaudeOpus55Model() ? "medium" : "high")
                : reasoning.Level.ToString().ToLowerInvariant();
            if (reasoning?.Cache == CachePreservation.Required)
                baseline.PersistentEffort = effort;
            input.Metadata ??= new Dictionary<string, object>();
            input.Metadata[ClaudeEffortMessageKey] = effort;
            baseline.MessageIds = ActivateChat.Messages.Select(message => message.Id).ToArray();
        }

        private void ValidateClaudePreservedHistory(ClaudeReasoningBaseline baseline)
        {
            if (!string.Equals(baseline.Model, RequestModel, StringComparison.OrdinalIgnoreCase) ||
                baseline.Endpoint != (HttpClient.BaseAddress?.AbsoluteUri ?? string.Empty))
                throw new NotSupportedException("A cache-preserving Claude conversation cannot change model or endpoint. Start a new conversation.");
            if (baseline.MessageIds.Length > ActivateChat.Messages.Count ||
                baseline.MessageIds.Where((id, index) => ActivateChat.Messages[index].Id != id).Any())
                throw new InvalidOperationException("Claude history containing effort updates cannot be truncated or reordered. Clear the conversation to start again.");
        }

        private void CaptureClaudePreservedHistory(ChatBlock chat)
        {
            if (_claudeReasoningBaselines.TryGetValue(chat, out var baseline) && baseline.PersistentEffort != null)
                baseline.MessageIds = chat.Messages.Select(message => message.Id).ToArray();
        }

        private void AppendClaudeEffortMarker(List<object> messages, Message message)
        {
            if (SupportsPerMessageClaudeEffort() && message.Role == ActorRole.User &&
                message.Metadata?.TryGetValue(ClaudeEffortMessageKey, out var effort) == true)
                messages.Add(new
                {
                    role = "system",
                    content = Array.Empty<object>(),
                    output_config = new { effort = effort.ToString() }
                });
        }

        private void ApplyCommonClaudeReasoning(Dictionary<string, object> requestBody)
        {
            var reasoning = CurrentRequestFeatures.Reasoning;
            if (reasoning != null && reasoning.Level != ReasoningLevel.Auto)
            {
                if (reasoning.Level == ReasoningLevel.None)
                {
                    requestBody["thinking"] = new Dictionary<string, object> { ["type"] = "disabled" };
                    requestBody.Remove("output_config");
                }
                else
                {
                    if (ModelSupportsAdaptiveThinking())
                    {
                        requestBody["thinking"] = new Dictionary<string, object>
                        {
                            ["type"] = "adaptive",
                            ["display"] = RequestAdaptiveThinkingDisplay == ClaudeThinkingDisplay.Summarized ? "summarized" : "omitted"
                        };
                        requestBody.Remove("temperature");
                    }
                    requestBody["output_config"] = new Dictionary<string, object>
                    {
                        ["effort"] = reasoning.Level.ToString().ToLowerInvariant()
                    };
                }
            }
            var baseline = _claudeReasoningBaselines.GetValue(ActivateChat, _ => new ClaudeReasoningBaseline());
            if (baseline.PersistentEffort != null && baseline.Model == RequestModel)
            {
                SetOrRemoveClaudeField(requestBody, "thinking", baseline.Thinking);
                SetOrRemoveClaudeField(requestBody, "output_config", baseline.OutputConfig);
                return;
            }
            if (reasoning?.Cache == CachePreservation.Required && reasoning.Level == ReasoningLevel.Auto)
                requestBody["thinking"] = new Dictionary<string, object> { ["type"] = "adaptive" };
            baseline.Model = RequestModel;
            baseline.Endpoint = HttpClient.BaseAddress?.AbsoluteUri ?? string.Empty;
            baseline.Thinking = requestBody.TryGetValue("thinking", out var thinking) ? thinking : null;
            baseline.OutputConfig = requestBody.TryGetValue("output_config", out var output) ? output : null;
            baseline.Disabled = baseline.Thinking != null &&
                JsonSerializer.SerializeToElement(baseline.Thinking).TryGetProperty("type", out var type) &&
                type.GetString() == "disabled";
        }

        private static void SetOrRemoveClaudeField(Dictionary<string, object> body, string name, object? value)
        {
            if (value == null) body.Remove(name);
            else body[name] = value;
        }

        private void ApplyNativeClaudeTools(Dictionary<string, object> requestBody)
        {
            if (CurrentRequestFeatures.WebSearch == null) return;
            var tools = requestBody.TryGetValue("tools", out var existing) && existing is IEnumerable<object> declared
                ? declared.ToList() : new List<object>();
            var search = new Dictionary<string, object>
            {
                ["type"] = "web_search_20250305",
                ["name"] = "web_search"
            };
            if (CurrentRequestFeatures.WebSearch.AllowedDomains?.Count > 0)
                search["allowed_domains"] = CurrentRequestFeatures.WebSearch.AllowedDomains.ToArray();
            tools.Add(search);
            requestBody["tools"] = tools;
        }

        private Message CreateNativeClaudeAssistantMessage(string text, string? rawContent)
        {
            var message = new Message(ActorRole.Assistant, text);
            if (!string.IsNullOrWhiteSpace(rawContent))
                message.Metadata = new Dictionary<string, object> { [ClaudeNativeContentKey] = rawContent! };
            return message;
        }

        private static string? ExtractClaudeRawContent(string response)
        {
            using var document = JsonDocument.Parse(response);
            return document.RootElement.TryGetProperty("content", out var content) ? content.GetRawText() : null;
        }

        private IEnumerable<AICitation> ExtractClaudeCitations(string? rawContent, string? responseId)
        {
            if (string.IsNullOrWhiteSpace(rawContent)) yield break;
            using var document = JsonDocument.Parse(rawContent!);
            if (document.RootElement.ValueKind != JsonValueKind.Array) yield break;
            var contentIndex = 0;
            foreach (var block in document.RootElement.EnumerateArray())
            {
                if (block.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var citation in citations.EnumerateArray())
                        yield return new AICitation
                        {
                            Provider = Provider,
                            ResponseId = responseId,
                            OutputIndex = 0,
                            ContentIndex = contentIndex,
                            Url = ReadClaudeString(citation, "url"),
                            Title = ReadClaudeString(citation, "title") ?? ReadClaudeString(citation, "document_title"),
                            Text = ReadClaudeString(citation, "cited_text")
                        };
                }
                contentIndex++;
            }
        }

        private static string? ReadClaudeString(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private void RecordClaudeResponseCitations(string response)
        {
            try
            {
                using var document = JsonDocument.Parse(response);
                var responseId = ReadClaudeString(document.RootElement, "id");
                foreach (var citation in ExtractClaudeCitations(ExtractClaudeRawContent(response), responseId))
                    RecordCitation(citation);
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException)
            {
                // Preserve the existing response parser's malformed-envelope exception contract.
            }
        }
    }
}
