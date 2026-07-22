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

            var p = CreateBaseRequestParams(messages, systemMsg, modelId);
            ApplyThinkingParameters(p);
            return p;
        }

        /// <summary>
        /// 호출자가 지정한 <see cref="ThinkingMode"/> 를 플랫폼 표현으로 번역해 그대로 싣는다.
        ///
        /// 모델명으로 "이 모델이 추론을 지원하나"를 추측하지 않는다. 서빙 이름(vLLM --served-model-name, 별칭 등)은
        /// 운영자가 자유롭게 정하므로 능력의 근거가 될 수 없고, 추측이 빗나가면 호출자는 껐다고 믿는데 실제로는 켜져
        /// 있는 '조용한 실패'가 된다 — 요약(RequestProfiles.Summarization → DisableReasoning) 요청이 추론까지
        /// 생성해 타임아웃 나던 원인이 정확히 이것이었다. 모델이 지원하지 않으면 서버가 무시하거나 에러로 드러나는
        /// 편이, 지정이 조용히 사라지는 것보다 옳다.
        /// </summary>
        private void ApplyThinkingParameters(ProtocolRequestParams p)
        {
            var enableThinking = ThinkingMode == QwenThinking.On;

            if (_endpointPlatform == EndpointPlatform.Ollama)
            {
                // Ollama 는 끄기를 위한 별도 표현이 없다 — 켤 때만 reasoning effort 를 싣는다.
                if (enableThinking)
                {
                    p.ExtraParameters = new Dictionary<string, object>
                    {
                        ["reasoning"] = new Dictionary<string, object> { ["effort"] = "high" }
                    };
                }
                return;
            }

            if (_endpointPlatform == EndpointPlatform.Vllm)
            {
                // vLLM 은 채팅 템플릿 변수로 전달해야 한다 — top-level 로 보내면 템플릿에 닿지 않아 무시된다.
                p.ExtraParameters = new Dictionary<string, object>
                {
                    ["chat_template_kwargs"] = new Dictionary<string, object>
                    {
                        ["enable_thinking"] = enableThinking
                    }
                };
                return;
            }

            // DashScope 등 — top-level 파라미터로 전달.
            p.ExtraParameters = new Dictionary<string, object>
            {
                ["enable_thinking"] = enableThinking
            };
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

    }
}
