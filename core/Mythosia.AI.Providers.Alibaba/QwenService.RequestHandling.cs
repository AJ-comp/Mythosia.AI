using Mythosia.AI.Models.Messages;
using Mythosia.AI.Protocols;
using System.Collections.Generic;
using System.Net.Http;

namespace Mythosia.AI.Providers.Alibaba
{
    public partial class QwenService
    {
        private static readonly ChatCompletionsProtocol _protocol = ChatCompletionsProtocol.Instance;

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var p = CreateRequestParams(GetLatestMessagesWithFunctionFallback());
            var body = _protocol.BuildRequestBody(p);
            return _protocol.CreateRequest(ApiKey, body);
        }

        private ProtocolRequestParams CreateRequestParams(IEnumerable<Message> messages, bool forFunctionCalling = false)
        {
            var systemMsg = GetEffectiveSystemMessageWithRequestContext();

            var modelId = GetEffectiveModelId();
            if (IsQwen35(modelId))
                return CreateQwen35RequestParams(messages, systemMsg, modelId);

            var p = CreateBaseRequestParams(messages, systemMsg, modelId);
            if (ThinkingMode == Mythosia.AI.Providers.Alibaba.QwenThinking.On)
            {
                if (_endpointPlatform == Mythosia.AI.Providers.Alibaba.EndpointPlatform.Ollama)
                {
                    p.ExtraParameters = new Dictionary<string, object>
                    {
                        ["reasoning"] = new Dictionary<string, object> { ["effort"] = "high" }
                    };
                }
                else
                {
                    p.ExtraParameters = new Dictionary<string, object>
                    {
                        ["enable_thinking"] = true
                    };
                }
            }

            return p;
        }

        private ProtocolRequestParams CreateQwen35RequestParams(IEnumerable<Message> messages, string systemMsg, string modelId)
        {
            var p = CreateBaseRequestParams(messages, systemMsg, modelId);
            if (_endpointPlatform == Mythosia.AI.Providers.Alibaba.EndpointPlatform.Ollama)
            {
                if (ThinkingMode == Mythosia.AI.Providers.Alibaba.QwenThinking.On)
                {
                    p.ExtraParameters = new Dictionary<string, object>
                    {
                        ["reasoning"] = new Dictionary<string, object> { ["effort"] = "high" }
                    };
                }
            }
            else
            {
                p.ExtraParameters = new Dictionary<string, object>
                {
                    ["chat_template_kwargs"] = new Dictionary<string, object>
                    {
                        ["enable_thinking"] = ThinkingMode == Mythosia.AI.Providers.Alibaba.QwenThinking.On
                    }
                };
            }

            return p;
        }

        private ProtocolRequestParams CreateBaseRequestParams(IEnumerable<Message> messages, string systemMsg, string modelId)
        {
            return new ProtocolRequestParams
            {
                Model = modelId,
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
        }

        private static bool IsQwen35(string modelId)
            => modelId.Contains("qwen3.5", System.StringComparison.OrdinalIgnoreCase);
    }
}
