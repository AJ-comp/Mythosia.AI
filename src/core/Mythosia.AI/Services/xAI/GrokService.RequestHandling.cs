using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Protocols;
using System.Collections.Generic;
using System.Net.Http;

namespace Mythosia.AI.Services.xAI
{
    public partial class GrokService
    {
        private static readonly ChatCompletionsProtocol _protocol = ChatCompletionsProtocol.Instance;

        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var p = CreateRequestParams(GetLatestMessagesWithFunctionFallback());
            var body = _protocol.BuildRequestBody(p);
            return _protocol.CreateRequest(ApiKey, body);
        }

        private ProtocolRequestParams CreateRequestParams(IEnumerable<Message> messages, bool forFunctionCalling = false)
        {
            var systemMsg = GetEffectiveSystemMessageWithRequestContext();

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

            if (IsReasoningModel())
            {
                p.ExcludeParameters = forFunctionCalling
                    ? new HashSet<string> { "temperature" }
                    : new HashSet<string> { "frequency_penalty", "presence_penalty" };
            }

            if (SupportsReasoningEffort() && ReasoningEffort != GrokReasoning.Off)
            {
                p.ExtraParameters = new Dictionary<string, object>
                {
                    ["reasoning_effort"] = ReasoningEffort.ToString().ToLowerInvariant()
                };
            }

            return p;
        }

        /// <summary>
        /// Determines if the current model is a reasoning model.
        /// xAI reasoning models reject frequency_penalty, presence_penalty, stop parameters.
        /// </summary>
        private bool IsReasoningModel()
        {
            var model = Model?.ToLower() ?? "";
            return model.Contains("grok-3-mini") ||
                   model.Contains("grok-4");
        }

        /// <summary>
        /// Only grok-3-mini supports the reasoning_effort parameter.
        /// grok-3, grok-4, grok-4-fast-reasoning do NOT support it.
        /// </summary>
        private bool SupportsReasoningEffort()
        {
            var model = Model?.ToLower() ?? "";
            return model.Contains("grok-3-mini");
        }

        #endregion
    }
}
