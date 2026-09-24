using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        private readonly ConditionalWeakTable<ChatBlock, PreservedReasoningState> _preservedReasoning =
            new ConditionalWeakTable<ChatBlock, PreservedReasoningState>();
        private readonly Queue<AICitation> _pendingNativeCitations = new Queue<AICitation>();
        private readonly HashSet<string> _nativeStreamCitationKeys = new HashSet<string>(StringComparer.Ordinal);
        private string? _nativeStreamResponseId;
        private PreservedReasoningAttempt? _pendingReasoningAttempt;

        private bool ShouldPreserveResponseItems => IsGpt6Model(RequestModel) ||
            CurrentRequestFeatures.WebSearch != null || CurrentRequestFeatures.FileSearch != null ||
            ActivateChat.Messages.Any(message => message.Metadata?.ContainsKey(ResponsesOutputItemsMetadataKey) == true);

        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            if (IsGpt6Model(RequestModel)) ValidateGpt6Settings(features);
            if (ActivateChat.Messages.Count == 0 ||
                (_preservedReasoning.TryGetValue(ActivateChat, out var unaccepted) && !unaccepted.AcceptedResponse &&
                 !ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant)))
                _preservedReasoning.Remove(ActivateChat);
            if (features.Reasoning != null)
            {
                ValidateCommonReasoningLevel(features.Reasoning.Level);
                if (features.Reasoning.Cache == CachePreservation.Required)
                {
                    var preservation = ResolveRequestCapabilities().ReasoningCachePreservation;
                    if (preservation == CapabilitySupport.Unsupported ||
                        (preservation == CapabilitySupport.Unknown &&
                         (!SupportsAsyncFunctionCalls || RequestGpt6ReasoningMode != Gpt6ReasoningMode.Standard)))
                        throw new NotSupportedException("Cache-preserving reasoning changes require GPT-6 Astra, Sol, or Luna in Standard mode.");
                    if (RequestStatelessMode)
                        throw new NotSupportedException("Cache-preserving reasoning changes require conversation history.");
                    if (!_preservedReasoning.TryGetValue(ActivateChat, out _) &&
                        ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant))
                        throw new NotSupportedException("Imported conversation history has no verified reasoning baseline. Start a new conversation before requiring cache preservation.");
                }
            }

            if (features.WebSearch != null || features.FileSearch != null)
            {
                var capabilities = ResolveRequestCapabilities();
                var unknownSearch = (features.WebSearch != null && capabilities.WebSearch == CapabilitySupport.Unknown) ||
                    (features.FileSearch != null && capabilities.FileSearch == CapabilitySupport.Unknown);
                if ((features.WebSearch != null && capabilities.WebSearch == CapabilitySupport.Unsupported) ||
                    (features.FileSearch != null && capabilities.FileSearch == CapabilitySupport.Unsupported) ||
                    (unknownSearch && !IsHostedSearchSupportedByAdapter(RequestModel)))
                    throw new NotSupportedException("This OpenAI model does not support the requested hosted search integration.");
            }

            if (features.WebSearch?.AllowedDomains != null)
            {
                var domains = features.WebSearch.AllowedDomains;
                if (domains.Count == 0 || domains.Count > 100 || domains.Any(domain =>
                    string.IsNullOrWhiteSpace(domain) || domain != domain.Trim() ||
                    Uri.CheckHostName(domain) != UriHostNameType.Dns))
                    throw new ArgumentException("AllowedDomains must contain 1 to 100 host names without schemes or paths.");
            }

            if (features.FileSearch != null)
            {
                var stores = features.FileSearch.Stores;
                if (stores == null || stores.Count == 0 || stores.Any(store => store == null))
                    throw new ArgumentException("File search requires at least one store.");
                if (stores.Any(store => !string.Equals(store.Provider, Provider, StringComparison.OrdinalIgnoreCase)))
                    throw new NotSupportedException("OpenAI file search requires OpenAI stores; provider stores are not interchangeable.");
                if (stores.Any(store => !store.Id.StartsWith("vs_", StringComparison.Ordinal)))
                    throw new ArgumentException("OpenAI file search requires vector store IDs beginning with vs_.");
            }

            if (_preservedReasoning.TryGetValue(ActivateChat, out var state) && state.Preserving)
            {
                ValidatePreservedHistory(state);
            }
        }

        private void ValidateCommonReasoningLevel(ReasoningLevel level)
        {
            if (!Enum.IsDefined(typeof(ReasoningLevel), level))
                throw new ArgumentOutOfRangeException(nameof(level));
            var support = ResolveRequestCapabilities().GetReasoningSupport(level);
            if (support == CapabilitySupport.Unsupported ||
                (support == CapabilitySupport.Unknown && !IsOpenAIReasoningLevelSupportedByAdapter(level)))
                throw new NotSupportedException($"Reasoning level {level} is not supported by OpenAI model {RequestModel}.");
        }

        private bool IsOpenAIReasoningLevelSupportedByAdapter(ReasoningLevel level)
        {
            var model = RequestModel.ToLowerInvariant();
            bool supported;
            if (IsGpt6Model(model))
                supported = Enum.TryParse(level.ToString(), out Gpt6Reasoning effort) && Enum.IsDefined(typeof(Gpt6Reasoning), effort) &&
                    (level != ReasoningLevel.None || IsGpt6OptionalReasoningModel(model));
            else if (IsGpt5_6Model(model))
                supported = Enum.TryParse(level.ToString(), out Gpt5_6Reasoning effort) && Enum.IsDefined(typeof(Gpt5_6Reasoning), effort);
            else if (IsGpt5_5Model(model) || IsGpt5_4Model(model) || IsGpt5_3Model(model) || IsGpt5_2Model(model))
            {
                supported = level != ReasoningLevel.Minimal && level != ReasoningLevel.Max;
                if (IsGpt5_3CodexModel(model)) supported &= level != ReasoningLevel.None;
                if (model.Contains("-pro")) supported &= level != ReasoningLevel.None && level != ReasoningLevel.Low;
            }
            else if (IsGpt5_1Model(model))
                supported = level == ReasoningLevel.Auto || level == ReasoningLevel.None ||
                    level == ReasoningLevel.Low || level == ReasoningLevel.Medium || level == ReasoningLevel.High;
            else if (IsO3Model(model))
                supported = level == ReasoningLevel.Auto || level == ReasoningLevel.Low ||
                    level == ReasoningLevel.Medium || level == ReasoningLevel.High;
            else if (IsGpt5Family(model))
                supported = model.StartsWith("gpt-5-pro", StringComparison.Ordinal)
                    ? level == ReasoningLevel.Auto || level == ReasoningLevel.High
                    : level == ReasoningLevel.Auto || level == ReasoningLevel.Minimal ||
                    level == ReasoningLevel.Low || level == ReasoningLevel.Medium || level == ReasoningLevel.High;
            else
                supported = false;
            return supported;
        }

        private void ApplyNativeRequestFeatures(Dictionary<string, object> request)
        {
            var features = CurrentRequestFeatures;
            if (features.Reasoning != null ||
                (_preservedReasoning.TryGetValue(ActivateChat, out var preserved) && preserved.Preserving))
            {
                var reasoning = request.TryGetValue("reasoning", out var existing)
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(existing))!
                    : new Dictionary<string, object>();
                if (_preservedReasoning.TryGetValue(ActivateChat, out var state) && state.Preserving)
                {
                    reasoning["effort"] = state.BaseEffort;
                    request["truncation"] = "disabled";
                }
                else if (features.Reasoning != null && features.Reasoning.Level != ReasoningLevel.Auto)
                    reasoning["effort"] = features.Reasoning.Level.ToString().ToLowerInvariant();
                request["reasoning"] = reasoning;
            }

            ApplyGpt6ReasoningDependentParameters(request);

            if (features.WebSearch == null && features.FileSearch == null) return;
            var tools = request.TryGetValue("tools", out var registered)
                ? JsonSerializer.Deserialize<List<object>>(JsonSerializer.Serialize(registered))!
                : new List<object>();
            if (features.WebSearch != null)
            {
                var web = new Dictionary<string, object> { ["type"] = "web_search" };
                if (features.WebSearch.AllowedDomains != null)
                    web["filters"] = new { allowed_domains = features.WebSearch.AllowedDomains };
                tools.Add(web);
            }
            if (features.FileSearch != null)
                tools.Add(new { type = "file_search", vector_store_ids = features.FileSearch.Stores.Select(store => store.Id).ToArray() });
            request["tools"] = tools;
            if (!request.ContainsKey("tool_choice")) request["tool_choice"] = "auto";
        }

        private string ConfiguredGpt6Effort => (RequestGpt6ReasoningEffort == Gpt6Reasoning.Auto
            ? Gpt6Reasoning.Medium : RequestGpt6ReasoningEffort).ToString().ToLowerInvariant();

        private string EffectiveGpt6Effort()
        {
            var option = CurrentRequestFeatures.Reasoning;
            if (option != null && option.Level != ReasoningLevel.Auto)
                return option.Level.ToString().ToLowerInvariant();
            var configured = ConfiguredGpt6Effort;
            // A required cache-preserving update persists across later requests. It may
            // differ from the initial effort kept on the wire to preserve the cache.
            if (option == null && !RequestStatelessMode &&
                _preservedReasoning.TryGetValue(ActivateChat, out var state) && state.Preserving &&
                string.Equals(state.Model, RequestModel, StringComparison.OrdinalIgnoreCase) &&
                state.ConfiguredEffort == configured)
                return state.PersistentEffort;
            return configured;
        }

        private bool SupportsGpt6Sampling() =>
            IsGpt6OptionalReasoningModel(RequestModel) && EffectiveGpt6Effort() == "none";

        private void ValidateGpt6Settings(AIRequestFeatures features)
        {
            if (!Enum.IsDefined(typeof(Gpt6ReasoningMode), RequestGpt6ReasoningMode))
                throw new ArgumentOutOfRangeException(nameof(Gpt6ReasoningMode));
            if (RequestGpt6Verbosity.HasValue && !Enum.IsDefined(typeof(Verbosity), RequestGpt6Verbosity.Value))
                throw new ArgumentOutOfRangeException(nameof(Gpt6Verbosity));
            if (RequestGpt6ReasoningSummary.HasValue && !Enum.IsDefined(typeof(ReasoningSummary), RequestGpt6ReasoningSummary.Value))
                throw new ArgumentOutOfRangeException(nameof(Gpt6ReasoningSummary));
            if (features.Reasoning == null || features.Reasoning.Level == ReasoningLevel.Auto)
            {
                if (!Enum.IsDefined(typeof(Gpt6Reasoning), RequestGpt6ReasoningEffort))
                    throw new ArgumentOutOfRangeException(nameof(Gpt6ReasoningEffort));
                if (RequestGpt6ReasoningEffort == Gpt6Reasoning.None && !IsGpt6OptionalReasoningModel(RequestModel))
                    throw new NotSupportedException($"Reasoning level None is not supported by OpenAI model {RequestModel}.");
            }
        }

        private void ApplyGpt6ReasoningDependentParameters(Dictionary<string, object> request)
        {
            if (!IsGpt6Model(RequestModel)) return;
            if (SupportsGpt6Sampling())
            {
                request["temperature"] = RequestTemperature;
                request["top_p"] = RequestTopP;
            }
            else
            {
                request.Remove("temperature");
                request.Remove("top_p");
                request.Remove("logprobs");
                request.Remove("top_logprobs");
            }
            if (request.TryGetValue("reasoning", out var value) && value is IDictionary<string, object> reasoning &&
                (EffectiveGpt6Effort() == "none" ||
                 (reasoning.TryGetValue("effort", out var serializedEffort) && serializedEffort.ToString() == "none")))
                reasoning.Remove("summary");
        }

        private void PreparePreservedReasoning()
        {
            if (!SupportsAsyncFunctionCalls) return;
            var current = ActivateChat.Messages.LastOrDefault(message => message.Role == ActorRole.User);
            if (current == null) return;
            // Serialization stages updates; only an accepted response boundary commits them.
            // Network errors or canceled reads must not change later requests' persistent effort.
            if (_pendingReasoningAttempt == null)
                _pendingReasoningAttempt = new PreservedReasoningAttempt
                {
                    Chat = ActivateChat,
                    Previous = _preservedReasoning.TryGetValue(ActivateChat, out var previous) ? previous.Copy() : null
                };
            var option = CurrentRequestFeatures.Reasoning;
            var configured = ConfiguredGpt6Effort;
            var desired = option == null || option.Level == ReasoningLevel.Auto
                ? configured : option.Level.ToString().ToLowerInvariant();

            if (!_preservedReasoning.TryGetValue(ActivateChat, out var state))
            {
                if (option?.Cache == CachePreservation.Required && ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant))
                    throw new NotSupportedException("Imported conversation history has no verified reasoning baseline. Start a new conversation before requiring cache preservation.");
                state = new PreservedReasoningState
                {
                    Model = RequestModel, Endpoint = HttpClient.BaseAddress?.AbsoluteUri ?? string.Empty,
                    BaseEffort = desired, PersistentEffort = desired, EffectiveEffort = desired,
                    ConfiguredEffort = configured
                };
                _preservedReasoning.Add(ActivateChat, state);
            }
            if (!state.Preserving)
            {
                if (option?.Cache != CachePreservation.Required)
                {
                    state.BaseEffort = desired;
                    state.PersistentEffort = desired;
                    state.EffectiveEffort = desired;
                    state.ConfiguredEffort = configured;
                    state.Model = RequestModel;
                    state.Endpoint = HttpClient.BaseAddress?.AbsoluteUri ?? string.Empty;
                    return;
                }
                state.Preserving = true;
            }
            ValidatePreservedHistory(state);
            if (option?.Cache == CachePreservation.Required)
                state.PersistentEffort = desired;
            else if (option == null && configured != state.ConfiguredEffort)
                state.PersistentEffort = configured;
            state.ConfiguredEffort = configured;
            if (option == null) desired = state.PersistentEffort;

            if (desired != state.EffectiveEffort)
            {
                if (state.Updates.ContainsKey(current.Id))
                    throw new InvalidOperationException("Reasoning settings cannot change after this user request has started.");
                state.Updates.Add(current.Id, desired);
                state.EffectiveEffort = desired;
            }
            state.MessageIds = ActivateChat.Messages.Select(message => message.Id).ToArray();
        }

        private void ValidatePreservedHistory(PreservedReasoningState state)
        {
            if (!string.Equals(state.Model, RequestModel, StringComparison.OrdinalIgnoreCase) ||
                state.Endpoint != (HttpClient.BaseAddress?.AbsoluteUri ?? string.Empty) ||
                RequestGpt6ReasoningMode != Gpt6ReasoningMode.Standard)
                throw new NotSupportedException("A conversation with cache-preserving reasoning updates cannot change its model, endpoint, or reasoning mode. Start a new conversation.");
            if (state.MessageIds.Length > ActivateChat.Messages.Count ||
                state.MessageIds.Where((id, index) => ActivateChat.Messages[index].Id != id).Any())
                throw new InvalidOperationException("Conversation history containing reasoning updates cannot be truncated or reordered. Clear the conversation to start again.");
        }

        private void RememberPreservedHistory()
        {
            if (_preservedReasoning.TryGetValue(ActivateChat, out var state))
            {
                state.AcceptedResponse = true;
                state.MessageIds = ActivateChat.Messages.Select(message => message.Id).ToArray();
            }
        }

        private void AcceptPreservedReasoning()
        {
            if (_pendingReasoningAttempt != null && ReferenceEquals(_pendingReasoningAttempt.Chat, ActivateChat))
            {
                _pendingReasoningAttempt = null;
                if (_preservedReasoning.TryGetValue(ActivateChat, out var state)) state.AcceptedResponse = true;
            }
        }

        private void RollbackUnacceptedReasoning()
        {
            var attempt = _pendingReasoningAttempt;
            if (attempt == null) return;
            _pendingReasoningAttempt = null;
            _preservedReasoning.Remove(attempt.Chat);
            if (attempt.Previous != null) _preservedReasoning.Add(attempt.Chat, attempt.Previous);
        }

        private void AppendReasoningUpdate(List<object> input, int historyIndex)
        {
            if (historyIndex >= ActivateChat.Messages.Count ||
                !_preservedReasoning.TryGetValue(ActivateChat, out var state) || !state.Preserving) return;
            if (state.Updates.TryGetValue(ActivateChat.Messages[historyIndex].Id, out var effort))
                input.Add(new { type = "configuration_update", reasoning = new { effort } });
        }

        protected override string? GetConversationCompactionBlockReason()
        {
            if (CurrentRequestFeatures.Reasoning?.Cache == CachePreservation.Required ||
                (_preservedReasoning.TryGetValue(ActivateChat, out var state) && state.Preserving && ActivateChat.Messages.Count > 0))
                return "cache-preserving-reasoning";
            return base.GetConversationCompactionBlockReason();
        }

        private sealed class PreservedReasoningState
        {
            public string Model = string.Empty;
            public string Endpoint = string.Empty;
            public string BaseEffort = string.Empty;
            public string PersistentEffort = string.Empty;
            public string EffectiveEffort = string.Empty;
            public string ConfiguredEffort = string.Empty;
            public bool Preserving;
            public bool AcceptedResponse;
            public string[] MessageIds = Array.Empty<string>();
            public Dictionary<string, string> Updates = new Dictionary<string, string>(StringComparer.Ordinal);
            public PreservedReasoningState Copy()
            {
                var copy = (PreservedReasoningState)MemberwiseClone();
                copy.MessageIds = MessageIds.ToArray();
                copy.Updates = new Dictionary<string, string>(Updates, StringComparer.Ordinal);
                return copy;
            }
        }

        private sealed class PreservedReasoningAttempt
        {
            public ChatBlock Chat = null!;
            public PreservedReasoningState? Previous;
        }

        private Message CreateResponsesAssistantMessage(string content, string responseJson, bool isResponses)
        {
            var message = new Message(ActorRole.Assistant, content);
            if (isResponses && ShouldPreserveResponseItems)
            {
                using var document = JsonDocument.Parse(responseJson);
                if (document.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                    message.Metadata = new Dictionary<string, object>
                    {
                        [ResponsesOutputItemsMetadataKey] = output.EnumerateArray().Select(item => item.Clone()).ToList()
                    };
            }
            return message;
        }

        private void CaptureResponseCitations(string responseJson)
        {
            using var document = JsonDocument.Parse(responseJson);
            foreach (var citation in ReadResponseCitations(document.RootElement)) RecordCitation(citation);
        }

        private IEnumerable<AICitation> ReadResponseCitations(JsonElement response)
        {
            if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) yield break;
            var outputIndex = 0;
            foreach (var item in output.EnumerateArray())
            {
                foreach (var citation in ReadItemCitations(item, GetString(response, "id"), outputIndex)) yield return citation;
                outputIndex++;
            }
        }

        private IEnumerable<AICitation> ReadItemCitations(JsonElement item, string? responseId, int? outputIndex)
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("content", out var contents) ||
                contents.ValueKind != JsonValueKind.Array) yield break;
            var contentIndex = 0;
            foreach (var part in contents.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Array)
                    foreach (var annotation in annotations.EnumerateArray())
                    {
                        var citation = ParseNativeCitation(annotation, responseId, outputIndex, contentIndex);
                        if (citation != null) yield return citation;
                    }
                contentIndex++;
            }
        }

        private AICitation? ParseNativeCitation(JsonElement annotation, string? responseId, int? outputIndex, int? contentIndex)
        {
            var type = GetString(annotation, "type");
            if (type != "url_citation" && type != "file_citation") return null;
            var citation = new AICitation
            {
                Provider = Provider, ResponseId = responseId, OutputIndex = outputIndex, ContentIndex = contentIndex,
                Url = GetString(annotation, "url"), FileId = GetString(annotation, "file_id"),
                Title = GetString(annotation, "title") ?? GetString(annotation, "filename"),
                Text = GetString(annotation, "text"), StartIndex = ReadOptionalInt(annotation, "start_index"),
                EndIndex = ReadOptionalInt(annotation, "end_index")
            };
            return citation.Url == null && citation.FileId == null ? null : citation;
        }

        private static int? ReadOptionalInt(JsonElement item, string name)
            => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : (int?)null;

        private void PopulateNativeStreamStatus(JsonElement root, OpenAIStreamChunk chunk)
        {
            if (chunk.Error != null) return;
            if (_pendingNativeCitations.Count > 0 && chunk.Status == null)
            {
                chunk.Status = new StreamingContent
                {
                    Type = StreamingContentType.Citation, Citation = _pendingNativeCitations.Dequeue()
                };
                return;
            }
            var type = GetString(root, "type");
            if (type != null && (type.StartsWith("response.web_search_call.", StringComparison.Ordinal) ||
                type.StartsWith("response.file_search_call.", StringComparison.Ordinal)))
                chunk.Status = new StreamingContent
                {
                    Type = StreamingContentType.Status,
                    Metadata = new Dictionary<string, object>
                    {
                        ["tool_type"] = type.StartsWith("response.web_", StringComparison.Ordinal) ? "web_search" : "file_search",
                        ["status"] = type.Substring(type.LastIndexOf('.') + 1),
                        ["item_id"] = GetString(root, "item_id") ?? string.Empty,
                        ["response_id"] = _nativeStreamResponseId ?? string.Empty
                    }
                };
        }

        private void ResetNativeStreamState()
        {
            _pendingNativeCitations.Clear();
            _nativeStreamCitationKeys.Clear();
            _nativeStreamResponseId = null;
        }

        private void CaptureNativeStreamEvent(JsonElement root)
        {
            var type = GetString(root, "type");
            if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
                _nativeStreamResponseId = GetString(response, "id") ?? _nativeStreamResponseId;
            if (type == "response.output_text.annotation.added" && root.TryGetProperty("annotation", out var annotation))
            {
                var citation = ParseNativeCitation(annotation, _nativeStreamResponseId,
                    ReadOptionalInt(root, "output_index"), ReadOptionalInt(root, "content_index"));
                if (citation != null) EnqueueNativeCitation(citation);
            }
            else if (type == "response.output_item.done" && root.TryGetProperty("item", out var item))
            {
                foreach (var citation in ReadItemCitations(item, _nativeStreamResponseId, ReadOptionalInt(root, "output_index")))
                    EnqueueNativeCitation(citation);
            }
            else if (type == "response.completed" && response.ValueKind == JsonValueKind.Object)
            {
                foreach (var citation in ReadResponseCitations(response)) EnqueueNativeCitation(citation);
            }
        }

        private void EnqueueNativeCitation(AICitation citation)
        {
            var key = JsonSerializer.Serialize(citation);
            if (_nativeStreamCitationKeys.Add(key)) _pendingNativeCitations.Enqueue(citation);
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options, bool useFunctions, FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var failed = false;
            try
            {
                await foreach (var item in base.StreamRoundAsync(options, useFunctions, policy, cancellationToken))
                {
                    if (item.Type == StreamingContentType.Error)
                    {
                        failed = true;
                        _pendingNativeCitations.Clear();
                    }
                    if (item.Citation != null)
                    {
                        RecordCitation(item.Citation);
                        yield return item;
                    }
                    while (_pendingNativeCitations.Count > 0)
                    {
                        var citation = _pendingNativeCitations.Dequeue();
                        RecordCitation(citation);
                        yield return new StreamingContent { Type = StreamingContentType.Citation, Citation = citation };
                    }
                    if (item.Citation == null) yield return item;
                }
                while (_pendingNativeCitations.Count > 0)
                {
                    var citation = _pendingNativeCitations.Dequeue();
                    RecordCitation(citation);
                    yield return new StreamingContent { Type = StreamingContentType.Citation, Citation = citation };
                }
                // A successful reasoning-only or hosted-tool response still carries conversation state.
                if (ShouldPreserveResponseItems && _currentStreamResponseOutputItems?.Count > 0)
                {
                    var assistant = new Message(ActorRole.Assistant, string.Empty);
                    EnrichStreamAssistantMessage(assistant);
                    ActivateChat.Messages.Add(assistant);
                }
                if (!failed) RememberPreservedHistory();
            }
            finally
            {
                RollbackUnacceptedReasoning();
            }
        }
    }
}
