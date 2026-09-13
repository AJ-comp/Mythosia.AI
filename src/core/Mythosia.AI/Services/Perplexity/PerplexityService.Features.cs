using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        protected override void CaptureRequestSettings(IDictionary<string, object?> settings)
        {
            base.CaptureRequestSettings(settings);
            settings[nameof(AgentOptions)] = CaptureAgentOptions(AgentOptions);
        }

        private static PerplexityAgentOptions CaptureAgentOptions(PerplexityAgentOptions source)
        {
            var options = (source ?? throw new ArgumentException("AgentOptions cannot be null.")).Clone();
            // Freeze JSON-compatible parameter graphs, including mutable caller-owned JSON nodes.
            foreach (var tool in options.Tools)
            {
                using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(tool.Parameters));
                tool.Parameters = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone());
            }
            return options;
        }

        protected override object? CaptureProviderRequestOptions(Message message)
            => CaptureAgentOptions(RequestAgentOptions);

        protected override object? CloneProviderRequestOptions(object? options)
            => options is PerplexityAgentOptions captured ? CaptureAgentOptions(captured) : null;

        protected override void ValidateProviderRequestOptions(object? options, Message message)
        {
            ValidateAgentMessage(message);
            ValidateAgentModelId(RequestModel);
            if (options is PerplexityAgentOptions captured)
            {
                ValidateAgentOptions(captured);
                ApplyAdvancedAgentOptions(new Dictionary<string, object>(), captured);
            }
        }

        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            if (features.FileSearch != null)
                throw new NotSupportedException("Perplexity Agent file search stores are not exposed by this adapter. Use hosted tools or the advanced Agent API.");
            if (features.Reasoning?.Cache == CachePreservation.Required)
                throw new NotSupportedException("Perplexity Agent does not guarantee cache preservation when changing reasoning.");
            if (features.Reasoning != null && !Enum.IsDefined(typeof(ReasoningLevel), features.Reasoning.Level))
                throw new ArgumentOutOfRangeException(nameof(features));
            if (features.Reasoning?.Level == ReasoningLevel.None)
                throw new NotSupportedException("Perplexity Agent accepts minimal, low, medium, high, xhigh and max reasoning effort; None is not supported.");
            if (features.WebSearch?.AllowedDomains?.Any(domain => domain.StartsWith("-", StringComparison.Ordinal)) == true)
                throw new NotSupportedException("WithWebSearch AllowedDomains must be an allowlist. Use native filters for a denylist.");
            // Initial validation runs before the captured execution is installed in AsyncLocal.
            // It must still reject unsupported combinations before appending the user turn.
            var options = CurrentProviderRequestOptions as PerplexityAgentOptions ?? RequestAgentOptions;
            if (options != null)
            {
                var level = features.Reasoning?.Level ?? ReasoningLevel.Auto;
                if (level == ReasoningLevel.Auto) level = options.ReasoningEffort;
                if (level != ReasoningLevel.Auto)
                    ValidateAgentReasoning(level, options.ModelOverride ??
                        (options.Preset.HasValue || options.Profile != null || options.Models != null ? null : RequestModel));
            }
        }

        private static void ValidateAgentOptions(PerplexityAgentOptions options)
        {
            if (options.Preset.HasValue && !Enum.IsDefined(typeof(PerplexityPreset), options.Preset.Value))
                throw new ArgumentOutOfRangeException(nameof(options.Preset));
            if (options.MaxSteps < 0 || options.MaxSteps > 100) throw new ArgumentOutOfRangeException(nameof(options.MaxSteps));
            if (options.ModelOverride != null && string.IsNullOrWhiteSpace(options.ModelOverride))
                throw new ArgumentException("ModelOverride must be a nonempty model identifier.");
            if (options.ModelOverride != null) ValidateAgentModelId(options.ModelOverride);
            if (!Enum.IsDefined(typeof(ReasoningLevel), options.ReasoningEffort)) throw new ArgumentOutOfRangeException(nameof(options.ReasoningEffort));
            if (options.ReasoningEffort == ReasoningLevel.None)
                throw new NotSupportedException("Perplexity Agent does not support reasoning effort None.");
            foreach (var tool in options.Tools)
            {
                if (tool.Type != "web_search" && tool.Type != "fetch_url" && tool.Type != "sandbox" &&
                    tool.Type != "finance_search" && tool.Type != "people_search" && tool.Type != "mcp" && tool.Type != "connector")
                    throw new NotSupportedException($"Unsupported Perplexity hosted tool '{tool.Type}'. Client functions must be registered through Functions.");
                if (tool.Parameters.ContainsKey("type")) throw new ArgumentException("Hosted tool Parameters cannot override Type.");
            }
        }

        private void ValidateAgentClientToolSelection()
        {
            if (RequestFrequencyPenalty != 0.0f || RequestPresencePenalty != 0.0f)
                throw new NotSupportedException("The Perplexity Agent request contract does not expose frequency or presence penalties.");
            if (float.IsNaN(RequestTemperature) || RequestTemperature < 0 || RequestTemperature > 2 || float.IsNaN(RequestTopP) || RequestTopP < 0 || RequestTopP > 1)
                throw new ArgumentOutOfRangeException("sampling", "Agent temperature must be 0 to 2 and top_p must be 0 to 1.");
            // The Agent API documents automatic custom calls, but does not expose tool_choice.
            if (ShouldUseFunctions && RequestFunctionCallMode != FunctionCallMode.None && !string.IsNullOrWhiteSpace(RequestForceFunctionName))
                throw new NotSupportedException("Perplexity Agent does not expose forced named client tools. Use automatic function selection.");
        }

        private static void ValidateAgentModelId(string model)
        {
            if (string.IsNullOrWhiteSpace(model) || !model.Contains("/"))
                throw new NotSupportedException("Perplexity Agent requires a provider/model identifier. Legacy Sonar model IDs are removed; use perplexity/sonar.");
        }

        protected override Action ApplyProviderSpecificRequestProfile(AIRequestProfile profile)
        {
            ApplyPerplexityProfileSettings(profile);
            return () => { };
        }

        protected override void ApplyCapabilityRequestProfile(AIRequestProfile profile)
            => ApplyPerplexityProfileSettings(profile);

        // Shared native flags only; execution-specific output reservations remain outside this helper.
        private void ApplyPerplexityProfileSettings(AIRequestProfile profile)
        {
            if (profile.Purpose != AIRequestPurpose.Default)
                SetExecutionSetting("Perplexity.SuppressAgentTools", true);
            if (profile.DisableReasoning == true)
                SetExecutionSetting("Perplexity.DisableAgentReasoning", true);
        }

        private PerplexityAgentOptions EffectiveAgentOptions()
        {
            // Internal rewrite/summary scopes have no captured provider options and must not inherit presets or tools.
            var options = CurrentProviderRequestOptions is PerplexityAgentOptions captured
                ? captured.Clone() : new PerplexityAgentOptions { DisableWebSearch = true };
            if (SuppressAgentTools)
            {
                options = new PerplexityAgentOptions { DisableWebSearch = true };
            }
            if (DisableAgentReasoning) options.ReasoningEffort = ReasoningLevel.Auto;
            return options;
        }

        protected override string? GetConversationCompactionBlockReason()
        {
            if (ActivateChat.Messages.Any(message => message.FunctionCallBatch != null &&
                message.Metadata?.ContainsKey(AgentOutputMetadataKey) == true))
                return "perplexity-agent-tool-history";
            return base.GetConversationCompactionBlockReason();
        }

        partial void ApplyAdvancedAgentOptions(Dictionary<string, object> body, PerplexityAgentOptions options);
    }
}
