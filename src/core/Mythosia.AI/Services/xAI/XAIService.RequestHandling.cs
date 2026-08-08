using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Protocols;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Mythosia.AI.Services.xAI
{
    public partial class XAIService
    {
        private static readonly ChatCompletionsProtocol _protocol = ChatCompletionsProtocol.Instance;

        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var p = CreateRequestParams(GetLatestMessagesWithFunctionFallback());
            var body = _protocol.BuildRequestBody(p);
            return _protocol.CreateRequest(ApiKey, body);
        }

        private ProtocolRequestParams CreateRequestParams(IEnumerable<Message> messages)
        {
            var systemMsg = GetEffectiveSystemMessageWithRequestContext();
            var modelFamily = GetModelFamily();
            var reasoningEffort = GetReasoningEffortParameter(modelFamily);

            var p = new ProtocolRequestParams
            {
                Model = Model,
                Messages = messages,
                SystemMessage = systemMsg,
                Temperature = Temperature,
                TopP = TopP,
                FrequencyPenalty = FrequencyPenalty,
                PresencePenalty = PresencePenalty,
                MaxTokens = GetEffectiveMaxTokens(),
                Stream = Stream,
                StructuredOutputSchemaJson = _structuredOutputSchemaJson
            };

            if (RejectsPenaltyParameters(modelFamily))
            {
                p.ExcludeParameters = new HashSet<string> { "frequency_penalty", "presence_penalty" };
            }

            if (reasoningEffort != null)
            {
                p.ExtraParameters = new Dictionary<string, object>
                {
                    ["reasoning_effort"] = reasoningEffort
                };
            }

            return p;
        }

        /// <summary>
        /// Current Grok Chat Completions models reject frequency and presence
        /// penalties, including Grok 4.3 with reasoning disabled and the explicit
        /// Grok 4.20 non-reasoning model. The shared protocol does not emit stop.
        /// </summary>
        private bool RejectsPenaltyParameters(GrokModelFamily modelFamily)
        {
            return modelFamily switch
            {
                GrokModelFamily.Grok4_5 => true,
                GrokModelFamily.Grok4_3 => true,
                GrokModelFamily.Grok4_20Reasoning => true,
                GrokModelFamily.Grok4_20NonReasoning => true,
                GrokModelFamily.GrokBuild => true,
                _ => false
            };
        }

        private string? GetReasoningEffortParameter(GrokModelFamily modelFamily)
        {
            if (ReasoningEffort == GrokReasoning.Auto)
                return null;

            switch (modelFamily)
            {
                case GrokModelFamily.Grok4_5:
                    if (ReasoningEffort == GrokReasoning.None)
                    {
                        throw new NotSupportedException(
                            $"{Model} cannot disable reasoning. Use Low, Medium, High, or Auto.");
                    }

                    return SerializeReasoningEffort(
                        GrokReasoning.Low,
                        GrokReasoning.Medium,
                        GrokReasoning.High);

                case GrokModelFamily.Grok4_3:
                    return SerializeReasoningEffort(
                        GrokReasoning.None,
                        GrokReasoning.Low,
                        GrokReasoning.Medium,
                        GrokReasoning.High);

                default:
                    return null;
            }
        }

        private string SerializeReasoningEffort(params GrokReasoning[] supportedValues)
        {
            foreach (var supportedValue in supportedValues)
            {
                if (ReasoningEffort == supportedValue)
                    return ReasoningEffort.ToString().ToLowerInvariant();
            }

            throw new NotSupportedException(
                $"{Model} does not support reasoning effort '{ReasoningEffort}'.");
        }

        private GrokReasoning GetMinimumReasoningEffortForModel()
        {
            return GetModelFamily() switch
            {
                GrokModelFamily.Grok4_5 => GrokReasoning.Low,
                GrokModelFamily.Grok4_3 => GrokReasoning.None,
                _ => GrokReasoning.Auto
            };
        }

        private GrokModelFamily GetModelFamily()
        {
            var model = (Model ?? string.Empty).Trim();

            if (model.Equals(AIModels.xAI.Grok4_5, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.Grok4_5Latest, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.GrokBuildLatest, StringComparison.OrdinalIgnoreCase))
            {
                return GrokModelFamily.Grok4_5;
            }

            if (model.Equals(AIModels.xAI.Grok4_3, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.Grok4_3Latest, StringComparison.OrdinalIgnoreCase) ||
                model.Equals(AIModels.xAI.GrokLatest, StringComparison.OrdinalIgnoreCase))
            {
                return GrokModelFamily.Grok4_3;
            }

            if (model.StartsWith("grok-4.20", StringComparison.OrdinalIgnoreCase))
            {
                return model.Contains("non-reasoning", StringComparison.OrdinalIgnoreCase)
                    ? GrokModelFamily.Grok4_20NonReasoning
                    : GrokModelFamily.Grok4_20Reasoning;
            }

            if (model.StartsWith(AIModels.xAI.GrokBuild0_1, StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith("grok-code-fast", StringComparison.OrdinalIgnoreCase))
            {
                return GrokModelFamily.GrokBuild;
            }

            return GrokModelFamily.Unknown;
        }

        private enum GrokModelFamily
        {
            Unknown,
            Grok4_5,
            Grok4_3,
            Grok4_20Reasoning,
            Grok4_20NonReasoning,
            GrokBuild
        }

        #endregion
    }
}
