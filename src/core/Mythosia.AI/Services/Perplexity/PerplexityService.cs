using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TiktokenSharp;

namespace Mythosia.AI.Services.Perplexity
{
    /// <summary>Perplexity Agent API with hosted search, local function calling and streaming.</summary>
    public partial class PerplexityService : AIService
    {
        public override string Provider => nameof(AIProvider.Perplexity);
        public PerplexityAgentOptions AgentOptions { get; set; } = new PerplexityAgentOptions();
        public string? LastResponseId { get; private set; }
        private const string AgentOutputMetadataKey = "perplexity_agent_output_items";
        private string? _agentRequestMessageId;
        private bool SuppressAgentTools => RequestSetting("Perplexity.SuppressAgentTools", false);
        private bool DisableAgentReasoning => RequestSetting("Perplexity.DisableAgentReasoning", false);
        private PerplexityAgentOptions RequestAgentOptions => RequestSetting(nameof(AgentOptions), AgentOptions);

        public PerplexityService(string apiKey, HttpClient httpClient)
            : base(apiKey, "https://api.perplexity.ai/", httpClient)
        {
            Model = "perplexity/sonar";
            MaxTokens = 4096;
            Temperature = 1.0f;
        }

        public PerplexityService(string apiKey, string model, HttpClient httpClient) : this(apiKey, httpClient)
            => ChangeModel(model);

        // The selected provider model owns its output limit; do not clamp every model to Sonar's old limit.
        protected override uint GetModelMaxOutputTokens() => int.MaxValue;
        protected override string? GetRequestMessageOverrideTargetId()
            => base.GetRequestMessageOverrideTargetId() ?? _agentRequestMessageId;

        public PerplexityService WithPerplexityOptions(PerplexityAgentOptions options)
        {
            AgentOptions = (options ?? throw new ArgumentNullException(nameof(options))).Clone();
            return this;
        }

        public PerplexityService UsePreset(PerplexityPreset preset)
        {
            if (!Enum.IsDefined(typeof(PerplexityPreset), preset)) throw new ArgumentOutOfRangeException(nameof(preset));
            AgentOptions.Preset = preset;
            return this;
        }

        public override async Task<string> GetCompletionAsync(Message message)
        {
            RequestCancellationToken.ThrowIfCancellationRequested();
            using var settingsScope = BeginRequestSettingsScope();
            using var features = BeginRequestFeaturesScope(message);
            ValidateAgentClientToolSelection();
            var policy = GetExecutionPolicy();
            var timeoutSeconds = ResolveRequestTimeoutSeconds(policy);
            using var timeout = CreateRequestTimeoutCts(policy);
            SetExecutionSetting(nameof(Stream), false);
            var previousAnchor = _agentRequestMessageId;
            _agentRequestMessageId = message.Id;
            ChatBlock? original = null;
            if (RequestStatelessMode)
            {
                original = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = original.SystemMessage };
            }
            try
            {
                timeout.Token.ThrowIfCancellationRequested();
                ActivateChat.Messages.Add(message);
                for (var round = 0; round < policy.MaxRounds; round++)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    var useFunctions = ShouldUseFunctions && RequestFunctionCallMode != FunctionCallMode.None;
                    using var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
                    using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    var json = await ReadCompletionResponseBodyAsync(response, timeout.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, json);
                    var parsed = ParseAgentResponse(json);
                    if (parsed.Usage != null) LastKnownInputTokens = parsed.Usage.InputTokens;
                    ValidateAgentFunctionBatch(parsed.Calls, useFunctions);
                    foreach (var citation in parsed.Citations) RecordCitation(citation);
                    LastResponseId = parsed.Id;
                    if (parsed.Calls.Calls.Count != 0)
                    {
                        await ProcessFunctionBatchForRoundAsync(parsed.Text, parsed.Calls,
                            CreateAgentOutputMetadata(parsed), policy, timeout.Token).ConfigureAwait(false);
                        continue;
                    }
                    timeout.Token.ThrowIfCancellationRequested();
                    ActivateChat.Messages.Add(new Message(ActorRole.Assistant, parsed.Text) { Metadata = CreateAgentOutputMetadata(parsed) });
                    return parsed.Text;
                }
                timeout.Token.ThrowIfCancellationRequested();
                throw new AIServiceException($"Maximum function-calling rounds ({policy.MaxRounds}) exceeded.");
            }
            catch (TaskCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested &&
                exception.InnerException is TimeoutException)
            {
                throw new AIServiceException("The HTTP request timed out.", exception);
            }
            catch (OperationCanceledException exception) when (!RequestCancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new AIServiceException($"Request timeout after {timeoutSeconds} seconds", exception);
            }
            finally
            {
                _agentRequestMessageId = previousAnchor;
                if (original != null) ActivateChat = original;
            }
        }

        public override Task<uint> GetInputTokenCountAsync()
        {
            var text = new StringBuilder(GetEffectiveSystemMessageWithRequestContext());
            foreach (var message in GetLatestMessages()) text.Append('\n').Append(message.GetDisplayText());
            return GetInputTokenCountAsync(text.ToString());
        }

        /// <summary>Returns a local approximation; authoritative usage comes from the Agent response.</summary>
        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult((uint)TikToken.EncodingForModel("gpt-4").Encode(prompt).Count);

        private static Dictionary<string, object> CreateAgentOutputMetadata(ParsedAgentResponse response)
            => new Dictionary<string, object> { [AgentOutputMetadataKey] = response.ReplayItems };
    }
}
